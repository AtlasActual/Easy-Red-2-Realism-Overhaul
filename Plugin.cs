using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.Reflection;

namespace ER2RealismOverhaul;

internal static class RuntimeLifecycle
{
    internal static bool IsQuitting { get; set; }
}

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "ca.antoi.er2.tacticalai";
    public const string PluginName = "Easy Red 2 Realism Overhaul";
    public const string PluginVersion = "1.1.4";

    internal static ManualLogSource LogSource { get; private set; } = null!;
    private bool _modEnabled;
    private Harmony? _harmony;
    private AtmosphericParticlePersistenceController? _atmosphericParticlePersistenceController;
    private SettingsSyncController? _settingsSyncController;
    private SettingsMenuController? _settingsMenuController;
    private AircraftFlightInstrumentsController? _aircraftFlightInstrumentsController;
    private FirstPersonPlayerShadowController? _firstPersonPlayerShadowController;
    private PlayerViewFeaturesController? _playerViewFeaturesController;
    private PlayerSuppressionBlurController? _playerSuppressionBlurController;
    private PlayerHeadshotBlackoutController? _playerHeadshotBlackoutController;
    private VehicleAimingReticleController? _vehicleAimingReticleController;
    private MultiplayerPlayerNameController? _multiplayerPlayerNameController;
    private MultiplayerSharedSquadController? _multiplayerSharedSquadController;
    private ImmersiveWorldHudController? _immersiveWorldHudController;
    private LeaveSquadRedeployController? _leaveSquadRedeployController;
    private SpectatorHudController? _spectatorHudController;
    private BulletPenetrationController? _bulletPenetrationController;
    private AiDebugOverlayController? _aiDebugOverlayController;

    public override void Load()
    {
        LogSource = Log;
        EnableMod();
    }

    internal void EnableMod()
    {
        if (_modEnabled)
            return;

        _modEnabled = true;
        // The mod's interop wrapper churn (per-soldier per-frame patch calls) feeds
        // the managed GC; its blocking full collections pause the game thread for
        // 100-300ms. SustainedLowLatency defers blocking gen2 collections to
        // genuinely necessary moments while keeping background collection active.
        try
        {
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Could not set GC latency mode: {ex.Message}");
        }

        CompatibilityReport.CaptureGameVersion();
        Log.LogInfo(
            $"{PluginName} {PluginVersion} starting on Easy Red 2 (Application.version {CompatibilityReport.GameVersion}, " +
            $"Unity {CompatibilityReport.UnityVersion}).");

        StartupSplashSkipper.TrySkip();
        Settings.Bind(Config);
        AudioVoiceCapacity.ApplyAtStartup();
        StutterProbe.InstallExceptionCounter();
        SettingsCatalog.Initialize();

        // Every component and patch module attaches on its own. A game update that
        // renames or removes one piece of game code then costs that one feature, and
        // the log and settings menu say which, instead of the whole plugin failing
        // to load while its remaining pieces silently stop working.
        _atmosphericParticlePersistenceController = TryAddComponent<AtmosphericParticlePersistenceController>();
        _settingsSyncController = TryAddComponent<SettingsSyncController>();
        _settingsMenuController = TryAddComponent<SettingsMenuController>();
        _aircraftFlightInstrumentsController = TryAddComponent<AircraftFlightInstrumentsController>();
        _firstPersonPlayerShadowController = TryAddComponent<FirstPersonPlayerShadowController>();
        _playerViewFeaturesController = TryAddComponent<PlayerViewFeaturesController>();
        _playerSuppressionBlurController = TryAddComponent<PlayerSuppressionBlurController>();
        _playerHeadshotBlackoutController = TryAddComponent<PlayerHeadshotBlackoutController>();
        _vehicleAimingReticleController = TryAddComponent<VehicleAimingReticleController>();
        _multiplayerPlayerNameController = TryAddComponent<MultiplayerPlayerNameController>();
        _multiplayerSharedSquadController = TryAddComponent<MultiplayerSharedSquadController>();
        _immersiveWorldHudController = TryAddComponent<ImmersiveWorldHudController>();
        _leaveSquadRedeployController = TryAddComponent<LeaveSquadRedeployController>();
        _spectatorHudController = TryAddComponent<SpectatorHudController>();
        _bulletPenetrationController = TryAddComponent<BulletPenetrationController>();
        _aiDebugOverlayController = TryAddComponent<AiDebugOverlayController>();

        _harmony = new Harmony(PluginGuid);
        if (Settings.InstallGameplayPatches.Value &&
            Settings.DeferredInteropHandleCleanupEnabled.Value)
        {
            try
            {
                InteropFinalizerReaper.TryInstall(_harmony);
            }
            catch (Exception ex)
            {
                CompatibilityReport.NotePatchModuleFailed(nameof(InteropFinalizerReaper), ex);
            }
        }

        PatchModules(_harmony, typeof(Plugin).Assembly);
        CompatibilityReport.LogSummary();

        Log.LogInfo($"{PluginName} {PluginVersion} loaded. objectiveCoordination={Settings.ObjectiveCoordinationEnabled.Value}, " +
                    $"attackPostureBonus={Settings.AttackingForceBonusEnabled.Value}, " +
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
            $"aircraftPhysics={Settings.AircraftFlightPhysicsEnabled.Value}, aircraftMousePointAim={Settings.AircraftMousePointAimingEnabled.Value}, aircraftSimplifiedManualRoll={Settings.AircraftSimplifiedManualRollEnabled.Value}, " +
                    $"bulletPenetration={Settings.BulletPenetrationEnabled.Value}, " +
                    $"addedRicochets={Settings.AddedSmallArmsRicochetsEnabled.Value}, " +
                    $"tracers={Settings.TracerReductionEnabled.Value}, tracerRetention={Settings.MachineGunTracerRetention.Value:F2}, tracerBrightness={Settings.TracerBrightness.Value:F2}x, tracerSize={Settings.TracerSizeMultiplier.Value:F2}x, tracerLength={Settings.TracerLengthMultiplier.Value:F2}x, chatter={Settings.BattleChatterEnabled.Value}, " +
                    $"playerSuppressionVignette={Settings.PlayerSuppressionVignetteMultiplier.Value}, " +
                    $"playerSuppressionWobble={Settings.PlayerSuppressionWobbleMultiplier.Value}, " +
                    $"playerSuppressionRadius={Settings.PlayerSuppressionNearMissRadiusMultiplier.Value:F2}x, " +
                    $"playerSuppressionBlur={Settings.PlayerSuppressionBlurEnabled.Value}, " +
                    $"playerSuppressionDirectionMarker={Settings.ShowPlayerSuppressionDirectionMarker.Value}, " +
                    $"aiCasualtySuppression={Settings.AiCasualtySuppressionEnabled.Value}, " +
                    $"meleeHitRegistration={Settings.ImprovedMeleeHitRegistrationEnabled.Value}, " +
                    $"meleeReachExtension={Settings.MeleeAdditionalReach.Value:F2}m, " +
                    $"firstPersonPlayerShadow={Settings.FirstPersonPlayerShadowEnabled.Value}, " +
                    $"aimFatigue={Settings.RealisticAimFatigueEnabled.Value}, " +
                    $"directTurretAiming={Settings.DirectTurretAimingEnabled.Value}, " +
                    $"groundVehicleAimRings={Settings.GroundVehicleAimRingsEnabled.Value}, " +
                    $"gunnerViewElevationLock={Settings.GunnerViewElevationLockEnabled.Value}, " +
                    $"unstabilizedGunsight={Settings.UnstabilizedGunsightEnabled.Value}, " +
                    $"vehicleOpticsZoom={Settings.OpticsZoom.Value:F3}x, " +
                    $"infantryThirdPersonZoom={Settings.ThirdPersonZoom.Value:F3}x, " +
                    $"aircraftFreeLookZoom={Settings.AircraftFreeLookZoom.Value:F3}x, " +
                    $"binoculars={Settings.BinocularsEnabled.Value}, binocularZoom={Settings.BinocularZoomMultiplier.Value:F1}x, " +
                    $"freeLook={Settings.FreeLookEnabled.Value}, freeLookArc={Settings.FreeLookHorizontalArcDegrees.Value:F0}deg, " +
                    $"compassAlwaysVisible={Settings.CompassAlwaysVisible.Value}, " +
                    $"compassUnits={(Settings.CompassUseMils.Value ? "mils" : "degrees")}, " +
                    $"namesWithHudDisabled={Settings.KeepMultiplayerPlayerNamesWithHudDisabled.Value}, " +
                    $"immersiveWorldHud={Settings.ImmersiveWorldHudEnabled.Value}, " +
                    $"hidePlayerNamesInSameVehicle={Settings.HidePlayerNamesInSameVehicle.Value}, " +
                    $"leaveSquadRedeploy={Settings.LeaveSquadRedeployEnabled.Value}, " +
                    $"spectatorHud={Settings.SpectatorHudEnabled.Value}, spectatorHudKey={Settings.SpectatorHudToggleKey.Value}, " +
                    $"ragdollMomentum={Settings.RagdollMomentumEnabled.Value}, " +
                    $"highQualityDistantAnimations={Settings.KeepHighQualityDistantAnimations.Value}, " +
                    $"audioBalance={Settings.AudioBalanceEnabled.Value}");
    }

    private T? TryAddComponent<T>() where T : UnityEngine.MonoBehaviour
    {
        try
        {
            return AddComponent<T>();
        }
        catch (Exception ex)
        {
            // Il2CppInterop registers the component's methods with the game when it is
            // added; a signature naming a game type that no longer exists fails here.
            CompatibilityReport.NoteControllerFailed(typeof(T).Name, ex);
            return null;
        }
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

        var skipped = 0;
        foreach (var patchType in GetLoadableTypes(assembly).OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            // Reading the HarmonyPatch attribute resolves the game types named in it.
            // One that this game build no longer has must fail only its own module: an
            // unguarded throw here used to abort the loop before any module was applied.
            bool isPatchModule;
            try
            {
                isPatchModule = patchType.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0;
            }
            catch (Exception ex)
            {
                CompatibilityReport.NotePatchModuleFailed(patchType.FullName ?? patchType.Name, ex);
                continue;
            }

            if (!isPatchModule)
                continue;

            if (!gameplayPatchesEnabled && !DiagnosticPatchTypes.Contains(patchType))
            {
                skipped++;
                continue;
            }

            LogSource.LogInfo($"Applying patch module {patchType.FullName}");
            try
            {
                var patched = harmony.CreateClassProcessor(patchType).Patch();
                ContainUnityMessageExceptions(harmony, patched);
                CompatibilityReport.NotePatchModuleApplied();
                LogSource.LogInfo($"Applied patch module {patchType.FullName}");
            }
            catch (Exception ex)
            {
                CompatibilityReport.NotePatchModuleFailed(patchType.FullName ?? patchType.Name, ex);
            }
        }

        if (!gameplayPatchesEnabled)
        {
            LogSource.LogWarning(
                $"InstallGameplayPatches is false: skipped {skipped} gameplay patch modules. " +
                "The mod is loaded but inert; set it back to true in the config to play with it.");
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A type whose own definition names a missing game type cannot be loaded at
            // all. Report those and continue with every type that did load.
            var loadable = ex.Types.Where(type => type != null).Select(type => type!).ToArray();
            LogSource.LogError(
                $"{ex.Types.Length - loadable.Length} type(s) in the mod reference game code this game build " +
                $"no longer has and were skipped: {CompatibilityReport.DescribeLoaderExceptions(ex)}");
            return loadable;
        }
    }

    // Unity calls these across its own scripting boundary, which logs a throw and
    // carries on. Detouring one turns it into a managed method whose exceptions unwind
    // through Il2CppInterop's native->managed trampoline instead, so a throw the
    // unmodded game absorbed can abort the caller and skip the rest of the game's own
    // work. Containing them keeps a patched message method behaving like an unpatched
    // one. All return void, so suppressing cannot fabricate a return value.
    private static readonly HashSet<string> UnityMessageMethods = new(StringComparer.Ordinal)
    {
        "Awake", "OnEnable", "Start", "FixedUpdate", "Update", "LateUpdate",
        "OnDisable", "OnDestroy", "OnApplicationQuit"
    };

    private static readonly HashSet<MethodBase> ContainedUnityMessages = new();
    private static readonly HashSet<string> ReportedUnityMessageFailures = new(StringComparer.Ordinal);

    private static void ContainUnityMessageExceptions(
        Harmony harmony,
        IReadOnlyList<MethodBase>? patchedMethods)
    {
        if (patchedMethods == null)
            return;

        foreach (var method in patchedMethods)
        {
            if (method == null ||
                !UnityMessageMethods.Contains(method.Name) ||
                (method is MethodInfo info && info.ReturnType != typeof(void)) ||
                !ContainedUnityMessages.Add(method))
            {
                continue;
            }

            try
            {
                harmony.Patch(
                    method,
                    finalizer: new HarmonyMethod(
                        typeof(Plugin).GetMethod(
                            nameof(UnityMessageFinalizer),
                            BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception ex)
            {
                ContainedUnityMessages.Remove(method);
                LogSource.LogWarning(
                    $"Could not contain exceptions for {method.DeclaringType?.Name}.{method.Name}: {ex.Message}");
            }
        }
    }

    private static Exception? UnityMessageFinalizer(Exception? __exception, MethodBase __originalMethod)
    {
        if (__exception == null)
            return null;

        var signature = $"{__originalMethod?.DeclaringType?.Name}.{__originalMethod?.Name}";
        if (ReportedUnityMessageFailures.Add(signature))
        {
            LogSource.LogWarning(
                $"Contained an exception from {signature} so it could not abort the game's call path " +
                $"(further repeats suppressed): {__exception.Message}");
        }

        return null;
    }
}
