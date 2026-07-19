using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.Rendering;

namespace ER2RealismOverhaul;

internal sealed class FirstPersonPlayerShadowController : MonoBehaviour
{
    private const float RendererRefreshInterval = 0.20f;

    private readonly Dictionary<int, RendererState> _suppressedRenderers = new();
    private int _suppressedSoldierId;
    private float _nextRendererRefreshAt;
    private string _lastErrorSignature = string.Empty;

    private sealed class RendererState
    {
        internal Renderer Renderer = null!;
        internal ShadowCastingMode NativeMode;
        internal bool NativeEnabled;
        internal bool DisabledForSuppression;
    }

    private void LateUpdate()
    {
        try
        {
            var playerShadowEnabled = Settings.FirstPersonPlayerShadowEnabled.Value;
            if (playerShadowEnabled && _suppressedRenderers.Count == 0)
                return;

            var player = Soldier.CurrentControlledSoldierOrNull();
            var playerId = player != null ? player.GetInstanceID() : 0;
            var inFirstPerson = PlayerController.fpsCamera;
            var suppressShadow = !playerShadowEnabled &&
                                 inFirstPerson && player != null;

            if (!suppressShadow)
            {
                // When the option is re-enabled during first person, put every
                // renderer back exactly as the game configured it. Camera and
                // controlled-soldier transitions own their own visibility changes,
                // so do not overwrite the modes they have just selected.
                if (playerShadowEnabled &&
                    inFirstPerson && playerId == _suppressedSoldierId)
                {
                    RestoreNativeModes();
                }
                else
                {
                    RestoreModesStillSuppressed();
                }

                return;
            }

            if (_suppressedSoldierId != playerId)
            {
                RestoreModesStillSuppressed();
                _suppressedSoldierId = playerId;
                _nextRendererRefreshAt = 0f;
            }

            // Reassert every frame because the game may change a renderer's
            // casting mode during stance, equipment, or LOD transitions. Any
            // such native change becomes the mode restored when the setting is
            // switched back on.
            ReassertSuppressedModes();

            if (Time.unscaledTime >= _nextRendererRefreshAt)
            {
                _nextRendererRefreshAt = Time.unscaledTime + RendererRefreshInterval;
                SuppressNewRenderers(player!);
            }
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void OnDestroy()
    {
        RestoreNativeModes();
    }

    [HideFromIl2Cpp]
    private void SuppressNewRenderers(Soldier player)
    {
        var renderers = player.gameObject.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;

            var id = renderer.GetInstanceID();
            if (!_suppressedRenderers.TryGetValue(id, out var state) || state.Renderer == null)
            {
                state = new RendererState
                {
                    Renderer = renderer,
                    NativeMode = renderer.shadowCastingMode,
                    NativeEnabled = renderer.enabled
                };
                _suppressedRenderers[id] = state;
            }

            ApplySuppression(state);
        }
    }

    [HideFromIl2Cpp]
    private void ReassertSuppressedModes()
    {
        foreach (var state in _suppressedRenderers.Values)
            ApplySuppression(state);
    }

    [HideFromIl2Cpp]
    private static void ApplySuppression(RendererState state)
    {
        var renderer = state.Renderer;
        if (renderer == null)
            return;

        var currentMode = renderer.shadowCastingMode;

        if (state.DisabledForSuppression)
        {
            if (currentMode == ShadowCastingMode.ShadowsOnly)
            {
                // The game may re-enable this renderer during a stance or LOD
                // transition. Remember that native state, then suppress it again.
                if (renderer.enabled)
                    state.NativeEnabled = true;

                renderer.enabled = false;
                return;
            }

            // The game changed how this renderer should be displayed. Release
            // the enabled flag we owned, then suppress its new casting mode.
            renderer.enabled = state.NativeEnabled;
            state.DisabledForSuppression = false;
        }

        if (currentMode == ShadowCastingMode.ShadowsOnly)
        {
            // Changing ShadowsOnly to Off makes the mesh visible. In first
            // person that exposes the inside of the player's head to the camera,
            // so disable shadow-only renderers instead.
            state.NativeMode = currentMode;
            state.NativeEnabled = renderer.enabled;
            state.DisabledForSuppression = renderer.enabled;
            renderer.enabled = false;
            return;
        }

        if (currentMode != ShadowCastingMode.Off)
        {
            state.NativeMode = currentMode;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    [HideFromIl2Cpp]
    private void RestoreNativeModes()
    {
        foreach (var state in _suppressedRenderers.Values)
        {
            try
            {
                if (state.Renderer != null)
                {
                    state.Renderer.shadowCastingMode = state.NativeMode;
                    if (state.DisabledForSuppression)
                        state.Renderer.enabled = state.NativeEnabled;
                }
            }
            catch
            {
                // Renderers can disappear during respawn and scene teardown.
            }
        }

        ForgetSuppressedRenderers();
    }

    [HideFromIl2Cpp]
    private void RestoreModesStillSuppressed()
    {
        foreach (var state in _suppressedRenderers.Values)
        {
            try
            {
                var renderer = state.Renderer;
                if (renderer == null)
                    continue;

                if (state.DisabledForSuppression && !renderer.enabled)
                {
                    // Third-person setup can change ShadowsOnly to a visible
                    // casting mode before this LateUpdate runs. We still own the
                    // disabled flag and must release it regardless of that mode.
                    renderer.enabled = state.NativeEnabled;
                }
                else if (!state.DisabledForSuppression &&
                         renderer.shadowCastingMode == ShadowCastingMode.Off &&
                         state.NativeMode != ShadowCastingMode.Off)
                {
                    renderer.shadowCastingMode = state.NativeMode;
                }
            }
            catch
            {
                // Renderers can disappear during control and scene transitions.
            }
        }

        ForgetSuppressedRenderers();
    }

    [HideFromIl2Cpp]
    private void ForgetSuppressedRenderers()
    {
        _suppressedRenderers.Clear();
        _suppressedSoldierId = 0;
        _nextRendererRefreshAt = 0f;
    }

    [HideFromIl2Cpp]
    private void ReportError(Exception exception)
    {
        var signature = exception.GetType().FullName + ": " + exception.Message;
        if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal))
            return;

        _lastErrorSignature = signature;
        Plugin.LogSource.LogWarning(
            $"First-person player-shadow control failed (further identical errors suppressed): {exception.Message}");
    }
}
