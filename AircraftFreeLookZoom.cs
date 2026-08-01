using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Applies an isolated optical zoom after the native aircraft camera has
/// finished for the frame. The native FOV is restored before the following
/// LateUpdate so the zoomed result is never fed back into Easy Red 2's own FOV
/// interpolation.
/// </summary>
internal static class AircraftFreeLookZoom
{
    private const float ZoomSharpness = 12f;
    private const float CompleteFovDifference = 0.01f;

    private static bool _ownsFov;
    private static int _cameraId = int.MinValue;
    private static int _aircraftId = int.MinValue;
    private static float _nativeFov;
    private static float _renderedFov;
    private static bool _loggedFailure;

    internal static void RestoreNativeFovBeforeCameraUpdate(
        PlayerController controller)
    {
        if (!_ownsFov)
            return;

        try
        {
            var camera = ResourcesManager.mainCamera;
            if (controller != PlayerController.currentController ||
                camera == null ||
                camera.GetInstanceID() != _cameraId)
            {
                Reset();
                return;
            }

            camera.fieldOfView = _nativeFov;
        }
        catch (System.Exception exception)
        {
            Reset();
            ReportFailure(exception);
        }
    }

    internal static void ApplyAfterCameraUpdate(PlayerController controller)
    {
        try
        {
            var camera = ResourcesManager.mainCamera;
            if (!TryGetAircraftView(
                    controller,
                    out var aircraft) ||
                camera == null)
            {
                Reset();
                return;
            }

            var cameraId = camera.GetInstanceID();
            var aircraftId = aircraft.GetInstanceID();
            if (_ownsFov &&
                (cameraId != _cameraId || aircraftId != _aircraftId))
            {
                Reset();
            }

            var zoomRequested =
                IsVehicleLookHeld() &&
                PlayerAimingInput.IsHoldBreathHeld();
            var magnification = Mathf.Clamp(
                Settings.AircraftFreeLookZoom.Value,
                0.5f,
                10f);

            if (!_ownsFov)
            {
                if (!zoomRequested ||
                    Mathf.Approximately(magnification, 1f))
                {
                    return;
                }

                _ownsFov = true;
                _cameraId = cameraId;
                _aircraftId = aircraftId;
                _nativeFov = Mathf.Clamp(camera.fieldOfView, 1f, 179f);
                _renderedFov = _nativeFov;
            }
            else
            {
                // The prefix restored the unmodified value before the native
                // camera ran, so this is Easy Red 2's fresh FOV for the frame.
                _nativeFov = Mathf.Clamp(camera.fieldOfView, 1f, 179f);
            }

            var targetFov = zoomRequested
                ? Mathf.Clamp(_nativeFov / magnification, 1f, 179f)
                : _nativeFov;
            var deltaTime = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
            var interpolation =
                1f - Mathf.Exp(-ZoomSharpness * deltaTime);
            _renderedFov = Mathf.Lerp(
                _renderedFov,
                targetFov,
                interpolation);

            if (!zoomRequested &&
                Mathf.Abs(_renderedFov - targetFov) <=
                    CompleteFovDifference)
            {
                camera.fieldOfView = _nativeFov;
                Reset();
                return;
            }

            camera.fieldOfView = Mathf.Clamp(_renderedFov, 1f, 179f);
        }
        catch (System.Exception exception)
        {
            Reset();
            ReportFailure(exception);
        }
    }

    private static bool TryGetAircraftView(
        PlayerController? controller,
        out VehiclePlane aircraft)
    {
        aircraft = null!;
        if (controller == null ||
            controller != PlayerController.currentController ||
            controller.IsUsingBombSights ||
            controller.ControlledVehicle is not VehiclePlane plane)
        {
            return false;
        }

        aircraft = plane;
        return true;
    }

    private static bool IsVehicleLookHeld()
    {
        var gamepad = GamepadsAPI.GetGamepad(0);
        return gamepad != null &&
               gamepad.GetButton(
                   GameInput.LookAroundInVehicle,
                   StickPressCondition.StickCentered);
    }

    private static void Reset()
    {
        _ownsFov = false;
        _cameraId = int.MinValue;
        _aircraftId = int.MinValue;
        _nativeFov = 0f;
        _renderedFov = 0f;
    }

    private static void ReportFailure(System.Exception exception)
    {
        if (_loggedFailure)
            return;

        _loggedFailure = true;
        Plugin.LogSource.LogWarning(
            $"Aircraft freelook zoom disabled after a camera/input failure: {exception.Message}");
    }
}

[HarmonyPatch(typeof(PlayerController), "LateUpdate")]
internal static class AircraftFreeLookZoomPatch
{
    [HarmonyPrefix]
    private static void Prefix(PlayerController __instance)
    {
        AircraftFreeLookZoom.RestoreNativeFovBeforeCameraUpdate(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerController __instance)
    {
        AircraftFreeLookZoom.ApplyAfterCameraUpdate(__instance);
    }
}
