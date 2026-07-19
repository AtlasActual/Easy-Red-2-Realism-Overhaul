using BepInEx.Configuration;

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

    internal static ConfigEntry<bool> ContactResponseEnabled = null!;
    internal static ConfigEntry<float> ContactImmediateFireDistance = null!;
    internal static ConfigEntry<float> ContactCoverSearchRadius = null!;
    internal static ConfigEntry<int> ContactCoverCandidateLimit = null!;
    internal static ConfigEntry<bool> ExclusiveCoverPositions = null!;
    internal static ConfigEntry<float> CoverOccupancyRadius = null!;
    internal static ConfigEntry<float> ContactDecisionInterval = null!;
    internal static ConfigEntry<float> ContactMoveCommitSeconds = null!;
    internal static ConfigEntry<float> ContactRelocationCooldownSeconds = null!;
    internal static ConfigEntry<float> ContactCoverHoldSeconds = null!;
    internal static ConfigEntry<float> StandingCoverPenalty = null!;
    internal static ConfigEntry<float> ContactEngagementHaltDistance = null!;
    internal static ConfigEntry<float> MaximumAttackCombatHaltSeconds = null!;
    internal static ConfigEntry<bool> KnownTargetSuppressionEnabled = null!;
    internal static ConfigEntry<bool> MovingFireRestrictionEnabled = null!;
    internal static ConfigEntry<float> AutomaticMovingFireMaxDistance = null!;
    internal static ConfigEntry<float> SmgMaximumEngagementDistance = null!;
    internal static ConfigEntry<bool> PreventReloadingAndBandagingWhileCrawling = null!;
    internal static ConfigEntry<bool> ImprovedMeleeHitRegistrationEnabled = null!;
    internal static ConfigEntry<float> MeleeAdditionalReach = null!;
    internal static ConfigEntry<float> MeleeMinimumSweepRadius = null!;

    internal static ConfigEntry<bool> ContactReportingEnabled = null!;
    internal static ConfigEntry<float> ContactReportLifetimeSeconds = null!;
    internal static ConfigEntry<bool> InterSquadContactSharingEnabled = null!;
    internal static ConfigEntry<float> NearbyVoiceRangeMeters = null!;
    internal static ConfigEntry<float> NearbyVoiceDelaySeconds = null!;
    internal static ConfigEntry<float> NearbyVoiceConfidenceMultiplier = null!;
    internal static ConfigEntry<float> DistantRadioDelaySeconds = null!;
    internal static ConfigEntry<float> DistantRadioMaximumRangeMeters = null!;
    internal static ConfigEntry<float> DistantRadioConfidenceMultiplier = null!;
    internal static ConfigEntry<float> DistantRadioPositionErrorMeters = null!;

    internal static ConfigEntry<bool> CommanderEnabled = null!;

    internal static ConfigEntry<bool> SuppressionAwarenessEnabled = null!;
    internal static ConfigEntry<float> SuppressedFovMultiplier = null!;
    internal static ConfigEntry<float> SuppressedPeripheralMultiplier = null!;
    internal static ConfigEntry<float> SuppressedMemoryMultiplier = null!;
    internal static ConfigEntry<float> SuppressedReportConfidence = null!;

    internal static ConfigEntry<bool> DangerReactionsEnabled = null!;
    internal static ConfigEntry<int> CrouchSuppression = null!;
    internal static ConfigEntry<int> ProneSuppression = null!;
    internal static ConfigEntry<int> ProneReleaseSuppression = null!;
    internal static ConfigEntry<float> PinnedMinimumSeconds = null!;
    internal static ConfigEntry<float> FlameSafetyMargin = null!;
    internal static ConfigEntry<float> FlameEscapeDistance = null!;
    internal static ConfigEntry<bool> MountedGunnerSuppressionEnabled = null!;
    internal static ConfigEntry<int> MountedGunnerDuckSuppression = null!;
    internal static ConfigEntry<int> MountedGunnerRiseSuppression = null!;
    internal static ConfigEntry<float> MountedGunnerMinimumDuckSeconds = null!;
    internal static ConfigEntry<float> MountedGunnerRiseSettleSeconds = null!;

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

    internal static ConfigEntry<bool> TankTacticsEnabled = null!;
    internal static ConfigEntry<float> TankStandoffDistance = null!;
    internal static ConfigEntry<float> TankReverseDistance = null!;
    internal static ConfigEntry<float> TankReverseSeconds = null!;
    internal static ConfigEntry<float> TankDamagedThreshold = null!;
    internal static ConfigEntry<float> TankMaximumHullFacingAngle = null!;
    internal static ConfigEntry<float> TankInfantryHoldDistance = null!;
    internal static ConfigEntry<float> TankAccelerationMultiplier = null!;
    internal static ConfigEntry<bool> StaticAtStaffingEnabled = null!;
    internal static ConfigEntry<float> StaticAtSearchRadius = null!;
    internal static ConfigEntry<float> StaticAtEnemyTankRange = null!;
    internal static ConfigEntry<float> StaticAtAssignmentCooldown = null!;
    internal static ConfigEntry<float> StaticAtMinimumCaliber = null!;

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

    internal static ConfigEntry<bool> AircraftSafetyEnabled = null!;
    internal static ConfigEntry<float> AircraftFriendlyAttackRadius = null!;
    internal static ConfigEntry<float> AircraftBombFriendlyRadius = null!;
    internal static ConfigEntry<bool> AircraftEvasionEnabled = null!;
    internal static ConfigEntry<float> AircraftEvasionSeconds = null!;
    internal static ConfigEntry<float> AircraftEvasionClimb = null!;

    internal static ConfigEntry<bool> AircraftFlightPhysicsEnabled = null!;
    internal static ConfigEntry<bool> AircraftPhysicsApplyToAi = null!;
    internal static ConfigEntry<bool> AircraftPhysicsApplyToOfflinePlayers = null!;
    internal static ConfigEntry<bool> AircraftPhysicsApplyToMultiplayerPlayers = null!;
    internal static ConfigEntry<bool> AircraftAdvancedTuningEnabled = null!;
    internal static ConfigEntry<float> AircraftPhysicsStrength = null!;
    internal static ConfigEntry<float> AircraftWorldSpeedScale = null!;
    internal static ConfigEntry<float> AircraftFighterSpeedMultiplier = null!;
    internal static ConfigEntry<float> AircraftBomberSpeedMultiplier = null!;
    internal static ConfigEntry<float> AircraftControlResponseMultiplier = null!;
    internal static ConfigEntry<float> AircraftEngineResponseMultiplier = null!;
    internal static ConfigEntry<float> AircraftEnginePowerMultiplier = null!;
    internal static ConfigEntry<bool> AircraftThrottleControlsEnginePower = null!;
    internal static ConfigEntry<float> AircraftThrottleReductionResponseMultiplier = null!;
    internal static ConfigEntry<float> AircraftEnergyLossMultiplier = null!;
    internal static ConfigEntry<bool> AircraftEnergyRetentionEnabled = null!;
    internal static ConfigEntry<float> AircraftNativeCoastDragMultiplier = null!;
    internal static ConfigEntry<float> AircraftNativeVelocityLossMultiplier = null!;
    internal static ConfigEntry<float> AircraftGlideEnergyLossMultiplier = null!;
    internal static ConfigEntry<float> AircraftMaximumEnergyRetentionAcceleration = null!;
    internal static ConfigEntry<bool> AircraftStallPhysicsEnabled = null!;
    internal static ConfigEntry<float> AircraftStallRecoveryPitchAuthority = null!;
    internal static ConfigEntry<float> AircraftStallRecoveryRollAuthority = null!;
    internal static ConfigEntry<float> AircraftStallNoseDropStrength = null!;
    internal static ConfigEntry<float> AircraftSpinStrength = null!;
    internal static ConfigEntry<float> AircraftSpinRecoverySpeedMultiplier = null!;
    internal static ConfigEntry<bool> AircraftDamagePhysicsEnabled = null!;
    internal static ConfigEntry<bool> AircraftAiEnergyManagementEnabled = null!;
    internal static ConfigEntry<bool> AircraftPhysicsTelemetryEnabled = null!;
    internal static ConfigEntry<float> AircraftPhysicsTelemetryInterval = null!;

    internal static ConfigEntry<bool> AircraftInstrumentHudEnabled = null!;
    internal static ConfigEntry<float> AircraftInstrumentHudScale = null!;
    internal static ConfigEntry<bool> AircraftInstrumentUseImperialUnits = null!;
    internal static ConfigEntry<bool> AircraftInstrumentShowAgl = null!;

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

    internal static ConfigEntry<bool> KeepMultiplayerPlayerNamesWithHudDisabled = null!;

    internal static ConfigEntry<float> AiRagdollWeightMultiplier = null!;

    internal static ConfigEntry<bool> ShowSettingsLauncherButton = null!;

    internal static ConfigEntry<bool> VerboseLogging = null!;

    internal static void Bind(ConfigFile config)
    {
        ConfigFile = config;

        AttackingForceBonusEnabled = config.Bind("0. Attack and defense balance", "AttackingForceBonusEnabled", true,
            "Gives AI units from the battle's attacking (invader) faction a modest proficiency bonus without changing health, damage, armor, or penetration.");
        AttackingForceAccuracySpreadMultiplier = config.Bind("0. Attack and defense balance", "AttackingForceAccuracySpreadMultiplier", 0.728f,
            new ConfigDescription("Multiplier applied to weapon spread for all attacking AI soldiers, including vehicle and emplacement crews. Lower values are more accurate; the default reduces spread by 27.2 percent.", new AcceptableValueRange<float>(0.6f, 1f)));
        AttackingForceSuppressionReceivedMultiplier = config.Bind("0. Attack and defense balance", "AttackingForceSuppressionReceivedMultiplier", 0.658f,
            new ConfigDescription("Multiplier applied to suppression received by attacking AI soldiers. Lower values make attackers harder to pin; the default reduces incoming suppression by 34.2 percent.", new AcceptableValueRange<float>(0.5f, 1f)));
        AttackingTankAdditionalAccuracySpreadMultiplier = config.Bind("0. Attack and defense balance", "AttackingTankAdditionalAccuracySpreadMultiplier", 0.91f,
            new ConfigDescription("Additional spread multiplier for attacking AI tank crews. Combined with the default force-wide multiplier, tanks retain about an 18 percent spread reduction.", new AcceptableValueRange<float>(0.7f, 1f)));

        PerceptionEnabled = config.Bind("1. Perception", "Enabled", true,
            "Requires AI to visually acquire a target before aiming or firing and stops indefinite target lock outside its forward field of view.");
        HorizontalFov = config.Bind("1. Perception", "HorizontalFovDegrees", 120f,
            new ConfigDescription("AI horizontal combat field of view.", new AcceptableValueRange<float>(60f, 240f)));
        CloseTargetAcquisitionSeconds = config.Bind("1. Perception", "CloseTargetAcquisitionSeconds", 1.1f,
            new ConfigDescription("Repeated valid observation required to acquire a nearby new target. Increasing this reduces close-range snap targeting.", new AcceptableValueRange<float>(0.15f, 5f)));
        DistantTargetAcquisitionSeconds = config.Bind("1. Perception", "DistantTargetAcquisitionSeconds", 1.356f,
            new ConfigDescription("Repeated valid observation required to acquire a new target at or beyond the distant-acquisition range. Time scales smoothly from the close value.", new AcceptableValueRange<float>(0.25f, 8f)));
        DistantTargetAcquisitionRange = config.Bind("1. Perception", "DistantTargetAcquisitionRangeMeters", 140f,
            new ConfigDescription("Distance at which the full distant target-acquisition time applies.", new AcceptableValueRange<float>(25f, 400f)));
        TargetMemorySeconds = config.Bind("1. Perception", "TargetMemorySeconds", 10f,
            new ConfigDescription("How long an AI may remember a target outside its FOV before losing fire authorization.", new AcceptableValueRange<float>(0f, 15f)));
        PeripheralAwarenessDistance = config.Bind("1. Perception", "PeripheralAwarenessDistance", 13.834f,
            new ConfigDescription("Targets this close remain noticeable even outside the normal FOV.", new AcceptableValueRange<float>(0f, 25f)));

        ContactResponseEnabled = config.Bind("1b. Contact response", "Enabled", true,
            "Coordinates cover selection, forward relocations, and close engagement halts when infantry make contact.");
        ContactImmediateFireDistance = config.Bind("1b. Contact response", "ImmediateFireDistanceMeters", 9f,
            new ConfigDescription("Inside this surprise-contact distance, an exposed soldier may return fire immediately instead of relocating first.", new AcceptableValueRange<float>(3f, 30f)));
        ContactCoverSearchRadius = config.Bind("1b. Contact response", "CoverSearchRadiusMeters", 18f,
            new ConfigDescription("Maximum radius searched for a threat-facing cover position after contact.", new AcceptableValueRange<float>(5f, 40f)));
        ContactCoverCandidateLimit = config.Bind("1b. Contact response", "CoverCandidateLimit", 16,
            new ConfigDescription("Maximum native cover candidates evaluated per decision.", new AcceptableValueRange<int>(4, 48)));
        ExclusiveCoverPositions = config.Bind("1b. Contact response", "ExclusiveCoverPositions", true,
            "Allows only one AI soldier to claim or occupy a native cover position at a time.");
        CoverOccupancyRadius = config.Bind("1b. Contact response", "CoverOccupancyRadiusMeters", 1.75f,
            new ConfigDescription("Minimum center-to-center spacing around a claimed cover position. Larger values reduce head-to-head crowding.", new AcceptableValueRange<float>(0.75f, 3.5f)));
        ContactDecisionInterval = config.Bind("1b. Contact response", "DecisionIntervalSeconds", 12f,
            new ConfigDescription("Deliberate delay between exposed-contact cover decisions; the 10-15 second range prevents rapid tactical churn.", new AcceptableValueRange<float>(10f, 15f)));
        ContactMoveCommitSeconds = config.Bind("1b. Contact response", "MoveCommitSeconds", 3.093f,
            new ConfigDescription("How long a soldier prioritizes reaching selected cover before reconsidering.", new AcceptableValueRange<float>(1f, 8f)));
        ContactRelocationCooldownSeconds = config.Bind("1b. Contact response", "RelocationCooldownSeconds", 9.973f,
            new ConfigDescription("Minimum time before an exposed soldier may choose another cover relocation.", new AcceptableValueRange<float>(2f, 20f)));
        ContactCoverHoldSeconds = config.Bind("1b. Contact response", "MinimumCoverHoldSeconds", 10f,
            new ConfigDescription("Minimum time a soldier with contact holds reached cover instead of shuffling to another position.", new AcceptableValueRange<float>(2f, 30f)));
        StandingCoverPenalty = config.Bind("1b. Contact response", "StandingCoverScorePenalty", 225f,
            new ConfigDescription("Score penalty for cover requiring a standing pose; higher values favor trench, crouched, and prone cover.", new AcceptableValueRange<float>(0f, 900f)));
        ContactEngagementHaltDistance = config.Bind("1b. Contact response", "EngagementHaltDistanceMeters", 160f,
            new ConfigDescription("Inside this distance, visible contact overrides ordinary attack waypoints and the soldier establishes a firing halt. A charge keeps moving except when a non-SMG soldier meets an immediate close threat.", new AcceptableValueRange<float>(40f, 300f)));
        MaximumAttackCombatHaltSeconds = config.Bind("1b. Contact response", "MaximumAttackCombatHaltSeconds", 12f,
            new ConfigDescription("Maximum continuous firing halt for a squad on an attack order before exposed troops resume forward progress. Heavily pinned attackers crawl; troops still seek forward cover and immediate close threats remain higher priority.", new AcceptableValueRange<float>(6f, 30f)));
        KnownTargetSuppressionEnabled = config.Bind("1b. Contact response", "SuppressKnownTargets", true,
            "Allows a stationary on-foot machine gunner to fire one bounded burst at a fresh, personally confirmed last-seen enemy position after sight is lost. It uses real ammunition and never tracks an unseen target.");

        MovingFireRestrictionEnabled = config.Bind("1e. Movement fire", "RestrictMovingFire", true,
            "Restricts handheld moving fire to recognized SMGs at close range. Riflemen and machine gunners halt before firing.");
        AutomaticMovingFireMaxDistance = BindSmgMovingFireRange(config);
        SmgMaximumEngagementDistance = config.Bind("1e. Movement fire", "SmgMaximumEngagementDistanceMeters", 80f,
            new ConfigDescription("Maximum distance at which AI may fire a submachine gun, whether stationary or moving.", new AcceptableValueRange<float>(30f, 180f)));
        PreventReloadingAndBandagingWhileCrawling = config.Bind("2c. Infantry movement", "PreventReloadingAndBandagingWhileCrawling", true,
            "Players must stop crawling before reloading or bandaging. AI soldiers automatically stop their crawl and then perform the action; stationary prone soldiers are unaffected.");
        ImprovedMeleeHitRegistrationEnabled = config.Bind("2d. Melee combat", "ImprovedHitRegistration", true,
            "Makes player and AI melee strikes use a longer and wider native hit query, reducing close-range ghost swings without changing melee damage.");
        MeleeAdditionalReach = BindMeleeAdditionalReach(config);
        MeleeMinimumSweepRadius = config.Bind("2d. Melee combat", "MinimumSweepRadiusMeters", 0.35f,
            new ConfigDescription("Minimum radius of the melee hit capsule. The base game uses 0.25 m; a modest increase forgives small animation and collider misalignments.", new AcceptableValueRange<float>(0.25f, 0.6f)));

        ContactReportingEnabled = config.Bind("1c. Contact reporting", "Enabled", true,
            "Records squad-local last-known-position reports without sharing a live target transform.");
        ContactReportLifetimeSeconds = config.Bind("1c. Contact reporting", "SquadReportLifetimeSeconds", 20f,
            new ConfigDescription("Time before an unrefreshed squad contact report expires.", new AcceptableValueRange<float>(2f, 90f)));
        InterSquadContactSharingEnabled = config.Bind("1c. Contact reporting", "InterSquadSharingEnabled", true,
            "Shares reports with nearby allied squads by voice and distant radio-equipped allied squads after realistic delays.");
        NearbyVoiceRangeMeters = config.Bind("1c. Contact reporting", "NearbyVoiceRangeMeters", 60f,
            new ConfigDescription("Maximum squad-leader distance for shouted contact reports.", new AcceptableValueRange<float>(10f, 150f)));
        NearbyVoiceDelaySeconds = config.Bind("1c. Contact reporting", "NearbyVoiceDelaySeconds", 1.25f,
            new ConfigDescription("Base interpretation delay before a nearby squad receives a voice report.", new AcceptableValueRange<float>(0.25f, 8f)));
        NearbyVoiceConfidenceMultiplier = config.Bind("1c. Contact reporting", "NearbyVoiceConfidenceMultiplier", 0.88f,
            new ConfigDescription("Confidence retained when a report is passed by voice.", new AcceptableValueRange<float>(0.25f, 1f)));
        DistantRadioDelaySeconds = config.Bind("1c. Contact reporting", "DistantRadioDelaySeconds", 5f,
            new ConfigDescription("Base command-network delay before a distant squad receives a radio report.", new AcceptableValueRange<float>(1f, 30f)));
        DistantRadioMaximumRangeMeters = config.Bind("1c. Contact reporting", "DistantRadioMaximumRangeMeters", 1500f,
            new ConfigDescription("Maximum distance for inter-squad radio reports; both squads need a working radio operator or radio-equipped leader.", new AcceptableValueRange<float>(100f, 5000f)));
        DistantRadioConfidenceMultiplier = config.Bind("1c. Contact reporting", "DistantRadioConfidenceMultiplier", 0.65f,
            new ConfigDescription("Confidence retained when a report crosses the radio network.", new AcceptableValueRange<float>(0.15f, 1f)));
        DistantRadioPositionErrorMeters = config.Bind("1c. Contact reporting", "DistantRadioPositionErrorMeters", 18f,
            new ConfigDescription("Maximum deterministic map-position error added to radio-delivered contacts.", new AcceptableValueRange<float>(0f, 75f)));

        CommanderEnabled = config.Bind("1f. High command", "Enabled", true,
            "Single switch for report-driven coordination of eligible host-controlled AI infantry, tanks, aircraft, artillery HE/APHE fire support, and smoke around one main objective per side. Strength, suppression, terrain, and congestion inform role allocation; player-controlled and mission-scripted units are excluded.");

        SuppressionAwarenessEnabled = config.Bind("1d. Suppression awareness", "Enabled", true,
            "Makes suppression narrow awareness, shorten target memory, and reduce contact-report confidence.");
        SuppressedFovMultiplier = config.Bind("1d. Suppression awareness", "FovMultiplierAtMaximumSuppression", 0.55f,
            new ConfigDescription("Horizontal FOV multiplier at maximum suppression.", new AcceptableValueRange<float>(0.3f, 1f)));
        SuppressedPeripheralMultiplier = config.Bind("1d. Suppression awareness", "PeripheralMultiplierAtMaximumSuppression", 0.45f,
            new ConfigDescription("Close peripheral-awareness multiplier at maximum suppression.", new AcceptableValueRange<float>(0.2f, 1f)));
        SuppressedMemoryMultiplier = config.Bind("1d. Suppression awareness", "MemoryMultiplierAtMaximumSuppression", 0.35f,
            new ConfigDescription("Target-memory duration multiplier at maximum suppression.", new AcceptableValueRange<float>(0.1f, 1f)));
        SuppressedReportConfidence = config.Bind("1d. Suppression awareness", "ReportConfidenceAtMaximumSuppression", 0.45f,
            new ConfigDescription("Initial confidence of a report made under maximum suppression.", new AcceptableValueRange<float>(0.1f, 1f)));

        DangerReactionsEnabled = config.Bind("2. Infantry danger", "Enabled", true,
            "Makes exposed soldiers get low for reloads, suppressed soldiers seek a lower stationary posture, recover from the initial shock to return fire, escape active flames, and dismount AI-led APCs before credible nearby contact.");
        CrouchSuppression = config.Bind("2. Infantry danger", "CrouchSuppressionThreshold", 35,
            new ConfigDescription("Suppression value that triggers crouching.", new AcceptableValueRange<int>(1, 254)));
        ProneSuppression = config.Bind("2. Infantry danger", "ProneSuppressionThreshold", 51,
            new ConfigDescription("Suppression value that triggers going prone.", new AcceptableValueRange<int>(2, 255)));
        ProneReleaseSuppression = config.Bind("2. Infantry danger", "ProneReleaseSuppressionThreshold", 25,
            new ConfigDescription("A pinned soldier remains prone until suppression falls below this lower threshold.", new AcceptableValueRange<int>(1, 254)));
        PinnedMinimumSeconds = config.Bind("2. Infantry danger", "PinnedMinimumSeconds", 6f,
            new ConfigDescription("Minimum commitment to a pinned stationary state before movement is reconsidered. Soldiers crouch behind valid cover and go prone when exposed.", new AcceptableValueRange<float>(1f, 20f)));
        FlameSafetyMargin = config.Bind("2. Infantry danger", "FlameSafetyMarginMeters", 2.5f,
            new ConfigDescription("Extra clearance added to a flame's damage radius.", new AcceptableValueRange<float>(0f, 10f)));
        FlameEscapeDistance = config.Bind("2. Infantry danger", "FlameEscapeDistanceMeters", 8f,
            new ConfigDescription("How far an AI attempts to move away from a nearby flame.", new AcceptableValueRange<float>(2f, 25f)));
        MountedGunnerSuppressionEnabled = config.Bind("2. Infantry danger", "MountedGunnerSuppressionDuck", true,
            "Allows AI turret and static-machine-gun users in native crouchable seats to duck under suppression.");
        MountedGunnerDuckSuppression = config.Bind("2. Infantry danger", "MountedGunnerDuckSuppressionThreshold", 45,
            new ConfigDescription("Suppression value at which an exposed AI mounted gunner ducks and ceases fire.", new AcceptableValueRange<int>(1, 254)));
        MountedGunnerRiseSuppression = config.Bind("2. Infantry danger", "MountedGunnerRiseSuppressionThreshold", 25,
            new ConfigDescription("Lower suppression value below which a ducked AI mounted gunner may rise again.", new AcceptableValueRange<int>(0, 253)));
        MountedGunnerMinimumDuckSeconds = config.Bind("2. Infantry danger", "MountedGunnerMinimumDuckSeconds", 2.25f,
            new ConfigDescription("Minimum time an AI mounted gunner remains ducked after reacting to suppression.", new AcceptableValueRange<float>(0.5f, 12f)));
        MountedGunnerRiseSettleSeconds = config.Bind("2. Infantry danger", "MountedGunnerRiseSettleSeconds", 0.4f,
            new ConfigDescription("Delay after rising before an AI mounted gunner may fire again.", new AcceptableValueRange<float>(0.1f, 2f)));

        FriendlyFireChecksEnabled = config.Bind("2b. Combat safety", "FriendlyFireChecks", true,
            "Withholds AI handheld and mounted fire while a friendly soldier occupies the firing lane.");
        FriendlyFireLaneRadius = config.Bind("2b. Combat safety", "HandheldLaneRadiusMeters", 0.9f,
            new ConfigDescription("Clearance around an AI handheld weapon's line of fire.", new AcceptableValueRange<float>(0.25f, 3f)));
        MountedFriendlyFireLaneRadius = config.Bind("2b. Combat safety", "MountedLaneRadiusMeters", 2.25f,
            new ConfigDescription("Clearance around mounted and aircraft gun lines of fire.", new AcceptableValueRange<float>(0.5f, 12f)));
        SafeAiGrenadeThrowsEnabled = BindSafeAiGrenadeThrows(config);
        GrenadeMinimumRange = config.Bind("2b. Combat safety", "GrenadeMinimumRangeMeters", 9f,
            new ConfigDescription("AI will not throw an explosive grenade at a closer target.", new AcceptableValueRange<float>(3f, 25f)));
        GrenadeMaximumRange = config.Bind("2b. Combat safety", "GrenadeMaximumRangeMeters", 42f,
            new ConfigDescription("AI will not attempt an implausibly long explosive-grenade throw.", new AcceptableValueRange<float>(15f, 75f)));
        GrenadeFriendlySafetyRadius = config.Bind("2b. Combat safety", "GrenadeFriendlySafetyRadiusMeters", 11f,
            new ConfigDescription("Required friendly clearance around the intended grenade impact.", new AcceptableValueRange<float>(4f, 25f)));
        GrenadeCooldownSeconds = config.Bind("2b. Combat safety", "GrenadeCooldownSeconds", 18f,
            new ConfigDescription("Minimum time between explosive-grenade throws by one AI soldier.", new AcceptableValueRange<float>(5f, 90f)));

        TankFearEnabled = config.Bind("3. Infantry vs tanks", "Enabled", false,
            "Makes non-AT infantry hide from nearby hostile tanks. Troops already in cover stay hidden; exposed troops seek one tank-masked position instead of repeatedly retreating.");
        TankAwarenessDistance = config.Bind("3. Infantry vs tanks", "AwarenessDistanceMeters", 120f,
            new ConfigDescription("Range at which infantry react to a hostile tank.", new AcceptableValueRange<float>(15f, 180f)));
        TankRetreatDistance = config.Bind("3. Infantry vs tanks", "RetreatDistanceMeters", 90f,
            new ConfigDescription("Range at which exposed non-AT infantry urgently seek tank-masked cover. The legacy setting name is retained for config compatibility.", new AcceptableValueRange<float>(5f, 140f)));
        TankEscapeDistance = config.Bind("3. Infantry vs tanks", "EscapeMoveMeters", 18f,
            new ConfigDescription("Minimum local search radius for tank-masked cover. The legacy setting name is retained for config compatibility.", new AcceptableValueRange<float>(4f, 35f)));

        TankTacticsEnabled = config.Bind("4. Tank tactics", "Enabled", true,
            "Makes AI tanks establish standoff and reverse when too close to an enemy tank or badly damaged, while tanks on attack orders keep pressure against infantry.");
        TankStandoffDistance = config.Bind("4. Tank tactics", "StopAndEngageDistanceMeters", 180f,
            new ConfigDescription("AI tanks stop advancing and rotate to engage enemy tanks inside this distance.", new AcceptableValueRange<float>(30f, 250f)));
        TankReverseDistance = config.Bind("4. Tank tactics", "ReverseDistanceMeters", 100f,
            new ConfigDescription("AI tanks reverse when an enemy tank is closer than this distance.", new AcceptableValueRange<float>(15f, 120f)));
        TankReverseSeconds = config.Bind("4. Tank tactics", "ReverseDurationSeconds", 3.5f,
            new ConfigDescription("Length of a tactical reverse.", new AcceptableValueRange<float>(1f, 10f)));
        TankDamagedThreshold = config.Bind("4. Tank tactics", "DamagedLifeFraction", 0.45f,
            new ConfigDescription("AI tanks may reverse while under threat below this fraction of hull life.", new AcceptableValueRange<float>(0.1f, 0.9f)));
        TankMaximumHullFacingAngle = config.Bind("4. Tank tactics", "MaximumHullFacingAngleDegrees", 30f,
            new ConfigDescription("Tank is considered frontally aligned when its hull points within this angle of an enemy tank; retreats preserve hull orientation and drive straight backward.", new AcceptableValueRange<float>(10f, 60f)));
        TankInfantryHoldDistance = config.Bind("4. Tank tactics", "HoldPositionAgainstInfantryMeters", 160f,
            new ConfigDescription("AI tanks without a forward attack order stop to engage visible infantry inside this range. Attacking tanks retain native fire-and-move behavior.", new AcceptableValueRange<float>(40f, 300f)));
        TankAccelerationMultiplier = config.Bind("4a. Tank physics", "AccelerationMultiplier", 0.35f,
            new ConfigDescription("Scales how quickly player and AI tanks reach their native motor torque without changing top speed or maximum torque. The 0.35 default makes the torque ramp about 2.86 times longer; 1.0 restores stock acceleration.", new AcceptableValueRange<float>(0.1f, 1f)));
        StaticAtStaffingEnabled = config.Bind("4b. Static anti-tank weapons", "Enabled", true,
            "Allows AI squads threatened by tanks to staff a nearby empty static anti-tank weapon.");
        StaticAtSearchRadius = config.Bind("4b. Static anti-tank weapons", "WeaponSearchRadiusMeters", 55f,
            new ConfigDescription("Maximum distance from the squad leader to an available static anti-tank weapon.", new AcceptableValueRange<float>(15f, 120f)));
        StaticAtEnemyTankRange = config.Bind("4b. Static anti-tank weapons", "EnemyTankResponseRangeMeters", 350f,
            new ConfigDescription("Enemy-tank range that makes staffing a nearby static anti-tank weapon urgent.", new AcceptableValueRange<float>(75f, 600f)));
        StaticAtAssignmentCooldown = config.Bind("4b. Static anti-tank weapons", "AssignmentCooldownSeconds", 12f,
            new ConfigDescription("Minimum delay between emplacement assignment checks for a squad.", new AcceptableValueRange<float>(3f, 60f)));
        StaticAtMinimumCaliber = config.Bind("4b. Static anti-tank weapons", "MinimumGunCaliberMm", 20f,
            new ConfigDescription("Minimum static-gun caliber considered suitable for anti-tank use.", new AcceptableValueRange<float>(12f, 75f)));

        SmokeSupportEnabled = config.Bind("5. Smoke support", "ExtraSmokeRequestsEnabled", true,
            "Allows a small number of additional AI smoke requests. This does not add HE or APHE fire missions.");
        SmokeRequestChance = config.Bind("5. Smoke support", "RequestChance", 0.08f,
            new ConfigDescription("Chance per eligible attack opportunity.", new AcceptableValueRange<float>(0f, 1f)));
        SmokeCooldownSeconds = config.Bind("5. Smoke support", "SquadCooldownSeconds", 240f,
            new ConfigDescription("Minimum time between extra smoke attempts by the same squad.", new AcceptableValueRange<float>(20f, 300f)));
        SmokeMinimumDistance = config.Bind("5. Smoke support", "MinimumTargetDistanceMeters", 25f,
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

        AircraftSafetyEnabled = config.Bind("6b. Aircraft safety", "SafeAttackRuns", true,
            "Makes AI aircraft reject targets and bomb releases whose immediate area contains friendly soldiers.");
        AircraftFriendlyAttackRadius = config.Bind("6b. Aircraft safety", "AttackFriendlyClearanceMeters", 18f,
            new ConfigDescription("Friendly clearance required around an aircraft's selected ground target.", new AcceptableValueRange<float>(5f, 60f)));
        AircraftBombFriendlyRadius = config.Bind("6b. Aircraft safety", "BombFriendlyClearanceMeters", 32f,
            new ConfigDescription("Friendly clearance required around a predicted AI bomb impact.", new AcceptableValueRange<float>(10f, 100f)));
        AircraftEvasionEnabled = config.Bind("6b. Aircraft safety", "ThreatEvasion", true,
            "Makes AI aircraft perform a short randomized climbing break when a hostile projectile passes nearby.");
        AircraftEvasionSeconds = config.Bind("6b. Aircraft safety", "EvasionDurationSeconds", 3.5f,
            new ConfigDescription("Duration of a threat-triggered aircraft break turn.", new AcceptableValueRange<float>(1f, 10f)));
        AircraftEvasionClimb = config.Bind("6b. Aircraft safety", "EvasionClimbBias", 0.35f,
            new ConfigDescription("Upward component of the evasive direction.", new AcceptableValueRange<float>(0f, 1f)));

        AircraftFlightPhysicsEnabled = config.Bind("6c. Aircraft flight physics", "Enabled", true,
            "Adds energy loss, speed-dependent control authority, progressive stalls, aerodynamic damping, and damage-sensitive handling while preserving the native vehicle controller.");
        AircraftPhysicsApplyToAi = config.Bind("6c. Aircraft flight physics", "ApplyToAiAircraft", true,
            "Applies aerodynamic, energy, stall, spin, and damage physics to AI aircraft while preserving their native mission controller and target-heading commands.");
        AircraftPhysicsApplyToOfflinePlayers = config.Bind("6c. Aircraft flight physics", "ApplyToPlayerAircraftOffline", true,
            "Applies the flight model to player-controlled aircraft while the game is offline.");
        AircraftPhysicsApplyToMultiplayerPlayers = config.Bind("6c. Aircraft flight physics", "ApplyToPlayerAircraftMultiplayer", false,
            "Experimental: applies the flight model to human-controlled aircraft on the multiplayer master client. Leave disabled until network synchronization has been tested with unmodified clients.");
        AircraftAdvancedTuningEnabled = config.Bind("6c. Aircraft flight physics", "UseAdvancedConfigTuning", false,
            "Uses the low-level aircraft values grouped in the in-game Advanced tab. Leave disabled for the coherent built-in flight preset so detailed values cannot counteract one another accidentally.");
        AircraftPhysicsStrength = config.Bind("6c. Aircraft flight physics", "RealismStrength", 1.221f,
            new ConfigDescription("Overall strength of corrective aerodynamic forces and control-rate limits. Zero leaves only the native model; one applies the original built-in strength.", new AcceptableValueRange<float>(0f, 2f)));
        AircraftWorldSpeedScale = config.Bind("6c. Aircraft flight physics", "WorldSpeedScale", 1.1f,
            new ConfigDescription("Scales aircraft propulsion and the full flight-speed envelope. Lower this when aircraft cross the map too quickly for its apparent scale.", new AcceptableValueRange<float>(0.65f, 1.35f)));
        AircraftFighterSpeedMultiplier = config.Bind("6c. Aircraft flight physics", "FighterSpeedMultiplier", 1f,
            new ConfigDescription("Additional speed-envelope multiplier for fighters.", new AcceptableValueRange<float>(0.75f, 1.25f)));
        AircraftBomberSpeedMultiplier = config.Bind("6c. Aircraft flight physics", "BomberSpeedMultiplier", 1f,
            new ConfigDescription("Additional speed-envelope multiplier for bombers.", new AcceptableValueRange<float>(0.75f, 1.25f)));
        AircraftControlResponseMultiplier = config.Bind("6c. Aircraft flight physics", "NativeControlResponseMultiplier", 0.685f,
            new ConfigDescription("Multiplier applied once to the native control-response coefficient. Lower values make rotation less immediate.", new AcceptableValueRange<float>(0.45f, 1f)));
        AircraftEngineResponseMultiplier = config.Bind("6c. Aircraft flight physics", "EngineResponseTimeMultiplier", 1.25f,
            new ConfigDescription("Multiplier for the native zero-to-maximum-thrust response time.", new AcceptableValueRange<float>(1f, 2.5f)));
        AircraftEnginePowerMultiplier = config.Bind("6c. Aircraft flight physics", "EnginePowerMultiplier", 2f,
            new ConfigDescription("Multiplier for physically available propulsive power. Higher values improve acceleration, climb, and sustained-turn performance, while the propeller curve remains power-limited at speed and below aircraft weight at low speed.", new AcceptableValueRange<float>(0.5f, 3f)));
        AircraftThrottleControlsEnginePower = config.Bind("6c. Aircraft flight physics", "ThrottleControlsEnginePower", true,
            "Treats throttle as an engine-power command instead of a target airspeed. Propulsive force remains available above the native throttle-proportional speed limit, while aerodynamic drag determines the resulting speed.");
        AircraftThrottleReductionResponseMultiplier = config.Bind("6c. Aircraft flight physics", "ThrottleReductionResponseMultiplier", 1.8f,
            new ConfigDescription("Additional smoothing applied only when commanded thrust is decreasing. Higher values make power reduction and the transition into a glide less abrupt; engine shutdown and propeller loss bypass it.", new AcceptableValueRange<float>(1f, 4f)));
        AircraftEnergyLossMultiplier = config.Bind("6c. Aircraft flight physics", "ManeuverEnergyLossMultiplier", 1.158f,
            new ConfigDescription("Scales parasite, induced, sideslip, landing-gear, and overspeed drag added by the flight model.", new AcceptableValueRange<float>(0f, 2f)));
        AircraftEnergyRetentionEnabled = config.Bind("6c. Aircraft flight physics", "EnergyRetentionEnabled", true,
            "Prevents the native controller from bleeding implausible amounts of total energy as soon as the throttle is reduced, while preserving configured maneuver, stall, gear, and damage drag.");
        AircraftNativeCoastDragMultiplier = config.Bind("6c. Aircraft flight physics", "NativeCoastDragMultiplier", 0.193f,
            new ConfigDescription("Multiplier for the native aircraft fall/coast drag. Lower values retain speed longer after reducing throttle; the realism model continues to supply aerodynamic drag.", new AcceptableValueRange<float>(0f, 1f)));
        AircraftNativeVelocityLossMultiplier = config.Bind("6c. Aircraft flight physics", "NativeVelocityLossMultiplier", 0f,
            new ConfigDescription("Fraction of the native per-tick velocity subtraction retained in addition to force-based aerodynamic drag. Zero removes the artificial speed rewrite; one restores the stock rewrite.", new AcceptableValueRange<float>(0f, 1f)));
        AircraftGlideEnergyLossMultiplier = config.Bind("6c. Aircraft flight physics", "GlideEnergyLossMultiplier", 0.706f,
            new ConfigDescription("Scales the permitted clean power-off glide energy loss. Lower values retain more speed and altitude; higher values produce a steeper glide.", new AcceptableValueRange<float>(0.25f, 2.5f)));
        AircraftMaximumEnergyRetentionAcceleration = config.Bind("6c. Aircraft flight physics", "MaximumEnergyRetentionAcceleration", 6f,
            new ConfigDescription("Maximum non-propulsive acceleration used to cancel excessive native energy loss after reducing throttle. It fades out near full throttle and is disabled during stalls, overspeed, and discontinuities.", new AcceptableValueRange<float>(0f, 12f)));
        AircraftStallPhysicsEnabled = config.Bind("6c. Aircraft flight physics", "ProgressiveStalls", true,
            "Reduces low-speed control authority and adds progressive drag, nose drop, and recoverable autorotation beyond the aircraft profile's critical angle of attack.");
        AircraftStallRecoveryPitchAuthority = config.Bind("6c. Aircraft flight physics", "StallRecoveryPitchAuthority", 0.741f,
            new ConfigDescription("Minimum pitch authority retained in a developed stall so the nose can be unloaded for recovery.", new AcceptableValueRange<float>(0.35f, 1f)));
        AircraftStallRecoveryRollAuthority = config.Bind("6c. Aircraft flight physics", "StallRecoveryRollAuthority", 0.801f,
            new ConfigDescription("Minimum roll authority retained in a developed stall, including inverted-stall recovery.", new AcceptableValueRange<float>(0.40f, 1f)));
        AircraftStallNoseDropStrength = config.Bind("6c. Aircraft flight physics", "StallNoseDropStrength", 0.715f,
            new ConfigDescription("Strength of the aerodynamic pitching moment that forces the nose toward the flight path during a developed stall, including inverted stalls.", new AcceptableValueRange<float>(0f, 2f)));
        AircraftSpinStrength = config.Bind("6c. Aircraft flight physics", "SpinStrength", 0.762f,
            new ConfigDescription("Strength of coupled yaw and roll autorotation in a developed stall. Zero disables forced spin moments without disabling the rest of the stall model.", new AcceptableValueRange<float>(0f, 2f)));
        AircraftSpinRecoverySpeedMultiplier = config.Bind("6c. Aircraft flight physics", "SpinRecoverySpeedMultiplier", 1.179f,
            new ConfigDescription("Forward airspeed, as a multiple of stall speed, required before an unloaded aircraft can stop autorotating.", new AcceptableValueRange<float>(1.05f, 1.60f)));
        AircraftDamagePhysicsEnabled = config.Bind("6c. Aircraft flight physics", "DamageAffectsHandling", true,
            "Makes detached wings, tails, and propellers produce asymmetric lift, instability, drag, and power loss.");
        AircraftAiEnergyManagementEnabled = config.Bind("6c. Aircraft flight physics", "AiEnergyManagement", true,
            "Lets AI aircraft lower the nose in a developed stall and begin terrain-aware dive recovery without replacing ordinary waypoint, climb, or attack headings.");
        AircraftPhysicsTelemetryEnabled = config.Bind("6c. Aircraft flight physics", "TelemetryLogging", false,
            "Writes rate-limited aircraft speed, angle-of-attack, sideslip, control-authority, stall, damage, and altitude data to the BepInEx log.");
        AircraftPhysicsTelemetryInterval = config.Bind("6c. Aircraft flight physics", "TelemetryIntervalSeconds", 1f,
            new ConfigDescription("Time between telemetry lines for each active aircraft.", new AcceptableValueRange<float>(0.25f, 10f)));

        AircraftInstrumentHudEnabled = config.Bind("6d. Aircraft instruments", "Enabled", true,
            "Shows compact airspeed and altitude instruments on the left side of the screen while piloting an aircraft.");
        AircraftInstrumentHudScale = config.Bind("6d. Aircraft instruments", "HudScale", 1f,
            new ConfigDescription("Scale of the left-side aircraft instrument cards.", new AcceptableValueRange<float>(0.65f, 1.50f)));
        AircraftInstrumentUseImperialUnits = config.Bind("6d. Aircraft instruments", "UseKnotsAndFeet", false,
            "Displays airspeed in knots and altitude in feet instead of kilometres per hour and metres.");
        AircraftInstrumentShowAgl = config.Bind("6d. Aircraft instruments", "ShowAltitudeAboveGround", true,
            "Adds radar-style height above the terrain or structure directly below the aircraft to the altitude card.");

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

        PlayerSuppressionVignetteMultiplier = config.Bind("7a. Player suppression effects", "VignetteMultiplier", 2.549f,
            new ConfigDescription("Scales the dark suppression vignette for the local player. Zero disables the suppression-driven change, one preserves the base game, and higher values reach the base game's maximum vignette at lower suppression.", new AcceptableValueRange<float>(0f, 4f)));
        PlayerSuppressionWobbleMultiplier = config.Bind("7a. Player suppression effects", "WeaponWobbleMultiplier", 2.151f,
            new ConfigDescription("Scales the brief first-person weapon and aim wobble caused by suppression. Zero disables it, one preserves the base game, and higher values make it stronger.", new AcceptableValueRange<float>(0f, 3f)));
        ShowPlayerSuppressionDirectionMarker = config.Bind("7a. Player suppression effects", "ShowDirectionMarker", false,
            "Shows the directional HUD marker for incoming suppression. Disable this to hide only the suppression marker while preserving damage direction indicators and all suppression mechanics.");

        LeaderOnlyOrderGestures = config.Bind("7b. AI animation restraint", "LeaderOnlyOrderGestures", true,
            "Prevents ordinary squad members from repeatedly playing command/pointing arm animations.");
        OrderGestureCooldownSeconds = config.Bind("7b. AI animation restraint", "LeaderOrderGestureCooldownSeconds", 10f,
            new ConfigDescription("Minimum time between command gestures by an AI squad leader.", new AcceptableValueRange<float>(2f, 30f)));

        BattleChatterEnabled = config.Bind("7c. AI battle chatter", "Enabled", true,
            "Uses the game's localized voice banks for restrained, event-driven AI battlefield callouts.");
        ChatterIndividualCooldownSeconds = config.Bind("7c. AI battle chatter", "IndividualCooldownSeconds", 24f,
            new ConfigDescription("Base minimum time between extra lines from the same soldier.", new AcceptableValueRange<float>(5f, 90f)));
        ChatterSquadCooldownSeconds = config.Bind("7c. AI battle chatter", "SquadCooldownSeconds", 6f,
            new ConfigDescription("Base minimum time between extra lines in the same squad. This prevents chorus-like callouts.", new AcceptableValueRange<float>(1.5f, 20f)));
        ChatterContactCalloutChance = config.Bind("7c. AI battle chatter", "NewContactCalloutChance", 0.7f,
            new ConfigDescription("Chance that a newly acquired infantry, armor, or artillery contact produces an extra native callout.", new AcceptableValueRange<float>(0f, 1f)));
        ChatterRoutineCalloutChance = config.Bind("7c. AI battle chatter", "RoutineCalloutChance", 0.25f,
            new ConfigDescription("Chance at each randomized chatter opportunity while a soldier is doing something worth calling out.", new AcceptableValueRange<float>(0f, 1f)));
        ChatterRoutineMinimumSeconds = config.Bind("7c. AI battle chatter", "RoutineMinimumIntervalSeconds", 16f,
            new ConfigDescription("Shortest interval between routine chatter opportunities for one soldier.", new AcceptableValueRange<float>(5f, 60f)));
        ChatterRoutineMaximumSeconds = config.Bind("7c. AI battle chatter", "RoutineMaximumIntervalSeconds", 36f,
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
        TankGunVolumeMultiplier = config.Bind("7d. Audio balance", "TankGunVolumeMultiplier", 2f,
            new ConfigDescription("Multiplier for tank-mounted cannon fire of 20 mm caliber or larger. This is a total multiplier, not an additional multiplier on top of weapon fire.", new AcceptableValueRange<float>(0.5f, 3f)));
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

        FirstPersonPlayerShadowEnabled = config.Bind("7e. First-person view", "PlayerShadowEnabled", false,
            "Allows the locally controlled soldier's body and equipment to cast shadows in first-person view. Disable it to hide only the local player's first-person shadow; third-person and other soldiers are unchanged.");
        HoldBreathZoomMultiplier = config.Bind("7e. First-person view", "HoldBreathZoomMultiplier", 1.646f,
            new ConfigDescription("Strength of the extra first-person zoom while the hold-breath input is active (Shift by default). One preserves the base game, values above one zoom farther in, and values below one zoom less.", new AcceptableValueRange<float>(0.5f, 2f)));

        KeepMultiplayerPlayerNamesWithHudDisabled = config.Bind("7f. Multiplayer nameplates", "KeepPlayerNamesWithHudDisabled", true,
            "Keeps names above living allied remote players in multiplayer when the base-game HUD is disabled. Enemy and local-player names remain hidden.");

        AiRagdollWeightMultiplier = config.Bind("7g. AI ragdoll physics", "AiRagdollWeightMultiplier", 2.084f,
            new ConfigDescription("Mass multiplier for every rigidbody in a dead AI soldier's ragdoll. One preserves the base game; higher values make bodies harder to push or throw. Player ragdolls and living AI are unchanged.", new AcceptableValueRange<float>(1f, 5f)));

        KeepHighQualityDistantAnimations = config.Bind("7h. Animation quality", "KeepHighQualityDistantAnimations", false,
            "Keeps visible distant soldiers at the full animation refresh rate instead of using distance-based animation throttling. This can reduce performance in large battles.");

        ShowSettingsLauncherButton = config.Bind("7i. Settings menu", "ShowLauncherButton", true,
            "Shows the Realism Overhaul settings button at the bottom center of the screen. F10 opens the settings menu even when this button is hidden.");

        VerboseLogging = config.Bind("8. Diagnostics", "VerboseLogging", false,
            "Writes rate-limited tactical decisions to the BepInEx log for tuning.");
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
            "Forward reach added to every base-game melee query. The default gives an ordinary strike roughly 1.32 m of total forward coverage and a bayonet roughly 1.68 m.",
            new AcceptableValueRange<float>(0f, 1f));
        var current = config.Bind(section, currentKey, 0.6f, description);

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
        const string section = "1e. Movement fire";
        const string legacyKey = "SmgAndMachineGunMaxDistanceMeters";
        const string currentKey = "SmgMovingFireMaxDistanceMeters";

        var legacyExists = ConfigFileContainsSetting(config.ConfigFilePath, section, legacyKey);
        var currentExists = ConfigFileContainsSetting(config.ConfigFilePath, section, currentKey);
        var description = new ConfigDescription(
            "Maximum visible-target distance at which an AI may fire an SMG while moving. Machine gunners halt and brace before firing.",
            new AcceptableValueRange<float>(3f, 50f));
        var current = config.Bind(section, currentKey, 20f, description);

        if (!legacyExists)
            return current;

        var legacyDefinition = new ConfigDefinition(section, legacyKey);
        var legacy = config.Bind(
            legacyDefinition,
            20f,
            new ConfigDescription("Legacy SMG/machine-gun moving-fire range; migrated to SMG-only moving fire."));
        if (!currentExists)
            current.Value = legacy.Value;

        config.Remove(legacyDefinition);
        config.Save();
        return current;
    }

    private static ConfigEntry<bool> BindSafeAiGrenadeThrows(ConfigFile config)
    {
        const string section = "2b. Combat safety";
        const string legacyKey = "GrenadesFromCover";
        const string currentKey = "SafeAiGrenadeThrows";

        var legacyExists = ConfigFileContainsSetting(config.ConfigFilePath, section, legacyKey);
        var currentExists = ConfigFileContainsSetting(config.ConfigFilePath, section, currentKey);
        var current = config.Bind(
            section,
            currentKey,
            true,
            "Allows range-checked, friendly-safe AI explosive-grenade throws from any stationary stance. Assaulting soldiers may make a brief crouched halt to throw.");

        if (!legacyExists)
            return current;

        var legacyDefinition = new ConfigDefinition(section, legacyKey);
        var legacy = config.Bind(
            legacyDefinition,
            true,
            new ConfigDescription("Legacy cover-only grenade setting; migrated to SafeAiGrenadeThrows."));
        if (!currentExists)
            current.Value = legacy.Value;

        config.Remove(legacyDefinition);
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
