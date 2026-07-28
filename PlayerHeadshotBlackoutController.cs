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
    private const float LethalHeadHitWindowSeconds = 0.35f;
    private const float MaximumBlackoutSeconds = 12f;
    private const float RecoveryFadeSeconds = 0.35f;
    private const int BlackoutGuiDepth = -5000;

    private static PlayerHeadshotBlackoutController? _instance;
    private static float _lastPlayerHeadHitAt = float.NegativeInfinity;

    private GameObject? _panelLayerObject;
    private Image? _panelLayer;
    private DeathPanel? _layerOwner;
    private bool _blackoutActive;
    private bool _recovering;
    private bool _ownsAudioVolume;
    private float _blackness;
    private float _blackoutStartedAt;
    private float _recoveryStartedAt;
    private float _nativeAudioVolume;
    private float _appliedAudioVolume;
    private string _lastErrorSignature = string.Empty;

    private void Awake()
    {
        _instance = this;
    }

    internal static void NotePlayerHeadHit()
    {
        _lastPlayerHeadHitAt = Time.unscaledTime;
    }

    internal static void NotePlayerKilled()
    {
        var instance = _instance;
        if (instance == null ||
            !Settings.HeadshotDeathBlackoutEnabled.Value ||
            Time.unscaledTime - _lastPlayerHeadHitAt > LethalHeadHitWindowSeconds)
        {
            return;
        }

        _lastPlayerHeadHitAt = float.NegativeInfinity;
        instance.BeginBlackout();
    }

    internal static void RefreshDeathPanelLayer()
    {
        var instance = _instance;
        if (instance == null || !instance._blackoutActive)
            return;

        try
        {
            instance.EnsurePanelLayer();
            instance.Apply(instance._blackness);
        }
        catch (Exception ex)
        {
            instance.ReportError(ex);
        }
    }

    internal static void EndForRespawn()
    {
        _instance?.Restore();
    }

    private void LateUpdate()
    {
        if (!_blackoutActive)
            return;

        try
        {
            EnsurePanelLayer();

            var now = Time.unscaledTime;
            if (!_recovering && ShouldRecover(now))
            {
                _recovering = true;
                _recoveryStartedAt = now;
            }

            var blackness = 1f;
            if (_recovering)
            {
                var progress = RecoveryFadeSeconds <= 0f
                    ? 1f
                    : Mathf.Clamp01((now - _recoveryStartedAt) / RecoveryFadeSeconds);
                if (progress >= 1f)
                {
                    Restore();
                    return;
                }

                blackness = 1f - progress;
            }

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
        if (!_blackoutActive ||
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

        _blackoutActive = true;
        _recovering = false;
        _blackoutStartedAt = Time.unscaledTime;
        EnsurePanelLayer();
        Apply(1f);

        Plugin.LogSource.LogInfo(
            $"Headshot blackout started using {(_panelLayer != null ? "the native death panel" : "the one-frame fallback")}.");
    }

    [HideFromIl2Cpp]
    private bool ShouldRecover(float now)
    {
        if (!Settings.HeadshotDeathBlackoutEnabled.Value ||
            now - _blackoutStartedAt >= MaximumBlackoutSeconds)
        {
            return true;
        }

        if (SkipPromptGUI.active)
            return false;

        var controlled = Soldier.CurrentControlledSoldierOrNull();
        return controlled != null && controlled.IsAlive;
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

            PlaceDeathTextAboveLayer();
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
        _blackoutActive = false;
        _recovering = false;
        _blackness = 0f;

        try
        {
            if (_panelLayerObject != null)
                _panelLayerObject.SetActive(false);

            if (_ownsAudioVolume)
            {
                AudioListener.volume = _nativeAudioVolume;
                _appliedAudioVolume = _nativeAudioVolume;
                _ownsAudioVolume = false;
            }
        }
        catch
        {
            // Scene teardown can destroy the panel between checks.
        }
    }

    [HideFromIl2Cpp]
    private void EnsurePanelLayer()
    {
        var panel = DeathPanel.instance;
        if (panel == null)
            return;

        if (_panelLayer != null && _layerOwner != null &&
            _layerOwner.GetInstanceID() == panel.GetInstanceID())
        {
            return;
        }

        DestroyPanelLayer();

        var layerObject = new GameObject("ER2 Headshot Blackout Layer");
        layerObject.layer = panel.gameObject.layer;
        var rectTransform = layerObject
            .AddComponent(Il2CppType.Of<RectTransform>())
            .TryCast<RectTransform>();
        if (rectTransform == null)
        {
            UnityEngine.Object.Destroy(layerObject);
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
            UnityEngine.Object.Destroy(layerObject);
            throw new InvalidOperationException("could not create the death-panel blackout image");
        }

        image.raycastTarget = false;
        image.color = new Color(0f, 0f, 0f, _blackness);

        _panelLayerObject = layerObject;
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
            UnityEngine.Object.Destroy(_panelLayerObject);

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
