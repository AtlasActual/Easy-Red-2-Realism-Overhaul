using System.Text;

namespace ER2RealismOverhaul;

internal static class SettingsExport
{
    private const string ExportFileSuffix = ".defaults-export.cfg";

    internal static bool TryWrite(
        SettingsDraft draft,
        out string exportPath,
        out int changedValueCount,
        out string error)
    {
        exportPath = string.Empty;
        changedValueCount = 0;

        if (!draft.TryGetNormalizedValues(out var values, out error))
            return false;

        try
        {
            exportPath = BuildExportPath();
            changedValueCount = CountChangedValues(values);
            var contents = BuildContents(values, changedValueCount);
            WriteAtomically(exportPath, contents);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = "The tuning export could not be written: " + ex.Message;
            return false;
        }
    }

    private static string BuildExportPath()
    {
        var configPath = Settings.ConfigFile.ConfigFilePath;
        var directory = Path.GetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(directory))
            directory = AppContext.BaseDirectory;

        var configName = Path.GetFileNameWithoutExtension(configPath);
        if (string.IsNullOrWhiteSpace(configName))
            configName = Plugin.PluginGuid;

        return Path.Combine(directory, configName + ExportFileSuffix);
    }

    private static int CountChangedValues(IReadOnlyDictionary<string, string> values)
    {
        var count = 0;
        foreach (var setting in SettingsCatalog.All)
        {
            var compiledDefault = SettingsCatalog.FormatValue(setting.Entry.DefaultValue);
            if (!string.Equals(values[setting.Id], compiledDefault, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    private static string BuildContents(IReadOnlyDictionary<string, string> values, int changedValueCount)
    {
        var builder = new StringBuilder(16384);
        builder.AppendLine("# Easy Red 2 Realism Overhaul default-tuning export");
        builder.Append("# PluginVersion = ").AppendLine(Plugin.PluginVersion);
        builder.Append("# ExportedUtc = ").AppendLine(DateTime.UtcNow.ToString("O"));
        builder.Append("# Settings = ").Append(SettingsCatalog.All.Count)
            .Append("; ChangedFromCompiledDefaults = ").AppendLine(changedValueCount.ToString());
        builder.AppendLine("# This is a complete, validated snapshot of the in-game settings draft.");
        builder.AppendLine("# It may include staged changes that were not applied to the active game.");
        builder.AppendLine("# Give this file to the project maintainer to promote its values to compiled defaults.");

        string? currentSection = null;
        foreach (var setting in SettingsCatalog.All)
        {
            var section = setting.Entry.Definition.Section;
            if (!string.Equals(section, currentSection, StringComparison.Ordinal))
            {
                builder.AppendLine();
                builder.Append('[').Append(section).AppendLine("]");
                currentSection = section;
            }

            var value = values[setting.Id];
            var compiledDefault = SettingsCatalog.FormatValue(setting.Entry.DefaultValue);
            if (!string.Equals(value, compiledDefault, StringComparison.Ordinal))
                builder.Append("# CompiledDefault = ").AppendLine(compiledDefault);
            builder.Append(setting.Entry.Definition.Key).Append(" = ").AppendLine(value);
        }

        return builder.ToString();
    }

    private static void WriteAtomically(string exportPath, string contents)
    {
        var directory = Path.GetDirectoryName(exportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = exportPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, contents, new UTF8Encoding(false));
            File.Move(temporaryPath, exportPath, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // A stale temporary file is harmless and must not hide the original export error.
            }
        }
    }
}
