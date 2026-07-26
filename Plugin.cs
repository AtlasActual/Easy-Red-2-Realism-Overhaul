using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.Reflection;

namespace ER2RealismOverhaul;

internal static class RuntimeLifecycle
{
    private static int _isQuitting;

    internal static bool IsQuitting
    {
        get => Volatile.Read(ref _isQuitting) != 0;
        set => Volatile.Write(ref _isQuitting, value ? 1 : 0);
    }
}

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "ca.antoi.er2.tacticalai";
    public const string PluginName = "Easy Red 2 Realism Overhaul";
    public const string PluginVersion = "1.0.5";

    internal static ManualLogSource LogSource { get; private set; } = null!;
    private Harmony? _harmony;
    private AtmosphericParticlePersistenceController? _atmosphericParticlePersistenceController;
    private SettingsSyncController? _settingsSyncController;
    private SettingsMenuController? _settingsMenuController;
    private AircraftFlightInstrumentsController? _aircraftFlightInstrumentsController;
    private FirstPersonPlayerShadowController? _firstPersonPlayerShadowController;
    private PlayerViewFeaturesController? _playerViewFeaturesController;
    private PlayerSuppressionBlurController? _playerSuppressionBlurController;
    private PlayerHeadshotBlackoutController? _playerHeadshotBlackoutController;
    private MultiplayerPlayerNameController? _multiplayerPlayerNameController;
    private MultiplayerSharedSquadController? _multiplayerSharedSquadController;
    private BulletPenetrationController? _bulletPenetrationController;
    private AiDebugOverlayController? _aiDebugOverlayController;
    private IncrementalGarbageCollectionController? _incrementalGarbageCollectionController;

    public override void Load()
    {
        LogSource = Log;
        StartupSplashSkipper.TrySkip();
        Settings.Bind(Config);
        SettingsCatalog.Initialize();

        _harmony = new Harmony(PluginGuid);
        if (Settings.InstallGameplayPatches.Value &&
            Settings.DeferredInteropHandleCleanupEnabled.Value)
        {
            InteropFinalizerReaper.TryInstall(_harmony);
        }

        _atmosphericParticlePersistenceController = AddComponent<AtmosphericParticlePersistenceController>();
        _settingsSyncController = AddComponent<SettingsSyncController>();
        _settingsMenuController = AddComponent<SettingsMenuController>();
        _aircraftFlightInstrumentsController = AddComponent<AircraftFlightInstrumentsController>();
        _firstPersonPlayerShadowController = AddComponent<FirstPersonPlayerShadowController>();
        _playerViewFeaturesController = AddComponent<PlayerViewFeaturesController>();
        _playerSuppressionBlurController = AddComponent<PlayerSuppressionBlurController>();
        _playerHeadshotBlackoutController = AddComponent<PlayerHeadshotBlackoutController>();
        _multiplayerPlayerNameController = AddComponent<MultiplayerPlayerNameController>();
        _multiplayerSharedSquadController = AddComponent<MultiplayerSharedSquadController>();
        _bulletPenetrationController = AddComponent<BulletPenetrationController>();
        _aiDebugOverlayController = AddComponent<AiDebugOverlayController>();
        _incrementalGarbageCollectionController = AddComponent<IncrementalGarbageCollectionController>();

        PatchModules(_harmony, typeof(Plugin).Assembly);

        Log.LogInfo($"{PluginName} {PluginVersion} loaded. attackPostureBonus={Settings.AttackingForceBonusEnabled.Value}, " +
                    $"FOV={Settings.PerceptionEnabled.Value}, " +
                    $"contact={Settings.ContactResponseEnabled.Value}, " +
                    $"staticWeaponStaffing={Settings.StaticWeaponStaffingEnabled.Value}, " +
                    $"danger={Settings.DangerReactionsEnabled.Value}, " +
                    $"gunnerDuck={Settings.MountedGunnerSuppressionEnabled.Value}, " +
                    $"fireSafety={Settings.FriendlyFireChecksEnabled.Value}, safeGrenades={Settings.SafeAiGrenadeThrowsEnabled.Value}, " +
                    $"tankTactics={Settings.TankTacticsEnabled.Value}, tankAcceleration={Settings.TankAccelerationMultiplier.Value:F2}x, smoke={Settings.SmokeSupportEnabled.Value}, " +
                    $"longBarrage={Settings.ArtilleryMissionLengthEnabled.Value}, persistentSmoke={Settings.SmokePersistenceEnabled.Value}, " +
                    $"persistentDust={Settings.ExplosionDustPersistenceEnabled.Value}, " +
                    $"largeCraters={Settings.HeavyOrdnanceCratersEnabled.Value}, " +
                    $"layeredBlast={Settings.LayeredBlastEffectsEnabled.Value}, " +
                    $"fragmentation={Settings.EnhancedFragmentationEnabled.Value}, " +
                    $"aircraftPhysics={Settings.AircraftFlightPhysicsEnabled.Value}, " +
                    $"bulletPenetration={Settings.BulletPenetrationEnabled.Value}, " +
                    $"addedRicochets={Settings.AddedSmallArmsRicochetsEnabled.Value}, " +
                    $"tracers={Settings.TracerReductionEnabled.Value}, chatter={Settings.BattleChatterEnabled.Value}, " +
                    $"playerSuppressionVignette={Settings.PlayerSuppressionVignetteMultiplier.Value}, " +
                    $"playerSuppressionWobble={Settings.PlayerSuppressionWobbleMultiplier.Value}, " +
                    $"playerSuppressionRadius={Settings.PlayerSuppressionNearMissRadiusMultiplier.Value:F2}x, " +
                    $"playerSuppressionBlur={Settings.PlayerSuppressionBlurEnabled.Value}, " +
                    $"playerSuppressionDirectionMarker={Settings.ShowPlayerSuppressionDirectionMarker.Value}, " +
                    $"aiCasualtySuppression={Settings.AiCasualtySuppressionEnabled.Value}, " +
                    $"meleeHitRegistration={Settings.ImprovedMeleeHitRegistrationEnabled.Value}, " +
                    $"meleeReachExtension={Settings.MeleeAdditionalReach.Value:F2}m, " +
                    $"firstPersonPlayerShadow={Settings.FirstPersonPlayerShadowEnabled.Value}, " +
                    $"binoculars={Settings.BinocularsEnabled.Value}, binocularZoom={Settings.BinocularZoomMultiplier.Value:F1}x, " +
                    $"freeLook={Settings.FreeLookEnabled.Value}, freeLookArc={Settings.FreeLookHorizontalArcDegrees.Value:F0}deg, " +
                    $"compassAlwaysVisible={Settings.CompassAlwaysVisible.Value}, " +
                    $"compassUnits={(Settings.CompassUseMils.Value ? "mils" : "degrees")}, " +
                    $"namesWithHudDisabled={Settings.KeepMultiplayerPlayerNamesWithHudDisabled.Value}, " +
                    $"highQualityDistantAnimations={Settings.KeepHighQualityDistantAnimations.Value}, " +
                    $"audioBalance={Settings.AudioBalanceEnabled.Value}, " +
                    $"deferredInteropCleanup={Settings.DeferredInteropHandleCleanupEnabled.Value}");
    }

    // Patch modules that stay installed even when gameplay patching is switched off:
    // the probe is how a patches-off run is measured, so removing it would make the
    // comparison the switch exists for impossible.
    private static readonly HashSet<Type> DiagnosticPatchTypes = new()
    {
        typeof(StutterProbeUpdatePatch)
    };

    private static void PatchModules(Harmony harmony, Assembly assembly)
    {
        // Per-system switches gate patch BODIES; the Harmony detour, its il2cpp->managed
        // transition and its argument marshalling are paid on every call regardless. Only
        // skipping installation outright makes the mod genuinely inert, which is what
        // isolating a stutter to the mod (or clearing it) requires.
        var gameplayPatchesEnabled = Settings.InstallGameplayPatches.Value;

        var patchTypes = assembly.GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0)
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        var skipped = 0;
        foreach (var patchType in patchTypes)
        {
            if (!gameplayPatchesEnabled && !DiagnosticPatchTypes.Contains(patchType))
            {
                skipped++;
                continue;
            }

            LogSource.LogInfo($"Applying patch module {patchType.FullName}");
            try
            {
                harmony.CreateClassProcessor(patchType).Patch();
                LogSource.LogInfo($"Applied patch module {patchType.FullName}");
            }
            catch (Exception ex)
            {
                LogSource.LogError($"Patch module {patchType.FullName} failed and was skipped: {ex}");
            }
        }

        if (!gameplayPatchesEnabled)
        {
            LogSource.LogWarning(
                $"InstallGameplayPatches is false: skipped {skipped} gameplay patch modules. " +
                "The mod is loaded but inert; set it back to true in the config to play with it.");
        }
    }

}
