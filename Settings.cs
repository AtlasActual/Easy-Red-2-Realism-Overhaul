using BepInEx.Configuration;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class Settings
{
    internal static ConfigFile ConfigFile { get; private set; } = null!;

    internal static ConfigEntry<bool> AttackingForceBonusEnabled = null!;
    internal static ConfigEntry<float> AttackingForceAccuracySpreadMultiplier = null!;
    internal static ConfigEntry<float> AttackingForceSuppressionReceivedMultiplier = null!;
    internal static ConfigEntry<float> AttackingTankAdditionalAccuracySpreadMultiplier = null!;

    internal static ConfigEntry<bool> PerceptionEnabled = null!;
    internal static ConfigEntry<float> HorizontalFov = null!;
    internal static ConfigEntry<float> CloseTargetAcquisitionSeconds = null!;
    internal static ConfigEntry<float> DistantTargetAcquisitionSeconds = null!;
    internal static ConfigEntry<float> DistantTargetAcquisitionRange = null!;
    internal static ConfigEntry<float> TargetMemorySeconds = null!;
    internal static ConfigEntry<float> PeripheralAwarenessDistance = null!;

    internal static ConfigEntry<bool> CloseQuartersEnabled = null!;
    internal static ConfigEntry<float> PointBlankAcquisitionSeconds = null!;
    internal static ConfigEntry<float> MinimumPeripheralAwarenessMeters = null!;
    internal static ConfigEntry<float> CloseQuartersRangeMeters = null!;
    internal static ConfigEntry<float> SpreadMultiplierAtPointBlank = null!;

    internal static ConfigEntry<bool> ContactResponseEnabled = null!;
    internal static ConfigEntry<bool> HaltSpacingEnabled = null!;
    internal static ConfigEntry<float> ContactImmediateFireDistance = null!;
    internal static ConfigEntry<float> ContactCoverSearchRadius = null!;
    internal static ConfigEntry<float> ContactEngagementHaltDistance = null!;
    internal static ConfigEntry<float> MaximumAttackCombatHaltSeconds = null!;
    internal static ConfigEntry<bool> KnownTargetSuppressionEnabled = null!;
    internal static ConfigEntry<bool> MovingFireRestrictionEnabled = null!;
    internal static ConfigEntry<float> AutomaticMovingFireMaxDistance = null!;
    internal static ConfigEntry<float> RifleMovingFireMaxDistance = null!;
    internal static ConfigEntry<float> SmgMaximumEngagementDistance = null!;
    internal static ConfigEntry<bool> PreventReloadingAndBandagingWhileCrawling = null!;
    internal static ConfigEntry<bool> ImprovedMeleeHitRegistrationEnabled = null!;
    internal static ConfigEntry<float> MeleeAdditionalReach = null!;
    internal static ConfigEntry<float> MeleeMinimumSweepRadius = null!;

    internal static ConfigEntry<bool> StaticWeaponStaffingEnabled = null!;

    internal static ConfigEntry<bool> SuppressionAwarenessEnabled = null!;
    internal static ConfigEntry<float> SuppressedFovMultiplier = null!;
    internal static ConfigEntry<float> SuppressedPeripheralMultiplier = null!;
    internal static ConfigEntry<float> SuppressedMemoryMultiplier = null!;

    internal static ConfigEntry<bool> DangerReactionsEnabled = null!;
    internal static ConfigEntry<int> CrouchSuppression = null!;
    internal static ConfigEntry<int> ProneSuppression = null!;
    internal static ConfigEntry<int> ProneReleaseSuppression = null!;
    internal static ConfigEntry<float> PinnedMinimumSeconds = null!;
    internal static ConfigEntry<float> MaximumPinnedSeconds = null!;
    internal static ConfigEntry<float> PinnedImmunitySeconds = null!;
    internal static ConfigEntry<float> FlameSafetyMargin = null!;
    internal static ConfigEntry<float> FlameEscapeDistance = null!;
    internal static ConfigEntry<bool> MountedGunnerSuppressionEnabled = null!;
    internal static ConfigEntry<int> MountedGunnerDuckSuppression = null!;
    internal static ConfigEntry<int> MountedGunnerRiseSuppression = null!;
    internal static ConfigEntry<float> MountedGunnerMinimumDuckSeconds = null!;
    internal static ConfigEntry<float> MountedGunnerRiseSettleSeconds = null!;

    internal static ConfigEntry<bool> AiCasualtySuppressionEnabled = null!;
    internal static ConfigEntry<float> AiWoundSuppressionRadius = null!;
    internal static ConfigEntry<int> AiWoundSuppressionAmount = null!;
    internal static ConfigEntry<float> AiDeathSuppressionRadius = null!;
    internal static ConfigEntry<int> AiDeathSuppressionAmount = null!;

    internal static ConfigEntry<bool> FriendlyFireChecksEnabled = null!;
    internal static ConfigEntry<float> FriendlyFireLaneRadius = null!;
    internal static ConfigEntry<float> MountedFriendlyFireLaneRadius = null!;
    internal static ConfigEntry<bool> SafeAiGrenadeThrowsEnabled = null!;
    internal static ConfigEntry<float> GrenadeMinimumRange = null!;
    internal static ConfigEntry<float> GrenadeMaximumRange = null!;
    internal static ConfigEntry<float> GrenadeFriendlySafetyRadius = null!;
    internal static ConfigEntry<float> GrenadeCooldownSeconds = null!;

    internal static ConfigEntry<bool> TankFearEnabled = null!;
    internal static ConfigEntry<float> TankAwarenessDistance = null!;
    internal static ConfigEntry<float> TankRetreatDistance = null!;
    internal static ConfigEntry<float> TankEscapeDistance = null!;
    internal static ConfigEntry<float> LauncherMaximumEngagementDistance = null!;

    internal static ConfigEntry<bool> TankTacticsEnabled = null!;
    internal static ConfigEntry<float> TankStandoffDistance = null!;
    internal static ConfigEntry<float> TankReverseDistance = null!;
    internal static ConfigEntry<float> TankReverseSeconds = null!;
    internal static ConfigEntry<float> TankDamagedThreshold = null!;
    internal static ConfigEntry<float> TankMaximumHullFacingAngle = null!;
    internal static ConfigEntry<float> TankInfantryHoldDistance = null!;
    internal static ConfigEntry<float> TankAccelerationMultiplier = null!;
    internal static ConfigEntry<float> StaticAtSearchRadius = null!;
    internal static ConfigEntry<float> StaticAtEnemyTankRange = null!;
    internal static ConfigEntry<float> StaticAtAssignmentCooldown = null!;
    internal static ConfigEntry<float> StaticAtMinimumCaliber = null!;
    internal static ConfigEntry<bool> VehicleGunStaffingEnabled = null!;
    internal static ConfigEntry<bool> StaticGunsEngageInfantry = null!;

    internal static ConfigEntry<bool> SmokeSupportEnabled = null!;
    internal static ConfigEntry<float> SmokeRequestChance = null!;
    internal static ConfigEntry<float> SmokeCooldownSeconds = null!;
    internal static ConfigEntry<float> SmokeMinimumDistance = null!;

    internal static ConfigEntry<bool> ArtilleryMissionLengthEnabled = null!;
    internal static ConfigEntry<float> ArtilleryShellCountMultiplier = null!;
    internal static ConfigEntry<bool> SmokePersistenceEnabled = null!;
    internal static ConfigEntry<float> SmokeLifetimeMultiplier = null!;
    internal static ConfigEntry<bool> ExplosionDustPersistenceEnabled = null!;
    internal static ConfigEntry<float> ExplosionDustLifetimeMultiplier = null!;
    internal static ConfigEntry<float> GrenadeExplosionVisualScale = null!;
    internal static ConfigEntry<bool> AircraftBombBlastEnabled = null!;
    internal static ConfigEntry<float> AircraftBombBlastRadiusMultiplier = null!;
    internal static ConfigEntry<bool> HeavyOrdnanceCratersEnabled = null!;
    internal static ConfigEntry<float> ArtilleryCraterRadiusMultiplier = null!;
    internal static ConfigEntry<float> AircraftBombCraterRadiusMultiplier = null!;
    internal static ConfigEntry<bool> LayeredBlastEffectsEnabled = null!;
    internal static ConfigEntry<bool> EnhancedFragmentationEnabled = null!;
    internal static ConfigEntry<float> FragmentRadiusMultiplier = null!;
    internal static ConfigEntry<int> ExtraFragmentChecksPerTarget = null!;
    internal static ConfigEntry<float> ExtraFragmentDamageMultiplier = null!;
    internal static ConfigEntry<float> SmallExplosionAiThrowForceMultiplier = null!;
    internal static ConfigEntry<float> GeneralExplosionSuppressionRadiusMultiplier = null!;
    internal static ConfigEntry<int> GeneralExplosionSuppression = null!;
    internal static ConfigEntry<float> MortarInjuryRadiusMultiplier = null!;
    internal static ConfigEntry<float> MortarSuppressionRadiusMultiplier = null!;
    internal static ConfigEntry<float> MortarOuterDamage = null!;
    internal static ConfigEntry<int> MortarSuppression = null!;
    internal static ConfigEntry<float> ArtilleryInjuryRadiusMultiplier = null!;
    internal static ConfigEntry<float> ArtillerySuppressionRadiusMultiplier = null!;
    internal static ConfigEntry<float> ArtilleryOuterDamage = null!;
    internal static ConfigEntry<int> ArtillerySuppression = null!;
    internal static ConfigEntry<float> AircraftBombInjuryRadiusMultiplier = null!;
    internal static ConfigEntry<float> AircraftBombSuppressionRadiusMultiplier = null!;
    internal static ConfigEntry<float> AircraftBombOuterDamage = null!;
    internal static ConfigEntry<int> AircraftBombSuppression = null!;
    internal static ConfigEntry<float> BlastCoverEffectMultiplier = null!;

    internal static ConfigEntry<bool> BulletPenetrationEnabled = null!;
    internal static ConfigEntry<float> OrdinaryRoundPenetrationStrength = null!;
    internal static ConfigEntry<float> ArmorPiercingPropPenetrationStrength = null!;
    internal static ConfigEntry<int> MaximumPropPenetrations = null!;
    internal static ConfigEntry<bool> AddedSmallArmsRicochetsEnabled = null!;
    internal static ConfigEntry<float> AddedRicochetChanceMultiplier = null!;

    internal static ConfigEntry<bool> TracerReductionEnabled = null!;
    internal static ConfigEntry<float> MachineGunTracerRetention = null!;
    internal static ConfigEntry<int> HitDecalDurationSeconds = null!;

    internal static ConfigEntry<float> PlayerSuppressionVignetteMultiplier = null!;
    internal static ConfigEntry<float> PlayerSuppressionWobbleMultiplier = null!;
    internal static ConfigEntry<float> PlayerSuppressionNearMissRadiusMultiplier = null!;
    internal static ConfigEntry<bool> PlayerSuppressionBlurEnabled = null!;
    internal static ConfigEntry<float> PlayerSuppressionBlurStrength = null!;
    internal static ConfigEntry<bool> ShowPlayerSuppressionDirectionMarker = null!;

    internal static ConfigEntry<bool> LeaderOnlyOrderGestures = null!;
    internal static ConfigEntry<float> OrderGestureCooldownSeconds = null!;

    internal static ConfigEntry<bool> KeepHighQualityDistantAnimations = null!;

    internal static ConfigEntry<bool> BattleChatterEnabled = null!;
    internal static ConfigEntry<float> ChatterIndividualCooldownSeconds = null!;
    internal static ConfigEntry<float> ChatterSquadCooldownSeconds = null!;
    internal static ConfigEntry<float> ChatterContactCalloutChance = null!;
    internal static ConfigEntry<float> ChatterRoutineCalloutChance = null!;
    internal static ConfigEntry<float> ChatterRoutineMinimumSeconds = null!;
    internal static ConfigEntry<float> ChatterRoutineMaximumSeconds = null!;

    internal static ConfigEntry<bool> AudioBalanceEnabled = null!;
    internal static ConfigEntry<float> VehicleEngineSound = null!;
    internal static ConfigEntry<float> TankTrackVolumeMultiplier = null!;
    internal static ConfigEntry<float> WeaponFireVolumeMultiplier = null!;
    internal static ConfigEntry<float> TankGunVolumeMultiplier = null!;
    internal static ConfigEntry<bool> DistantSoundShapingEnabled = null!;
    internal static ConfigEntry<float> DistantSoundStartDistance = null!;
    internal static ConfigEntry<float> DistantSoundFullEffectDistance = null!;
    internal static ConfigEntry<float> DistantSoundMinimumCutoff = null!;
    internal static ConfigEntry<float> DistantReverbAmount = null!;
    internal static ConfigEntry<float> PlayerFootstepVolumeMultiplier = null!;

    internal static ConfigEntry<bool> FirstPersonPlayerShadowEnabled = null!;
    internal static ConfigEntry<float> HoldBreathZoomMultiplier = null!;
    internal static ConfigEntry<bool> BinocularsEnabled = null!;
    internal static ConfigEntry<KeyCode> BinocularsKey = null!;
    internal static ConfigEntry<float> BinocularZoomMultiplier = null!;
    internal static ConfigEntry<bool> FreeLookEnabled = null!;
    internal static ConfigEntry<KeyCode> FreeLookKey = null!;
    internal static ConfigEntry<float> FreeLookHorizontalArcDegrees = null!;
    internal static ConfigEntry<bool> CompassAlwaysVisible = null!;
    internal static ConfigEntry<KeyCode> CompassKey = null!;
    internal static ConfigEntry<bool> CompassUseMils = null!;

    internal static ConfigEntry<bool> KeepMultiplayerPlayerNamesWithHudDisabled = null!;

    internal static ConfigEntry<bool> ShowSettingsLauncherButton = null!;
    internal static ConfigEntry<string> DisabledSwitchSnapshot = null!;

    internal static ConfigEntry<bool> StutterProbeEnabled = null!;

    internal static ConfigEntry<bool> AiDebugOverlayStartEnabled = null!;
    internal static ConfigEntry<KeyCode> AiDebugOverlayToggleKey = null!;
    internal static ConfigEntry<float> AiDebugOverlayMaximumDistance = null!;
    internal static ConfigEntry<int> AiDebugOverlayMaximumActors = null!;
    internal static ConfigEntry<float> AiDebugOverlayEventHistorySeconds = null!;
    internal static ConfigEntry<bool> VerboseLogging = null!;

    internal static void Bind(ConfigFile config)
    {
        ConfigFile = config;

        AttackingForceBonusEnabled = config.Bind("AI - Attack posture bonuses", "AttackPostureBonusesEnabled", true,
            "Gives host-controlled AI in the active Attack posture a modest proficiency bonus. The bonus follows objective ownership and changes sides when posture changes.");
        AttackingForceAccuracySpreadMultiplier = config.Bind("AI - Attack posture bonuses", "AttackPostureAccuracySpreadMultiplier", 0.728f,
            new ConfigDescription("Multiplier applied to weapon spread for all attacking AI soldiers, including vehicle and emplacement crews. Lower values are more accurate; the default reduces spread by 27.2 percent.", new AcceptableValueRange<float>(0.6f, 1f)));
        AttackingForceSuppressionReceivedMultiplier = config.Bind("AI - Attack posture bonuses", "AttackPostureSuppressionReceivedMultiplier", 0.658f,
            new ConfigDescription("Multiplier applied to suppression received by attacking AI soldiers. Lower values make attackers harder to pin; the default reduces incoming suppression by 34.2 percent.", new AcceptableValueRange<float>(0.5f, 1f)));
        AttackingTankAdditionalAccuracySpreadMultiplier = config.Bind("AI - Attack posture bonuses", "AttackPostureTankAccuracySpreadMultiplier", 0.91f,
            new ConfigDescription("Additional spread multiplier for attacking AI tank crews. Combined with the default force-wide multiplier, tanks retain about an 18 percent spread reduction.", new AcceptableValueRange<float>(0.7f, 1f)));

        PerceptionEnabled = config.Bind("AI - Infantry tactics - Perception", "Enabled", true,
            "Requires AI to visually acquire a target before aiming or firing and stops indefinite target lock outside its forward field of view.");
        HorizontalFov = config.Bind("AI - Infantry tactics - Perception", "HorizontalFovDegrees", 120f,
            new ConfigDescription("AI horizontal combat field of view.", new AcceptableValueRange<float>(60f, 240f)));
        CloseTargetAcquisitionSeconds = config.Bind("AI - Infantry tactics - Perception", "CloseTargetAcquisitionSeconds", 0.59f,
            new ConfigDescription("Repeated valid observation required to acquire a nearby new target. Increasing this reduces close-range snap targeting.", new AcceptableValueRange<float>(0.15f, 5f)));
        DistantTargetAcquisitionSeconds = config.Bind("AI - Infantry tactics - Perception", "DistantTargetAcquisitionSeconds", 1.356f,
            new ConfigDescription("Repeated valid observation required to acquire a new target at or beyond the distant-acquisition range. Time scales smoothly from the close value.", new AcceptableValueRange<float>(0.25f, 8f)));
        DistantTargetAcquisitionRange = config.Bind("AI - Infantry tactics - Perception", "DistantTargetAcquisitionRangeMeters", 140f,
            new ConfigDescription("Distance at which the full distant target-acquisition time applies.", new AcceptableValueRange<float>(25f, 400f)));
        TargetMemorySeconds = config.Bind("AI - Infantry tactics - Perception", "TargetMemorySeconds", 10f,
            new ConfigDescription("How long an AI may remember a target outside its FOV before losing fire authorization.", new AcceptableValueRange<float>(0f, 15f)));
        PeripheralAwarenessDistance = config.Bind("AI - Infantry tactics - Perception", "PeripheralAwarenessDistance", 13.834f,
            new ConfigDescription("Targets this close remain noticeable even outside the normal FOV.", new AcceptableValueRange<float>(0f, 25f)));

        CloseQuartersEnabled = config.Bind("AI - Infantry tactics - Close quarters", "Enabled", true,
            "Speeds up target identification at point-blank range, keeps heavy suppression from blinding a soldier to an immediate close threat, and tightens weapon spread inside close-quarters range.");
        PointBlankAcquisitionSeconds = config.Bind("AI - Infantry tactics - Close quarters", "PointBlankAcquisitionSeconds", 0.3f,
            new ConfigDescription("Observation time required to identify a target at 0 m, lerping up to the normal close acquisition time at the immediate-fire distance. Lower values identify point-blank threats faster.", new AcceptableValueRange<float>(0.1f, 1.5f)));
        MinimumPeripheralAwarenessMeters = config.Bind("AI - Infantry tactics - Close quarters", "MinimumPeripheralAwarenessMeters", 6f,
            new ConfigDescription("Suppression can never shrink the peripheral-awareness ring below this distance, so a heavily suppressed soldier still notices a threat at arm's length. Never raises awareness above the unsuppressed value.", new AcceptableValueRange<float>(0f, 15f)));
        CloseQuartersRangeMeters = config.Bind("AI - Infantry tactics - Close quarters", "CloseQuartersRangeMeters", 25f,
            new ConfigDescription("Range inside which AI weapon spread tightens. No effect at or beyond this distance.", new AcceptableValueRange<float>(5f, 50f)));
        SpreadMultiplierAtPointBlank = config.Bind("AI - Infantry tactics - Close quarters", "SpreadMultiplierAtPointBlank", 0.55f,
            new ConfigDescription("Weapon spread multiplier at 0 m, lerping to 1.0 at the close-quarters range. Lower values make point-blank fire deadlier.", new AcceptableValueRange<float>(0.4f, 1f)));

        ContactResponseEnabled = config.Bind("AI - Infantry tactics - Contact response", "Enabled", true,
            "Coordinates cover selection, forward relocations, and close engagement halts when infantry make contact.");
        HaltSpacingEnabled = config.Bind("AI - Infantry tactics - Contact response", "StepClearOfStackedSquadmates", false,
            "When a soldier is about to take a fighting halt on top of an already-halted squadmate, he first takes one short sideways step to open the gap. OFF by default: the step grants locomotion for a fixed window, so a soldier who finishes (or cannot finish) the step keeps his walk animation running for the remainder of it, which reads as walking in place. Cover-slot spacing is handled separately by the cover-search crowding penalty and is unaffected by this setting.");
        ContactImmediateFireDistance = config.Bind("AI - Infantry tactics - Contact response", "ImmediateFireDistanceMeters", 30f,
            new ConfigDescription("Inside this surprise-contact distance, an exposed soldier halts and returns fire immediately instead of continuing a cover move. Raised to 30 m so a rifleman caught mid-dash at ordinary infantry engagement range stops and shoots instead of running past the enemy without firing.", new AcceptableValueRange<float>(3f, 45f)));
        ContactCoverSearchRadius = config.Bind("AI - Infantry tactics - Contact response", "CoverSearchRadiusMeters", 28f,
            new ConfigDescription("Local cover radius for maneuvering attackers. Raised so attackers can reach flanking cover, doorways, and building slots instead of only a tiny forward wedge. Defenders inventory their entire position out to at least 55 m or the objective radius plus 12 m.", new AcceptableValueRange<float>(5f, 60f)));
        ContactEngagementHaltDistance = config.Bind("AI - Infantry tactics - Contact response", "EngagementHaltDistanceMeters", 196.449f,
            new ConfigDescription("Inside this distance, visible contact overrides ordinary attack waypoints and the soldier establishes a firing halt. A charge keeps moving except when a non-SMG soldier meets an immediate close threat.", new AcceptableValueRange<float>(40f, 300f)));
        MaximumAttackCombatHaltSeconds = config.Bind("AI - Infantry tactics - Contact response", "MaximumAttackCombatHaltSeconds", 12f,
            new ConfigDescription("Maximum continuous firing halt for a squad on an attack order before exposed troops resume forward progress. Heavily pinned attackers crawl; troops still seek forward cover and immediate close threats remain higher priority.", new AcceptableValueRange<float>(6f, 30f)));
        KnownTargetSuppressionEnabled = config.Bind("AI - Infantry tactics - Contact response", "SuppressKnownTargets", true,
            "Allows a stationary on-foot machine gunner to fire one bounded burst at a fresh, personally confirmed last-seen enemy position after sight is lost. It uses real ammunition and never tracks an unseen target.");

        MovingFireRestrictionEnabled = config.Bind("AI - Infantry tactics - Moving fire", "RestrictMovingFire", true,
            "Restricts handheld moving fire to recognized SMGs at close range. Riflemen and machine gunners halt before firing.");
        AutomaticMovingFireMaxDistance = BindSmgMovingFireRange(config);
        RifleMovingFireMaxDistance = config.Bind("AI - Infantry tactics - Moving fire", "RifleMovingFireMaxDistanceMeters", 0f,
            new ConfigDescription("Maximum visible-target distance at which a rifle or carbine may be fired while moving. 0 disables rifle moving fire entirely, which is the doctrine default: riflemen halt and shoot (see the contact-response immediate-fire distance). Raise it only if you want hip-fire while advancing. Machine guns and launchers never fire while moving.", new AcceptableValueRange<float>(0f, 25f)));
        SmgMaximumEngagementDistance = config.Bind("AI - Infantry tactics - Moving fire", "SmgMaximumEngagementDistanceMeters", 80f,
            new ConfigDescription("Maximum distance at which AI may fire a submachine gun, whether stationary or moving.", new AcceptableValueRange<float>(30f, 180f)));
        PreventReloadingAndBandagingWhileCrawling = config.Bind("AI - Infantry tactics - Movement", "PreventReloadingAndBandagingWhileCrawling", true,
            "Players must stop crawling before reloading or bandaging. AI soldiers automatically stop their crawl and then perform the action; stationary prone soldiers are unaffected.");
        ImprovedMeleeHitRegistrationEnabled = config.Bind("2d. Melee combat", "ImprovedHitRegistration", true,
            "Makes player and AI melee strikes use a longer and wider native hit query, reducing close-range ghost swings without changing melee damage.");
        MeleeAdditionalReach = BindMeleeAdditionalReach(config);
        MeleeMinimumSweepRadius = config.Bind("2d. Melee combat", "MinimumSweepRadiusMeters", 0.448f,
            new ConfigDescription("Minimum radius of the melee hit capsule. The base game uses 0.25 m; a modest increase forgives small animation and collider misalignments.", new AcceptableValueRange<float>(0.25f, 0.6f)));

        StaticWeaponStaffingEnabled = config.Bind("AI - Defense", "Enabled", true,
            "Sends AI defenders to staff viable static defensive weapons (crewed guns, emplacements) inside their squad's defend order area.");

        SuppressionAwarenessEnabled = config.Bind("AI - Infantry tactics - Suppression", "Enabled", true,
            "Makes suppression narrow awareness and shorten target memory.");
        SuppressedFovMultiplier = config.Bind("AI - Infantry tactics - Suppression", "FovMultiplierAtMaximumSuppression", 0.55f,
            new ConfigDescription("Horizontal FOV multiplier at maximum suppression.", new AcceptableValueRange<float>(0.3f, 1f)));
        SuppressedPeripheralMultiplier = config.Bind("AI - Infantry tactics - Suppression", "PeripheralMultiplierAtMaximumSuppression", 0.45f,
            new ConfigDescription("Close peripheral-awareness multiplier at maximum suppression.", new AcceptableValueRange<float>(0.2f, 1f)));
        SuppressedMemoryMultiplier = config.Bind("AI - Infantry tactics - Suppression", "MemoryMultiplierAtMaximumSuppression", 0.35f,
            new ConfigDescription("Target-memory duration multiplier at maximum suppression.", new AcceptableValueRange<float>(0.1f, 1f)));

        DangerReactionsEnabled = config.Bind("AI - Infantry tactics - Danger", "Enabled", true,
            "Makes exposed soldiers get low for reloads, suppressed soldiers seek a lower stationary posture, recover from the initial shock to return fire, escape active flames, and dismount AI-led APCs before credible nearby contact.");
        CrouchSuppression = config.Bind("AI - Infantry tactics - Danger", "CrouchSuppressionThreshold", 35,
            new ConfigDescription("Suppression value that triggers crouching.", new AcceptableValueRange<int>(1, 254)));
        ProneSuppression = config.Bind("AI - Infantry tactics - Danger", "ProneSuppressionThreshold", 51,
            new ConfigDescription("Suppression value that triggers going prone.", new AcceptableValueRange<int>(2, 255)));
        ProneReleaseSuppression = config.Bind("AI - Infantry tactics - Danger", "ProneReleaseSuppressionThreshold", 25,
            new ConfigDescription("A pinned soldier remains prone until suppression falls below this lower threshold.", new AcceptableValueRange<int>(1, 254)));
        PinnedMinimumSeconds = config.Bind("AI - Infantry tactics - Danger", "PinnedMinimumSeconds", 6f,
            new ConfigDescription("Minimum commitment to a pinned stationary state before movement is reconsidered. Soldiers crouch behind valid cover and go prone when exposed.", new AcceptableValueRange<float>(1f, 20f)));
        MaximumPinnedSeconds = config.Bind("AI - Infantry tactics - Danger", "MaximumPinnedSeconds", 25f,
            new ConfigDescription("Hard time cap on a suppression pin: a soldier still pinned this long releases regardless of current suppression, so sustained fire cannot pin a soldier forever.", new AcceptableValueRange<float>(10f, 60f)));
        PinnedImmunitySeconds = config.Bind("AI - Infantry tactics - Danger", "PinnedImmunitySeconds", 10f,
            new ConfigDescription("After a time-cap pin release, how long the soldier is immune to being re-pinned by the same ongoing suppression.", new AcceptableValueRange<float>(2f, 20f)));
        FlameSafetyMargin = config.Bind("AI - Infantry tactics - Danger", "FlameSafetyMarginMeters", 2.5f,
            new ConfigDescription("Extra clearance added to a flame's damage radius.", new AcceptableValueRange<float>(0f, 10f)));
        FlameEscapeDistance = config.Bind("AI - Infantry tactics - Danger", "FlameEscapeDistanceMeters", 8f,
            new ConfigDescription("How far an AI attempts to move away from a nearby flame.", new AcceptableValueRange<float>(2f, 25f)));
        MountedGunnerSuppressionEnabled = config.Bind("AI - Infantry tactics - Danger", "MountedGunnerSuppressionDuck", true,
            "Allows AI turret and static-machine-gun users in native crouchable seats to duck under suppression.");
        MountedGunnerDuckSuppression = config.Bind("AI - Infantry tactics - Danger", "MountedGunnerDuckSuppressionThreshold", 45,
            new ConfigDescription("Suppression value at which an exposed AI mounted gunner ducks and ceases fire.", new AcceptableValueRange<int>(1, 254)));
        MountedGunnerRiseSuppression = config.Bind("AI - Infantry tactics - Danger", "MountedGunnerRiseSuppressionThreshold", 25,
            new ConfigDescription("Lower suppression value below which a ducked AI mounted gunner may rise again.", new AcceptableValueRange<int>(0, 253)));
        MountedGunnerMinimumDuckSeconds = config.Bind("AI - Infantry tactics - Danger", "MountedGunnerMinimumDuckSeconds", 2.25f,
            new ConfigDescription("Minimum time an AI mounted gunner remains ducked after reacting to suppression.", new AcceptableValueRange<float>(0.5f, 12f)));
        MountedGunnerRiseSettleSeconds = config.Bind("AI - Infantry tactics - Danger", "MountedGunnerRiseSettleSeconds", 0.4f,
            new ConfigDescription("Delay after rising before an AI mounted gunner may fire again.", new AcceptableValueRange<float>(0.1f, 2f)));

        AiCasualtySuppressionEnabled = config.Bind("AI - Infantry tactics - Casualty suppression", "Enabled", true,
            "Makes nearby allied AI react to a soldier being wounded or killed. The local player is never a recipient of casualty suppression.");
        AiWoundSuppressionRadius = config.Bind("AI - Infantry tactics - Casualty suppression", "WoundRadiusMeters", 14f,
            new ConfigDescription("Radius in which an allied wound adds suppression to AI soldiers.", new AcceptableValueRange<float>(2f, 40f)));
        AiWoundSuppressionAmount = config.Bind("AI - Infantry tactics - Casualty suppression", "WoundSuppression", 18,
            new ConfigDescription("Maximum suppression added to nearby allied AI by a wound. The effect tapers with distance and repeated hits on one casualty are briefly debounced.", new AcceptableValueRange<int>(0, 100)));
        AiDeathSuppressionRadius = config.Bind("AI - Infantry tactics - Casualty suppression", "DeathRadiusMeters", 10.313f,
            new ConfigDescription("Radius in which an allied death adds suppression to AI soldiers.", new AcceptableValueRange<float>(2f, 60f)));
        AiDeathSuppressionAmount = config.Bind("AI - Infantry tactics - Casualty suppression", "DeathSuppression", 45,
            new ConfigDescription("Maximum suppression added to nearby allied AI by a death. The effect tapers with distance.", new AcceptableValueRange<int>(0, 150)));

        FriendlyFireChecksEnabled = config.Bind("AI - Infantry tactics - Combat safety", "FriendlyFireChecks", true,
            "Withholds AI handheld and mounted fire while a friendly soldier occupies the firing lane.");
        FriendlyFireLaneRadius = config.Bind("AI - Infantry tactics - Combat safety", "HandheldLaneRadiusMeters", 0.9f,
            new ConfigDescription("Clearance around an AI handheld weapon's line of fire.", new AcceptableValueRange<float>(0.25f, 3f)));
        MountedFriendlyFireLaneRadius = config.Bind("AI - Infantry tactics - Combat safety", "MountedLaneRadiusMeters", 2.25f,
            new ConfigDescription("Clearance around mounted and aircraft gun lines of fire.", new AcceptableValueRange<float>(0.5f, 12f)));
        SafeAiGrenadeThrowsEnabled = BindSafeAiGrenadeThrows(config);
        GrenadeMinimumRange = config.Bind("AI - Infantry tactics - Combat safety", "GrenadeMinimumRangeMeters", 9f,
            new ConfigDescription("AI will not throw an explosive grenade at a closer target.", new AcceptableValueRange<float>(3f, 25f)));
        GrenadeMaximumRange = config.Bind("AI - Infantry tactics - Combat safety", "GrenadeMaximumRangeMeters", 42f,
            new ConfigDescription("AI will not attempt an implausibly long explosive-grenade throw.", new AcceptableValueRange<float>(15f, 75f)));
        GrenadeFriendlySafetyRadius = config.Bind("AI - Infantry tactics - Combat safety", "GrenadeFriendlySafetyRadiusMeters", 11f,
            new ConfigDescription("Required friendly clearance around the intended grenade impact.", new AcceptableValueRange<float>(4f, 25f)));
        GrenadeCooldownSeconds = config.Bind("AI - Infantry tactics - Combat safety", "GrenadeCooldownSeconds", 18f,
            new ConfigDescription("Minimum time between explosive-grenade throws by one AI soldier.", new AcceptableValueRange<float>(5f, 90f)));

        TankFearEnabled = config.Bind("AI - Infantry tactics - Armor response", "Enabled", true,
            "Makes non-AT infantry hide from nearby hostile tanks. Troops already in cover stay hidden; exposed troops seek one tank-masked position instead of repeatedly retreating.");
        TankAwarenessDistance = config.Bind("AI - Infantry tactics - Armor response", "AwarenessDistanceMeters", 120f,
            new ConfigDescription("Range at which infantry react to a hostile tank.", new AcceptableValueRange<float>(15f, 180f)));
        TankRetreatDistance = config.Bind("AI - Infantry tactics - Armor response", "RetreatDistanceMeters", 90f,
            new ConfigDescription("Range at which exposed non-AT infantry urgently seek tank-masked cover. The legacy setting name is retained for config compatibility.", new AcceptableValueRange<float>(5f, 140f)));
        TankEscapeDistance = config.Bind("AI - Infantry tactics - Armor response", "EscapeMoveMeters", 18f,
            new ConfigDescription("Minimum local search radius for tank-masked cover. The legacy setting name is retained for config compatibility.", new AcceptableValueRange<float>(4f, 35f)));
        LauncherMaximumEngagementDistance = config.Bind("AI - Infantry tactics - Armor response", "LauncherMaximumEngagementDistanceMeters", 90f,
            new ConfigDescription("Maximum distance at which AI may fire a low-velocity handheld anti-tank launcher. High-velocity anti-tank rifles retain their native range.", new AcceptableValueRange<float>(40f, 160f)));

        TankTacticsEnabled = config.Bind("AI - Vehicle tactics", "Enabled", true,
            "Makes AI tanks establish standoff and reverse when too close to an enemy tank or badly damaged, while tanks on attack orders keep pressure against infantry.");
        TankStandoffDistance = config.Bind("AI - Vehicle tactics", "StopAndEngageDistanceMeters", 180f,
            new ConfigDescription("AI tanks stop advancing and rotate to engage enemy tanks inside this distance.", new AcceptableValueRange<float>(30f, 250f)));
        TankReverseDistance = config.Bind("AI - Vehicle tactics", "ReverseDistanceMeters", 100f,
            new ConfigDescription("AI tanks reverse when an enemy tank is closer than this distance.", new AcceptableValueRange<float>(15f, 120f)));
        TankReverseSeconds = config.Bind("AI - Vehicle tactics", "ReverseDurationSeconds", 3.5f,
            new ConfigDescription("Length of a tactical reverse.", new AcceptableValueRange<float>(1f, 10f)));
        TankDamagedThreshold = config.Bind("AI - Vehicle tactics", "DamagedLifeFraction", 0.45f,
            new ConfigDescription("AI tanks may reverse while under threat below this fraction of hull life.", new AcceptableValueRange<float>(0.1f, 0.9f)));
        TankMaximumHullFacingAngle = config.Bind("AI - Vehicle tactics", "MaximumHullFacingAngleDegrees", 30f,
            new ConfigDescription("Tank is considered frontally aligned when its hull points within this angle of an enemy tank; retreats preserve hull orientation and drive straight backward.", new AcceptableValueRange<float>(10f, 60f)));
        TankInfantryHoldDistance = config.Bind("AI - Vehicle tactics", "HoldPositionAgainstInfantryMeters", 160f,
            new ConfigDescription("AI tanks without a forward attack order stop to engage visible infantry inside this range. Attacking tanks retain native fire-and-move behavior.", new AcceptableValueRange<float>(40f, 300f)));
        TankAccelerationMultiplier = config.Bind("4a. Tank physics", "AccelerationMultiplier", 0.302f,
            new ConfigDescription("Scales how quickly player and AI tanks reach their native motor torque without changing top speed or maximum torque. The 0.302 default makes the torque ramp about 3.31 times longer; 1.0 restores stock acceleration.", new AcceptableValueRange<float>(0.1f, 1f)));
        StaticAtSearchRadius = config.Bind("AI - Defense", "WeaponSearchRadiusMeters", 95.724f,
            new ConfigDescription("Minimum objective-position inventory radius for viable static weapons. The director automatically expands this to the objective radius plus 12 m.", new AcceptableValueRange<float>(15f, 120f)));
        StaticAtEnemyTankRange = config.Bind("AI - Defense", "EnemyTankResponseRangeMeters", 350f,
            new ConfigDescription("Range used to identify reported armor and prioritize AP-capable guns. All other viable defensive weapons are staffed even without armor contact.", new AcceptableValueRange<float>(75f, 600f)));
        StaticAtAssignmentCooldown = config.Bind("AI - Defense", "AssignmentCooldownSeconds", 12f,
            new ConfigDescription("Maximum interval between full defensive-emplacement inventories; vacancies caused by death, destruction, empty ammunition, or lost ownership are handled immediately.", new AcceptableValueRange<float>(3f, 60f)));
        StaticAtMinimumCaliber = config.Bind("AI - Defense", "MinimumGunCaliberMm", 20f,
            new ConfigDescription("Minimum static-gun caliber considered suitable for anti-tank use.", new AcceptableValueRange<float>(12f, 75f)));
        VehicleGunStaffingEnabled = config.Bind("AI - Defense", "StaffAbandonedVehicleGuns", true,
            "Defenders also man the gun of an empty armed troop transport parked inside their defend area, such as a halftrack or gun truck with a mounted machine gun. Tanks, assault guns, and aircraft are never crewed this way, and the gunner gives the vehicle up as soon as a player orders a squad onto it or it drives away.");
        StaticGunsEngageInfantry = config.Bind("AI - Defense", "EngageInfantryWhenNoVehicleTarget", true,
            "Crewed static AT guns fire on visible enemy infantry when their native targeting finds no vehicle. Vehicles still take absolute priority.");

        SmokeSupportEnabled = config.Bind("AI - Support coordination - Smoke", "ExtraSmokeRequestsEnabled", true,
            "Allows a small number of additional AI smoke requests. This does not add HE or APHE fire missions.");
        SmokeRequestChance = config.Bind("AI - Support coordination - Smoke", "RequestChance", 0.08f,
            new ConfigDescription("Chance per eligible attack opportunity.", new AcceptableValueRange<float>(0f, 1f)));
        SmokeCooldownSeconds = config.Bind("AI - Support coordination - Smoke", "SquadCooldownSeconds", 240f,
            new ConfigDescription("Minimum time between extra smoke attempts by the same squad.", new AcceptableValueRange<float>(20f, 300f)));
        SmokeMinimumDistance = config.Bind("AI - Support coordination - Smoke", "MinimumTargetDistanceMeters", 25f,
            new ConfigDescription("Avoids dropping smoke directly on the squad.", new AcceptableValueRange<float>(5f, 100f)));

        ArtilleryMissionLengthEnabled = config.Bind("6. Ordnance effects", "LongerFireMissions", true,
            "Lengthens each existing artillery fire mission by adding rounds at the original cadence. Does not request missions more often.");
        ArtilleryShellCountMultiplier = config.Bind("6. Ordnance effects", "ArtilleryRoundCountMultiplier", 1.75f,
            new ConfigDescription("Multiplier for rounds in an existing artillery mission. Strike interval and request frequency are unchanged.", new AcceptableValueRange<float>(1f, 4f)));
        SmokePersistenceEnabled = config.Bind("6. Ordnance effects", "LongerSmokeEffects", true,
            "Experimentally lengthens particle emitters identified as smoke effects.");
        SmokeLifetimeMultiplier = config.Bind("6. Ordnance effects", "SmokeLifetimeMultiplier", 6f,
            new ConfigDescription("Multiplier for smoke emitter and particle lifetime.", new AcceptableValueRange<float>(1f, 8f)));
        ExplosionDustPersistenceEnabled = config.Bind("6. Ordnance effects", "LongerExplosionDust", true,
            "Lengthens dust, dirt, soil, and sand-cloud emitters used by explosions and destructible scenery.");
        ExplosionDustLifetimeMultiplier = config.Bind("6. Ordnance effects", "ExplosionDustLifetimeMultiplier", 3f,
            new ConfigDescription("Multiplier for airborne dust emitter duration.", new AcceptableValueRange<float>(1f, 8f)));
        GrenadeExplosionVisualScale = config.Bind("6. Ordnance effects", "GrenadeExplosionVisualScale", 0.55f,
            new ConfigDescription("Visual-only scale for ordinary explosive-grenade effects. Does not change damage, penetration, or blast radius.", new AcceptableValueRange<float>(0.25f, 1.25f)));
        AircraftBombBlastEnabled = config.Bind("6. Ordnance effects", "LargerAircraftBombBlast", true,
            "Increases the effective blast radius of aircraft-dropped bombs and uses the artillery-scale explosion effect.");
        AircraftBombBlastRadiusMultiplier = config.Bind("6. Ordnance effects", "AircraftBombBlastRadiusMultiplier", 2f,
            new ConfigDescription("Multiplier applied to aircraft-bomb explosion radius; damage and penetration values are unchanged.", new AcceptableValueRange<float>(1f, 4f)));
        HeavyOrdnanceCratersEnabled = config.Bind("6. Ordnance effects", "LargerHeavyOrdnanceCraters", true,
            "Makes the visual ground decals from artillery shells and aircraft bombs modestly wider. This does not deform terrain or affect damage.");
        ArtilleryCraterRadiusMultiplier = config.Bind("6. Ordnance effects", "ArtilleryCraterRadiusMultiplier", 1.35f,
            new ConfigDescription("Multiplier applied only to artillery-shell crater decal radius.", new AcceptableValueRange<float>(1f, 2.5f)));
        AircraftBombCraterRadiusMultiplier = config.Bind("6. Ordnance effects", "AircraftBombCraterRadiusMultiplier", 1.5f,
            new ConfigDescription("Multiplier applied only to aircraft-bomb crater decal radius.", new AcceptableValueRange<float>(1f, 2.5f)));
        LayeredBlastEffectsEnabled = config.Bind("6. Ordnance effects", "LayeredBlastEffects", true,
            "Adds obstruction-scaled outer injury and suppression effects while retaining each explosion's native inner blast and fragmentation.");
        EnhancedFragmentationEnabled = config.Bind("6. Ordnance effects", "EnhancedFragmentation", true,
            "Expands the base game's native fragment-hit region for every damaging explosion and adds extra body-part fragment exposure checks.");
        FragmentRadiusMultiplier = config.Bind("6. Ordnance effects", "FragmentRadiusMultiplier", 1.35f,
            new ConfigDescription("Multiplier applied to the base game's native fragmentation radius, without enlarging the full-damage blast radius.", new AcceptableValueRange<float>(1f, 3f)));
        ExtraFragmentChecksPerTarget = config.Bind("6. Ordnance effects", "ExtraFragmentChecksPerTarget", 5,
            new ConfigDescription("Additional probabilistic, cover-blockable fragment rays tested per soldier inside the fragment region.", new AcceptableValueRange<int>(0, 8)));
        ExtraFragmentDamageMultiplier = config.Bind("6. Ordnance effects", "ExtraFragmentDamageMultiplier", 0.35f,
            new ConfigDescription("Fraction of the ammunition's native maximum explosion damage carried by each additional fragment hit.", new AcceptableValueRange<float>(0f, 1f)));
        SmallExplosionAiThrowForceMultiplier = config.Bind("6. Ordnance effects", "SmallExplosionAiThrowForceMultiplier", 0.232f,
            new ConfigDescription("Multiplier for the physical force that ordinary explosions such as grenades and gun HE use to throw AI infantry. Zero removes the launch impulse and one preserves the base game; damage, blast radius, suppression, and heavy-ordnance force are unchanged.", new AcceptableValueRange<float>(0f, 1.5f)));
        GeneralExplosionSuppressionRadiusMultiplier = config.Bind("6. Ordnance effects", "GeneralExplosionSuppressionRadiusMultiplier", 3.228f,
            new ConfigDescription("Suppression radius relative to native explosion radius for grenades, tank and gun HE, and other unclassified explosions.", new AcceptableValueRange<float>(1f, 5f)));
        GeneralExplosionSuppression = config.Bind("6. Ordnance effects", "GeneralExplosionSuppression", 113,
            new ConfigDescription("Maximum suppression added by an unclassified explosion, with distance and cover falloff.", new AcceptableValueRange<int>(0, 150)));
        MortarInjuryRadiusMultiplier = config.Bind("6. Ordnance effects", "MortarInjuryRadiusMultiplier", 1.35f,
            new ConfigDescription("Mortar outer-injury radius relative to its native blast radius.", new AcceptableValueRange<float>(1f, 3f)));
        MortarSuppressionRadiusMultiplier = config.Bind("6. Ordnance effects", "MortarSuppressionRadiusMultiplier", 2.2f,
            new ConfigDescription("Mortar suppression radius relative to its native blast radius.", new AcceptableValueRange<float>(1f, 5f)));
        MortarOuterDamage = config.Bind("6. Ordnance effects", "MortarOuterDamage", 18f,
            new ConfigDescription("Maximum injury at the inside edge of a mortar's outer ring.", new AcceptableValueRange<float>(0f, 60f)));
        MortarSuppression = config.Bind("6. Ordnance effects", "MortarSuppression", 35,
            new ConfigDescription("Maximum suppression added by a mortar's outer blast.", new AcceptableValueRange<int>(0, 150)));
        ArtilleryInjuryRadiusMultiplier = config.Bind("6. Ordnance effects", "ArtilleryInjuryRadiusMultiplier", 1.5f,
            new ConfigDescription("Artillery outer-injury radius relative to its native blast radius.", new AcceptableValueRange<float>(1f, 3f)));
        ArtillerySuppressionRadiusMultiplier = config.Bind("6. Ordnance effects", "ArtillerySuppressionRadiusMultiplier", 2.75f,
            new ConfigDescription("Artillery suppression radius relative to its native blast radius.", new AcceptableValueRange<float>(1f, 5f)));
        ArtilleryOuterDamage = config.Bind("6. Ordnance effects", "ArtilleryOuterDamage", 28f,
            new ConfigDescription("Maximum injury at the inside edge of an artillery shell's outer ring.", new AcceptableValueRange<float>(0f, 80f)));
        ArtillerySuppression = config.Bind("6. Ordnance effects", "ArtillerySuppression", 55,
            new ConfigDescription("Maximum suppression added by an artillery shell's outer blast.", new AcceptableValueRange<int>(0, 200)));
        AircraftBombInjuryRadiusMultiplier = config.Bind("6. Ordnance effects", "AircraftBombInjuryRadiusMultiplier", 1.4f,
            new ConfigDescription("Aircraft-bomb outer-injury radius relative to its configured blast radius.", new AcceptableValueRange<float>(1f, 3f)));
        AircraftBombSuppressionRadiusMultiplier = config.Bind("6. Ordnance effects", "AircraftBombSuppressionRadiusMultiplier", 2.6f,
            new ConfigDescription("Aircraft-bomb suppression radius relative to its configured blast radius.", new AcceptableValueRange<float>(1f, 5f)));
        AircraftBombOuterDamage = config.Bind("6. Ordnance effects", "AircraftBombOuterDamage", 35f,
            new ConfigDescription("Maximum injury at the inside edge of an aircraft bomb's outer ring.", new AcceptableValueRange<float>(0f, 100f)));
        AircraftBombSuppression = config.Bind("6. Ordnance effects", "AircraftBombSuppression", 70,
            new ConfigDescription("Maximum suppression added by an aircraft bomb's outer blast.", new AcceptableValueRange<int>(0, 255)));
        BlastCoverEffectMultiplier = config.Bind("6. Ordnance effects", "BlastCoverEffectMultiplier", 0.3f,
            new ConfigDescription("Outer-ring injury and suppression retained when terrain or a structure obstructs the blast.", new AcceptableValueRange<float>(0f, 1f)));

        BulletPenetrationEnabled = config.Bind("6e. Bullet penetration", "Enabled", true,
            "Lets player and AI small-arms projectiles continue through penetrable props. Native armor hits, terrain, water, bodies, bunkers, reinforced fortifications, and ricochets keep their base-game handling.");
        OrdinaryRoundPenetrationStrength = config.Bind("6e. Bullet penetration", "OrdinaryRoundPenetrationStrength", 1f,
            new ConfigDescription("Scales the material-and-thickness energy budget for ordinary pistol, SMG, rifle, and machine-gun ammunition.", new AcceptableValueRange<float>(0.25f, 3f)));
        ArmorPiercingPropPenetrationStrength = config.Bind("6e. Bullet penetration", "ArmorPiercingPropPenetrationStrength", 1f,
            new ConfigDescription("Scales the much larger prop-penetration budget for ammunition the game identifies as armor piercing. Bunkers, terrain, and vehicle armor remain protected hard stops.", new AcceptableValueRange<float>(0.25f, 4f)));
        MaximumPropPenetrations = config.Bind("6e. Bullet penetration", "MaximumPropPenetrations", 12,
            new ConfigDescription("Safety cap on distinct prop penetrations in one projectile chain. Energy and material thickness normally stop the round first.", new AcceptableValueRange<int>(1, 32)));
        AddedSmallArmsRicochetsEnabled = config.Bind("6e. Bullet penetration", "AddedSmallArmsRicochets", true,
            "Adds energy-losing shallow-angle ricochets for small arms when the game's native AP ricochet path does not trigger. Native ricochets always keep priority.");
        AddedRicochetChanceMultiplier = config.Bind("6e. Bullet penetration", "AddedRicochetChanceMultiplier", 1f,
            new ConfigDescription("Scales the chance of added small-arms ricochets from metal, masonry, earth, wood, and glass. Angle and material still determine whether a ricochet is possible.", new AcceptableValueRange<float>(0f, 2f)));

        TracerReductionEnabled = config.Bind("7. Weapon presentation", "MachineGunOnlyTracers", true,
            "Removes tracers from rifles, submachine guns, pistols, and other handheld non-machine-guns without changing their projectiles.");
        MachineGunTracerRetention = config.Bind("7. Weapon presentation", "MachineGunTracerRetention", 0.35f,
            new ConfigDescription("Fraction of base-game tracer rounds retained by recognized machine guns.", new AcceptableValueRange<float>(0f, 1f)));
        HitDecalDurationSeconds = config.Bind("7. Weapon presentation", "HitDecalDurationSeconds", 30,
            new ConfigDescription("How long bullet-hit decals remain before returning to the prefab pool.", new AcceptableValueRange<int>(1, 300)));

        PlayerSuppressionVignetteMultiplier = config.Bind("7a. Player suppression effects", "VignetteMultiplier", 2.14f,
            new ConfigDescription("Scales the dark suppression vignette for the local player. Zero disables the suppression-driven change, one preserves the base game, and higher values reach the base game's maximum vignette at lower suppression. Changes apply immediately to the current vignette.", new AcceptableValueRange<float>(0f, 4f)));
        PlayerSuppressionWobbleMultiplier = config.Bind("7a. Player suppression effects", "WeaponWobbleMultiplier", 2.151f,
            new ConfigDescription("Scales the brief first-person weapon and aim wobble caused by suppression. Zero disables it, one preserves the base game, and higher values make it stronger.", new AcceptableValueRange<float>(0f, 3f)));
        PlayerSuppressionNearMissRadiusMultiplier = config.Bind("7a. Player suppression effects", "NearMissRadiusMultiplier", 1.124f,
            new ConfigDescription("Expands the local player's bullet near-miss suppression radius around the first-person camera. One preserves the base game; two doubles the native caliber-dependent radius. AI suppression radii are unchanged.", new AcceptableValueRange<float>(1f, 4f)));
        PlayerSuppressionBlurEnabled = config.Bind("7a. Player suppression effects", "BlurEnabled", true,
            "Adds a temporary depth-of-field blur from suppression actually received by the local first-person player. It clears fully after incoming suppression stops and does not affect AI or HUD legibility.");
        PlayerSuppressionBlurStrength = config.Bind("7a. Player suppression effects", "BlurStrength", 0.094f,
            new ConfigDescription("Maximum weight of the local suppression blur. Effect weight scales linearly with fresh suppression received; zero disables the blur and one allows the strongest blend.", new AcceptableValueRange<float>(0f, 1f)));
        ShowPlayerSuppressionDirectionMarker = config.Bind("7a. Player suppression effects", "ShowDirectionMarker", true,
            "Shows the directional HUD marker for incoming suppression. Disable this to hide only the suppression marker while preserving damage direction indicators and all suppression mechanics.");

        LeaderOnlyOrderGestures = config.Bind("7b. AI animation restraint", "LeaderOnlyOrderGestures", true,
            "Prevents ordinary squad members from repeatedly playing command/pointing arm animations.");
        OrderGestureCooldownSeconds = config.Bind("7b. AI animation restraint", "LeaderOrderGestureCooldownSeconds", 10f,
            new ConfigDescription("Minimum time between command gestures by an AI squad leader.", new AcceptableValueRange<float>(2f, 30f)));

        BattleChatterEnabled = config.Bind("AI - Infantry tactics - Battle chatter", "Enabled", true,
            "Uses the game's localized voice banks for restrained, event-driven AI battlefield callouts.");
        ChatterIndividualCooldownSeconds = config.Bind("AI - Infantry tactics - Battle chatter", "IndividualCooldownSeconds", 24f,
            new ConfigDescription("Base minimum time between extra lines from the same soldier.", new AcceptableValueRange<float>(5f, 90f)));
        ChatterSquadCooldownSeconds = config.Bind("AI - Infantry tactics - Battle chatter", "SquadCooldownSeconds", 6f,
            new ConfigDescription("Base minimum time between extra lines in the same squad. This prevents chorus-like callouts.", new AcceptableValueRange<float>(1.5f, 20f)));
        ChatterContactCalloutChance = config.Bind("AI - Infantry tactics - Battle chatter", "NewContactCalloutChance", 0.7f,
            new ConfigDescription("Chance that a newly acquired infantry, armor, or artillery contact produces an extra native callout.", new AcceptableValueRange<float>(0f, 1f)));
        ChatterRoutineCalloutChance = config.Bind("AI - Infantry tactics - Battle chatter", "RoutineCalloutChance", 0.25f,
            new ConfigDescription("Chance at each randomized chatter opportunity while a soldier is doing something worth calling out.", new AcceptableValueRange<float>(0f, 1f)));
        ChatterRoutineMinimumSeconds = config.Bind("AI - Infantry tactics - Battle chatter", "RoutineMinimumIntervalSeconds", 16f,
            new ConfigDescription("Shortest interval between routine chatter opportunities for one soldier.", new AcceptableValueRange<float>(5f, 60f)));
        ChatterRoutineMaximumSeconds = config.Bind("AI - Infantry tactics - Battle chatter", "RoutineMaximumIntervalSeconds", 36f,
            new ConfigDescription("Longest interval between routine chatter opportunities for one soldier.", new AcceptableValueRange<float>(8f, 120f)));

        AudioBalanceEnabled = config.Bind("7d. Audio balance", "Enabled", true,
            "Rebalances vehicle-engine and weapon-fire loudness without changing audible distance or other sound categories.");
        VehicleEngineSound = BindVehicleEngineSound(config);
        TankTrackVolumeMultiplier = config.Bind("7d. Audio balance", "TankTrackVolumeMultiplier", 0.285f,
            new ConfigDescription("Multiplier for tank track and rolling loudness. Ground-vehicle engines, weapon fire, and wheeled-vehicle rolling sounds are unchanged.", new AcceptableValueRange<float>(0f, 2f)));
        AudioBalanceEnabled.SettingChanged += (_, _) => VehicleAudioBalance.RefreshTrackedSources();
        VehicleEngineSound.SettingChanged += (_, _) => VehicleAudioBalance.RefreshTrackedSources();
        TankTrackVolumeMultiplier.SettingChanged += (_, _) => VehicleAudioBalance.RefreshTrackedSources();
        WeaponFireVolumeMultiplier = config.Bind("7d. Audio balance", "WeaponFireVolumeMultiplier", 1.828f,
            new ConfigDescription("Multiplier for handheld and ordinary mounted weapon fire loudness.", new AcceptableValueRange<float>(0.5f, 3f)));
        TankGunVolumeMultiplier = config.Bind("7d. Audio balance", "TankGunVolumeMultiplier", 3f,
            new ConfigDescription("Multiplier for tank-mounted cannon fire of 20 mm caliber or larger. This is a total multiplier, not an additional multiplier on top of weapon fire.", new AcceptableValueRange<float>(0.5f, 4f)));
        DistantSoundShapingEnabled = config.Bind("7d. Audio balance", "DistantSoundShapingEnabled", true,
            "Adds progressive high-frequency air absorption to distant weapon fire, explosions, ground-vehicle engines, and tank tracks while preserving the game's original volume rolloff.");
        DistantSoundStartDistance = config.Bind("7d. Audio balance", "DistantSoundStartDistanceMeters", 10f,
            new ConfigDescription("Distance where high-frequency absorption begins.", new AcceptableValueRange<float>(0f, 250f)));
        DistantSoundFullEffectDistance = config.Bind("7d. Audio balance", "DistantSoundFullEffectDistanceMeters", 1000f,
            new ConfigDescription("Distance where the configured maximum distant filtering is reached.", new AcceptableValueRange<float>(100f, 2000f)));
        DistantSoundMinimumCutoff = config.Bind("7d. Audio balance", "DistantSoundMinimumCutoffHz", 2326.169f,
            new ConfigDescription("Low-pass cutoff reached at the full-effect distance. Lower values sound more muffled; higher values retain more crack and detail.", new AcceptableValueRange<float>(1000f, 12000f)));
        DistantReverbAmount = config.Bind("7d. Audio balance", "DistantReverbAmount", 0.558f,
            new ConfigDescription("Additional distant open-air reverb intensity for weapon fire and explosions. Zero disables the effect; one is the strongest mix.", new AcceptableValueRange<float>(0f, 1f)));
        PlayerFootstepVolumeMultiplier = config.Bind("7d. Audio balance", "PlayerFootstepVolumeMultiplier", 0.485f,
            new ConfigDescription("Volume multiplier for the locally controlled player's footsteps only. AI footsteps are unchanged.", new AcceptableValueRange<float>(0f, 2f)));

        FirstPersonPlayerShadowEnabled = config.Bind("7e. First-person view", "PlayerShadowEnabled", true,
            "Allows the locally controlled soldier's body and equipment to cast shadows in first-person view. Disable it to hide only the local player's first-person shadow; third-person and other soldiers are unchanged.");
        HoldBreathZoomMultiplier = config.Bind("7e. First-person view", "HoldBreathZoomMultiplier", 1.646f,
            new ConfigDescription("Strength of the extra first-person zoom while the hold-breath input is active (Shift by default). One preserves the base game, values above one zoom farther in, and values below one zoom less.", new AcceptableValueRange<float>(0.5f, 2f)));
        BinocularsEnabled = config.Bind("7e. First-person view", "BinocularsEnabled", true,
            "Enables first-person binoculars while alive and on foot.");
        BinocularsKey = config.Bind("7e. First-person view", "BinocularsKey", KeyCode.CapsLock,
            "Key that toggles first-person binoculars. Rebind it from the F10 settings menu.");
        BinocularZoomMultiplier = config.Bind("7e. First-person view", "BinocularZoomMultiplier", 10f,
            new ConfigDescription("Optical magnification of the binocular view. The overlay contains no range markings.", new AcceptableValueRange<float>(2f, 20f)));
        FreeLookEnabled = config.Bind("7e. First-person view", "FreeLookEnabled", true,
            "Enables hold-to-freelook while alive and on foot. Releasing the bound key smoothly recenters the view.");
        FreeLookKey = config.Bind("7e. First-person view", "FreeLookKey", KeyCode.LeftAlt,
            "Key held for first-person freelook. Rebind it from the F10 settings menu.");
        FreeLookHorizontalArcDegrees = config.Bind("7e. First-person view", "FreeLookHorizontalArcDegrees", 200f,
            new ConfigDescription("Total horizontal freelook arc. Two hundred degrees permits looking 100 degrees left or right without turning the soldier or weapon.", new AcceptableValueRange<float>(60f, 300f)));
        CompassAlwaysVisible = config.Bind("7e. First-person view", "CompassAlwaysVisible", true,
            "Keeps the scrolling bottom-screen heading compass visible during gameplay. When disabled, the bound compass key shows it for five seconds.");
        CompassKey = config.Bind("7e. First-person view", "CompassKey", KeyCode.K,
            "Key that shows the scrolling compass for five seconds. Rebind it from the F10 settings menu.");
        CompassUseMils = config.Bind("7e. First-person view", "CompassUseMils", true,
            "Uses NATO angular mils (0-6400) on the scrolling compass. Disable this option to show 0-360 degree bearings instead.");

        KeepMultiplayerPlayerNamesWithHudDisabled = config.Bind("7f. Multiplayer nameplates", "KeepPlayerNamesWithHudDisabled", true,
            "Keeps names above living allied remote players in multiplayer when the base-game HUD is disabled. Enemy and local-player names remain hidden.");

        KeepHighQualityDistantAnimations = config.Bind("7h. Animation quality", "KeepHighQualityDistantAnimations", true,
            "Keeps visible distant soldiers at the full animation refresh rate instead of using distance-based animation throttling. This can reduce performance in large battles.");

        ShowSettingsLauncherButton = config.Bind("7i. Settings menu", "ShowLauncherButton", true,
            "Shows the Realism Overhaul settings button at the bottom center of the screen. F10 opens the settings menu even when this button is hidden.");
        DisabledSwitchSnapshot = config.Bind("7i. Settings menu", "DisabledSwitchSnapshot", string.Empty,
            "Internal: remembers which system switches were off when DISABLE ALL SYSTEMS was last used, so ENABLE ALL SYSTEMS can restore them. Managed automatically; clear to forget.");

        AiDebugOverlayStartEnabled = config.Bind("AI - Diagnostics", "VisualDebugStartEnabled", false,
            "Starts the local visual AI debug layer when the plugin loads. The overlay is diagnostic-only and never changes AI decisions or synchronized gameplay.");
        AiDebugOverlayToggleKey = config.Bind("AI - Diagnostics", "VisualDebugToggleKey", KeyCode.F8,
            "Key that shows or hides the local visual AI debug layer during gameplay.");
        AiDebugOverlayMaximumDistance = config.Bind("AI - Diagnostics", "VisualDebugMaximumDistanceMeters", 300f,
            new ConfigDescription("Maximum camera distance at which AI entities are sampled and drawn. The live overlay hotkeys can temporarily adjust this value.", new AcceptableValueRange<float>(25f, 1500f)));
        AiDebugOverlayMaximumActors = config.Bind("AI - Diagnostics", "VisualDebugMaximumActors", 256,
            new ConfigDescription("Maximum nearby soldiers shown by the visual debug layer. Vehicles, aircraft, command leases, and aggregate counters have separate bounded displays.", new AcceptableValueRange<int>(1, 256)));
        AiDebugOverlayEventHistorySeconds = config.Bind("AI - Diagnostics", "VisualDebugEventHistorySeconds", 30f,
            new ConfigDescription("How long tactical decisions remain in the visual debug event feed.", new AcceptableValueRange<float>(5f, 180f)));
        VerboseLogging = config.Bind("AI - Diagnostics", "VerboseLogging", false,
            "Writes rate-limited tactical decisions to the BepInEx log for tuning.");

        StutterProbeEnabled = config.Bind("AI - Diagnostics", "StutterProbeEnabled", false,
            "Logs one line whenever a frame takes far longer than the recent average, recording which mod systems coincided with the spike. Diagnostic-only; disable once stutter hunting is finished.");
    }

    private static ConfigEntry<float> BindMeleeAdditionalReach(ConfigFile config)
    {
        const string section = "2d. Melee combat";
        const string legacyKey = "AdditionalReachMeters";
        const string currentKey = "ForwardReachExtensionMeters";
        const float legacyDefault = 0.25f;

        var legacyExists = ConfigFileContainsSetting(config.ConfigFilePath, section, legacyKey);
        var currentExists = ConfigFileContainsSetting(config.ConfigFilePath, section, currentKey);
        var description = new ConfigDescription(
            "Forward reach added to every base-game melee query. The default gives an ordinary strike roughly 1.72 m of total forward coverage and a bayonet roughly 2.08 m.",
            new AcceptableValueRange<float>(0f, 1.5f));
        var current = config.Bind(section, currentKey, 1f, description);

        if (!legacyExists)
            return current;

        var legacyDefinition = new ConfigDefinition(section, legacyKey);
        var legacy = config.Bind(
            legacyDefinition,
            legacyDefault,
            new ConfigDescription("Legacy melee reach extension; migrated to the longer forward-reach setting."));
        if (!currentExists && MathF.Abs(legacy.Value - legacyDefault) > 0.0001f)
            current.Value = legacy.Value;

        config.Remove(legacyDefinition);
        config.Save();
        return current;
    }

    private static ConfigEntry<float> BindSmgMovingFireRange(ConfigFile config)
    {
        const string section = "AI - Infantry tactics - Moving fire";
        const string legacySection = "1e. Movement fire";
        const string legacyKey = "SmgAndMachineGunMaxDistanceMeters";
        const string currentKey = "SmgMovingFireMaxDistanceMeters";

        var legacyExists = ConfigFileContainsSetting(config.ConfigFilePath, legacySection, legacyKey);
        var oldCurrentExists = ConfigFileContainsSetting(
            config.ConfigFilePath, legacySection, currentKey);
        var currentExists = ConfigFileContainsSetting(config.ConfigFilePath, section, currentKey);
        var description = new ConfigDescription(
            "Maximum visible-target distance at which an AI may fire an SMG while moving. Machine gunners halt and brace before firing.",
            new AcceptableValueRange<float>(3f, 50f));
        var current = config.Bind(section, currentKey, 20f, description);

        if (oldCurrentExists && !currentExists)
        {
            var oldDefinition = new ConfigDefinition(legacySection, currentKey);
            var oldCurrent = config.Bind(oldDefinition, 20f,
                new ConfigDescription("Legacy moving-fire section; migrated to AI infantry tactics."));
            current.Value = oldCurrent.Value;
            config.Remove(oldDefinition);
        }

        if (legacyExists)
        {
            var legacyDefinition = new ConfigDefinition(legacySection, legacyKey);
            var legacy = config.Bind(
                legacyDefinition,
                20f,
                new ConfigDescription("Legacy SMG/machine-gun moving-fire range; migrated to SMG-only moving fire."));
            if (!currentExists && !oldCurrentExists)
                current.Value = legacy.Value;

            config.Remove(legacyDefinition);
        }

        if (oldCurrentExists || legacyExists)
            config.Save();
        return current;
    }

    private static ConfigEntry<bool> BindSafeAiGrenadeThrows(ConfigFile config)
    {
        const string section = "AI - Infantry tactics - Combat safety";
        const string legacySection = "2b. Combat safety";
        const string legacyKey = "GrenadesFromCover";
        const string currentKey = "SafeAiGrenadeThrows";

        var legacyExists = ConfigFileContainsSetting(config.ConfigFilePath, legacySection, legacyKey);
        var oldCurrentExists = ConfigFileContainsSetting(
            config.ConfigFilePath, legacySection, currentKey);
        var currentExists = ConfigFileContainsSetting(config.ConfigFilePath, section, currentKey);
        var current = config.Bind(
            section,
            currentKey,
            true,
            "Allows range-checked, friendly-safe AI explosive-grenade throws from any stationary stance. Assaulting soldiers may make a brief crouched halt to throw.");

        if (oldCurrentExists && !currentExists)
        {
            var oldDefinition = new ConfigDefinition(legacySection, currentKey);
            var oldCurrent = config.Bind(oldDefinition, true,
                new ConfigDescription("Legacy combat-safety section; migrated to AI infantry tactics."));
            current.Value = oldCurrent.Value;
            config.Remove(oldDefinition);
        }

        if (legacyExists)
        {
            var legacyDefinition = new ConfigDefinition(legacySection, legacyKey);
            var legacy = config.Bind(
                legacyDefinition,
                true,
                new ConfigDescription("Legacy cover-only grenade setting; migrated to SafeAiGrenadeThrows."));
            if (!currentExists && !oldCurrentExists)
                current.Value = legacy.Value;

            config.Remove(legacyDefinition);
        }

        if (oldCurrentExists || legacyExists)
            config.Save();
        return current;
    }

    private static ConfigEntry<float> BindVehicleEngineSound(ConfigFile config)
    {
        const string section = "7d. Audio balance";
        const string legacyKey = "TankEngineVolumeMultiplier";
        const string currentKey = "VehicleEngineSound";

        var legacyExists = ConfigFileContainsSetting(config.ConfigFilePath, section, legacyKey);
        var currentExists = ConfigFileContainsSetting(config.ConfigFilePath, section, currentKey);
        var description = new ConfigDescription(
            "Multiplier for all ground-vehicle engine loudness, including tanks and wheeled vehicles. Aircraft engines and tank tracks are unchanged.",
            new AcceptableValueRange<float>(0.4f, 1.2f));
        var current = config.Bind(section, currentKey, 0.764f, description);

        if (!legacyExists)
            return current;

        var legacyDefinition = new ConfigDefinition(section, legacyKey);
        var legacy = config.Bind(
            legacyDefinition,
            0.82f,
            new ConfigDescription("Legacy tank-engine setting; migrated to VehicleEngineSound."));
        if (!currentExists)
            current.Value = legacy.Value;

        config.Remove(legacyDefinition);
        config.Save();
        return current;
    }

    private static bool ConfigFileContainsSetting(string path, string section, string key)
    {
        try
        {
            var currentSection = string.Empty;
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length >= 2 && line[0] == '[' && line[^1] == ']')
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }

                if (!string.Equals(currentSection, section, StringComparison.Ordinal))
                    continue;

                var separator = line.IndexOf('=');
                if (separator > 0 && string.Equals(
                        line[..separator].Trim(),
                        key,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
            // A missing or temporarily unavailable file behaves like a fresh config.
        }
        catch (UnauthorizedAccessException)
        {
            // Binding still works; only the optional legacy-value migration is skipped.
        }

        return false;
    }

    internal static ConfigEntryBase[] GetConfigEntries() => ConfigFile.Values.ToArray();
}
