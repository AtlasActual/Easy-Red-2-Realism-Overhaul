using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Adds an explicit, per-life infantry squad join action to the native multiplayer
/// player profile. Squad.Join already owns the game's buffered SyncSquad RPC, so
/// the resulting membership is visible to every client without custom networking.
/// </summary>
internal sealed class MultiplayerSharedSquadController : MonoBehaviour
{
    private const float TargetRefreshInterval = 0.25f;
    private const float FeedbackDuration = 3.5f;
    private const float PanelWidth = 400f;
    private const float PanelHeight = 104f;

    private static int _selectedActorNumber = -1;
    private static string _selectedNickname = string.Empty;
    private static int _selectionRevision;

    private SyncSoldier? _targetSync;
    private int _observedSelectionRevision = -1;
    private float _nextTargetRefreshAt;
    private float _feedbackUntil;
    private string _feedback = string.Empty;
    private string _lastErrorSignature = string.Empty;
    private GUIStyle? _titleStyle;
    private GUIStyle? _messageStyle;
    private GUIStyle? _buttonStyle;

    internal static void SelectTarget(Player? player)
    {
        if (player == null || player.ActorNumber == PhotonNetwork.LocalPlayer?.ActorNumber)
        {
            ClearTarget();
            return;
        }

        _selectedActorNumber = player.ActorNumber;
        _selectedNickname = CleanNickname((string?)player.NickName);
        _selectionRevision++;
    }

    internal static void ClearTarget()
    {
        _selectedActorNumber = -1;
        _selectedNickname = string.Empty;
        _selectionRevision++;
    }

    private void OnGUI()
    {
        try
        {
            if (!ShouldShowJoinPanel())
                return;

            RefreshTargetIfNeeded();
            EnsureStyles();

            var previousDepth = GUI.depth;
            GUI.depth = -980;
            try
            {
                DrawJoinPanel();
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
    private bool ShouldShowJoinPanel()
    {
        if (!PhotonNetwork.InRoom || _selectedActorNumber <= 0)
            return false;

        var minimap = MiniMapGUI.Instance;
        return minimap != null && minimap.playerProfile != null &&
               minimap.playerProfile.gameObject.activeInHierarchy;
    }

    [HideFromIl2Cpp]
    private void RefreshTargetIfNeeded()
    {
        if (_observedSelectionRevision != _selectionRevision)
        {
            _observedSelectionRevision = _selectionRevision;
            _targetSync = null;
            _feedback = string.Empty;
            _feedbackUntil = 0f;
            _nextTargetRefreshAt = 0f;
        }

        if (Time.unscaledTime < _nextTargetRefreshAt)
            return;

        _nextTargetRefreshAt = Time.unscaledTime + TargetRefreshInterval;
        _targetSync = null;
        SyncSoldier? fallback = null;

        foreach (var syncSoldier in UnityEngine.Object.FindObjectsOfType<SyncSoldier>())
        {
            if (syncSoldier == null || !syncSoldier.isActiveAndEnabled ||
                syncSoldier.OwnerActorNr != _selectedActorNumber ||
                !syncSoldier.IsControlledByAPlayer())
            {
                continue;
            }

            fallback ??= syncSoldier;
            var soldier = syncSoldier.soldier;
            if (soldier != null && soldier.NotDeadAndSurrendered())
            {
                _targetSync = syncSoldier;
                return;
            }
        }

        _targetSync = fallback;
    }

    [HideFromIl2Cpp]
    private void DrawJoinPanel()
    {
        var scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.35f);
        var width = PanelWidth * scale;
        var height = PanelHeight * scale;
        var margin = 24f * scale;
        var rect = GetPanelRect(width, height, margin);

        GUI.Box(rect, GUIContent.none);

        var pad = 12f * scale;
        var titleHeight = 24f * scale;
        var buttonHeight = 42f * scale;
        GUI.Label(
            new Rect(rect.x + pad, rect.y + 5f * scale, rect.width - pad * 2f, titleHeight),
            "INFANTRY SQUAD",
            _titleStyle!);

        var evaluation = EvaluateJoin();
        var buttonRect = new Rect(
            rect.x + pad,
            rect.yMax - pad - buttonHeight,
            rect.width - pad * 2f,
            buttonHeight);

        if (Time.unscaledTime < _feedbackUntil && !string.IsNullOrWhiteSpace(_feedback))
        {
            GUI.Label(buttonRect, _feedback, _messageStyle!);
            return;
        }

        var previousEnabled = GUI.enabled;
        GUI.enabled = evaluation.CanJoin;
        try
        {
            if (GUI.Button(buttonRect, evaluation.Label, _buttonStyle!))
                TryJoinSelectedPlayer();
        }
        finally
        {
            GUI.enabled = previousEnabled;
        }
    }

    [HideFromIl2Cpp]
    private static Rect GetPanelRect(float width, float height, float margin)
    {
        var fallback = new Rect(Screen.width - width - margin, Screen.height - height - margin, width, height);
        var profile = MiniMapGUI.Instance?.playerProfile;
        if (profile == null)
            return fallback;

        try
        {
            var corners = new Il2CppStructArray<Vector3>(4);
            profile.GetWorldCorners(corners);

            var canvas = profile.GetComponentInParent<Canvas>();
            Camera? canvasCamera = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                canvasCamera = canvas.worldCamera;

            var lowerLeft = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[0]);
            var upperRight = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[2]);
            var profileLeft = Mathf.Min(lowerLeft.x, upperRight.x);
            var profileRight = Mathf.Max(lowerLeft.x, upperRight.x);
            var profileBottom = Mathf.Min(lowerLeft.y, upperRight.y);
            var profileTop = Mathf.Max(lowerLeft.y, upperRight.y);

            var alignedX = Mathf.Clamp(
                (profileLeft + profileRight - width) * 0.5f,
                margin,
                Mathf.Max(margin, Screen.width - width - margin));

            var belowY = Screen.height - profileBottom + margin;
            if (belowY + height <= Screen.height - margin)
                return new Rect(alignedX, belowY, width, height);

            var aboveY = Screen.height - profileTop - height - margin;
            if (aboveY >= margin)
                return new Rect(alignedX, aboveY, width, height);

            if (profileRight + margin + width <= Screen.width - margin)
            {
                var sideY = Mathf.Clamp(
                    Screen.height - profileTop,
                    margin,
                    Mathf.Max(margin, Screen.height - height - margin));
                return new Rect(profileRight + margin, sideY, width, height);
            }

            if (profileLeft - margin - width >= margin)
            {
                var sideY = Mathf.Clamp(
                    Screen.height - profileTop,
                    margin,
                    Mathf.Max(margin, Screen.height - height - margin));
                return new Rect(profileLeft - margin - width, sideY, width, height);
            }
        }
        catch
        {
            // Some custom UI canvases do not expose usable screen-space corners.
        }

        return fallback;
    }

    [HideFromIl2Cpp]
    private JoinEvaluation EvaluateJoin()
    {
        var targetSoldier = _targetSync?.soldier;
        if (_targetSync == null || targetSoldier == null || !targetSoldier.NotDeadAndSurrendered())
            return JoinEvaluation.Unavailable($"{DisplayName()} IS NOT AVAILABLE");

        var localSoldier = Soldier.CurrentControlledSoldierOrNull();
        if (localSoldier == null || !localSoldier.NotDeadAndSurrendered())
            return JoinEvaluation.Unavailable("SPAWN AS INFANTRY TO JOIN");

        var localSync = localSoldier.GetComponent<SyncSoldier>();
        if (localSync == null || (!localSync.IsMine && !localSync.OwnSyncherOnline))
            return JoinEvaluation.Unavailable("LOCAL PLAYER IS NOT READY");

        if (!ResourcesManager.IsSameFaction(localSoldier.faction, targetSoldier.faction))
            return JoinEvaluation.Unavailable("CANNOT JOIN AN ENEMY SQUAD");

        var localSquad = localSoldier.joinedSquad;
        var targetSquad = targetSoldier.joinedSquad;
        if (localSquad == null || targetSquad == null || !targetSquad.fullySpawned)
            return JoinEvaluation.Unavailable("SQUAD IS NOT READY");

        if (localSquad.IsVehicleCrew || targetSquad.IsVehicleCrew)
            return JoinEvaluation.Unavailable("INFANTRY SQUADS ONLY");

        if (localSquad == targetSquad)
            return JoinEvaluation.Unavailable($"ALREADY IN {DisplayName()}'S SQUAD");

        return new JoinEvaluation(true, $"JOIN {DisplayName()}'S SQUAD");
    }

    [HideFromIl2Cpp]
    private void TryJoinSelectedPlayer()
    {
        var evaluation = EvaluateJoin();
        if (!evaluation.CanJoin)
            return;

        var localSoldier = Soldier.CurrentControlledSoldierOrNull();
        var targetSoldier = _targetSync?.soldier;
        var targetSquad = targetSoldier?.joinedSquad;
        if (localSoldier == null || targetSquad == null)
            return;

        targetSquad.Join(localSoldier);
        _feedback = $"JOINED {DisplayName()}'S SQUAD";
        _feedbackUntil = Time.unscaledTime + FeedbackDuration;
        Plugin.LogSource.LogInfo(
            $"Player joined {_selectedNickname}'s infantry squad by explicit player-list action.");
    }

    [HideFromIl2Cpp]
    private void EnsureStyles()
    {
        var fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.016f), 15, 24);
        if (_buttonStyle != null && _buttonStyle.fontSize == fontSize)
            return;

        _titleStyle = CloneStyle(GUI.skin.label);
        _titleStyle.alignment = TextAnchor.MiddleCenter;
        _titleStyle.fontSize = Mathf.Max(12, fontSize - 3);
        _titleStyle.fontStyle = FontStyle.Bold;
        _titleStyle.richText = false;
        SetTextColor(_titleStyle, new Color(0.90f, 0.56f, 0.18f, 1f));

        _messageStyle = CloneStyle(GUI.skin.label);
        _messageStyle.alignment = TextAnchor.MiddleCenter;
        _messageStyle.fontSize = fontSize;
        _messageStyle.fontStyle = FontStyle.Bold;
        _messageStyle.richText = false;
        SetTextColor(_messageStyle, new Color(0.45f, 0.90f, 0.58f, 1f));

        _buttonStyle = CloneStyle(GUI.skin.button);
        _buttonStyle.alignment = TextAnchor.MiddleCenter;
        _buttonStyle.fontSize = fontSize;
        _buttonStyle.fontStyle = FontStyle.Bold;
        _buttonStyle.richText = false;
    }

    [HideFromIl2Cpp]
    private string DisplayName()
    {
        return string.IsNullOrWhiteSpace(_selectedNickname) ? "PLAYER" : _selectedNickname;
    }

    [HideFromIl2Cpp]
    private static string CleanNickname(string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
            return "PLAYER";

        var cleaned = nickname.Trim().Replace("\r", " ").Replace("\n", " ");
        return cleaned.Length <= 32 ? cleaned : cleaned[..32];
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
            $"Multiplayer shared-squad UI failed (further identical errors suppressed): {exception.Message}");
    }

    private readonly record struct JoinEvaluation(bool CanJoin, string Label)
    {
        internal static JoinEvaluation Unavailable(string label) => new(false, label);
    }
}

[HarmonyPatch(typeof(MiniMapGUI), nameof(MiniMapGUI.ShowPlayerTab))]
internal static class MultiplayerSharedSquadPlayerTabPatch
{
    private static void Postfix(PlayerListLine player)
    {
        MultiplayerSharedSquadController.SelectTarget(player?.assignedPlayer);
    }
}

[HarmonyPatch(typeof(MiniMapGUI), "ClosePlayerTab")]
internal static class MultiplayerSharedSquadClosePlayerTabPatch
{
    private static void Postfix()
    {
        MultiplayerSharedSquadController.ClearTarget();
    }
}
