using System.Text;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.InstructionSets;
using Cpp2IL.Core.Model.Contexts;
using Iced.Intel;
using LibCpp2IL;

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: probe <gameDir> <outDir> [--types T1,T2] <Type::Method|Type::*>...");
    return 1;
}

var gameDir = args[0];
var outDir = args[1];
Directory.CreateDirectory(outDir);
var extraTypes = new List<string>();
var specs = new List<string>();
for (var i = 2; i < args.Length; i++)
{
    if (args[i] == "--types" || args[i] == "-types") { extraTypes.AddRange(args[++i].Split(',')); continue; }
    specs.Add(args[i]);
}

var exe = Path.Combine(gameDir, "Easy Red 2.exe");
var dataDir = Path.Combine(gameDir, "Easy Red 2_Data");
var gameAssembly = Path.Combine(gameDir, "GameAssembly.dll");
var metadata = Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat");

InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_64);
var unityVersion = Cpp2IlApi.DetermineUnityVersion(exe, dataDir);
Console.Error.WriteLine($"Unity {unityVersion}; loading...");
Cpp2IlApi.InitializeLibCpp2Il(gameAssembly, metadata, unityVersion, false);
var app = Cpp2IlApi.CurrentAppContext!;
Console.Error.WriteLine("Loaded. Resolving key functions...");
var keyByAddress = new Dictionary<ulong, string>();
try
{
    var key = app.GetOrCreateKeyFunctionAddresses();
    foreach (var pair in key.Pairs)
        if (pair.Value != 0) keyByAddress[pair.Value] = pair.Key;
}
catch (Exception ex) { Console.Error.WriteLine("Key functions unavailable: " + ex.Message); }

var asm = app.GetAssemblyByName("Assembly-CSharp")!;
TypeAnalysisContext? FindType(string name)
{
    var t = asm.GetTypeByFullName(name);
    if (t != null) return t;
    var slashName = name.Replace('.', '/');
    return app.AllTypes.FirstOrDefault(x => x.FullName == name || x.FullName == slashName);
}

var fieldMaps = new Dictionary<string, Dictionary<long, List<string>>>();
Dictionary<long, List<string>> FieldMap(TypeAnalysisContext type)
{
    if (fieldMaps.TryGetValue(type.FullName, out var cached)) return cached;
    var map = new Dictionary<long, List<string>>();
    for (var t = type; t != null; t = t.BaseType)
    {
        foreach (var f in t.Fields)
        {
            if (f.Offset < 0) continue;
            if (!map.TryGetValue(f.Offset, out var list)) map[f.Offset] = list = new List<string>();
            list.Add((f.IsStatic ? "static " : "") + t.DefaultName + "." + f.DefaultName);
        }
    }
    fieldMaps[type.FullName] = map;
    return map;
}

var extraMaps = extraTypes.Select(FindType).Where(t => t != null).Select(t => FieldMap(t!)).ToList();

string Symbolize(ulong address)
{
    if (app.MethodsByAddress.TryGetValue(address, out var methods) && methods.Count > 0)
        return string.Join(" | ", methods.Select(m => m.FullNameWithSignature ?? m.FullName));
    if (keyByAddress.TryGetValue(address, out var key)) return "il2cpp:" + key;
    return "";
}

var formatter = new NasmFormatter();
formatter.Options.HexPrefix = "0x";
formatter.Options.HexSuffix = null;
formatter.Options.UppercaseHex = false;
formatter.Options.SpaceAfterOperandSeparator = true;

int written = 0;
foreach (var spec in specs)
{
    var parts = spec.Split("::");
    var type = FindType(parts[0]);
    if (type == null) { Console.Error.WriteLine("Type not found: " + parts[0]); continue; }
    var typeMap = FieldMap(type);
    var methods = type.Methods.Where(m => parts[1] == "*" || m.DefaultName == parts[1]).ToList();
    if (methods.Count == 0) { Console.Error.WriteLine("No method " + spec); continue; }
    var n = 0;
    foreach (var m in methods)
    {
        n++;
        var sb = new StringBuilder();
        sb.AppendLine($"// {m.FullNameWithSignature}");
        sb.AppendLine($"// rva=0x{m.Rva:X} ptr=0x{m.UnderlyingPointer:X}");
        byte[] bytes;
        try { bytes = m.RawBytes.ToArray(); } catch (Exception ex) { bytes = Array.Empty<byte>(); sb.AppendLine("// raw bytes unavailable: " + ex.Message); }
        sb.AppendLine($"// size={bytes.Length}");
        if (bytes.Length > 0)
        {
            var decoder = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(bytes), m.UnderlyingPointer, DecoderOptions.None);
            var end = m.UnderlyingPointer + (ulong)bytes.Length;
            var output = new StringOutput();
            while (decoder.IP < end)
            {
                decoder.Decode(out var instr);
                formatter.Format(instr, output);
                var text = output.ToStringAndReset();
                var notes = new List<string>();
                if (instr.FlowControl == FlowControl.Call || instr.FlowControl == FlowControl.UnconditionalBranch)
                {
                    if (instr.Op0Kind == OpKind.NearBranch64)
                    {
                        var target = instr.NearBranch64;
                        var sym = Symbolize(target);
                        if (sym.Length != 0) notes.Add("-> " + sym);
                        else if (target < m.UnderlyingPointer || target >= end) notes.Add("-> sub_" + target.ToString("X"));
                    }
                }
                for (var op = 0; op < instr.OpCount; op++)
                {
                    if (instr.GetOpKind(op) != OpKind.Memory) continue;
                    if (instr.IsIPRelativeMemoryOperand)
                    {
                        var addr = instr.IPRelativeMemoryAddress;
                        try
                        {
                            var usage = LibCpp2IlMain.GetAnyGlobalByAddress(addr);
                            if (usage != null && usage.IsValid) notes.Add("global " + usage);
                        }
                        catch { }
                        var sym = Symbolize(addr);
                        if (sym.Length != 0) notes.Add("addr " + sym);
                    }
                    else if (instr.MemoryBase != Register.None && instr.MemoryIndex == Register.None)
                    {
                        var disp = (long)instr.MemoryDisplacement64;
                        var candidates = new List<string>();
                        if (typeMap.TryGetValue(disp, out var own)) candidates.AddRange(own);
                        foreach (var extra in extraMaps)
                            if (extra != typeMap && extra.TryGetValue(disp, out var other)) candidates.AddRange(other);
                        if (candidates.Count != 0) notes.Add("{" + string.Join(" | ", candidates.Distinct()) + "}");
                    }
                }
                sb.Append("  ").Append(instr.IP.ToString("X")).Append("  ").Append(text.PadRight(48));
                if (notes.Count != 0) sb.Append(" ; ").Append(string.Join("  ", notes));
                sb.AppendLine();
            }
        }
        sb.AppendLine("=== ISIL ===");
        try
        {
            m.Analyze();
            foreach (var i in m.ConvertedIsil ?? new List<Cpp2IL.Core.ISIL.InstructionSetIndependentInstruction>())
                sb.AppendLine("  " + i);
        }
        catch (Exception ex) { sb.AppendLine("  ISIL failed: " + ex.GetType().Name + ": " + ex.Message); }
        var file = Path.Combine(outDir, $"{type.DefaultName}.{m.DefaultName}{(methods.Count > 1 ? "." + n : "")}.txt");
        File.WriteAllText(file, sb.ToString());
        written++;
    }
}
Console.Error.WriteLine($"Wrote {written} method listing(s) to {outDir}");
return 0;
