using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.Rendering;

namespace ER2RealismOverhaul;

internal sealed class FirstPersonPlayerShadowController : MonoBehaviour
{
    private const float RendererRefreshInterval = 0.20f;

    private readonly Dictionary<int, RendererState> _suppressedRenderers = new();
    private int _suppressedSoldierId;
    private SuppressionScope _scope;
    private float _nextRendererRefreshAt;
    private string _lastErrorSignature = string.Empty;

    private enum SuppressionScope
    {
        None,
        ViewModelOnly,
        WholePlayer
    }

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
            var player = Soldier.CurrentControlledSoldierOrNull();
            var playerId = player != null ? player.GetInstanceID() : 0;
            var inFirstPerson = PlayerController.fpsCamera;
            var desiredScope = !inFirstPerson || player == null
                ? SuppressionScope.None
                : playerShadowEnabled
                    ? SuppressionScope.ViewModelOnly
                    : SuppressionScope.WholePlayer;

            if (desiredScope == SuppressionScope.None)
            {
                RestoreModesStillSuppressed();
                return;
            }

            if (_suppressedSoldierId != playerId || _scope != desiredScope)
            {
                RestoreNativeModes();
                _suppressedSoldierId = playerId;
                _scope = desiredScope;
                _nextRendererRefreshAt = 0f;
            }

            ReassertSuppressedModes();

            if (Time.unscaledTime >= _nextRendererRefreshAt)
            {
                _nextRendererRefreshAt = Time.unscaledTime + RendererRefreshInterval;
                var root = ResolveSuppressionRoot(player!, desiredScope);
                if (root != null)
                    SuppressNewRenderers(root);
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
    private static GameObject? ResolveSuppressionRoot(Soldier player, SuppressionScope scope)
    {
        if (scope == SuppressionScope.WholePlayer)
            return player.gameObject;

        // The FPS gun manager owns the separate first-person arms, uniform, and
        // weapon models. Suppressing only this subtree leaves the soldier's
        // ordinary third-person body available to cast the visible body shadow.
        var manager = player.GetFPSGunManager();
        return manager != null ? manager.gameObject : null;
    }

    [HideFromIl2Cpp]
    private void SuppressNewRenderers(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
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
        _scope = SuppressionScope.None;
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
