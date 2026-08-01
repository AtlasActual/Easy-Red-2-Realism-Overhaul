using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class WorldHudDrawContext
{
    internal static bool InsidePlayerWorldHud;
    private static Texture2D? _clearTexture;
    private static Sprite? _clearSprite;

    internal static Sprite ClearMarkerSprite()
    {
        if (_clearSprite != null)
            return _clearSprite;

        _clearTexture = new Texture2D(1, 1);
        _clearTexture.name = "ER2 Realism Clear World Marker";
        _clearTexture.SetPixel(0, 0, Color.clear);
        _clearTexture.Apply(false, true);
        _clearSprite = Sprite.Create(
            _clearTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        _clearSprite.name = "ER2 Realism Clear World Marker Sprite";
        return _clearSprite;
    }
}

internal static class WorldHudVisibility
{
    internal static bool ShouldHideSquadmateNameInSameVehicle(Soldier? otherSoldier)
    {
        if (!Settings.HidePlayerNamesInSameVehicle.Value || otherSoldier == null)
            return false;

        try
        {
            var player = Soldier.CurrentControlledSoldierOrNull();
            if (player == null ||
                player.GetInstanceID() == otherSoldier.GetInstanceID())
            {
                return false;
            }

            var playerSeat = player.currentVehicleSeat;
            var otherSeat = otherSoldier.currentVehicleSeat;
            var playerVehicle = playerSeat?.GetSeatVehicle();
            var otherVehicle = otherSeat?.GetSeatVehicle();
            return playerVehicle != null &&
                   otherVehicle != null &&
                   playerVehicle.GetInstanceID() == otherVehicle.GetInstanceID();
        }
        catch
        {
            return false;
        }
    }
}

internal static class ContextualWorldNameProjection
{
    private const float MaximumViewAngle = 45f;

    internal static bool TryProject(
        Camera camera,
        Vector3 anchor,
        float labelWidth,
        float labelHeight,
        out Rect rect,
        out float alpha)
    {
        rect = default;
        alpha = 0f;

        var direction = anchor - camera.transform.position;
        if (direction.sqrMagnitude <= 0.0001f ||
            Vector3.Angle(camera.transform.forward, direction) > MaximumViewAngle)
        {
            return false;
        }

        var screenPoint = camera.WorldToScreenPoint(anchor);
        if (screenPoint.z <= 0f ||
            screenPoint.x < 0f || screenPoint.x > Screen.width ||
            screenPoint.y < 0f || screenPoint.y > Screen.height)
        {
            return false;
        }

        var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var distanceFromCenter = Vector2.Distance(
            new Vector2(screenPoint.x, screenPoint.y),
            screenCenter);
        alpha = Mathf.Clamp01(1f - distanceFromCenter / (Screen.height * 0.42f));
        if (alpha <= 0.02f)
            return false;

        rect = new Rect(
            screenPoint.x - labelWidth * 0.5f,
            Screen.height - screenPoint.y - labelHeight,
            labelWidth,
            labelHeight);
        return true;
    }
}

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.OnGUI))]
internal static class PlayerWorldHudContextPatch
{
    [HarmonyPrefix]
    private static void Prefix(out bool __state)
    {
        __state = WorldHudDrawContext.InsidePlayerWorldHud;
        WorldHudDrawContext.InsidePlayerWorldHud = true;
    }

    [HarmonyPostfix]
    private static void Postfix(bool __state)
    {
        WorldHudDrawContext.InsidePlayerWorldHud = __state;
    }
}

[HarmonyPatch(typeof(Squad), nameof(Squad.GetAllyMarkerIcon), typeof(bool))]
internal static class WorldAllyMarkerPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref Sprite __result)
    {
        if (!Settings.ImmersiveWorldHudEnabled.Value ||
            !WorldHudDrawContext.InsidePlayerWorldHud)
        {
            return true;
        }

        __result = WorldHudDrawContext.ClearMarkerSprite();
        return false;
    }
}

[HarmonyPatch(typeof(Squad), nameof(Squad.GetMarkerIcon))]
internal static class WorldUnitMarkerPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref Sprite __result)
    {
        if (!Settings.ImmersiveWorldHudEnabled.Value ||
            !WorldHudDrawContext.InsidePlayerWorldHud)
        {
            return true;
        }

        __result = WorldHudDrawContext.ClearMarkerSprite();
        return false;
    }
}

[HarmonyPatch(
    typeof(GuiExtension),
    nameof(GuiExtension.OutlinedLabel),
    typeof(Rect),
    typeof(string),
    typeof(float),
    typeof(int))]
internal static class NativeContextualSquadNamePatch
{
    [HarmonyPrefix]
    private static bool Prefix(string __1, float __2)
    {
        if (ShouldSuppressSameVehicleName(__1))
            return false;

        if (!WorldHudDrawContext.InsidePlayerWorldHud ||
            Mathf.Abs(__2 - 0.4f) > 0.015f ||
            string.IsNullOrWhiteSpace(__1))
        {
            return true;
        }

        var suppressAiNames = Settings.ImmersiveWorldHudEnabled.Value &&
                              Settings.ContextualSquadNamesEnabled.Value;
        var suppressMultiplayerNames = MultiplayerPlayerNameController.ShouldDrawNames();
        if (!Settings.HidePlayerNamesInSameVehicle.Value &&
            !suppressAiNames &&
            !suppressMultiplayerNames)
            return true;

        // The native squad-name pass is identified by both its fixed outline
        // alpha and its own label color. When both replacement paths are live,
        // suppress that pass outright so Photon nicknames cannot slip past a
        // comparison against the soldier's generated campaign name.
        if (suppressAiNames &&
            suppressMultiplayerNames &&
            IsNativeSquadNameColor(GUI.color))
        {
            return false;
        }

        var player = Soldier.CurrentControlledSoldierOrNull();
        var squad = player?.joinedSquad;
        if (player == null || squad == null)
            return true;

        for (var index = 0; index < squad.CountMembers; index++)
        {
            var member = squad.GetMember(index);
            if (member == null || member == player ||
                !MatchesNativeLabel(member, __1))
            {
                continue;
            }

            var sync = member.GetSyncher();
            var remotePlayer = MultiplayerPlayerNameController.IsRemotePlayer(sync);
            return remotePlayer ? !suppressMultiplayerNames : !suppressAiNames;
        }

        return true;
    }

    internal static bool ShouldSuppressSameVehicleName(string label)
    {
        if (!WorldHudDrawContext.InsidePlayerWorldHud ||
            !Settings.HidePlayerNamesInSameVehicle.Value ||
            string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var player = Soldier.CurrentControlledSoldierOrNull();
        var squad = player?.joinedSquad;
        if (player == null || squad == null)
            return false;

        for (var index = 0; index < squad.CountMembers; index++)
        {
            var member = squad.GetMember(index);
            if (member == null ||
                member == player ||
                !MatchesNativeLabel(member, label))
            {
                continue;
            }

            return WorldHudVisibility.ShouldHideSquadmateNameInSameVehicle(member);
        }

        return false;
    }

    private static bool MatchesNativeLabel(Soldier member, string label)
    {
        if (string.Equals(member.name_surname, label, StringComparison.Ordinal))
            return true;

        var sync = member.GetSyncher();
        if (!MultiplayerPlayerNameController.IsRemotePlayer(sync))
            return false;

        var controllingPlayer = sync.Controller ?? sync.Owner;
        if (controllingPlayer == null)
            return false;

        var rawNickname = (string?)controllingPlayer.NickName ?? string.Empty;
        return string.Equals(rawNickname, label, StringComparison.Ordinal);
    }

    private static bool IsNativeSquadNameColor(Color color)
    {
        return IsNear(color.r, 0.72f) &&
               IsNear(color.g, 0.83f) &&
               IsNear(color.b, 1f) ||
               IsNear(color.r, 0.6f) &&
               IsNear(color.g, 1f) &&
               IsNear(color.b, 0.65f);
    }

    private static bool IsNear(float value, float target)
    {
        return Mathf.Abs(value - target) <= 0.03f;
    }
}

[HarmonyPatch(
    typeof(GuiExtension),
    nameof(GuiExtension.OutlinedLabel),
    typeof(Rect),
    typeof(string),
    typeof(int))]
internal static class NativeSimpleSquadNamePatch
{
    [HarmonyPrefix]
    private static bool Prefix(string __1)
    {
        return !NativeContextualSquadNamePatch.ShouldSuppressSameVehicleName(__1);
    }
}

[HarmonyPatch(
    typeof(GuiExtension),
    nameof(GuiExtension.OutlinedLabel),
    typeof(Rect),
    typeof(string),
    typeof(GUIStyle),
    typeof(int))]
internal static class NativeStyledSquadNamePatch
{
    [HarmonyPrefix]
    private static bool Prefix(string __1)
    {
        return !NativeContextualSquadNamePatch.ShouldSuppressSameVehicleName(__1);
    }
}

/// <summary>
/// Replaces omnipresent AI squad labels with short-range, center-view names.
/// Human multiplayer names use the same projection through MultiplayerPlayerNameController.
/// </summary>
internal sealed class ImmersiveWorldHudController : MonoBehaviour
{
    private const float LabelWidth = 260f;
    private const float LabelHeight = 30f;

    private GUIStyle? _nameStyle;
    private string _lastErrorSignature = string.Empty;

    private void OnGUI()
    {
        try
        {
            if (!ShouldDraw() || Event.current == null || Event.current.type != EventType.Repaint)
                return;

            var player = Soldier.CurrentControlledSoldierOrNull();
            var squad = player?.joinedSquad;
            var camera = ResourcesManager.mainCamera;
            if (player == null || squad == null || camera == null)
                return;

            EnsureStyles();
            var maximumRange = Mathf.Max(1f, Settings.ContextualSquadNameRangeMeters.Value);
            var previousDepth = GUI.depth;
            GUI.depth = -940;
            try
            {
                for (var index = 0; index < squad.CountMembers; index++)
                    DrawMember(squad.GetMember(index), player, camera, maximumRange);
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    [HideFromIl2Cpp]
    private static bool ShouldDraw()
    {
        if (!Settings.ImmersiveWorldHudEnabled.Value ||
            !Settings.ContextualSquadNamesEnabled.Value ||
            MiniMapGUI.MiniMapOpened)
        {
            return false;
        }

        var system = SavableData.Settings?.system;
        return system != null && system.enableGUI;
    }

    [HideFromIl2Cpp]
    private void DrawMember(
        Soldier member,
        Soldier player,
        Camera camera,
        float maximumRange)
    {
        if (member == null || member == player || !member.IsAlive ||
            !member.gameObject.activeInHierarchy ||
            WorldHudVisibility.ShouldHideSquadmateNameInSameVehicle(member) ||
            Vector3.Distance(player.transform.position, member.transform.position) > maximumRange)
        {
            return;
        }

        var sync = member.GetSyncher();
        if (sync != null && sync.IsControlledByAPlayer())
            return;

        var label = member.name_surname;
        if (string.IsNullOrWhiteSpace(label))
            return;

        if (!ContextualWorldNameProjection.TryProject(
                camera,
                member.UINamePos(),
                LabelWidth,
                LabelHeight,
                out var rect,
                out var alpha))
        {
            return;
        }

        var priorColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(rect, label, _nameStyle!);
        GUI.color = priorColor;
    }

    [HideFromIl2Cpp]
    private void EnsureStyles()
    {
        var fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.014f), 14, 22);
        if (_nameStyle != null && _nameStyle.fontSize == fontSize)
            return;

        _nameStyle = CloneStyle(GUI.skin.label);
        _nameStyle.alignment = TextAnchor.MiddleCenter;
        _nameStyle.fontSize = fontSize;
        _nameStyle.fontStyle = FontStyle.Bold;
        _nameStyle.richText = false;
        _nameStyle.wordWrap = false;
        _nameStyle.normal.textColor = new Color(1f, 0.82f, 0.2f, 1f);
    }

    [HideFromIl2Cpp]
    private static GUIStyle CloneStyle(GUIStyle source)
    {
        var style = new GUIStyle();
        GUIStyle.Internal_Copy(style, source);
        return style;
    }

    [HideFromIl2Cpp]
    private void ReportError(Exception exception)
    {
        var signature = exception.GetType().FullName + ": " + exception.Message;
        if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal))
            return;

        _lastErrorSignature = signature;
        Plugin.LogSource.LogWarning(
            $"Immersive world HUD failed (further identical errors suppressed): {exception.Message}");
    }
}
