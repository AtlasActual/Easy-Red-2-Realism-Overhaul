using System.Globalization;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using UnityEngine;

namespace ER2RealismOverhaul;

internal enum SettingsMenuCategory
{
    QuickSetup,
    AttackPostureBonuses,
    Defense,
    InfantryTactics,
    VehicleTactics,
    SupportCoordination,
    AiDiagnostics,
    BalanceAndAi,
    Infantry,
    Vehicles,
    Aircraft,
    Battlefield,
    Audio,
    PlayerExperience,
    VisualsAndAnimation,
    Advanced,
    Diagnostics
}

internal sealed class MenuSetting
{
    internal MenuSetting(ConfigEntryBase entry, SettingsMenuCategory category, bool quickSetup)
    {
        Entry = entry;
        Category = category;
        IsQuickSetup = quickSetup;
        Id = entry.Definition.Section + "\u001f" + entry.Definition.Key;
        SectionName = SettingsCatalog.CleanSectionName(entry.Definition.Section);
        DisplayName = entry.Definition.Key == "Enabled"
            ? SectionName
            : SettingsCatalog.Humanize(entry.Definition.Key);
        Description = entry.Description.Description ?? string.Empty;
        Unit = SettingsCatalog.InferUnit(entry.Definition.Key);
        (LowerEffect, HigherEffect) = SettingsCatalog.DirectionFor(
            entry.Definition.Section,
            entry.Definition.Key);
    }

    internal ConfigEntryBase Entry { get; }
    internal SettingsMenuCategory Category { get; }
    internal bool IsQuickSetup { get; }
    internal string Id { get; }
    internal string SectionName { get; }
    internal string DisplayName { get; }
    internal string Description { get; }
    internal string Unit { get; }
    internal string LowerEffect { get; }
    internal string HigherEffect { get; }

    internal bool Matches(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               SectionName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               Entry.Definition.Key.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               LowerEffect.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               HigherEffect.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class SettingsCatalog
{
    private static readonly HashSet<string> NonSystemSwitchIds = new(StringComparer.Ordinal)
    {
        // These govern menu availability, diagnostic output, or presentation format;
        // they are not overhaul systems and must remain usable after Disable All.
        "6c. Aircraft flight physics\u001fTelemetryLogging",
        "6d. Aircraft instruments\u001fUseKnotsAndFeet",
        "6d. Aircraft instruments\u001fShowAltitudeAboveGround",
        "7e. First-person view\u001fCompassUseMils",
        "7i. Settings menu\u001fShowLauncherButton",
        "AI - Diagnostics\u001fVisualDebugStartEnabled",
        "AI - Diagnostics\u001fVerboseLogging",
        "AI - Diagnostics\u001fIncrementalGarbageCollection"
    };

    // Config-only knobs: bound so they are saved/loadable from the config file, but
    // deliberately not surfaced in the in-game F10 menu (tuning too narrow/internal
    // to expose there; see plan 015).
    private static readonly HashSet<string> ConfigOnlyIds = new(StringComparer.Ordinal)
    {
        "AI - Infantry tactics - Danger\u001fMaximumPinnedSeconds",
        "AI - Infantry tactics - Danger\u001fPinnedImmunitySeconds",
        // Only meaningful at load time, so it must never look like a live toggle:
        // keeping it out of All also keeps it out of Disable All and settings sync.
        "AI - Diagnostics\u001fInstallGameplayPatches"
    };

    private static readonly Regex SectionPrefix = new(@"^\d+[a-z]?\.\s*", RegexOptions.Compiled);
    private static readonly Regex WordBoundary = new(@"(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);
    // Broadly useful controls are also exposed in Quick Setup. Unified AI sections keep
    // all of their detailed controls together on their doctrine page; unrelated detailed
    // tuning still defaults to Advanced until deliberately promoted into this list.
    private static readonly HashSet<string> PrimarySettingIds = new(StringComparer.Ordinal)
    {
        "AI - Attack posture bonuses\u001fAttackPostureBonusesEnabled",
        "AI - Attack posture bonuses\u001fAttackPostureAccuracySpreadMultiplier",
        "AI - Attack posture bonuses\u001fAttackPostureSuppressionReceivedMultiplier",

        "AI - Infantry tactics - Perception\u001fEnabled",
        "AI - Infantry tactics - Perception\u001fHorizontalFovDegrees",
        "AI - Infantry tactics - Perception\u001fDistantTargetAcquisitionSeconds",
        "AI - Infantry tactics - Perception\u001fTargetMemorySeconds",

        "AI - Infantry tactics - Contact response\u001fEnabled",
        "AI - Infantry tactics - Contact response\u001fImmediateFireDistanceMeters",
        "AI - Infantry tactics - Contact response\u001fCoverSearchRadiusMeters",
        "AI - Infantry tactics - Contact response\u001fEngagementHaltDistanceMeters",
        "AI - Infantry tactics - Contact response\u001fMaximumAttackCombatHaltSeconds",
        "AI - Infantry tactics - Contact response\u001fSuppressKnownTargets",

        "AI - Infantry tactics - Moving fire\u001fRestrictMovingFire",
        "AI - Infantry tactics - Moving fire\u001fSmgMaximumEngagementDistanceMeters",
        "AI - Infantry tactics - Moving fire\u001fLauncherMaximumEngagementDistanceMeters",

        "AI - Infantry tactics - Suppression\u001fEnabled",

        "AI - Infantry tactics - Danger\u001fEnabled",
        "AI - Infantry tactics - Danger\u001fProneSuppressionThreshold",
        "AI - Infantry tactics - Danger\u001fPinnedMinimumSeconds",
        "AI - Infantry tactics - Danger\u001fMountedGunnerSuppressionDuck",

        "AI - Infantry tactics - Casualty suppression\u001fEnabled",
        "AI - Infantry tactics - Casualty suppression\u001fWoundRadiusMeters",
        "AI - Infantry tactics - Casualty suppression\u001fWoundSuppression",
        "AI - Infantry tactics - Casualty suppression\u001fDeathRadiusMeters",
        "AI - Infantry tactics - Casualty suppression\u001fDeathSuppression",

        "AI - Infantry tactics - Combat safety\u001fFriendlyFireChecks",
        "AI - Infantry tactics - Combat safety\u001fSafeAiGrenadeThrows",
        "AI - Infantry tactics - Combat safety\u001fGrenadeMaximumRangeMeters",

        "AI - Infantry tactics - Movement\u001fPreventReloadingAndBandagingWhileCrawling",

        "2d. Melee combat\u001fImprovedHitRegistration",
        "2d. Melee combat\u001fForwardReachExtensionMeters",
        "2d. Melee combat\u001fMinimumSweepRadiusMeters",

        "AI - Vehicle tactics\u001fEnabled",
        "AI - Vehicle tactics\u001fStopAndEngageDistanceMeters",
        "AI - Vehicle tactics\u001fReverseDistanceMeters",
        "AI - Vehicle tactics\u001fDamagedLifeFraction",

        "4a. Tank physics\u001fAccelerationMultiplier",

        "AI - Defense\u001fEnabled",
        "AI - Defense\u001fWeaponSearchRadiusMeters",
        "AI - Defense\u001fEnemyTankResponseRangeMeters",
        "AI - Defense\u001fStaffAbandonedVehicleGuns",
        "AI - Defense\u001fEngageInfantryWhenNoVehicleTarget",

        "AI - Support coordination - Smoke\u001fExtraSmokeRequestsEnabled",
        "AI - Support coordination - Smoke\u001fRequestChance",

        "6. Ordnance effects\u001fLongerFireMissions",
        "6. Ordnance effects\u001fLongerSmokeEffects",
        "6. Ordnance effects\u001fLongerExplosionDust",
        "6. Ordnance effects\u001fGrenadeExplosionVisualScale",
        "6. Ordnance effects\u001fLargerAircraftBombBlast",
        "6. Ordnance effects\u001fLargerHeavyOrdnanceCraters",
        "6. Ordnance effects\u001fLayeredBlastEffects",
        "6. Ordnance effects\u001fEnhancedFragmentation",
        "6. Ordnance effects\u001fSmallExplosionAiThrowForceMultiplier",

        "6c. Aircraft flight physics\u001fFreeLookSteering",
        "6c. Aircraft flight physics\u001fEnabled",
        "6c. Aircraft flight physics\u001fRealismStrength",
        "6c. Aircraft flight physics\u001fWorldSpeedScale",
        "6c. Aircraft flight physics\u001fEnginePowerMultiplier",
        "6c. Aircraft flight physics\u001fEnergyRetentionEnabled",
        "6c. Aircraft flight physics\u001fProgressiveStalls",
        "6c. Aircraft flight physics\u001fDamageAffectsHandling",

        "6d. Aircraft instruments\u001fEnabled",
        "6d. Aircraft instruments\u001fHudScale",
        "6d. Aircraft instruments\u001fUseKnotsAndFeet",
        "6d. Aircraft instruments\u001fShowAltitudeAboveGround",

        "6e. Bullet penetration\u001fEnabled",
        "6e. Bullet penetration\u001fOrdinaryRoundPenetrationStrength",
        "6e. Bullet penetration\u001fArmorPiercingPropPenetrationStrength",
        "6e. Bullet penetration\u001fMaximumPropPenetrations",
        "6e. Bullet penetration\u001fAddedSmallArmsRicochets",
        "6e. Bullet penetration\u001fAddedRicochetChanceMultiplier",

        "7. Weapon presentation\u001fMachineGunOnlyTracers",
        "7. Weapon presentation\u001fHitDecalDurationSeconds",

        "7a. Player suppression effects\u001fVignetteMultiplier",
        "7a. Player suppression effects\u001fWeaponWobbleMultiplier",
        "7a. Player suppression effects\u001fNearMissRadiusMultiplier",
        "7a. Player suppression effects\u001fBlurEnabled",
        "7a. Player suppression effects\u001fBlurStrength",
        "7a. Player suppression effects\u001fShowDirectionMarker",

        "7b. AI animation restraint\u001fLeaderOnlyOrderGestures",


        "AI - Infantry tactics - Battle chatter\u001fEnabled",

        "7d. Audio balance\u001fEnabled",
        "7d. Audio balance\u001fVehicleEngineSound",
        "7d. Audio balance\u001fTankTrackVolumeMultiplier",
        "7d. Audio balance\u001fWeaponFireVolumeMultiplier",
        "7d. Audio balance\u001fTankGunVolumeMultiplier",
        "7d. Audio balance\u001fPlayerFootstepVolumeMultiplier",

        "7e. First-person view\u001fPlayerShadowEnabled",
        "7e. First-person view\u001fHoldBreathZoomMultiplier",
        "7e. First-person view\u001fBinocularsEnabled",
        "7e. First-person view\u001fBinocularsKey",
        "7e. First-person view\u001fBinocularZoomMultiplier",
        "7e. First-person view\u001fFreeLookEnabled",
        "7e. First-person view\u001fFreeLookKey",
        "7e. First-person view\u001fFreeLookHorizontalArcDegrees",
        "7e. First-person view\u001fCompassAlwaysVisible",
        "7e. First-person view\u001fCompassKey",
        "7e. First-person view\u001fCompassUseMils",
        "7e. First-person view\u001fHeadshotDeathBlackout",

        "7f. Multiplayer nameplates\u001fKeepPlayerNamesWithHudDisabled",

        "AI - Diagnostics\u001fVisualDebugStartEnabled",
        "AI - Diagnostics\u001fVisualDebugToggleKey",
        "AI - Diagnostics\u001fVisualDebugMaximumDistanceMeters",
        "AI - Diagnostics\u001fVisualDebugMaximumActors",
        "AI - Diagnostics\u001fVisualDebugEventHistorySeconds",
        "AI - Diagnostics\u001fVerboseLogging"
    };

    internal static IReadOnlyList<MenuSetting> All { get; private set; } = Array.Empty<MenuSetting>();

    internal static void Initialize()
    {
        var quickSections = new HashSet<string>(StringComparer.Ordinal);
        var settings = new List<MenuSetting>();

        foreach (var entry in Settings.GetConfigEntries())
        {
            if (entry.SettingType != typeof(bool) && entry.SettingType != typeof(int) &&
                entry.SettingType != typeof(float) && entry.SettingType != typeof(KeyCode))
                continue;
            if (IsMigrationOnlyEntry(entry))
                continue;

            var id = entry.Definition.Section + "\u001f" + entry.Definition.Key;
            if (ConfigOnlyIds.Contains(id))
                continue;

            var primary = PrimarySettingIds.Contains(id);
            var quick = primary && entry.SettingType == typeof(bool) &&
                        quickSections.Add(entry.Definition.Section);
            // Presentation-facing controls are already narrow, user-facing groups. Keep
            // their detailed controls with the rest of their section instead of burying
            // sound shaping, chatter timing, or tracer tuning in Advanced.
            var category = primary || HasDedicatedCategory(entry.Definition.Section)
                ? CategoryFor(entry.Definition.Section)
                : SettingsMenuCategory.Advanced;
            settings.Add(new MenuSetting(entry, category, quick));
        }

        All = settings;
    }

    internal static bool IsSystemSwitch(MenuSetting setting) =>
        setting.Entry.SettingType == typeof(bool) && !NonSystemSwitchIds.Contains(setting.Id);

    internal static string CategoryName(SettingsMenuCategory category) => category switch
    {
        SettingsMenuCategory.QuickSetup => "Quick Setup",
        SettingsMenuCategory.AttackPostureBonuses => "AI / Attack Bonuses",
        SettingsMenuCategory.Defense => "AI / Defense",
        SettingsMenuCategory.InfantryTactics => "AI / Infantry Tactics",
        SettingsMenuCategory.VehicleTactics => "AI / Vehicle Tactics",
        SettingsMenuCategory.SupportCoordination => "AI / Support",
        SettingsMenuCategory.AiDiagnostics => "AI / Diagnostics",
        SettingsMenuCategory.BalanceAndAi => "Balance & AI",
        SettingsMenuCategory.Infantry => "Infantry",
        SettingsMenuCategory.Vehicles => "Vehicles",
        SettingsMenuCategory.Aircraft => "Aircraft",
        SettingsMenuCategory.Battlefield => "Battlefield",
        SettingsMenuCategory.Audio => "Audio",
        SettingsMenuCategory.PlayerExperience => "Player Experience",
        SettingsMenuCategory.VisualsAndAnimation => "Visuals & Animation",
        SettingsMenuCategory.Advanced => "Advanced",
        SettingsMenuCategory.Diagnostics => "Diagnostics",
        _ => category.ToString()
    };

    internal static string CleanSectionName(string section)
    {
        if (section.Equals("AI - Attack posture bonuses", StringComparison.Ordinal))
            return "Attack Posture Bonuses";
        if (section.Equals("AI - Defense", StringComparison.Ordinal))
            return "Defense";
        if (section.Equals("AI - Infantry tactics", StringComparison.Ordinal))
            return "Infantry Tactics";
        if (section.StartsWith("AI - Infantry tactics - ", StringComparison.Ordinal))
            return Capitalize(section[24..]);
        if (section.Equals("AI - Vehicle tactics", StringComparison.Ordinal))
            return "Vehicle Tactics";
        if (section.Equals("AI - Support coordination", StringComparison.Ordinal))
            return "Support Coordination";
        if (section.StartsWith("AI - Support coordination - ", StringComparison.Ordinal))
            return Capitalize(section[28..]);
        if (section.Equals("AI - Diagnostics", StringComparison.Ordinal))
            return "Diagnostics";

        return SectionPrefix.Replace(section, string.Empty);
    }

    internal static string Humanize(string key)
    {
        var text = WordBoundary.Replace(key, " ");
        return text
            .Replace("Fov", "FOV", StringComparison.Ordinal)
            .Replace("Hud", "HUD", StringComparison.Ordinal)
            .Replace("Ai ", "AI ", StringComparison.Ordinal)
            .Replace("Smg", "SMG", StringComparison.Ordinal)
            .Replace("At ", "AT ", StringComparison.Ordinal)
            .Replace("Hz", "Hz", StringComparison.Ordinal);
    }

    internal static string HumanizeKeyCode(string keyCode)
    {
        var text = WordBoundary.Replace(keyCode, " ");
        return text
            .Replace("Alpha ", string.Empty, StringComparison.Ordinal)
            .Replace("Keypad", "Keypad ", StringComparison.Ordinal);
    }

    internal static string FormatValue(object value)
    {
        return value switch
        {
            bool boolean => boolean ? "true" : "false",
            float single => single.ToString("0.###", CultureInfo.InvariantCulture),
            int integer => integer.ToString(CultureInfo.InvariantCulture),
            KeyCode keyCode => keyCode.ToString(),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    internal static string InferUnit(string key)
    {
        if (key is "VehicleEngineSound")
            return "x";
        if (key.Contains("Seconds", StringComparison.OrdinalIgnoreCase))
            return "s";
        if (key.Contains("Multiplier", StringComparison.OrdinalIgnoreCase))
            return "x";
        if (key.Contains("Distance", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Range", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Radius", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Spacing", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Margin", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Offset", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Length", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Gap", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Step", StringComparison.OrdinalIgnoreCase))
            return "m";
        if (key.Contains("Fov", StringComparison.OrdinalIgnoreCase) || key.Contains("Angle", StringComparison.OrdinalIgnoreCase))
            return "deg";
        if (key.EndsWith("Mm", StringComparison.OrdinalIgnoreCase))
            return "mm";
        if (key.Contains("Hz", StringComparison.OrdinalIgnoreCase))
            return "Hz";
        if (key.Contains("Chance", StringComparison.OrdinalIgnoreCase) || key.Contains("Fraction", StringComparison.OrdinalIgnoreCase))
            return "0-1";
        return string.Empty;
    }

    internal static (string Lower, string Higher) DirectionFor(string section, string key)
    {
        if (key == "SquadCooldownSeconds")
        {
            return section.StartsWith("5.", StringComparison.Ordinal)
                ? ("request smoke sooner", "request smoke later")
                : ("squad talks more", "squad talks less");
        }

        return key switch
        {
        "AttackPostureAccuracySpreadMultiplier" or
        "AttackPostureTankAccuracySpreadMultiplier" => ("more accurate", "less accurate"),
        "AttackPostureSuppressionReceivedMultiplier" => ("harder to pin", "easier to pin"),

        "HorizontalFovDegrees" => ("narrower vision", "wider vision"),
        "CloseTargetAcquisitionSeconds" or
        "DistantTargetAcquisitionSeconds" => ("faster spotting", "slower spotting"),
        "DistantTargetAcquisitionRangeMeters" => ("delay starts closer", "delay starts farther"),
        "TargetMemorySeconds" => ("forget sooner", "remember longer"),
        "PeripheralAwarenessDistance" => ("less side awareness", "more side awareness"),

        "PointBlankAcquisitionSeconds" => ("faster point-blank ID", "slower point-blank ID"),
        "MinimumPeripheralAwarenessMeters" => ("smaller awareness floor", "larger awareness floor"),
        "CloseQuartersRangeMeters" => ("shorter tight-spread range", "longer tight-spread range"),
        "SpreadMultiplierAtPointBlank" => ("deadlier up close", "gentler up close"),

        "ImmediateFireDistanceMeters" => ("instant fire closer", "instant fire farther"),
        "CoverSearchRadiusMeters" => ("search nearer cover", "search farther cover"),
        "EngagementHaltDistanceMeters" => ("halt only closer", "halt from farther"),
        "MaximumAttackCombatHaltSeconds" => ("push sooner under fire", "hold and fire longer"),
        "MountedGunnerRiseSettleSeconds" => ("fire sooner", "wait longer to fire"),
        "SmgMovingFireMaxDistanceMeters" or
        "RifleMovingFireMaxDistanceMeters" => ("moving fire closer", "moving fire farther"),
        "SmgMaximumEngagementDistanceMeters" => ("SMG fires closer", "SMG fires farther"),
        "ForwardReachExtensionMeters" => ("shorter melee reach", "longer melee reach"),
        "MinimumSweepRadiusMeters" => ("narrower melee sweep", "wider melee sweep"),

        "FovMultiplierAtMaximumSuppression" => ("narrower when pinned", "wider when pinned"),
        "PeripheralMultiplierAtMaximumSuppression" => ("less side awareness", "more side awareness"),
        "MemoryMultiplierAtMaximumSuppression" => ("forget faster pinned", "remember longer pinned"),

        "CrouchSuppressionThreshold" => ("crouch sooner", "crouch later"),
        "ProneSuppressionThreshold" => ("go prone sooner", "go prone later"),
        "ProneReleaseSuppressionThreshold" => ("stay prone longer", "rise sooner"),
        "PinnedMinimumSeconds" => ("shorter pinned time", "longer pinned time"),
        "FlameSafetyMarginMeters" => ("approach flames closer", "keep farther away"),
        "FlameEscapeDistanceMeters" => ("shorter escape move", "longer escape move"),
        "MountedGunnerDuckSuppressionThreshold" => ("gunner ducks sooner", "gunner ducks later"),
        "MountedGunnerRiseSuppressionThreshold" => ("stay ducked longer", "gunner rises sooner"),
        "MountedGunnerMinimumDuckSeconds" => ("shorter duck time", "longer duck time"),

        "HandheldLaneRadiusMeters" or
        "MountedLaneRadiusMeters" => ("less fire blocking", "more fire blocking"),
        "GrenadeMinimumRangeMeters" => ("allow closer throws", "require farther target"),
        "GrenadeMaximumRangeMeters" => ("shorter throws only", "allow longer throws"),
        "GrenadeFriendlySafetyRadiusMeters" => ("less ally clearance", "more ally clearance"),
        "GrenadeCooldownSeconds" => ("throw more often", "throw less often"),

        "AwarenessDistanceMeters" => ("react only closer", "react from farther"),
        "RetreatDistanceMeters" => ("hide only closer", "hide from farther"),
        "EscapeMoveMeters" => ("smaller cover search", "larger cover search"),
        "LauncherMaximumEngagementDistanceMeters" => ("launchers fire closer", "launchers fire farther"),
        "StopAndEngageDistanceMeters" => ("stop only closer", "stop from farther"),
        "ReverseDistanceMeters" => ("reverse only closer", "reverse from farther"),
        "ReverseDurationSeconds" => ("shorter reverse", "longer reverse"),
        "DamagedLifeFraction" => ("retreat only badly hurt", "retreat while healthier"),
        "MaximumHullFacingAngleDegrees" => ("stricter frontal aim", "looser frontal aim"),
        "HoldPositionAgainstInfantryMeters" => ("hold only closer", "hold from farther"),
        "AccelerationMultiplier" => ("slower tank acceleration", "faster tank acceleration"),
        "WeaponSearchRadiusMeters" => ("nearby guns only", "search farther guns"),
        "EnemyTankResponseRangeMeters" => ("urgent only closer", "urgent from farther"),
        "AssignmentCooldownSeconds" => ("check more often", "check less often"),
        "MinimumGunCaliberMm" => ("more guns qualify", "only heavier guns"),

        "RequestChance" => ("less extra smoke", "more extra smoke"),
        "MinimumTargetDistanceMeters" => ("allow smoke closer", "keep smoke farther"),

        "ArtilleryRoundCountMultiplier" => ("fewer rounds", "more rounds"),
        "SmokeLifetimeMultiplier" => ("smoke clears sooner", "smoke lasts longer"),
        "ExplosionDustLifetimeMultiplier" => ("dust clears sooner", "dust lasts longer"),
        "GrenadeExplosionVisualScale" => ("smaller visual", "larger visual"),
        "AircraftBombBlastRadiusMultiplier" => ("smaller bomb blast", "larger bomb blast"),
        "ArtilleryCraterRadiusMultiplier" or
        "AircraftBombCraterRadiusMultiplier" => ("smaller craters", "larger craters"),
        "FragmentRadiusMultiplier" => ("shorter fragment reach", "longer fragment reach"),
        "ExtraFragmentChecksPerTarget" => ("fewer fragment rays", "more fragment rays"),
        "ExtraFragmentDamageMultiplier" => ("weaker fragments", "stronger fragments"),
        "SmallExplosionAiThrowForceMultiplier" => ("less body throw", "more body throw"),
        "GeneralExplosionSuppressionRadiusMultiplier" or
        "MortarSuppressionRadiusMultiplier" or
        "ArtillerySuppressionRadiusMultiplier" or
        "AircraftBombSuppressionRadiusMultiplier" => ("smaller suppression area", "larger suppression area"),
        "GeneralExplosionSuppression" or
        "MortarSuppression" or
        "ArtillerySuppression" or
        "AircraftBombSuppression" => ("less suppression", "more suppression"),
        "MortarInjuryRadiusMultiplier" or
        "ArtilleryInjuryRadiusMultiplier" or
        "AircraftBombInjuryRadiusMultiplier" => ("smaller injury area", "larger injury area"),
        "MortarOuterDamage" or
        "ArtilleryOuterDamage" or
        "AircraftBombOuterDamage" => ("less outer damage", "more outer damage"),
        "BlastCoverEffectMultiplier" => ("cover protects more", "cover protects less"),

        "RealismStrength" => ("more native handling", "stronger realism forces"),
        "WorldSpeedScale" => ("slower aircraft", "faster aircraft"),
        "FighterSpeedMultiplier" => ("slower fighters", "faster fighters"),
        "BomberSpeedMultiplier" => ("slower bombers", "faster bombers"),
        "NativeControlResponseMultiplier" => ("slower controls", "more immediate controls"),
        "EngineResponseTimeMultiplier" => ("faster thrust response", "slower thrust response"),
        "EnginePowerMultiplier" => ("less engine thrust", "more engine thrust"),
        "ThrottleReductionResponseMultiplier" => ("quicker power reduction", "slower power reduction"),
        "ManeuverEnergyLossMultiplier" => ("retain more energy", "lose more energy"),
        "NativeCoastDragMultiplier" => ("coast longer", "slow down faster"),
        "NativeVelocityLossMultiplier" => ("preserve physical momentum", "restore stock speed loss"),
        "GlideEnergyLossMultiplier" => ("flatter glide", "steeper glide"),
        "MaximumEnergyRetentionAcceleration" => ("weaker retention correction", "stronger retention correction"),
        "StallRecoveryPitchAuthority" => ("less pitch control", "more pitch control"),
        "StallRecoveryRollAuthority" => ("less roll control", "more roll control"),
        "StallNoseDropStrength" => ("gentler nose drop", "stronger nose drop"),
        "SpinStrength" => ("weaker spin forces", "stronger spin forces"),
        "SpinRecoverySpeedMultiplier" => ("recover at lower speed", "need more recovery speed"),
        "TelemetryIntervalSeconds" => ("log more often", "log less often"),
        "HudScale" => ("smaller instrument HUD", "larger instrument HUD"),

        "MachineGunTracerRetention" => ("fewer tracers", "more tracers"),
        "HitDecalDurationSeconds" => ("decals clear sooner", "decals stay longer"),
        "VignetteMultiplier" => ("weaker dark vignette", "stronger dark vignette"),
        "WeaponWobbleMultiplier" => ("less weapon wobble", "more weapon wobble"),
        "NearMissRadiusMultiplier" => ("smaller near-miss radius", "larger near-miss radius"),
        "BlurStrength" => ("weaker suppression blur", "stronger suppression blur"),
        "WoundRadiusMeters" => ("wounds affect closer AI", "wounds affect farther AI"),
        "WoundSuppression" => ("weaker wound shock", "stronger wound shock"),
        "DeathRadiusMeters" => ("deaths affect closer AI", "deaths affect farther AI"),
        "DeathSuppression" => ("weaker death shock", "stronger death shock"),
        "LeaderOrderGestureCooldownSeconds" => ("gesture more often", "gesture less often"),
        "IndividualCooldownSeconds" => ("soldier talks more", "soldier talks less"),
        "NewContactCalloutChance" or
        "RoutineCalloutChance" => ("fewer callouts", "more callouts"),
        "RoutineMinimumIntervalSeconds" => ("chatter can start sooner", "chatter starts later"),
        "RoutineMaximumIntervalSeconds" => ("shorter maximum wait", "longer maximum wait"),
        "VehicleEngineSound" => ("quieter vehicle engines", "louder vehicle engines"),
        "TankTrackVolumeMultiplier" => ("quieter tank tracks", "louder tank tracks"),
        "WeaponFireVolumeMultiplier" => ("quieter weapon fire", "louder weapon fire"),
        "TankGunVolumeMultiplier" => ("quieter tank guns", "louder tank guns"),
        "PlayerFootstepVolumeMultiplier" => ("quieter player footsteps", "louder player footsteps"),
        "HoldBreathZoomMultiplier" => ("weaker hold-breath zoom", "stronger hold-breath zoom"),
        "BinocularZoomMultiplier" => ("wider binocular view", "stronger binocular zoom"),
        "FreeLookHorizontalArcDegrees" => ("narrower freelook arc", "wider freelook arc"),
            _ => ("smaller effect", "larger effect")
        };
    }

    private static SettingsMenuCategory CategoryFor(string section)
    {
        if (section.StartsWith("AI - Attack posture", StringComparison.Ordinal))
            return SettingsMenuCategory.AttackPostureBonuses;
        if (section.StartsWith("AI - Defense", StringComparison.Ordinal))
            return SettingsMenuCategory.Defense;
        if (section.StartsWith("AI - Infantry tactics", StringComparison.Ordinal))
            return SettingsMenuCategory.InfantryTactics;
        if (section.StartsWith("AI - Vehicle tactics", StringComparison.Ordinal))
            return SettingsMenuCategory.VehicleTactics;
        if (section.StartsWith("AI - Support coordination", StringComparison.Ordinal))
            return SettingsMenuCategory.SupportCoordination;
        if (section.StartsWith("AI - Diagnostics", StringComparison.Ordinal))
            return SettingsMenuCategory.AiDiagnostics;

        if (section.StartsWith("0.", StringComparison.Ordinal) || section.StartsWith("1", StringComparison.Ordinal))
            return SettingsMenuCategory.BalanceAndAi;
        if (section.StartsWith("2", StringComparison.Ordinal) || section.StartsWith("3", StringComparison.Ordinal))
            return SettingsMenuCategory.Infantry;
        if (section.StartsWith("4", StringComparison.Ordinal))
            return SettingsMenuCategory.Vehicles;
        if (section.StartsWith("6c.", StringComparison.Ordinal) ||
            section.StartsWith("6d.", StringComparison.Ordinal))
            return SettingsMenuCategory.Aircraft;
        if (section.StartsWith("5", StringComparison.Ordinal) || section.StartsWith("6", StringComparison.Ordinal))
            return SettingsMenuCategory.Battlefield;
        if (section.StartsWith("7c.", StringComparison.Ordinal) ||
            section.StartsWith("7d.", StringComparison.Ordinal))
            return SettingsMenuCategory.Audio;
        if (section.StartsWith("7a.", StringComparison.Ordinal) ||
            section.StartsWith("7e.", StringComparison.Ordinal) ||
            section.StartsWith("7f.", StringComparison.Ordinal) ||
            section.StartsWith("7i.", StringComparison.Ordinal))
            return SettingsMenuCategory.PlayerExperience;
        if (section.StartsWith("7", StringComparison.Ordinal))
            return SettingsMenuCategory.VisualsAndAnimation;
        return SettingsMenuCategory.Diagnostics;
    }

    private static bool HasDedicatedCategory(string section) =>
        IsUnifiedAiSection(section) || section.StartsWith("7", StringComparison.Ordinal);

    private static bool IsUnifiedAiSection(string section) =>
        section.StartsWith("AI - Attack posture", StringComparison.Ordinal) ||
        section.StartsWith("AI - Defense", StringComparison.Ordinal) ||
        section.StartsWith("AI - Infantry tactics", StringComparison.Ordinal) ||
        section.StartsWith("AI - Vehicle tactics", StringComparison.Ordinal) ||
        section.StartsWith("AI - Support coordination", StringComparison.Ordinal) ||
        section.StartsWith("AI - Diagnostics", StringComparison.Ordinal);

    private static bool IsMigrationOnlyEntry(ConfigEntryBase entry) =>
        (entry.Description.Description ?? string.Empty).StartsWith("Legacy ", StringComparison.OrdinalIgnoreCase);

    private static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
