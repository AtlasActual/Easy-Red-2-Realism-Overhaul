using Mono.Cecil;
var path = args[0];
var withCallers = args.Length > 1 && (args[1] == "--callers" || args[1] == "-callers");
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(path));
var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
static bool Noise(string n) => n.StartsWith("NativeFieldInfoPtr_") || n.StartsWith("NativeMethodInfoPtr_") || n == "NativeClassPtr" || n.StartsWith("_delegate_") ;
static string Callers(MethodDefinition m)
{
    var a = m.CustomAttributes.FirstOrDefault(c => c.AttributeType.Name == "CallerCountAttribute");
    return a == null ? "" : $" callers={a.ConstructorArguments[0].Value}";
}
foreach (var t in asm.MainModule.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
{
    Console.WriteLine($"T {t.FullName}" + (t.BaseType != null ? $" : {t.BaseType.FullName}" : ""));
    foreach (var f in t.Fields.Where(f => !Noise(f.Name)).OrderBy(f => f.Name, StringComparer.Ordinal))
        Console.WriteLine($"F {t.FullName}::{f.Name} : {f.FieldType.FullName}");
    foreach (var p in t.Properties.OrderBy(p => p.Name, StringComparer.Ordinal))
        Console.WriteLine($"P {t.FullName}::{p.Name} : {p.PropertyType.FullName}");
    foreach (var m in t.Methods.Where(m => !Noise(m.Name)).OrderBy(m => m.FullName, StringComparer.Ordinal))
        Console.WriteLine($"M {t.FullName}::{m.Name}({string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName))}) : {m.ReturnType.FullName}" + (withCallers ? Callers(m) : ""));
}
