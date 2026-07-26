using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.UI;

namespace ER2RealismOverhaul;

/// <summary>
/// Cuts the local player's view to black and drops all sound to zero on the
/// frame a bullet to the head kills them, in place of the death camera that
/// otherwise lingers on the body. The black is a canvas of the mod's own,
/// sorted one step below the game's death panel, so the world disappears while
/// the panel that names the soldier and offers the skip prompt still reads on
/// top of it. Nothing of the game's is reconfigured, and the blackout is
/// released by a per-frame watchdog rather than by whatever runs next.
/// </summary>
internal sealed class PlayerHeadshotBlackoutController : MonoBehaviour
{
    // The hit and the kill it causes are resolved inside one native damage call,
    // so this window only has to survive a kill deferred by a frame or two.
    private const float LethalHeadHitWindowSeconds = 0.35f;
    // Recovery normally comes from regaining control of a living soldier. This is
    // the unconditional backstop for everything else: a respawn that never
    // arrives, a mission end, a scene reload, an interrupted death sequence.
    private const float MaximumBlackoutSeconds = 12f;
    private const float RecoveryFadeSeconds = 0.35f;
    // Used only until the death panel's own canvas has been found. Any screen
    // space canvas draws over every camera, so this still blacks out the world.
    private const int FallbackSortingOrder = -1;
    // Unity's built-in UI layer, used until the death panel's layer is known.
    private const int DefaultUiLayer = 5;
    // Immediate-mode fallback, in front of the game but behind the mod's F10
    // settings menu at -10000, which stays usable while the screen is black.
    private const int BlackoutGuiDepth = -5000;

    private static PlayerHeadshotBlackoutController? _instance;
    private static float _lastPlayerHeadHitAt = float.NegativeInfinity;

    private GameObject? _overlayObject;
    private Canvas? _overlayCanvas;
    private Image? _overlayImage;
    private Canvas? _deathPanelCanvas;
    private bool _overlayFailed;

    private bool _blackoutActive;
    private bool _recovering;
    private bool _ownsAudioVolume;
    private float _blackness;
    private float _blackoutStartedAt;
    private float _recoveryStartedAt;
    private float _nativeAudioVolume;
    private float _appliedAudioVolume;
    private string _lastErrorSignature = string.Empty;

    public PlayerHeadshotBlackoutController(IntPtr pointer) : base(pointer)
    {
    }

    private void Awake()
    {
        _instance = this;
    }

    /// <summary>
    /// Records that the locally controlled soldier has just taken a bullet in
    /// the head. The native damage call that reports it also applies the damage.
    /// </summary>
    internal static void NotePlayerHeadHit()
    {
        _lastPlayerHeadHitAt = Time.unscaledTime;
    }

    /// <summary>
    /// Starts the blackout if the locally controlled soldier is dying from a
    /// head hit that has just been recorded.
    /// </summary>
    internal static void NotePlayerKilled()
    {
        var instance = _instance;
        if (instance == null)
        {
            Plugin.LogSource.LogWarning("Headshot blackout: player death seen but the controller is missing.");
            return;
        }

        if (!Settings.HeadshotDeathBlackoutEnabled.Value ||
            Time.unscaledTime - _lastPlayerHeadHitAt > LethalHeadHitWindowSeconds)
        {
            return;
        }

        // One blackout per lethal head hit; a later death presents its own.
        _lastPlayerHeadHitAt = float.NegativeInfinity;
        instance.BeginBlackout();
    }

    private void LateUpdate()
    {
        if (!_blackoutActive)
            return;

        try
        {
            var now = Time.unscaledTime;
            if (!_recovering && ShouldRecover(now))
            {
                _recovering = true;
                _recoveryStartedAt = now;
                Plugin.LogSource.LogInfo(
                    $"Headshot blackout releasing after {now - _blackoutStartedAt:F2}s (death screen prompt {(SkipPromptGUI.active ? "still up" : "down")}).");
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
            // Whatever failed, the player must end this frame able to see and hear.
            Restore();
            ReportError(ex);
        }
    }

    private void OnGUI()
    {
        // Only reached when the canvas could not be created at all.
        if (!_blackoutActive || !_overlayFailed || _blackness <= 0.001f ||
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
        catch (Exception ex)
        {
            Restore();
            ReportError(ex);
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
        DestroyOverlay();
    }

    [HideFromIl2Cpp]
    private void BeginBlackout()
    {
        EnsureOverlay();

        if (!_ownsAudioVolume)
        {
            _nativeAudioVolume = Mathf.Clamp01(AudioListener.volume);
            _appliedAudioVolume = _nativeAudioVolume;
            _ownsAudioVolume = true;
        }

        _blackoutActive = true;
        _recovering = false;
        _blackoutStartedAt = Time.unscaledTime;

        // The kill runs inside the game's own update, so applying here makes the
        // frame the player dies on the first black and silent one.
        Apply(1f);
        LogBlackoutState();
    }

    /// <summary>
    /// One line per blackout describing what is actually on screen. A canvas that
    /// is configured wrong renders nothing and reports nothing, which is exactly
    /// the failure this has to be able to tell apart from not starting at all.
    /// </summary>
    [HideFromIl2Cpp]
    private void LogBlackoutState()
    {
        try
        {
            var overlay = _overlayImage == null || _overlayCanvas == null || _overlayObject == null
                ? "overlay=immediate-mode fallback"
                : $"overlay=canvas mode={_overlayCanvas.renderMode} order={_overlayCanvas.sortingOrder} " +
                  $"layer={_overlayObject.layer} active={_overlayObject.activeInHierarchy} " +
                  $"enabled={_overlayCanvas.isActiveAndEnabled} " +
                  $"camera={(_overlayCanvas.worldCamera == null ? "none" : _overlayCanvas.worldCamera.name)} " +
                  $"rect={_overlayImage.rectTransform.rect.width:F0}x{_overlayImage.rectTransform.rect.height:F0} " +
                  $"alpha={_overlayImage.color.a:F2}";

            var target = ResolveDeathPanelCanvas();
            var panel = target == null
                ? "deathPanelCanvas=not found"
                : $"deathPanelCanvas={target.name} mode={target.renderMode} order={target.sortingOrder} " +
                  $"layer={target.gameObject.layer} " +
                  $"camera={(target.worldCamera == null ? "none" : target.worldCamera.name)}";

            Plugin.LogSource.LogInfo($"Headshot blackout started: {overlay}; {panel}.");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Headshot blackout: could not report its own state: {ex.Message}");
        }
    }

    [HideFromIl2Cpp]
    private bool ShouldRecover(float now)
    {
        if (now - _blackoutStartedAt >= MaximumBlackoutSeconds ||
            !Settings.HeadshotDeathBlackoutEnabled.Value)
        {
            return true;
        }

        // The death screen owns the frame from the moment it appears until the
        // game takes its hold-to-skip prompt back down. The next soldier is
        // assigned to the player while that screen is still up, so releasing on
        // "controls a living soldier" alone put the world back behind the panel,
        // which is the view the blackout exists to replace.
        if (SkipPromptGUI.active)
            return false;

        // Respawning, or taking control of another soldier, ends it immediately.
        var controlled = Soldier.CurrentControlledSoldierOrNull();
        return controlled != null && controlled.IsAlive;
    }

    [HideFromIl2Cpp]
    private void Apply(float blackness)
    {
        _blackness = Mathf.Clamp01(blackness);

        if (_overlayImage != null && _overlayObject != null)
        {
            SyncOverlaySorting();
            _overlayImage.color = new Color(0f, 0f, 0f, _blackness);
            if (!_overlayObject.activeSelf)
                _overlayObject.SetActive(true);
        }

        // Adopt a master-volume change made elsewhere while the blackout is up so
        // recovery restores what the player last chose, not a stale value.
        var current = AudioListener.volume;
        if (!Mathf.Approximately(current, _appliedAudioVolume))
            _nativeAudioVolume = Mathf.Clamp01(current);

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
            if (_overlayObject != null && _overlayObject.activeSelf)
                _overlayObject.SetActive(false);

            if (_ownsAudioVolume)
            {
                AudioListener.volume = _nativeAudioVolume;
                _appliedAudioVolume = _nativeAudioVolume;
                _ownsAudioVolume = false;
            }
        }
        catch
        {
            // Teardown races are not worth reporting; nothing of the game's is
            // left changed once the master volume above has been put back.
        }
    }

    [HideFromIl2Cpp]
    private void EnsureOverlay()
    {
        if (_overlayImage != null || _overlayFailed)
            return;

        try
        {
            _overlayObject = new GameObject("ER2 Headshot Blackout");
            UnityEngine.Object.DontDestroyOnLoad(_overlayObject);
            _overlayObject.SetActive(false);
            // A camera-rendered UI canvas only draws layers its camera renders, so
            // the default layer would be culled away without a word. The death
            // panel's own layer is adopted as soon as its canvas is resolved.
            _overlayObject.layer = DefaultUiLayer;

            _overlayCanvas = _overlayObject.AddComponent(Il2CppType.Of<Canvas>()).TryCast<Canvas>();
            if (_overlayCanvas == null)
                throw new InvalidOperationException("the blackout canvas component could not be created");

            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = FallbackSortingOrder;

            // The graphic goes on a child of the canvas, which is the arrangement
            // uGUI expects, and is stretched to the canvas rect.
            var imageObject = new GameObject("Black");
            imageObject.layer = _overlayObject.layer;
            var rectTransform = imageObject
                .AddComponent(Il2CppType.Of<RectTransform>())
                .TryCast<RectTransform>();
            if (rectTransform == null)
                throw new InvalidOperationException("the blackout rect transform could not be created");

            rectTransform.SetParent(_overlayObject.transform, false);
            imageObject.AddComponent(Il2CppType.Of<CanvasRenderer>());
            _overlayImage = imageObject.AddComponent(Il2CppType.Of<Image>()).TryCast<Image>();
            if (_overlayImage == null)
                throw new InvalidOperationException("the blackout image component could not be created");

            _overlayImage.color = new Color(0f, 0f, 0f, 0f);
            // The blackout must never swallow a click meant for the game.
            _overlayImage.raycastTarget = false;

            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.localScale = Vector3.one;
            rectTransform.anchoredPosition3D = Vector3.zero;
        }
        catch (Exception ex)
        {
            _overlayFailed = true;
            Plugin.LogSource.LogWarning(
                $"Headshot blackout: falling back to the immediate-mode overlay, which also covers the death panel ({ex.Message}).");
            DestroyOverlay();
        }
    }

    /// <summary>
    /// Keeps the black exactly one sorting step below the game's death panel, so
    /// the panel and its skip prompt stay readable over it. Re-read while the
    /// blackout is up because the panel's canvas is only reliably reachable once
    /// the death sequence has built it.
    /// </summary>
    [HideFromIl2Cpp]
    private void SyncOverlaySorting()
    {
        var canvas = _overlayCanvas;
        if (canvas == null)
            return;

        var target = ResolveDeathPanelCanvas();
        if (target == null)
            return;

        var mode = target.renderMode;
        if (mode == RenderMode.WorldSpace ||
            (mode == RenderMode.ScreenSpaceCamera && target.worldCamera == null))
        {
            mode = RenderMode.ScreenSpaceOverlay;
        }

        canvas.renderMode = mode;
        if (mode == RenderMode.ScreenSpaceCamera)
        {
            canvas.worldCamera = target.worldCamera;
            canvas.planeDistance = target.planeDistance;
        }

        canvas.sortingLayerID = target.sortingLayerID;
        canvas.sortingOrder = target.sortingOrder - 1;

        // Adopt the panel's layer as well, or a camera-rendered canvas culls the
        // blackout out of the frame with nothing to show for it.
        var layer = target.gameObject.layer;
        if (_overlayObject != null && _overlayObject.layer != layer)
        {
            _overlayObject.layer = layer;
            if (_overlayImage != null)
                _overlayImage.gameObject.layer = layer;
        }
    }

    [HideFromIl2Cpp]
    private Canvas? ResolveDeathPanelCanvas()
    {
        if (_deathPanelCanvas != null)
            return _deathPanelCanvas;

        var panel = DeathPanel.instance;
        if (panel == null)
            return null;

        // The panel is inactive until the player dies, so its canvas has to be
        // looked up including inactive parents.
        var canvas = panel.GetComponentInParent<Canvas>(true);
        if (canvas == null)
            return null;

        _deathPanelCanvas = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
        return _deathPanelCanvas;
    }

    [HideFromIl2Cpp]
    private void DestroyOverlay()
    {
        if (_overlayObject != null)
            UnityEngine.Object.Destroy(_overlayObject);

        _overlayObject = null;
        _overlayCanvas = null;
        _overlayImage = null;
        _deathPanelCanvas = null;
    }

    [HideFromIl2Cpp]
    private void ReportError(Exception exception)
    {
        var signature = exception.GetType().FullName + ": " + exception.Message;
        if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal))
            return;

        _lastErrorSignature = signature;
        Plugin.LogSource.LogWarning(
            $"Headshot death blackout failed and was cleared (further identical errors suppressed): {exception.Message}");
    }
}
