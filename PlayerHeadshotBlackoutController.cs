using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.UI;

namespace ER2RealismOverhaul;

/// <summary>
/// Presents a lethal local-player headshot inside the native death panel. The
/// black layer belongs to that panel, so it follows the game's actual death UI
/// lifecycle while the panel's reason, name, and year remain readable above it.
/// </summary>
internal sealed class PlayerHeadshotBlackoutController : MonoBehaviour
{
    private const int BlackoutGuiDepth = -5000;

    private static PlayerHeadshotBlackoutController? _instance;
    private HeadshotBlackoutState _state;

    private GameObject? _panelLayerObject;
    private Image? _panelLayer;
    private DeathPanel? _layerOwner;
    private bool _ownsAudioVolume;
    private float _blackness;
    private float _nativeAudioVolume;
    private float _appliedAudioVolume;
    private string _lastErrorSignature = string.Empty;

    private void Awake()
    {
        _instance = this;
    }

    internal static bool IsPresenting => _instance != null && _instance._state.Active;

    internal static void NotePlayerHeadHit(int soldierId)
    {
        if (_instance != null)
            _instance._state.RecordHeadHit(soldierId, Time.realtimeSinceStartupAsDouble);
    }

    internal static void NotePlayerKilled(int soldierId)
    {
        var instance = _instance;
        if (instance == null ||
            !Settings.HeadshotDeathBlackoutEnabled.Value ||
            !instance._state.TryBegin(soldierId, Time.realtimeSinceStartupAsDouble))
        {
            return;
        }

        try
        {
            instance.BeginBlackout();
        }
        catch (Exception ex)
        {
            instance.Restore();
            instance.ReportError(ex);
        }
    }

    internal static void RefreshDeathPanelLayer()
    {
        var instance = _instance;
        if (instance == null || !instance._state.Active)
            return;

        try
        {
            instance.EnsurePanelLayer();
            instance.PlaceDeathTextAboveLayer();
            instance.Apply(instance._blackness);
        }
        catch (Exception ex)
        {
            instance.Restore();
            instance.ReportError(ex);
        }
    }

    internal static void EndForRespawn()
    {
        _instance?.Restore();
    }

    internal static void OnSquadSelection()
    {
        var instance = _instance;
        if (instance == null)
            return;
        try
        {
            if (IsSquadPickerOpen())
                instance.Restore();
        }
        catch (Exception ex)
        {
            instance.Restore();
            instance.ReportError(ex);
        }
    }

    private void LateUpdate()
    {
        if (!_state.Active)
        {
            if (_ownsAudioVolume)
                Restore();
            return;
        }

        try
        {
            var controlled = Soldier.CurrentControlledSoldierOrNull();
            var livingId = controlled != null && controlled.IsAlive ? controlled.GetInstanceID() : 0;
            var blackness = _state.Opacity(Time.realtimeSinceStartupAsDouble, livingId, IsSquadPickerOpen());
            if (blackness <= 0f || !Settings.HeadshotDeathBlackoutEnabled.Value)
            {
                Restore();
                return;
            }

            EnsurePanelLayer();
            Apply(blackness);
        }
        catch (Exception ex)
        {
            Restore();
            ReportError(ex);
        }
    }

    private void OnGUI()
    {
        // The damage call can happen before DeathPanel is activated. This covers
        // that first frame, then yields to the panel-owned uGUI layer.
        if (!_state.Active ||
            (_panelLayerObject != null && _panelLayerObject.activeInHierarchy) ||
            _blackness <= 0.001f ||
            Event.current == null || Event.current.type != EventType.Repaint)
        {
            return;
        }

        var previousDepth = GUI.depth;
        var previousColor = GUI.color;
        try
        {
            GUI.depth = BlackoutGuiDepth;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(_blackness));
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        }
        finally
        {
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }
    }

    private void OnDisable()
    {
        Restore();
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        Restore();
        DestroyPanelLayer();
    }

    [HideFromIl2Cpp]
    private void BeginBlackout()
    {
        if (!_ownsAudioVolume)
        {
            _nativeAudioVolume = Mathf.Clamp01(AudioListener.volume);
            _appliedAudioVolume = _nativeAudioVolume;
            _ownsAudioVolume = true;
        }

        EnsurePanelLayer();
        Apply(1f);
    }

    [HideFromIl2Cpp]
    private static bool IsSquadPickerOpen()
    {
        var gui = PlayerGUI.instance;
        var mask = gui != null ? gui.squadGUIMask : null;
        return mask != null && mask.gameObject.activeInHierarchy && PlayerGUI.GetGUISquad() != null;
    }

    [HideFromIl2Cpp]
    private void Apply(float blackness)
    {
        _blackness = Mathf.Clamp01(blackness);
        if (_panelLayer != null && _panelLayerObject != null)
        {
            _panelLayer.color = new Color(0f, 0f, 0f, _blackness);
            if (!_panelLayerObject.activeSelf)
                _panelLayerObject.SetActive(true);
        }

        var currentVolume = AudioListener.volume;
        if (!Mathf.Approximately(currentVolume, _appliedAudioVolume))
            _nativeAudioVolume = Mathf.Clamp01(currentVolume);

        _appliedAudioVolume = _nativeAudioVolume * (1f - _blackness);
        AudioListener.volume = _appliedAudioVolume;
    }

    [HideFromIl2Cpp]
    private void Restore()
    {
        _state = default;
        _blackness = 0f;

        try
        {
            if (_panelLayerObject != null && _panelLayerObject.activeSelf)
                _panelLayerObject.SetActive(false);
        }
        catch
        {
            // Scene teardown can destroy the panel between checks.
        }

        if (_ownsAudioVolume)
        {
            try
            {
                if (Mathf.Approximately(AudioListener.volume, _appliedAudioVolume))
                    AudioListener.volume = _nativeAudioVolume;
                _ownsAudioVolume = false;
            }
            catch (Exception ex)
            {
                // A scene change can briefly make the listener unavailable.
                ReportError(ex);
            }
        }
    }

    [HideFromIl2Cpp]
    private void EnsurePanelLayer()
    {
        var panel = DeathPanel.instance;
        if (panel == null)
        {
            if (_panelLayerObject != null)
                DestroyPanelLayer();
            return;
        }

        if (_panelLayer != null && _layerOwner != null &&
            _layerOwner.GetInstanceID() == panel.GetInstanceID())
        {
            return;
        }

        DestroyPanelLayer();

        var layerObject = new GameObject("ER2 Headshot Blackout Layer");
        // A panel can disappear during initialization. Keep the layer hidden until
        // Apply and retain ownership even if creating a component fails.
        layerObject.SetActive(false);
        _panelLayerObject = layerObject;
        layerObject.layer = panel.gameObject.layer;
        var rectTransform = layerObject
            .AddComponent(Il2CppType.Of<RectTransform>())
            .TryCast<RectTransform>();
        if (rectTransform == null)
        {
            DestroyPanelLayer();
            throw new InvalidOperationException("could not create the death-panel blackout rect");
        }

        rectTransform.SetParent(panel.transform, false);
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
        rectTransform.anchoredPosition3D = Vector3.zero;

        layerObject.AddComponent(Il2CppType.Of<CanvasRenderer>());
        var image = layerObject.AddComponent(Il2CppType.Of<Image>()).TryCast<Image>();
        if (image == null)
        {
            DestroyPanelLayer();
            throw new InvalidOperationException("could not create the death-panel blackout image");
        }

        image.raycastTarget = false;
        image.color = new Color(0f, 0f, 0f, _blackness);

        _panelLayer = image;
        _layerOwner = panel;
        PlaceDeathTextAboveLayer();
    }

    [HideFromIl2Cpp]
    private void PlaceDeathTextAboveLayer()
    {
        if (_panelLayerObject == null || _layerOwner == null)
            return;

        _panelLayerObject.transform.SetAsLastSibling();
        Promote(_layerOwner.text_reason);
        Promote(_layerOwner.text_name);
        Promote(_layerOwner.text_year);
    }

    [HideFromIl2Cpp]
    private static void Promote(Component? component)
    {
        if (component != null)
            component.transform.SetAsLastSibling();
    }

    [HideFromIl2Cpp]
    private void DestroyPanelLayer()
    {
        if (_panelLayerObject != null)
        {
            _panelLayerObject.SetActive(false);
            UnityEngine.Object.Destroy(_panelLayerObject);
        }

        _panelLayerObject = null;
        _panelLayer = null;
        _layerOwner = null;
    }

    [HideFromIl2Cpp]
    private void ReportError(Exception exception)
    {
        var signature = exception.GetType().FullName + ": " + exception.Message;
        if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal))
            return;

        _lastErrorSignature = signature;
        Plugin.LogSource.LogWarning(
            $"Headshot death presentation failed (further identical errors suppressed): {exception.Message}");
    }
}

[HarmonyPatch(typeof(SkipPromptGUI), nameof(SkipPromptGUI.Draw))]
internal static class HeadshotBlackoutSkipPromptPatch
{
    // Draw is presentation only. The native skip input and respawn coroutine keep running.
    private static bool Prefix() => !PlayerHeadshotBlackoutController.IsPresenting;
}

[HarmonyPatch(typeof(PlayerGUI), nameof(PlayerGUI.StartSquadSelection))]
internal static class HeadshotBlackoutSquadSelectionPatch
{
    private static void Postfix() => PlayerHeadshotBlackoutController.OnSquadSelection();
}

[HarmonyPatch(typeof(DeathPanel), nameof(DeathPanel.ShowDeath))]
internal static class HeadshotDeathPanelPresentationPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        PlayerHeadshotBlackoutController.RefreshDeathPanelLayer();
    }
}

[HarmonyPatch(
    typeof(PlayerController),
    nameof(PlayerController.SetPlayer),
    typeof(Soldier),
    typeof(float))]
internal static class HeadshotBlackoutPlayerAssignmentPatch
{
    [HarmonyPostfix]
    private static void Postfix(Soldier __0)
    {
        if (__0 != null && __0.IsAlive)
            PlayerHeadshotBlackoutController.EndForRespawn();
    }
}

[HarmonyPatch(typeof(RespawnPanel), nameof(RespawnPanel.EnableRespawnPanel))]
internal static class HeadshotBlackoutRespawnPanelPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        PlayerHeadshotBlackoutController.EndForRespawn();
    }
}

[HarmonyPatch(typeof(RespawnPanel), nameof(RespawnPanel.SetRespawningView))]
internal static class HeadshotBlackoutRespawningViewPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        PlayerHeadshotBlackoutController.EndForRespawn();
    }
}
