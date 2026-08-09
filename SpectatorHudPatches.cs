using Corvostudio.CinematicCamera;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class SpectatorHudVisibility
{
    private const int SquadMarkerSlot = 0;
    private const int FriendlyUnitMarkerSlot = 1;
    private const float FriendlyUnitMarkerDistance = 700f;
    private const float FriendlyUnitMarkerSize = 7f;
    private const float SquadMarkerSize = 26f;
    private const float FriendlyUnitAlpha = 0.75f;
    private const float FriendlyUnitLeaderAlpha = 1f;
    private const float FriendlySquadAlpha = 0.65f;

    internal static bool RuntimeVisible { get; set; } = true;

    private static PlayerController? _lastController;
    private static Soldier? _lastControlledCharacter;
    private static Squad? _lastPlayerSquad;
    private static string _lastPlayerFaction = string.Empty;
    private static bool _drivingNativeWorldHud;
    private static bool _reportedMarkerHudHidden;
    private static bool _reportedMissingMarkerContext;
    private static bool _reportedMarkerActivity;
    private static string _lastMarkerError = string.Empty;

    internal static bool DrivingNativeWorldHud => _drivingNativeWorldHud;

    internal static bool IsCinematicSpectator()
    {
        try
        {
            return CinematicCameraGUI.cinematicCameraEnabled && BattleManager.IsBattleActive();
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsManagedSpectator()
        => Settings.SpectatorHudEnabled.Value && IsCinematicSpectator();

    internal static bool ShouldShow()
    {
        if (!IsManagedSpectator() || !RuntimeVisible)
        {
            return false;
        }

        try
        {
            var system = SavableData.Settings?.system;
            return system != null &&
                   system.enableGUI &&
                   !Pause.IsPaused() &&
                   !InventoryPanel.IsInventoryOpen() &&
                   !MiniMapGUI.MiniMapOpened &&
                   !MissionEditor.IsEditingMission() &&
                   !TerrainEditor.IsEditingTerrain();
        }
        catch
        {
            return false;
        }
    }

    internal static bool ShouldShowObjectives()
    {
        if (!ShouldShow())
            return false;

        try
        {
            return SavableData.Settings?.system?.disableObjectiveUi == false;
        }
        catch
        {
            return false;
        }
    }

    internal static void CapturePlayerContext()
    {
        if (SpectatorNativeHudContext.Evaluating)
            return;

        try
        {
            var controller = PlayerController.currentController;
            if (Exists(controller))
                _lastController = controller;

            var character = controller?._ControlledCharacter_k__BackingField;
            if (!Exists(character))
                return;

            CaptureCharacterContext(character);
        }
        catch
        {
            // Player and squad objects are replaced during deployment transitions.
        }
    }

    private static void CaptureCharacterContext(Soldier? character)
    {
        if (!Exists(character))
            return;

        _lastControlledCharacter = character;

        var faction = character!.faction;
        if (IsKnownFaction(faction))
        {
            _lastPlayerFaction = faction;
            _reportedMissingMarkerContext = false;
        }

        if (Exists(character.joinedSquad))
            _lastPlayerSquad = character.joinedSquad;
    }

    internal static PlayerController? ResolveHudController()
    {
        try
        {
            var current = PlayerController.currentController;
            if (Exists(current))
            {
                _lastController = current;
                return current;
            }

            return Exists(_lastController) ? _lastController : null;
        }
        catch
        {
            return null;
        }
    }

    internal static Soldier? ResolveHudCharacter(PlayerController? controller)
    {
        try
        {
            var current = controller?._ControlledCharacter_k__BackingField;
            if (IsLiving(current))
                return current;

            var squadCharacter = FindLivingMember(_lastPlayerSquad);
            if (squadCharacter != null)
                return squadCharacter;

            squadCharacter = FindLivingMember(PlayerGUI.GetGUISquad());
            if (squadCharacter != null)
                return squadCharacter;

            return IsLiving(_lastControlledCharacter) ? _lastControlledCharacter : null;
        }
        catch
        {
            return null;
        }
    }

    internal static Squad? ResolveHudSquad(Soldier? character)
    {
        try
        {
            if (Exists(character?.joinedSquad))
                return character!.joinedSquad;

            if (Exists(_lastPlayerSquad))
                return _lastPlayerSquad;

            var guiSquad = PlayerGUI.GetGUISquad();
            return Exists(guiSquad) ? guiSquad : null;
        }
        catch
        {
            return null;
        }
    }

    internal static void RefreshNativePlayerHud()
    {
        var state = SpectatorNativeHudContext.Enter(withCharacter: true);
        if (!state.Entered)
            return;

        try
        {
            var character = state.Controller?._ControlledCharacter_k__BackingField;
            var squad = ResolveHudSquad(character);
            if (squad != null)
            {
                PlayerGUI.ShowSquadList(squad, 0f);
                PlayerGUI.UpdateSquadGUI();
            }

            // This is normally called by player equipment and pose events. Run it
            // once on spectator entry so the stats portion of the native HUD is
            // restored alongside the continuously refreshed squad HUD.
            PlayerGUI.OnPlayerUiStatsChanged();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not refresh the native spectator HUD: {ex.Message}");
        }
        finally
        {
            SpectatorNativeHudContext.Exit(state);
        }
    }

    internal static void DrawNativeWorldHud()
    {
        if (!ShouldShow() || Event.current == null || Event.current.type != EventType.Repaint)
            return;

        try
        {
            var controller = ResolveHudController();
            if (_drivingNativeWorldHud)
                return;

            _drivingNativeWorldHud = true;
            try
            {
                // Preserve the remaining IMGUI world HUD owned by the native
                // callback. Pooled squad/contact markers are refreshed from
                // UpdateNativeWorldMarkers because the injected spectator
                // component is not guaranteed to receive Unity OnGUI events.
                controller?.OnGUI();
            }
            finally
            {
                _drivingNativeWorldHud = false;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not draw the native spectator world HUD: {ex.Message}");
        }
    }

    internal static void UpdateNativeWorldMarkers()
    {
        if (!ShouldShow())
        {
            if (IsManagedSpectator() && RuntimeVisible && !_reportedMarkerHudHidden)
            {
                _reportedMarkerHudHidden = true;
                Plugin.LogSource.LogWarning(
                    "Spectator marker refresh is waiting because the native HUD visibility gate is closed.");
            }

            return;
        }

        try
        {
            DrawSquadAndContactMarkers(ResolveHudController());
        }
        catch (Exception ex)
        {
            var signature = $"{ex.GetType().FullName}: {ex.Message}";
            if (signature != _lastMarkerError)
            {
                _lastMarkerError = signature;
                Plugin.LogSource.LogWarning($"Could not update spectator squad/contact markers: {ex}");
            }
        }
    }

    private static void DrawSquadAndContactMarkers(PlayerController? controller)
    {
        var viewer = ResolveHudCharacter(controller);
        var viewerFaction = ResolveHudFaction(viewer);
        var allSquads = Squad.AllSquads;
        if (allSquads == null || string.IsNullOrEmpty(viewerFaction))
        {
            if (!_reportedMissingMarkerContext)
            {
                _reportedMissingMarkerContext = true;
                Plugin.LogSource.LogWarning(
                    $"Spectator marker context unavailable: controller={controller != null}, " +
                    $"viewer={viewer != null}, squads={allSquads != null}, " +
                    $"faction={(string.IsNullOrEmpty(viewerFaction) ? "none" : viewerFaction)}");
            }

            return;
        }

        var viewerIsLeader = viewer?.IsSquadLeader() == true;
        var friendlyUnitTexture = controller?.GetSingleUnitMarkerFriendly();
        var friendlySquadMarkers = 0;
        var friendlyUnitMarkers = 0;
        var contactMarkers = 0;

        foreach (var pair in allSquads)
        {
            var squad = pair.Value;
            var leader = squad?.Leader;
            if (squad == null || !IsLiving(leader))
                continue;

            if (ResourcesManager.IsEnemyFaction(viewerFaction, leader!.faction))
            {
                if (!squad.isMarked)
                    continue;

                var contactIcon = squad.GetMarkerIcon();
                if (Exists(contactIcon))
                {
                    Marker3DGUI.Draw(
                        squad,
                        SquadMarkerSlot,
                        contactIcon.texture,
                        squad.GetMarkerPosition(),
                        SquadMarkerSize,
                        squad.GetMarkedMultiplier(viewerIsLeader));
                    contactMarkers++;
                }

                continue;
            }

            if (Exists(friendlyUnitTexture))
            {
                var unitAlpha = viewerIsLeader ? FriendlyUnitLeaderAlpha : FriendlyUnitAlpha;
                var count = Math.Min(squad.CountMembers, 64);
                for (var i = 0; i < count; i++)
                {
                    var member = squad.GetMember(i);
                    if (!IsLiving(member) || member!.LodDistanceOver(FriendlyUnitMarkerDistance))
                        continue;

                    Marker3DGUI.Draw(
                        member,
                        FriendlyUnitMarkerSlot,
                        friendlyUnitTexture,
                        member.transform.position + Vector3.up,
                        FriendlyUnitMarkerSize,
                        unitAlpha);
                    friendlyUnitMarkers++;
                }
            }

            var squadIcon = squad.GetAllyMarkerIcon(true);
            if (Exists(squadIcon))
            {
                Marker3DGUI.Draw(
                    squad,
                    SquadMarkerSlot,
                    squadIcon.texture,
                    squad.GetCenteredMarkPosition(),
                    SquadMarkerSize,
                    FriendlySquadAlpha);
                friendlySquadMarkers++;
            }
        }

        if (!_reportedMarkerActivity)
        {
            _reportedMarkerActivity = true;
            Plugin.LogSource.LogInfo(
                $"Spectator marker refresh active: friendlySquads={friendlySquadMarkers}, " +
                $"friendlyUnits={friendlyUnitMarkers}, contacts={contactMarkers}, " +
                $"unitTexture={friendlyUnitTexture != null}, faction={viewerFaction}, " +
                $"liveViewer={viewer != null}");
        }
    }

    private static string ResolveHudFaction(Soldier? viewer)
    {
        try
        {
            if (Exists(viewer) && IsKnownFaction(viewer!.faction))
            {
                _lastPlayerFaction = viewer.faction;
                return _lastPlayerFaction;
            }

            // The cinematic camera deliberately has no controlled soldier.
            // BattleManager retains the faction selected for the local player
            // independently of that soldier and is the game's authoritative
            // source during deployment, death, and spectator camera use.
            var battleFaction = BattleManager.PlayerFaction;
            if (IsKnownFaction(battleFaction))
                _lastPlayerFaction = battleFaction;
        }
        catch
        {
            // Keep the last valid faction through battle-state transitions.
        }

        return _lastPlayerFaction;
    }

    private static bool IsKnownFaction(string? faction)
        => !string.IsNullOrWhiteSpace(faction) &&
           !string.Equals(faction, Soldier.UnknownFaction, StringComparison.OrdinalIgnoreCase);

    internal static void HideNativePlayerHud()
    {
        try
        {
            var gui = PlayerGUI.instance;
            if (gui == null)
                return;

            SetActive(gui.squadGUI?.gameObject, false);
            SetActive(gui.squadData_Panel, false);
            SetActive(gui.squadOrderPosIcon?.gameObject, false);
            SetActive(gui.gui_obj, false);
            SetActive(gui.bleedingOutUi, false);
            SetActive(gui.vault_icon, false);
            SetActive(gui.doRoleNearbyicon?.gameObject, false);
        }
        catch
        {
            // Native HUD objects can disappear while returning to deployment.
        }
    }

    private static Soldier? FindLivingMember(Squad? squad)
    {
        if (!Exists(squad))
            return null;

        try
        {
            var count = Math.Min(squad!.CountMembers, 64);
            for (var i = 0; i < count; i++)
            {
                var member = squad.GetMember(i);
                if (IsLiving(member))
                    return member;
            }
        }
        catch
        {
            // The squad may be rebuilding its member list this frame.
        }

        return null;
    }

    private static bool IsLiving(Soldier? soldier)
    {
        try
        {
            return Exists(soldier) && soldier!.IsAlive;
        }
        catch
        {
            return false;
        }
    }

    private static bool Exists(UnityEngine.Object? value)
    {
        try
        {
            return value != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool Exists(Squad? value)
    {
        try
        {
            return value != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetActive(GameObject? gameObject, bool active)
    {
        if (gameObject != null && gameObject.activeSelf != active)
            gameObject.SetActive(active);
    }
}

internal readonly struct SpectatorNativeHudState
{
    internal SpectatorNativeHudState(
        bool entered,
        PlayerController? controller,
        Soldier? originalCharacter,
        bool replacedCharacter)
    {
        Entered = entered;
        Controller = controller;
        OriginalCharacter = originalCharacter;
        ReplacedCharacter = replacedCharacter;
    }

    internal bool Entered { get; }
    internal PlayerController? Controller { get; }
    internal Soldier? OriginalCharacter { get; }
    internal bool ReplacedCharacter { get; }
}

internal static class SpectatorNativeHudContext
{
    private static int _depth;

    internal static bool Evaluating => _depth > 0;

    internal static SpectatorNativeHudState Enter(bool withCharacter)
    {
        if (!SpectatorHudVisibility.ShouldShow())
            return default;

        _depth++;
        if (!withCharacter)
            return new SpectatorNativeHudState(true, null, null, false);

        try
        {
            var controller = SpectatorHudVisibility.ResolveHudController();
            if (controller == null)
                return new SpectatorNativeHudState(true, null, null, false);

            var original = controller._ControlledCharacter_k__BackingField;
            var replacement = SpectatorHudVisibility.ResolveHudCharacter(controller);
            if (replacement == null || ReferenceEquals(original, replacement))
                return new SpectatorNativeHudState(true, controller, original, false);

            controller._ControlledCharacter_k__BackingField = replacement;
            return new SpectatorNativeHudState(true, controller, original, true);
        }
        catch
        {
            _depth--;
            return default;
        }
    }

    internal static void Exit(SpectatorNativeHudState state)
    {
        if (!state.Entered)
            return;

        try
        {
            if (state.ReplacedCharacter && state.Controller != null)
                state.Controller._ControlledCharacter_k__BackingField = state.OriginalCharacter;
        }
        catch
        {
            // The player controller can be destroyed during scene transitions.
        }
        finally
        {
            _depth = Math.Max(0, _depth - 1);
        }
    }
}

internal sealed class SpectatorHudController : MonoBehaviour
{
    private const float FeedbackDurationSeconds = 1.75f;

    private bool _wasSpectating;
    private float _feedbackUntil;
    private string _feedback = string.Empty;
    private GUIStyle? _feedbackStyle;
    private string _lastErrorSignature = string.Empty;

    private void Update()
    {
        try
        {
            SpectatorHudVisibility.CapturePlayerContext();
            var spectating = SpectatorHudVisibility.IsCinematicSpectator();
            if (!spectating)
            {
                _wasSpectating = false;
                return;
            }

            if (!_wasSpectating)
            {
                _wasSpectating = true;
                SpectatorHudVisibility.RuntimeVisible = true;
                if (Settings.SpectatorHudEnabled.Value)
                    SpectatorHudVisibility.RefreshNativePlayerHud();
            }

            SpectatorHudVisibility.UpdateNativeWorldMarkers();

            if (!Settings.SpectatorHudEnabled.Value ||
                !Input.GetKeyDown(Settings.SpectatorHudToggleKey.Value))
            {
                return;
            }

            SpectatorHudVisibility.RuntimeVisible = !SpectatorHudVisibility.RuntimeVisible;
            if (SpectatorHudVisibility.RuntimeVisible)
                SpectatorHudVisibility.RefreshNativePlayerHud();
            else
                SpectatorHudVisibility.HideNativePlayerHud();
            _feedback = $"SPECTATOR HUD  {(SpectatorHudVisibility.RuntimeVisible ? "ON" : "OFF")}";
            _feedbackUntil = Time.unscaledTime + FeedbackDurationSeconds;
            Plugin.LogSource.LogInfo(
                $"Spectator HUD {(SpectatorHudVisibility.RuntimeVisible ? "enabled" : "disabled")} " +
                $"with {Settings.SpectatorHudToggleKey.Value}");
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void OnGUI()
    {
        if (SpectatorHudVisibility.IsCinematicSpectator())
            SpectatorHudVisibility.DrawNativeWorldHud();

        if (Event.current == null || Event.current.type != EventType.Repaint ||
            Time.unscaledTime >= _feedbackUntil ||
            !SpectatorHudVisibility.IsCinematicSpectator())
        {
            return;
        }

        try
        {
            EnsureStyle();
            var scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.35f);
            var width = 310f * scale;
            var height = 44f * scale;
            var rect = new Rect((Screen.width - width) * 0.5f, 70f * scale, width, height);
            var priorDepth = GUI.depth;
            GUI.depth = -1005;
            try
            {
                GUI.Box(rect, GUIContent.none);
                GUI.Label(rect, _feedback, _feedbackStyle!);
            }
            finally
            {
                GUI.depth = priorDepth;
            }
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    [HideFromIl2Cpp]
    private void EnsureStyle()
    {
        if (_feedbackStyle != null)
            return;

        _feedbackStyle = new GUIStyle();
        GUIStyle.Internal_Copy(_feedbackStyle, GUI.skin.label);
        _feedbackStyle.alignment = TextAnchor.MiddleCenter;
        _feedbackStyle.fontSize = 20;
        _feedbackStyle.fontStyle = FontStyle.Bold;
        _feedbackStyle.normal.textColor = Color.white;
    }

    [HideFromIl2Cpp]
    private void ReportError(Exception ex)
    {
        var signature = $"{ex.GetType().FullName}: {ex.Message}";
        if (signature == _lastErrorSignature)
            return;

        _lastErrorSignature = signature;
        Plugin.LogSource.LogWarning($"Spectator HUD controller error: {ex}");
    }
}

// ObjectiveGUI has its own controlled-player gate outside the normal player HUD
// callbacks, so retain its native refresh while the full spectator HUD is on.
[HarmonyPatch(typeof(ObjectiveGUI), "LateUpdate")]
internal static class SpectatorObjectiveHudPatch
{
    [HarmonyPrefix]
    private static void Prefix(ObjectiveGUI __instance)
    {
        if (SpectatorHudVisibility.ShouldShowObjectives())
            __instance.guiEnabled = true;
    }

    [HarmonyPostfix]
    private static void Postfix(ObjectiveGUI __instance)
    {
        if (!SpectatorHudVisibility.IsManagedSpectator())
            return;

        var show = SpectatorHudVisibility.ShouldShowObjectives();
        var nativeRefreshRejectedSpectator = show && !__instance.guiEnabled;
        __instance.guiEnabled = show;

        var panel = __instance.guiPanel;
        if (panel != null && panel.activeSelf != show)
            panel.SetActive(show);

        // Every periodic native eligibility check clears guiEnabled because the
        // cinematic camera has no controlled soldier. Complete the one skipped
        // refresh immediately, then let the native LateUpdate path run normally
        // until its next eligibility check.
        if (nativeRefreshRejectedSpectator)
        {
            __instance.RefreshData();
            __instance.UpdateGUI();
            __instance.UpdateSmoothing();
        }
    }
}

[HarmonyPatch(typeof(PhaseBarGUI), "LateUpdate")]
internal static class SpectatorPhaseBarHudPatch
{
    [HarmonyPrefix]
    private static void Prefix(out SpectatorNativeHudState __state)
        => __state = SpectatorNativeHudContext.Enter(withCharacter: false);

    [HarmonyFinalizer]
    private static void Finalizer(SpectatorNativeHudState __state)
        => SpectatorNativeHudContext.Exit(__state);
}

[HarmonyPatch(typeof(PlayerGUI), "LateUpdate")]
internal static class SpectatorPlayerGuiHudPatch
{
    [HarmonyPrefix]
    private static void Prefix(out SpectatorNativeHudState __state)
        => __state = SpectatorNativeHudContext.Enter(withCharacter: true);

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (SpectatorHudVisibility.IsManagedSpectator() &&
            !SpectatorHudVisibility.ShouldShow())
        {
            SpectatorHudVisibility.HideNativePlayerHud();
        }
    }

    [HarmonyFinalizer]
    private static void Finalizer(SpectatorNativeHudState __state)
        => SpectatorNativeHudContext.Exit(__state);
}

[HarmonyPatch(typeof(PlayerController), "OnGUI")]
internal static class SpectatorWorldHudPatch
{
    [HarmonyPrefix]
    private static bool Prefix(out SpectatorNativeHudState __state)
    {
        __state = default;
        if (!SpectatorHudVisibility.IsManagedSpectator())
            return true;

        // Unity can still send an occasional OnGUI event to a disabled gameplay
        // controller. Ignore that path and draw exactly once from our live host.
        if (!SpectatorHudVisibility.DrivingNativeWorldHud)
            return false;

        __state = SpectatorNativeHudContext.Enter(withCharacter: true);
        return __state.Entered;
    }

    [HarmonyFinalizer]
    private static void Finalizer(SpectatorNativeHudState __state)
        => SpectatorNativeHudContext.Exit(__state);
}

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.IsControllingPlayer))]
internal static class SpectatorHudControlStatePatch
{
    [HarmonyPostfix]
    private static void Postfix(ref bool __result)
    {
        if (!__result &&
            SpectatorNativeHudContext.Evaluating)
        {
            __result = true;
        }
    }
}

// Every pooled world marker, including squad/unit icons, flows through this
// overload. Blocking it while F has hidden the spectator HUD also clears any
// remaining pooled marker on the following native Marker3DGUI refresh.
[HarmonyPatch(
    typeof(Marker3DGUI),
    nameof(Marker3DGUI.Draw),
    new Type[]
    {
        typeof(Il2CppSystem.Object),
        typeof(int),
        typeof(Texture),
        typeof(Vector3),
        typeof(float),
        typeof(float),
        typeof(Color)
    })]
internal static class SpectatorMarkerDrawPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
        => !SpectatorHudVisibility.IsManagedSpectator() || SpectatorHudVisibility.ShouldShow();
}
