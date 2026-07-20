using System.Globalization;
using BepInEx.Configuration;

namespace ER2RealismOverhaul;

internal sealed class SettingsDraft
{
    private readonly Dictionary<string, string> _baseline = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    internal SettingsDraft()
    {
        foreach (var setting in SettingsCatalog.All)
        {
            var serialized = setting.Entry.GetSerializedValue();
            _baseline[setting.Id] = serialized;
            _values[setting.Id] = serialized;
        }
    }

    internal bool IsDirty => _values.Any(pair => !_baseline.TryGetValue(pair.Key, out var baseline) || baseline != pair.Value);

    internal string Get(MenuSetting setting) => _values[setting.Id];

    internal void Set(MenuSetting setting, string value) => _values[setting.Id] = value;

    internal void Reset(MenuSetting setting) => _values[setting.Id] = SettingsCatalog.FormatValue(setting.Entry.DefaultValue);

    internal void ResetCategory(SettingsMenuCategory category)
    {
        foreach (var setting in SettingsCatalog.All.Where(setting => IsInCategory(setting, category)))
            Reset(setting);
    }

    internal void ResetAll()
    {
        foreach (var setting in SettingsCatalog.All)
            Reset(setting);
    }

    internal bool TryGetNormalizedValues(out IReadOnlyDictionary<string, string> values, out string error)
    {
        var parsed = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var setting in SettingsCatalog.All)
        {
            if (!TryParse(setting.Entry, _values[setting.Id], out var value, out error))
            {
                error = $"{setting.SectionName} / {setting.DisplayName}: {error}";
                values = new Dictionary<string, string>();
                return false;
            }

            var acceptable = setting.Entry.Description.AcceptableValues;
            if (acceptable != null)
                value = acceptable.Clamp(value);
            parsed[setting.Id] = value;
        }

        if (!ValidateRelationships(parsed, out error))
        {
            values = new Dictionary<string, string>();
            return false;
        }

        values = parsed.ToDictionary(
            pair => pair.Key,
            pair => SettingsCatalog.FormatValue(pair.Value),
            StringComparer.Ordinal);
        return true;
    }

    internal bool Apply(out string error)
    {
        if (!TryGetNormalizedValues(out var values, out error))
            return false;

        var config = Settings.ConfigFile;
        var oldValues = SettingsCatalog.All.ToDictionary(setting => setting.Id, setting => setting.Entry.GetSerializedValue());
        var saveOnSet = config.SaveOnConfigSet;

        try
        {
            config.SaveOnConfigSet = false;
            foreach (var setting in SettingsCatalog.All)
            {
                var normalized = values[setting.Id];
                setting.Entry.SetSerializedValue(normalized);
                _values[setting.Id] = normalized;
            }

            if (SettingsSyncController.ShouldPersistAppliedSettings)
                config.Save();

            foreach (var setting in SettingsCatalog.All)
                _baseline[setting.Id] = _values[setting.Id];

            SettingsSyncController.NotifySettingsChanged();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            foreach (var setting in SettingsCatalog.All)
                setting.Entry.SetSerializedValue(oldValues[setting.Id]);
            error = "The settings could not be applied: " + ex.Message;
            return false;
        }
        finally
        {
            config.SaveOnConfigSet = saveOnSet;
        }
    }

    internal static bool IsInCategory(MenuSetting setting, SettingsMenuCategory category)
    {
        return category == SettingsMenuCategory.QuickSetup ? setting.IsQuickSetup : setting.Category == category;
    }

    private static bool TryParse(ConfigEntryBase entry, string text, out object value, out string error)
    {
        text = text.Trim();
        if (entry.SettingType == typeof(bool) && bool.TryParse(text, out var boolean))
        {
            value = boolean;
            error = string.Empty;
            return true;
        }

        if (entry.SettingType == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            value = integer;
            error = string.Empty;
            return true;
        }

        if (entry.SettingType == typeof(float) &&
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) &&
            !float.IsNaN(single) && !float.IsInfinity(single))
        {
            value = single;
            error = string.Empty;
            return true;
        }

        value = entry.DefaultValue;
        error = entry.SettingType == typeof(bool)
            ? "enter true or false."
            : "enter a valid number using a decimal point.";
        return false;
    }

    private static bool ValidateRelationships(IReadOnlyDictionary<string, object> parsed, out string error)
    {
        if (!LessOrEqual(parsed, "AI - Infantry tactics - Perception", "CloseTargetAcquisitionSeconds", "DistantTargetAcquisitionSeconds"))
        {
            error = "Close target-acquisition time cannot exceed distant target-acquisition time.";
            return false;
        }

        if (!LessOrEqual(parsed, "AI - Infantry tactics - Combat safety", "GrenadeMinimumRangeMeters", "GrenadeMaximumRangeMeters"))
        {
            error = "Grenade minimum range cannot exceed grenade maximum range.";
            return false;
        }

        if (!Less(parsed, "AI - Infantry tactics - Danger", "ProneReleaseSuppressionThreshold", "ProneSuppressionThreshold"))
        {
            error = "Prone release suppression must be lower than prone suppression.";
            return false;
        }

        if (!Less(parsed, "AI - Infantry tactics - Danger", "CrouchSuppressionThreshold", "ProneSuppressionThreshold"))
        {
            error = "Crouch suppression must be lower than prone suppression.";
            return false;
        }

        if (!Less(parsed, "AI - Infantry tactics - Danger", "MountedGunnerRiseSuppressionThreshold", "MountedGunnerDuckSuppressionThreshold"))
        {
            error = "Mounted-gunner rise suppression must be lower than duck suppression.";
            return false;
        }

        if (!LessOrEqual(parsed, "AI - Infantry tactics - Battle chatter", "RoutineMinimumIntervalSeconds", "RoutineMaximumIntervalSeconds"))
        {
            error = "Routine chatter minimum interval cannot exceed its maximum interval.";
            return false;
        }

        if (!Less(parsed, "7d. Audio balance", "DistantSoundStartDistanceMeters", "DistantSoundFullEffectDistanceMeters"))
        {
            error = "Distant-sound start distance must be lower than its full-effect distance.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool Less(IReadOnlyDictionary<string, object> parsed, string section, string leftKey, string rightKey) =>
        Number(parsed, section, leftKey) < Number(parsed, section, rightKey);

    private static bool LessOrEqual(IReadOnlyDictionary<string, object> parsed, string section, string leftKey, string rightKey) =>
        Number(parsed, section, leftKey) <= Number(parsed, section, rightKey);

    private static double Number(IReadOnlyDictionary<string, object> parsed, string section, string key)
    {
        var id = section + "\u001f" + key;
        return parsed.TryGetValue(id, out var value) ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : 0d;
    }
}
