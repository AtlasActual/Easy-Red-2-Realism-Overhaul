namespace ER2RealismOverhaul;

internal readonly record struct PerformancePresetApplyResult(
    int ChangedCount,
    int MissingCount,
    bool RestartRequired);

internal static class PerformancePresets
{
    private static readonly HashSet<string> RestartSensitiveIds = new(StringComparer.Ordinal)
    {
        Id("7d. Audio balance", "RaiseAudioVoiceCapacity"),
        Id("7d. Audio balance", "MinimumRealAudioVoices"),
        Id("7d. Audio balance", "MinimumVirtualAudioVoices")
    };

    private static Dictionary<string, MenuSetting> _settingsById = new(StringComparer.Ordinal);
    private static int _catalogCount = -1;

    internal static PerformancePresetApplyResult Apply(
        SettingsDraft draft,
        PerformancePresetKind preset)
    {
        RefreshCatalog();

        var missing = 0;
        foreach (var target in PerformancePresetCore.Targets)
        {
            if (!_settingsById.ContainsKey(target.Id))
                missing++;
        }

        if (missing > 0)
            return new PerformancePresetApplyResult(0, missing, false);

        var changed = 0;
        var restartRequired = false;
        foreach (var target in PerformancePresetCore.Targets)
        {
            var setting = _settingsById[target.Id];

            var targetValue = preset == PerformancePresetKind.Quality
                ? SettingsCatalog.FormatValue(setting.Entry.DefaultValue)
                : target.ValueFor(preset);
            if (draft.Get(setting) == targetValue)
                continue;

            draft.Set(setting, targetValue);
            changed++;
            restartRequired |= RestartSensitiveIds.Contains(target.Id);
        }

        return new PerformancePresetApplyResult(changed, missing, restartRequired);
    }

    internal static PerformancePresetKind? Detect(SettingsDraft draft)
    {
        RefreshCatalog();

        foreach (var preset in PerformancePresetCore.Presets)
        {
            if (Matches(draft, preset.Kind))
                return preset.Kind;
        }

        return null;
    }

    private static bool Matches(SettingsDraft draft, PerformancePresetKind preset)
    {
        foreach (var target in PerformancePresetCore.Targets)
        {
            if (!_settingsById.TryGetValue(target.Id, out var setting))
                return false;

            var expected = preset == PerformancePresetKind.Quality
                ? SettingsCatalog.FormatValue(setting.Entry.DefaultValue)
                : target.ValueFor(preset);
            if (draft.Get(setting) != expected)
                return false;
        }

        return true;
    }

    private static void RefreshCatalog()
    {
        if (_catalogCount == SettingsCatalog.All.Count)
            return;

        _settingsById = SettingsCatalog.All.ToDictionary(setting => setting.Id, StringComparer.Ordinal);
        _catalogCount = SettingsCatalog.All.Count;
    }

    private static string Id(string section, string key) => section + "\u001f" + key;
}
