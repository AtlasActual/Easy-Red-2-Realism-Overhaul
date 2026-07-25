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
    public const string PluginVersion = "1.0.4";

    internal static ManualLogSource LogSource { get; private set; } = null!;
    private Harmony? _harmony;
    private AtmosphericParticlePersistenceController? _atmosphericParticlePersistenceController;
    private SettingsSyncController? _settingsSyncController;
    private SettingsMenuController? _settingsMenuController;
    private FirstPersonPlayerShadowController? _firstPersonPlayerShadowController;
    private PlayerViewFeaturesController? _playerViewFeaturesController;
    private PlayerSuppressionBlurController? _playerSuppressionBlurController;
    private MultiplayerPlayerNameController? _multiplayerPlayerNameController;
    private MultiplayerSharedSquadController? _multiplayerSharedSquadController;
    private BulletPenetrationController? _bulletPenetrationController;
    private AiDebugOverlayController? _aiDebugOverlayController;

    public override void Load()
    {
        LogSource = Log;
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

        StartupSplashSkipper.TrySkip();
        Settings.Bind(Config);
        SettingsCatalog.Initialize();
        _atmosphericParticlePersistenceController = AddComponent<AtmosphericParticlePersistenceController>();
        _settingsSyncController = AddComponent<SettingsSyncController>();
        _settingsMenuController = AddComponent<SettingsMenuController>();
        _firstPersonPlayerShadowController = AddComponent<FirstPersonPlayerShadowController>();
        _playerViewFeaturesController = AddComponent<PlayerViewFeaturesController>();
        _playerSuppressionBlurController = AddComponent<PlayerSuppressionBlurController>();
        _multiplayerPlayerNameController = AddComponent<MultiplayerPlayerNameController>();
        _multiplayerSharedSquadController = AddComponent<MultiplayerSharedSquadController>();
        _bulletPenetrationController = AddComponent<BulletPenetrationController>();
        _aiDebugOverlayController = AddComponent<AiDebugOverlayController>();

        _harmony = new Harmony(PluginGuid);
        PatchModules(_harmony, typeof(Plugin).Assembly);

        Log.LogInfo($"{PluginName} {PluginVersion} loaded. attackPostureBonus={Settings.AttackingForceBonusEnabled.Value}, " +
                    $"FOV={Settings.PerceptionEnabled.Value}, " +
                    $"contact={Settings.ContactResponseEnabled.Value}, " +
                    $"staticWeaponStaffing={Settings.StaticWeaponStaffingEnabled.Value}, " +
                    $"danger={Settings.DangerReactionsEnabled.Value}, tankFear={Settings.TankFearEnabled.Value}, " +
                    $"gunnerDuck={Settings.MountedGunnerSuppressionEnabled.Value}, " +
                    $"fireSafety={Settings.FriendlyFireChecksEnabled.Value}, safeGrenades={Settings.SafeAiGrenadeThrowsEnabled.Value}, " +
                    $"tankTactics={Settings.TankTacticsEnabled.Value}, tankAcceleration={Settings.TankAccelerationMultiplier.Value:F2}x, smoke={Settings.SmokeSupportEnabled.Value}, " +
                    $"longBarrage={Settings.ArtilleryMissionLengthEnabled.Value}, persistentSmoke={Settings.SmokePersistenceEnabled.Value}, " +
                    $"persistentDust={Settings.ExplosionDustPersistenceEnabled.Value}, " +
                    $"largeCraters={Settings.HeavyOrdnanceCratersEnabled.Value}, " +
                    $"layeredBlast={Settings.LayeredBlastEffectsEnabled.Value}, " +
                    $"fragmentation={Settings.EnhancedFragmentationEnabled.Value}, " +
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
                    $"distantSound={Settings.DistantSoundShapingEnabled.Value}");
    }

    private static void PatchModules(Harmony harmony, Assembly assembly)
    {
        var patchTypes = assembly.GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0)
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        foreach (var patchType in patchTypes)
        {
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
    }
}
