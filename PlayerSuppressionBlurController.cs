using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ER2RealismOverhaul;

/// <summary>
/// Blends a separate high-priority post-processing volume from suppression
/// actually received by the local player. A separate volume avoids rewriting
/// the game's aiming and accessibility depth-of-field settings.
/// </summary>
internal sealed class PlayerSuppressionBlurController : MonoBehaviour
{
    // A single native bullet flyby normally contributes 0.25 suppression. Hold
    // its peak just long enough to render clearly, then use a linear release
    // that is guaranteed to reach zero instead of an asymptotic smoothing tail.
    private const float PeakHoldSeconds = 0.18f;
    private const float SuppressionReleasePerSecond = 0.65f;
    private const float ActiveWeightThreshold = 0.0001f;

    private static PlayerSuppressionBlurController? _instance;

    private GameObject? _volumeObject;
    private Volume? _volume;
    private VolumeProfile? _profile;
    private float _suppressionEnvelope;
    private float _lastSuppressionAt = float.NegativeInfinity;
    private float _currentWeight;
    private float _lastVignetteMultiplier;
    private bool _initializationFailed;

    public PlayerSuppressionBlurController(IntPtr pointer) : base(pointer)
    {
    }

    private void Awake()
    {
        _instance = this;
        _lastVignetteMultiplier = Settings.PlayerSuppressionVignetteMultiplier.Value;
    }

    internal static void AddReceivedSuppression(float normalizedAmount)
    {
        var instance = _instance;
        if (instance == null || normalizedAmount <= 0f)
            return;

        // Rapid hits build the effect, but old cumulative native suppression
        // cannot resurrect it after this envelope has released.
        instance._suppressionEnvelope = Mathf.Clamp01(
            instance._suppressionEnvelope + Mathf.Clamp01(normalizedAmount));
        instance._lastSuppressionAt = Time.unscaledTime;
    }

    private void LateUpdate()
    {
        RefreshVignetteIfSettingChanged();

        var enabled = Settings.PlayerSuppressionBlurEnabled.Value &&
                      Settings.PlayerSuppressionBlurStrength.Value > 0.001f &&
                      PlayerController.fpsCamera;
        var deltaTime = Time.unscaledDeltaTime;

        if (!enabled)
        {
            ClearBlur();
            return;
        }

        if (Time.unscaledTime - _lastSuppressionAt > PeakHoldSeconds)
        {
            _suppressionEnvelope = Mathf.MoveTowards(
                _suppressionEnvelope,
                0f,
                SuppressionReleasePerSecond * deltaTime);
        }

        // Linear scaling makes one 25-point hit one quarter as intense as a
        // fully built suppression effect. Avoiding sqrt prevents tiny stale
        // values from looking disproportionately strong.
        _currentWeight = Mathf.Clamp01(
            _suppressionEnvelope * Settings.PlayerSuppressionBlurStrength.Value);

        if (_currentWeight > ActiveWeightThreshold && _volume == null)
            EnsureVolume();

        if (_volume != null)
        {
            var active = _currentWeight > ActiveWeightThreshold;
            _volume.weight = active ? _currentWeight : 0f;
            _volume.enabled = active;
        }
    }

    private void RefreshVignetteIfSettingChanged()
    {
        var multiplier = Settings.PlayerSuppressionVignetteMultiplier.Value;
        if (Mathf.Approximately(multiplier, _lastVignetteMultiplier))
            return;

        _lastVignetteMultiplier = multiplier;
        PlayerSuppressionVignettePatch.RefreshLastSuppression();
    }

    private void ClearBlur()
    {
        _suppressionEnvelope = 0f;
        _lastSuppressionAt = float.NegativeInfinity;
        _currentWeight = 0f;

        if (_volume == null)
            return;

        // Weight zero should exclude a volume, but disabling the component as
        // well guarantees its discrete Gaussian-mode override cannot linger.
        _volume.weight = 0f;
        _volume.enabled = false;
    }

    private void EnsureVolume()
    {
        if (_initializationFailed || _volume != null)
            return;

        try
        {
            _volumeObject = new GameObject("ER2 Player Suppression Blur");
            UnityEngine.Object.DontDestroyOnLoad(_volumeObject);
            var nativeVolumeGui = UnityEngine.Object.FindObjectOfType<VolumeGUI>();
            if (nativeVolumeGui != null)
                _volumeObject.layer = nativeVolumeGui.gameObject.layer;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "ER2 Player Suppression Blur Profile";

            var depthOfField = ScriptableObject.CreateInstance<DepthOfField>();
            depthOfField.name = "ER2 Suppression Depth Of Field";
            depthOfField.active = true;
            depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
            depthOfField.gaussianStart.Override(0.1f);
            depthOfField.gaussianEnd.Override(12f);
            depthOfField.gaussianMaxRadius.Override(1f);
            depthOfField.highQualitySampling.Override(false);
            _profile.components.Add(depthOfField);

            _volume = _volumeObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 1000f;
            _volume.sharedProfile = _profile;
            _volume.weight = _currentWeight;
            _volume.enabled = _currentWeight > ActiveWeightThreshold;
        }
        catch (Exception ex)
        {
            _initializationFailed = true;
            _currentWeight = 0f;
            Plugin.LogSource.LogWarning(
                $"Player suppression blur could not be initialized; other suppression effects remain active: {ex.Message}");
            DestroyVolume();
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        DestroyVolume();
    }

    private void DestroyVolume()
    {
        if (_volume != null)
            _volume.weight = 0f;

        if (_volumeObject != null)
            UnityEngine.Object.Destroy(_volumeObject);
        if (_profile != null)
            UnityEngine.Object.Destroy(_profile);

        _volume = null;
        _volumeObject = null;
        _profile = null;
    }
}
