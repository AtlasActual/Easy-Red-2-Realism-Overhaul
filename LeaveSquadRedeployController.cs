using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Adds a prominent change-squad action while the dead-player squad picker is
/// open. Easy Red 2 opens that picker before deassigning the dead soldier, so
/// controller ownership alone is not a reliable death-state signal.
/// </summary>
internal sealed class LeaveSquadRedeployController : MonoBehaviour
{
    private const float ButtonWidth = 360f;
    private const float ButtonHeight = 52f;

    private bool _visible;
    private GUIStyle? _buttonStyle;
    private string _lastErrorSignature = string.Empty;

    private void Update()
    {
        try
        {
            _visible = ShouldShow();
        }
        catch (Exception ex)
        {
            _visible = false;
            ReportError(ex);
        }
    }

    private void OnGUI()
    {
        if (!_visible)
            return;

        try
        {
            EnsureStyle();

            var scale = Mathf.Clamp(Screen.height / 1080f, 0.8f, 1.5f);
            var width = ButtonWidth * scale;
            var height = ButtonHeight * scale;
            var rect = new Rect(
                (Screen.width - width) * 0.5f,
                Screen.height - height - (26f * scale),
                width,
                height);

            var previousDepth = GUI.depth;
            bool clicked;
            try
            {
                GUI.depth = -10000;
                clicked = GUI.Button(rect, "CHANGE SQUAD", _buttonStyle!);
            }
            finally
            {
                GUI.depth = previousDepth;
            }

            if (clicked)
                ChangeSquad();
        }
        catch (Exception ex)
        {
            _visible = false;
            ReportError(ex);
        }
    }

    [HideFromIl2Cpp]
    private static bool ShouldShow()
    {
        if (!Settings.LeaveSquadRedeployEnabled.Value)
            return false;

        // IsSelectingSquad throws inside the game whenever PlayerGUI or its
        // selection mask has not been initialized, which is normal between
        // scenes. Read the same active state only after both objects exist.
        var playerGui = PlayerGUI.instance;
        if (playerGui == null)
            return false;

        var selectionMask = playerGui.squadGUIMask;
        if (selectionMask == null)
            return false;

        var selectionObject = selectionMask.gameObject;
        if (selectionObject == null ||
            !selectionObject.activeSelf ||
            PlayerGUI.GetGUISquad() == null)
        {
            return false;
        }

        var controller = PlayerController.currentController;
        if (controller == null)
            return false;

        var soldier = controller.ControlledCharacter;
        return soldier == null || soldier.IsDead;
    }

    [HideFromIl2Cpp]
    private void EnsureStyle()
    {
        if (_buttonStyle != null)
            return;

        _buttonStyle = new GUIStyle();
        GUIStyle.Internal_Copy(_buttonStyle, GUI.skin.button);
        _buttonStyle.alignment = TextAnchor.MiddleCenter;
        _buttonStyle.fontSize = 20;
        _buttonStyle.fontStyle = FontStyle.Bold;
        _buttonStyle.normal.textColor = new Color(1f, 0.92f, 0.86f, 1f);
        _buttonStyle.hover.textColor = Color.white;
        _buttonStyle.active.textColor = Color.white;
    }

    [HideFromIl2Cpp]
    private void ChangeSquad()
    {
        _visible = false;

        var playerGui = PlayerGUI.instance;
        if (playerGui != null)
        {
            playerGui.RespawnNewSquad();
            return;
        }

        // Retain the same safe fallback if the UI instance disappears during
        // the click because the battle is changing scenes.
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
