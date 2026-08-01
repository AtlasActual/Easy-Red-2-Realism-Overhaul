using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Photon.Pun;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class MultiplayerPlayerNameController : MonoBehaviour
{
    private const float LabelWidth = 240f;
    private const float LabelHeight = 28f;

    private GUIStyle? _labelStyle;
    private string _lastErrorSignature = string.Empty;

    private void OnGUI()
    {
        try
        {
            if (!ShouldDrawNames() ||
                Event.current == null ||
                Event.current.type != EventType.Repaint)
            {
                return;
            }

            var camera = ResourcesManager.mainCamera;
            var localPlayer = Soldier.CurrentControlledSoldierOrNull();
            var livingCreatures = Creature.aliveCreatures;
            if (camera == null || localPlayer == null || livingCreatures == null)
                return;

            EnsureStyles();
            RefreshStyleFont();

            var previousDepth = GUI.depth;
            GUI.depth = -950;
            try
            {
                for (var index = 0; index < livingCreatures.Count; index++)
                {
                    var soldier = livingCreatures[index]?.TryCast<Soldier>();
                    if (soldier != null)
                        TryDrawPlayerName(soldier, localPlayer, camera);
                }
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
    internal static bool ShouldDrawNames()
    {
        if (!Settings.KeepMultiplayerPlayerNamesWithHudDisabled.Value ||
            !PhotonNetwork.InRoom ||
            MiniMapGUI.MiniMapOpened)
        {
            return false;
        }

        var system = SavableData.Settings?.system;
        return system != null &&
               (!system.enableGUI ||
                system.disable3DMarkers ||
                Settings.ImmersiveWorldHudEnabled.Value);
    }

    [HideFromIl2Cpp]
    internal static bool IsRemotePlayer(SyncSoldier? syncPlayer)
    {
        if (syncPlayer == null ||
            (!syncPlayer.controlled_by_player && !syncPlayer.IsControlledByAPlayer()))
        {
            return false;
        }

        var controllingPlayer = syncPlayer.Controller ?? syncPlayer.Owner;
        var localPhotonPlayer = PhotonNetwork.LocalPlayer;
        return controllingPlayer != null &&
               (localPhotonPlayer == null ||
                controllingPlayer.ActorNumber != localPhotonPlayer.ActorNumber);
    }

    [HideFromIl2Cpp]
    private bool TryDrawPlayerName(
        Soldier soldier,
        Soldier localPlayer,
        Camera camera)
    {
        if (soldier == localPlayer ||
            !soldier.NotDeadAndSurrendered() ||
            !soldier.gameObject.activeInHierarchy)
        {
            return false;
        }

        var syncPlayer = soldier.GetSyncher();
        if (!IsRemotePlayer(syncPlayer))
            return false;

        var controllingPlayer = syncPlayer.Controller ?? syncPlayer.Owner;
        if (controllingPlayer == null)
            return false;

        var sameSquad = soldier.joinedSquad != null &&
                        soldier.joinedSquad == localPlayer.joinedSquad;
        if (WorldHudVisibility.ShouldHideSquadmateNameInSameVehicle(soldier))
            return false;

        if (!sameSquad &&
            !ResourcesManager.IsSameFaction(soldier.faction, localPlayer.faction))
        {
            return false;
        }

        var nickname = (string?)controllingPlayer.NickName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nickname))
            return false;

        if (!ContextualWorldNameProjection.TryProject(
                camera,
                soldier.UINamePos(),
                LabelWidth,
                LabelHeight,
                out var rect,
                out var alpha))
        {
            return false;
        }

        var priorColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(rect, nickname, _labelStyle!);
        GUI.color = priorColor;
        return true;
    }

    [HideFromIl2Cpp]
    private void EnsureStyles()
    {
        var fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.014f), 14, 22);
        if (_labelStyle != null && _labelStyle.fontSize == fontSize)
            return;

        _labelStyle = CloneStyle(GUI.skin.label);
        _labelStyle.alignment = TextAnchor.MiddleCenter;
        _labelStyle.fontSize = fontSize;
        _labelStyle.fontStyle = FontStyle.Bold;
        _labelStyle.richText = false;
        _labelStyle.wordWrap = false;
        SetTextColor(_labelStyle, new Color(1f, 0.82f, 0.2f, 1f));
    }

    [HideFromIl2Cpp]
    private void RefreshStyleFont()
    {
        var playerNameFont = PlayerController.playerNameFont;
        if (playerNameFont == null || _labelStyle == null)
            return;

        _labelStyle.font = playerNameFont;
    }

    [HideFromIl2Cpp]
    private static GUIStyle CloneStyle(GUIStyle source)
    {
        var style = new GUIStyle();
        GUIStyle.Internal_Copy(style, source);
        return style;
    }

    [HideFromIl2Cpp]
    private static void SetTextColor(GUIStyle style, Color color)
    {
        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        style.focused.textColor = color;
    }

    [HideFromIl2Cpp]
    private void ReportError(Exception exception)
    {
        var signature = exception.GetType().FullName + ": " + exception.Message;
        if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal))
            return;

        _lastErrorSignature = signature;
        Plugin.LogSource.LogWarning(
            $"Multiplayer player-name overlay failed (further identical errors suppressed): {exception.Message}");
    }
}
