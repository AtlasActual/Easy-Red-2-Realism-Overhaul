using Il2CppInterop.Runtime.Attributes;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ER2RealismOverhaul;

/// <summary>
/// Adds a native-canvas action to the dead-player squad picker. Easy Red 2
/// represents this state by leaving squad selection open after the controller
/// has stopped controlling a soldier; the dead Soldier reference itself is not
/// a reliable visibility signal.
/// </summary>
internal sealed class LeaveSquadRedeployController : MonoBehaviour
{
    private const string ButtonObjectName = "ER2RealismOverhaulLeaveSquadRedeploy";

    private GameObject? _buttonObject;
    private RectTransform? _buttonCanvas;
    private UnityAction? _buttonAction;
    private string _lastErrorSignature = string.Empty;

    private void Awake()
    {
        _buttonAction = (Action)LeaveAndRedeploy;
    }

    private void Update()
    {
        try
        {
            var shouldShow = ShouldShow();
            if (shouldShow)
                EnsureButton();

            SetButtonVisible(shouldShow);
        }
        catch (Exception ex)
        {
            SetButtonVisible(false);
            ReportError(ex);
        }
    }

    private void OnDestroy()
    {
        try
        {
            var button = _buttonObject != null
                ? _buttonObject.GetComponent<Button>()
                : null;
            if (button != null && _buttonAction != null)
                button.onClick.RemoveListener(_buttonAction);
        }
        catch
        {
            // The UI canvas can already be gone during scene teardown.
        }
    }

    [HideFromIl2Cpp]
    private static bool ShouldShow()
    {
        if (!Settings.LeaveSquadRedeployEnabled.Value ||
            !PhotonNetwork.InRoom ||
            !PlayerGUI.IsSelectingSquad())
        {
            return false;
        }

        var controller = PlayerController.currentController;
        return controller != null && !controller.IsControllingPlayer();
    }

    [HideFromIl2Cpp]
    private void EnsureButton()
    {
        var canvas = RespawnPanel.GetCanvas();
        if (canvas == null)
            return;

        if (_buttonObject != null && _buttonCanvas == canvas)
            return;

        var existing = canvas.Find(ButtonObjectName);
        if (existing != null)
        {
            _buttonObject = existing.gameObject;
            _buttonCanvas = canvas;
            return;
        }

        var buttonObject = new GameObject(ButtonObjectName);
        buttonObject.transform.SetParent(canvas, false);

        var background = buttonObject.AddComponent<Image>();
        background.color = new Color(0.42f, 0.16f, 0.12f, 0.96f);

        var rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 42f);
        rect.sizeDelta = new Vector2(340f, 52f);

        var button = buttonObject.AddComponent<Button>();
        if (_buttonAction != null)
            button.onClick.AddListener(_buttonAction);

        var labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        var label = labelObject.AddComponent<Text>();
        label.text = "LEAVE SQUAD & REDEPLOY";
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(1f, 0.92f, 0.86f, 1f);
        label.fontSize = 18;
        label.fontStyle = FontStyle.Bold;
        label.raycastTarget = false;
        label.font = ResolveUiFont(canvas);

        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        _buttonObject = buttonObject;
        _buttonCanvas = canvas;
    }

    [HideFromIl2Cpp]
    private static Font? ResolveUiFont(RectTransform canvas)
    {
        try
        {
            var localizedFont = LocalizationManager.GetFont();
            if (localizedFont != null)
                return localizedFont;
        }
        catch
        {
            // Fall through to a font already owned by this canvas.
        }

        return canvas.GetComponentInChildren<Text>(true)?.font;
    }

    [HideFromIl2Cpp]
    private void SetButtonVisible(bool visible)
    {
        if (_buttonObject != null && _buttonObject.activeSelf != visible)
            _buttonObject.SetActive(visible);
    }

    [HideFromIl2Cpp]
    private void LeaveAndRedeploy()
    {
        SetButtonVisible(false);

        var controller = PlayerController.currentController;
        var soldier = controller != null ? controller.ControlledCharacter : null;
        var squad = soldier != null ? soldier.joinedSquad : null;
        if (soldier != null && squad != null)
            squad.Leave(soldier, true);

        PlayerGUI.CloseSquadSelection();
        RespawnPanel.EnableRespawnPanel(0.2f);
    }

    [HideFromIl2Cpp]
    private void ReportError(Exception exception)
    {
        var signature = exception.GetType().FullName + ": " + exception.Message;
        if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal))
            return;

        _lastErrorSignature = signature;
        Plugin.LogSource.LogWarning(
            $"Leave-squad redeploy UI failed (further identical errors suppressed): {exception.Message}");
    }
}
