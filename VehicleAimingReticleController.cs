using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class VehicleAimingReticleController : MonoBehaviour
{
    private const int RingTextureSize = 64;
    private const float CameraRingDiameterAt1080P =
        AircraftCameraCore.CameraAimRingDiameterAt1080P;
    private const float GunRingDiameterAt1080P = 14f;
    private const float ProjectionDistance = 1000f;

    private Texture2D? _ringTexture;
    private Vector2 _lastHeldMarkerDirection = Vector2.right;
    private bool _loggedActivation;
    private bool _loggedFailure;

    private void Awake()
    {
        _ringTexture = CreateRingTexture();
    }

    private void OnGUI()
    {
        if (Event.current == null || Event.current.type != EventType.Repaint)
            return;

        try
        {
            var controller = PlayerController.currentController;
            if (_ringTexture == null ||
                controller == null ||
                !PlayerController.TPSEnabled)
            {
                return;
            }

            var camera = ResourcesManager.mainCamera;
            if (camera == null)
                return;

            Vector3 gunDirection;
            var useAircraftRingColor = false;
            var hasDetachedAimDirection = false;
            var detachedAimDirection = Vector3.zero;
            if (DirectTurretAiming.TryGetSelectedPlayerTurret(
                    controller,
                    out _,
                    out var turret))
            {
                if (controller.IsAiming ||
                    !Settings.GroundVehicleAimRingsEnabled.Value)
                    return;

                var fireTransform = turret.GetFirePosTransform();
                gunDirection = fireTransform != null
                    ? fireTransform.forward
                    : turret.GetFireDir();
                hasDetachedAimDirection =
                    PlayerVehicleFreeLook.TryGetHeldCameraAimDirection(
                        out detachedAimDirection);
            }
            else if (AircraftMousePointAiming.TryGetPlayerPlane(
                         controller,
                         out var plane))
            {
                useAircraftRingColor = true;

                // Use the selected pilot weapon's native zeroed crosshair
                // direction, with the aircraft nose retained as a safe
                // fallback for unusual weapon setups.
                if (!AircraftMousePointAiming.TryGetBoreDirection(
                        plane,
                        out gunDirection))
                {
                    return;
                }

                hasDetachedAimDirection =
                    AircraftMousePointAiming.TryGetAimDirection(
                        out detachedAimDirection);
            }
            else
            {
                return;
            }

            var scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.5f);
            var ringColor = useAircraftRingColor
                ? Color.white
                : new Color(0.64f, 0.66f, 0.68f, 0.58f);
            var cameraCenter = new Vector2(
                Screen.width * 0.5f,
                Screen.height * 0.5f);
            if (hasDetachedAimDirection)
            {
                cameraCenter = GetDirectionMarkerCenter(
                    camera,
                    detachedAimDirection,
                    cameraCenter,
                    CameraRingDiameterAt1080P * scale);
            }

            // Easy Red 2 draws parts of its HUD after ordinary plugin OnGUI calls.
            // A negative depth keeps the rings above the native third-person HUD.
            var previousDepth = GUI.depth;
            GUI.depth = -750;
            try
            {
                // The aircraft ring projects its live world-space command during
                // ordinary point aim and its held command during freelook. The
                // edge clamp keeps the full ring visible when that ray leaves
                // the current view.
                DrawRing(
                    cameraCenter,
                    CameraRingDiameterAt1080P * scale,
                    ringColor);

                if (gunDirection.sqrMagnitude > 0.000001f)
                {
                    // Project direction from the camera rather than the muzzle
                    // position so the marker represents angular bore alignment
                    // without false third-person parallax.
                    var gunScreenPoint = camera.WorldToScreenPoint(
                        camera.transform.position +
                        gunDirection.normalized * ProjectionDistance);
                    if (gunScreenPoint.z > 0f)
                    {
                        var gunCenter = new Vector2(
                            gunScreenPoint.x,
                            Screen.height - gunScreenPoint.y);
                        DrawRing(
                            gunCenter,
                            GunRingDiameterAt1080P * scale,
                            ringColor);
                    }
                }
            }
            finally
            {
                GUI.depth = previousDepth;
            }

            if (!_loggedActivation)
            {
                _loggedActivation = true;
                Plugin.LogSource.LogInfo(
                    "Third-person War Thunder-style ground-vehicle and aircraft aim rings are visible.");
            }
        }
        catch (Exception ex)
        {
            if (_loggedFailure)
                return;

            _loggedFailure = true;
            Plugin.LogSource.LogWarning(
                $"Vehicle aiming reticle disabled after a draw failure: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        if (_ringTexture != null && !RuntimeLifecycle.IsQuitting)
            Destroy(_ringTexture);

        _ringTexture = null;
    }

    [HideFromIl2Cpp]
    private Vector2 GetDirectionMarkerCenter(
        Camera camera,
        Vector3 direction,
        Vector2 screenCenter,
        float diameter)
    {
        var screenPoint = camera.WorldToScreenPoint(
            camera.transform.position +
            direction.normalized * ProjectionDistance);
        var markerCenter = new Vector2(
            screenPoint.x,
            Screen.height - screenPoint.y);
        var offset = markerCenter - screenCenter;

        // Unity mirrors points behind the camera. Reverse that projection and
        // place the marker on the nearest screen edge so it remains visible.
        var behindCamera = screenPoint.z <= 0f;
        if (behindCamera)
            offset = -offset;

        var inset =
            diameter * 0.5f +
            AircraftCameraCore.CameraAimRingEdgePaddingPixels;
        var halfWidth = Mathf.Max(1f, Screen.width * 0.5f - inset);
        var halfHeight = Mathf.Max(1f, Screen.height * 0.5f - inset);
        var insideScreen =
            !behindCamera &&
            Mathf.Abs(offset.x) <= halfWidth &&
            Mathf.Abs(offset.y) <= halfHeight;
        if (insideScreen)
        {
            if (offset.sqrMagnitude > 1f)
                _lastHeldMarkerDirection = offset.normalized;

            return markerCenter;
        }

        if (offset.sqrMagnitude <= 1f)
            offset = _lastHeldMarkerDirection;
        else
            _lastHeldMarkerDirection = offset.normalized;

        var horizontalScale =
            halfWidth / Mathf.Max(Mathf.Abs(offset.x), 0.0001f);
        var verticalScale =
            halfHeight / Mathf.Max(Mathf.Abs(offset.y), 0.0001f);
        return screenCenter +
               offset * Mathf.Min(horizontalScale, verticalScale);
    }

    [HideFromIl2Cpp]
    private void DrawRing(Vector2 center, float diameter, Color color)
    {
        if (_ringTexture == null)
            return;

        var previousColor = GUI.color;
        GUI.color = color;
        DrawCenteredTexture(center, diameter);
        GUI.color = previousColor;
    }

    [HideFromIl2Cpp]
    private void DrawCenteredTexture(Vector2 center, float diameter)
    {
        GUI.DrawTexture(
            new Rect(
                center.x - diameter * 0.5f,
                center.y - diameter * 0.5f,
                diameter,
                diameter),
            _ringTexture);
    }

    [HideFromIl2Cpp]
    private static Texture2D CreateRingTexture()
    {
        var texture = new Texture2D(
            RingTextureSize,
            RingTextureSize,
            TextureFormat.RGBA32,
            false)
        {
            name = "ER2 Realism Vehicle Aim Ring",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color[RingTextureSize * RingTextureSize];
        var center = (RingTextureSize - 1f) * 0.5f;
        var radius = RingTextureSize * 0.41f;
        const float halfThickness = 1.6f;
        const float edgeFeather = 0.8f;

        for (var y = 0; y < RingTextureSize; y++)
        {
            for (var x = 0; x < RingTextureSize; x++)
            {
                var distance = Mathf.Sqrt(
                    ((x - center) * (x - center)) +
                    ((y - center) * (y - center)));
                var edgeDistance = Mathf.Abs(distance - radius);
                var edgeBlend = Mathf.InverseLerp(
                    halfThickness - edgeFeather,
                    halfThickness + edgeFeather,
                    edgeDistance);
                var alpha = 1f - Mathf.SmoothStep(0f, 1f, edgeBlend);
                pixels[y * RingTextureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
