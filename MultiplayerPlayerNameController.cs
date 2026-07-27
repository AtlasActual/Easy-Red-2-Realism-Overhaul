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
    private string _lastDiagnosticSignature = string.Empty;
    private float _nextDiagnosticAt;

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

            if (Time.unscaledTime >= _nextPlayerRefreshAt)
                RefreshPlayers();

            var camera = ResourcesManager.mainCamera;
            var localPlayer = Soldier.CurrentControlledSoldierOrNull();
            if (camera == null || localPlayer == null)
            {
                ReportNoNamesDiagnostic(
                    camera,
                    localPlayer,
                    activeRemote: 0,
                    controlled: 0,
                    identified: 0,
                    living: 0,
                    allied: 0,
                    projected: 0);
                return;
            }

            EnsureStyles();
            RefreshStyleFont();

            var activeRemote = 0;
            var controlled = 0;
            var identified = 0;
            var living = 0;
            var allied = 0;
            var projected = 0;
            var previousDepth = GUI.depth;
            GUI.depth = -950;
            try
            {
                foreach (var syncPlayer in _players)
                {
                    TryDrawPlayerName(syncPlayer, localPlayer, camera, out var gate);
                    if (gate >= 1)
                        activeRemote++;
                    if (gate >= 2)
                        controlled++;
                    if (gate >= 3)
                        identified++;
                    if (gate >= 4)
                        living++;
                    if (gate >= 5)
                        allied++;
                    if (gate >= 6)
                        projected++;
                }
            }
            finally
            {
                GUI.depth = previousDepth;
            }

            if (projected == 0)
            {
                ReportNoNamesDiagnostic(
                    camera,
                    localPlayer,
                    activeRemote,
                    controlled,
                    identified,
                    living,
                    allied,
                    projected);
            }
            else
            {
                _lastDiagnosticSignature = string.Empty;
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
        return settings != null &&
               settings.system != null &&
               (!settings.system.enableGUI || settings.system.disable3DMarkers);
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
    private bool TryDrawPlayerName(
        SyncSoldier syncPlayer,
        Soldier localPlayer,
        Camera camera,
        out int gate)
    {
        gate = 0;
        if (syncPlayer == null || !syncPlayer.isActiveAndEnabled)
            return false;

        var controllingPlayer = syncPlayer.Controller ?? syncPlayer.Owner;
        var localPhotonPlayer = PhotonNetwork.LocalPlayer;
        if (controllingPlayer != null &&
            localPhotonPlayer != null &&
            controllingPlayer.ActorNumber == localPhotonPlayer.ActorNumber)
            return false;

        gate = 1;
        if (!syncPlayer.IsControlledByAPlayer())
            return false;

        gate = 2;
        if (controllingPlayer == null)
            return false;

        gate = 3;
        var soldier = syncPlayer.soldier;
        if (soldier == null || soldier == localPlayer || !soldier.NotDeadAndSurrendered() ||
            !soldier.gameObject.activeInHierarchy)
            return false;

        gate = 4;
        if (!ResourcesManager.IsSameFaction(soldier.faction, localPlayer.faction))
            return false;

        gate = 5;
        var nickname = ResourcesManager.FilterNickname((string?)controllingPlayer.NickName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(nickname))
            return false;

        var anchor = soldier.GetPosition() + Vector3.up * 2.1f;
        var screenPoint = camera.WorldToScreenPoint(anchor);
        if (screenPoint.z <= 0f)
            return false;

        var rect = new Rect(
            screenPoint.x - LabelWidth * 0.5f,
            Screen.height - screenPoint.y - LabelHeight * 0.5f,
            LabelWidth,
            LabelHeight);
        var shadowRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height);
        GUI.Label(shadowRect, nickname, _shadowStyle!);
        GUI.Label(rect, nickname, _labelStyle!);
        gate = 6;
        return true;
    }

    [HideFromIl2Cpp]
    private void ReportNoNamesDiagnostic(
        Camera? camera,
        Soldier? localPlayer,
        int activeRemote,
        int controlled,
        int identified,
        int living,
        int allied,
        int projected)
    {
        if (PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.PlayerCount < 2 ||
            Time.unscaledTime < _nextDiagnosticAt)
        {
            return;
        }

        _nextDiagnosticAt = Time.unscaledTime + 10f;
        var system = SavableData.Settings?.system;
        var signature =
            $"hud={system?.enableGUI},markersOff={system?.disable3DMarkers},camera={camera != null}," +
            $"local={localPlayer != null},synchers={_players.Count},activeRemote={activeRemote}," +
            $"controlled={controlled},identified={identified},living={living},allied={allied},projected={projected}";
        if (string.Equals(signature, _lastDiagnosticSignature, StringComparison.Ordinal))
            return;

        _lastDiagnosticSignature = signature;
        Plugin.LogSource.LogInfo($"Multiplayer nameplate diagnostic (no names drawn): {signature}");
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
