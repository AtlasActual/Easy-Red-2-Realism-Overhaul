using System.Reflection;
using System.Text;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Records which parts of the mod could not attach to the running game build.
/// Easy Red 2 updates rename and remove code the mod hooks; each hook is
/// installed independently so one removed method disables one feature instead
/// of the whole plugin, and the outcome is written to the log and shown in the
/// in-game settings menu so players can report exactly what stopped working.
/// </summary>
internal static class CompatibilityReport
{
    private static readonly List<string> _failedPatchModules = new();
    private static readonly List<string> _failedControllers = new();

    internal static int AppliedPatchModules { get; private set; }
    internal static IReadOnlyList<string> FailedPatchModules => _failedPatchModules;
    internal static IReadOnlyList<string> FailedControllers => _failedControllers;
    internal static bool HasFailures => _failedPatchModules.Count != 0 || _failedControllers.Count != 0;
    internal static int FailureCount => _failedPatchModules.Count + _failedControllers.Count;
    internal static string GameVersion { get; private set; } = "unknown";
    internal static string UnityVersion { get; private set; } = "unknown";

    internal static void CaptureGameVersion()
    {
        try
        {
            GameVersion = string.IsNullOrWhiteSpace(Application.version) ? "unknown" : Application.version;
            UnityVersion = string.IsNullOrWhiteSpace(Application.unityVersion) ? "unknown" : Application.unityVersion;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not read the game version: {ex.Message}");
        }
    }

    internal static void NotePatchModuleApplied() => AppliedPatchModules++;

    internal static void NotePatchModuleFailed(string module, Exception exception)
    {
        _failedPatchModules.Add(module);
        Plugin.LogSource.LogError(
            $"Patch module {module} could not attach to this game build and was skipped " +
            $"({Describe(exception)}). The rest of the mod keeps running.\n{exception}");
    }

    internal static void NoteControllerFailed(string controller, Exception exception)
    {
        _failedControllers.Add(controller);
        Plugin.LogSource.LogError(
            $"Component {controller} could not be created against this game build and was skipped " +
            $"({Describe(exception)}). The rest of the mod keeps running.\n{exception}");
    }

    /// <summary>
    /// One line for the settings-menu header. Empty when everything attached.
    /// </summary>
    internal static string MenuNotice
    {
        get
        {
            if (!HasFailures)
                return string.Empty;

            var count = FailureCount;
            return count == 1
                ? "1 feature could not attach to this game version (see BepInEx/LogOutput.log)"
                : $"{count} features could not attach to this game version (see BepInEx/LogOutput.log)";
        }
    }

    internal static void LogSummary()
    {
        var builder = new StringBuilder();
        builder.Append($"Compatibility summary for Easy Red 2 (Application.version {GameVersion}, Unity {UnityVersion}): ")
               .Append($"{AppliedPatchModules} patch module(s) applied");

        if (!HasFailures)
        {
            builder.Append(", no failures.");
            Plugin.LogSource.LogInfo(builder.ToString());
            return;
        }

        builder.Append($", {_failedPatchModules.Count} patch module(s) and {_failedControllers.Count} component(s) failed.");
        if (_failedControllers.Count != 0)
            builder.Append(" Failed components: ").Append(string.Join(", ", _failedControllers)).Append('.');
        if (_failedPatchModules.Count != 0)
            builder.Append(" Failed patch modules: ").Append(string.Join(", ", _failedPatchModules)).Append('.');
        builder.Append(" Each failure names game code that this game build no longer has in the form the mod expects;");
        builder.Append(" the corresponding features stay off until the mod is updated for this game version.");
        Plugin.LogSource.LogWarning(builder.ToString());
    }

    /// <summary>
    /// Reduces the usual "game code moved" exception shapes to a short, readable cause.
    /// </summary>
    internal static string Describe(Exception exception)
    {
        var root = exception;
        while (root.InnerException != null &&
               (root is TargetInvocationException || root is TypeInitializationException || root is HarmonyLib.HarmonyException))
        {
            root = root.InnerException;
        }

        return root switch
        {
            ReflectionTypeLoadException loader => "the mod references game types that no longer exist: " +
                                                  DescribeLoaderExceptions(loader),
            TypeLoadException typeLoad => $"a referenced game type no longer exists: {typeLoad.Message}",
            FileNotFoundException notFound => $"a referenced assembly could not be found: {notFound.Message}",
            MissingMemberException missing => $"a referenced game member no longer exists: {missing.Message}",
            _ => $"{root.GetType().Name}: {root.Message}"
        };
    }

    internal static string DescribeLoaderExceptions(ReflectionTypeLoadException exception)
    {
        var messages = exception.LoaderExceptions
            .Where(e => e != null)
            .Select(e => e!.Message)
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToArray();
        return messages.Length == 0 ? exception.Message : string.Join(" | ", messages);
    }
}
