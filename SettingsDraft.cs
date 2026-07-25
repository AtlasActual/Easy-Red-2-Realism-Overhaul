using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class SettingsDraft
{
    private readonly Dictionary<string, string> _baseline = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    // Null = no staged change to the DisabledSwitchSnapshot config entry this session.
    // Non-null (including empty string) = a value to write on the next successful Apply.
    private string? _pendingSnapshot;

    internal SettingsDraft()
    {
        foreach (var setting in SettingsCatalog.All)
        {
            var serialized = setting.Entry.GetSerializedValue();
            _baseline[setting.Id] = serialized;
            _values[setting.Id] = serialized;
        }
    }

    internal bool IsDirty =>
        _values.Any(pair => !_baseline.TryGetValue(pair.Key, out var baseline) || baseline != pair.Value) ||
        (_pendingSnapshot != null && _pendingSnapshot != Settings.DisabledSwitchSnapshot.Value);

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

    /// <summary>
    /// Sets every boolean system switch together without disturbing numeric tuning,
    /// input preferences, or the always-available F10 settings-menu shortcut.
    /// Disabling stages a snapshot of the switches that were off beforehand so
    /// <see cref="EnableAllSwitches"/> can restore the user's curated mix later.
    /// </summary>
    internal int SetAllSwitches(bool enabled)
    {
        var value = SettingsCatalog.FormatValue(enabled);
        var falseValue = SettingsCatalog.FormatValue(false);

        List<string>? offIdsBeforeFlatten = null;
        if (!enabled)
        {
            offIdsBeforeFlatten = new List<string>();
            foreach (var setting in SettingsCatalog.All)
            {
                if (setting.Entry.SettingType != typeof(bool) || !SettingsCatalog.IsSystemSwitch(setting))
                    continue;
                if (_values[setting.Id] == falseValue)
                    offIdsBeforeFlatten.Add(setting.Id);
            }
        }

        var changed = 0;
        foreach (var setting in SettingsCatalog.All)
        {
            if (setting.Entry.SettingType != typeof(bool) || !SettingsCatalog.IsSystemSwitch(setting))
                continue;

            if (_values[setting.Id] != value)
                changed++;
            _values[setting.Id] = value;
        }

        // Only remember a new mix when it actually changed something; if everything
        // was already off, keep whatever snapshot (staged or persisted) already exists.
        if (!enabled && changed > 0)
            _pendingSnapshot = SerializeSnapshot(offIdsBeforeFlatten!);

        return changed;
    }

    /// <summary>
    /// Restores system switches from the staged-or-persisted DISABLE ALL snapshot,
    /// leaving the switches that were off at snapshot time off and turning every
    /// other system switch on. Falls back to enabling everything when no snapshot
    /// exists. <paramref name="restored"/> reports whether a snapshot was used.
    /// </summary>
    internal int EnableAllSwitches(out bool restored)
    {
        var effectiveSnapshot = _pendingSnapshot ?? Settings.DisabledSwitchSnapshot.Value;
        if (string.IsNullOrEmpty(effectiveSnapshot))
        {
            restored = false;
            return SetAllSwitches(true);
        }

        var offIds = ParseSnapshot(effectiveSnapshot);
        var trueValue = SettingsCatalog.FormatValue(true);
        var falseValue = SettingsCatalog.FormatValue(false);
        var changed = 0;
        foreach (var setting in SettingsCatalog.All)
        {
            if (setting.Entry.SettingType != typeof(bool) || !SettingsCatalog.IsSystemSwitch(setting))
                continue;

            var target = offIds.Contains(setting.Id) ? falseValue : trueValue;
            if (_values[setting.Id] != target)
                changed++;
            _values[setting.Id] = target;
        }

        _pendingSnapshot = string.Empty;
        restored = true;
        return changed;
    }

    private static string SerializeSnapshot(IEnumerable<string> offIds) => string.Join("\u001e", offIds);

    private static HashSet<string> ParseSnapshot(string snapshot) =>
        new(snapshot.Split('\u001e'), StringComparer.Ordinal);

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
        var oldSnapshotValue = Settings.DisabledSwitchSnapshot.GetSerializedValue();
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

            if (_pendingSnapshot != null)
                Settings.DisabledSwitchSnapshot.SetSerializedValue(_pendingSnapshot);

            if (SettingsSyncController.ShouldPersistAppliedSettings)
                config.Save();

            foreach (var setting in SettingsCatalog.All)
                _baseline[setting.Id] = _values[setting.Id];
            _pendingSnapshot = null;

            SettingsSyncController.NotifySettingsChanged();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            foreach (var setting in SettingsCatalog.All)
                setting.Entry.SetSerializedValue(oldValues[setting.Id]);
            Settings.DisabledSwitchSnapshot.SetSerializedValue(oldSnapshotValue);
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

        if (entry.SettingType == typeof(KeyCode) &&
            Enum.TryParse<KeyCode>(text, true, out var keyCode) &&
            Enum.IsDefined(keyCode))
        {
            value = keyCode;
            error = string.Empty;
            return true;
        }

        value = entry.DefaultValue;
        error = entry.SettingType == typeof(bool)
            ? "enter true or false."
            : entry.SettingType == typeof(KeyCode)
                ? "choose a valid key."
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
