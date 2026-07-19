using Il2CppInterop.Runtime.Attributes;
using Photon.Pun;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class MultiplayerPlayerNameController : MonoBehaviour
{
    private const float PlayerRefreshInterval = 0.50f;
    private const float LabelWidth = 240f;
    private const float LabelHeight = 28f;

    private readonly List<SyncSoldier> _players = new();
    private float _nextPlayerRefreshAt;
    private GUIStyle? _labelStyle;
    private GUIStyle? _shadowStyle;
    private string _lastErrorSignature = string.Empty;

    private void Update()
    {
        try
        {
            if (!ShouldMaintainPlayerList())
            {
                _players.Clear();
                _nextPlayerRefreshAt = 0f;
                return;
            }

            if (Time.unscaledTime >= _nextPlayerRefreshAt)
                RefreshPlayers();
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void OnGUI()
    {
        try
        {
            if (!ShouldDrawNames() || Event.current.type != EventType.Repaint)
                return;

            var camera = Camera.main;
            var localPlayer = Soldier.CurrentControlledSoldierOrNull();
            if (camera == null || localPlayer == null)
                return;

            EnsureStyles();
            RefreshStyleFont();

            var previousDepth = GUI.depth;
            GUI.depth = -950;
            try
            {
                foreach (var syncPlayer in _players)
                    DrawPlayerName(syncPlayer, localPlayer, camera);
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
    private bool ShouldMaintainPlayerList()
    {
        return Settings.KeepMultiplayerPlayerNamesWithHudDisabled.Value && PhotonNetwork.InRoom;
    }

    [HideFromIl2Cpp]
    private bool ShouldDrawNames()
    {
        if (!ShouldMaintainPlayerList())
            return false;

        var settings = SavableData.Settings;
        return settings != null && settings.system != null && !settings.system.enableGUI;
    }

    [HideFromIl2Cpp]
    private void RefreshPlayers()
    {
        _nextPlayerRefreshAt = Time.unscaledTime + PlayerRefreshInterval;
        _players.Clear();

        foreach (var syncPlayer in UnityEngine.Object.FindObjectsOfType<SyncSoldier>())
        {
            if (syncPlayer != null)
                _players.Add(syncPlayer);
        }
    }

    [HideFromIl2Cpp]
    private void DrawPlayerName(SyncSoldier syncPlayer, Soldier localPlayer, Camera camera)
    {
        if (syncPlayer == null || !syncPlayer.isActiveAndEnabled || syncPlayer.IsMine ||
            !syncPlayer.IsControlledByAPlayer() || syncPlayer.Owner == null)
        {
            return;
        }

        var soldier = syncPlayer.soldier;
        if (soldier == null || soldier == localPlayer || !soldier.NotDeadAndSurrendered() ||
            !string.Equals(soldier.faction, localPlayer.faction, StringComparison.Ordinal))
        {
            return;
        }

        var nickname = (string?)syncPlayer.Owner.NickName;
        if (string.IsNullOrWhiteSpace(nickname))
            return;

        var anchor = soldier.cameraPosition != null
            ? soldier.cameraPosition.position + Vector3.up * 0.25f
            : soldier.transform.position + Vector3.up * 2.1f;
        var screenPoint = camera.WorldToScreenPoint(anchor);
        if (screenPoint.z <= 0f)
            return;

        var rect = new Rect(
            screenPoint.x - LabelWidth * 0.5f,
            Screen.height - screenPoint.y - LabelHeight * 0.5f,
            LabelWidth,
            LabelHeight);
        var shadowRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height);
        GUI.Label(shadowRect, nickname, _shadowStyle!);
        GUI.Label(rect, nickname, _labelStyle!);
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
        SetTextColor(_labelStyle, Color.white);

        _shadowStyle = CloneStyle(_labelStyle);
        SetTextColor(_shadowStyle, new Color(0f, 0f, 0f, 0.85f));
    }

    [HideFromIl2Cpp]
    private void RefreshStyleFont()
    {
        var playerNameFont = PlayerController.playerNameFont;
        if (playerNameFont == null || _labelStyle == null || _shadowStyle == null)
            return;

        _labelStyle.font = playerNameFont;
        _shadowStyle.font = playerNameFont;
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
