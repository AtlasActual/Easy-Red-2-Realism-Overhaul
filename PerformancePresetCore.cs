namespace ER2RealismOverhaul;

internal enum PerformancePresetKind
{
    Quality,
    Balanced,
    LargeBattle
}

internal readonly record struct PerformancePresetInfo(
    PerformancePresetKind Kind,
    string Label,
    string Description);

internal readonly record struct PerformanceSettingTarget(
    string Section,
    string Key,
    string BalancedValue,
    string LargeBattleValue)
{
    internal string Id => Section + "\u001f" + Key;

    internal string ValueFor(PerformancePresetKind preset) => preset switch
    {
        PerformancePresetKind.Balanced => BalancedValue,
        PerformancePresetKind.LargeBattle => LargeBattleValue,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
    };
}

internal static class PerformancePresetCore
{
    internal static readonly PerformancePresetInfo[] Presets =
    {
        new(
            PerformancePresetKind.Quality,
            "QUALITY",
            "Recommended defaults: full distant animation, battlefield effects, ballistics, and audio."),
        new(
            PerformancePresetKind.Balanced,
            "BALANCED",
            "Keeps every gameplay system while trimming the costliest animation, effects, decals, and audio."),
        new(
            PerformancePresetKind.LargeBattle,
            "LARGE BATTLE",
            "Keeps core AI and fire safety, but prioritizes frame consistency during the largest battles.")
    };

    // These are deliberately limited to settings whose runtime cost is direct and
    // understandable. Core AI behavior, perception, cover, and friendly-fire safety
    // remain under the player's individual control in every performance preset.
    internal static readonly PerformanceSettingTarget[] Targets =
    {
        new("7h. Animation quality", "KeepHighQualityDistantAnimations", "false", "false"),

        new("6. Ordnance effects", "LongerFireMissions", "true", "false"),
        new("6. Ordnance effects", "ArtilleryRoundCountMultiplier", "1.35", "1"),
        new("6. Ordnance effects", "LongerSmokeEffects", "true", "false"),
        new("6. Ordnance effects", "SmokeLifetimeMultiplier", "3", "1"),
        new("6. Ordnance effects", "LongerExplosionDust", "true", "false"),
        new("6. Ordnance effects", "ExplosionDustLifetimeMultiplier", "1.5", "1"),
        new("6. Ordnance effects", "LargerAircraftBombBlast", "true", "false"),
        new("6. Ordnance effects", "LargerHeavyOrdnanceCraters", "true", "false"),
        new("6. Ordnance effects", "LayeredBlastEffects", "true", "false"),
        new("6. Ordnance effects", "EnhancedFragmentation", "true", "false"),
        new("6. Ordnance effects", "FragmentRadiusMultiplier", "1.2", "1"),
        new("6. Ordnance effects", "ExtraFragmentChecksPerTarget", "2", "1"),

        new("6e. Bullet penetration", "MaximumPropPenetrations", "8", "4"),
        new("6e. Bullet penetration", "AddedSmallArmsRicochets", "true", "false"),
        new("7. Weapon presentation", "HitDecalDurationSeconds", "15", "8"),

        new("7d. Audio balance", "MaximumLoopedWeaponSounds", "16", "7"),
        new("7d. Audio balance", "RaiseAudioVoiceCapacity", "true", "false"),
        new("7d. Audio balance", "MinimumRealAudioVoices", "128", "64"),
        new("7d. Audio balance", "MinimumVirtualAudioVoices", "256", "128"),

        new("AI - Diagnostics", "VisualDebugStartEnabled", "false", "false"),
        new("AI - Diagnostics", "VerboseLogging", "false", "false"),
        new("AI - Diagnostics", "StutterProbeEnabled", "false", "false"),
        new("AI - Diagnostics", "IncrementalGarbageCollection", "false", "true"),
        new("AI - Diagnostics", "IncrementalGarbageCollectionSliceMicroseconds", "400", "400")
    };
}
