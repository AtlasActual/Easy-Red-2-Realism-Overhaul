using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class PlayerVehicleFreeLook
{
    private const float ReturnSpeedDegreesPerSecond = 360f;
    private const float ReturnCompleteAngleDegrees = 0.05f;

    private static bool _active;
    private static bool _returning;
    private static Quaternion _cameraRotationRelativeToVehicle = Quaternion.identity;
    private static Quaternion _returnCameraRotation = Quaternion.identity;
    private static Quaternion _resolvedCameraRotation = Quaternion.identity;
    private static Vector3 _heldAimDirection = Vector3.forward;
    private static int _lastCameraResolveFrame = -1;
    private static bool _loggedFailure;

    internal static bool IsCameraDetached => _active;

    internal static void UpdateState(PlayerController controller)
    {
        try
        {
            if (!TryGetGroundVehicleInThirdPerson(controller, out var vehicle))
            {
                _active = false;
                _returning = false;
                return;
            }

            var held = IsVehicleLookHeld();
            if (held)
            {
                if (!_active)
                {
                    if (!_returning)
                    {
                        var camera = ResourcesManager.mainCamera;
                        if (camera == null)
                            return;

                        _cameraRotationRelativeToVehicle =
                            Quaternion.Inverse(vehicle.transform.rotation) *
                            camera.transform.rotation;
                        _heldAimDirection = camera.transform.forward.normalized;
                    }

                    _active = true;
                    _returning = false;
                }

                return;
            }

            if (_active)
            {
                _active = false;
                _returning = true;
                var camera = ResourcesManager.mainCamera;
                if (camera != null)
                    _returnCameraRotation = camera.transform.rotation;
                _lastCameraResolveFrame = -1;
            }

            // Vehicle freelook remains usable when the optional direct-aiming
            // system is disabled. Its native-camera fallback returns to the
            // heading that was held when freelook began.
            var directTurretCameraActive =
                Settings.DirectTurretAimingEnabled.Value &&
                DirectTurretAiming.IsOperatingSeatTurret(controller);

            if (_returning && !directTurretCameraActive)
            {
                var camera = ResourcesManager.mainCamera;
                if (camera == null)
                    return;

                var targetRotation =
                    vehicle.transform.rotation * _cameraRotationRelativeToVehicle;
                var returnedRotation = Quaternion.RotateTowards(
                    camera.transform.rotation,
                    targetRotation,
                    ReturnSpeedDegreesPerSecond * Time.unscaledDeltaTime);
                camera.transform.rotation = returnedRotation;

                if (Quaternion.Angle(returnedRotation, targetRotation) <=
                    ReturnCompleteAngleDegrees)
                {
                    camera.transform.rotation = targetRotation;
                    _returning = false;
                }
            }
        }
        catch (Exception ex)
        {
            _active = false;
            _returning = false;
            ReportFailure(ex);
        }
    }

    internal static Quaternion ResolveCameraRotation(Quaternion lockedRotation)
    {
        if (!_returning)
            return lockedRotation;

        var targetRotation = _heldAimDirection.sqrMagnitude > 0.000001f
            ? Quaternion.LookRotation(_heldAimDirection.normalized, Vector3.up)
            : lockedRotation;
        var frame = Time.frameCount;
        if (_lastCameraResolveFrame == frame)
            return _resolvedCameraRotation;

        _returnCameraRotation = Quaternion.RotateTowards(
            _returnCameraRotation,
            targetRotation,
            ReturnSpeedDegreesPerSecond * Time.unscaledDeltaTime);

        if (Quaternion.Angle(_returnCameraRotation, targetRotation) <=
            ReturnCompleteAngleDegrees)
        {
            _returnCameraRotation = targetRotation;
            _returning = false;
        }

        _lastCameraResolveFrame = frame;
        _resolvedCameraRotation = _returnCameraRotation;
        return _resolvedCameraRotation;
    }

    internal static bool TryGetHeldCameraAimDirection(out Vector3 direction)
    {
        direction = _heldAimDirection;
        return (_active || _returning) &&
               direction.sqrMagnitude > 0.000001f;
    }

    internal static bool TryGetHeldAimDirection(
        Turret turret,
        out Vector3 direction)
    {
        direction = Vector3.zero;
        if (!_active && !_returning)
            return false;

        try
        {
            var controller = PlayerController.currentController;
            if (!TryGetGroundVehicleInThirdPerson(controller, out var vehicle) ||
                !DirectTurretAiming.IsOperatingSeatTurret(controller))
            {
                return false;
            }

            var selectedTurret = vehicle.GetTurret(controller.selectedVehicleTurret);
            if (selectedTurret == null ||
                turret == null ||
                selectedTurret.GetInstanceID() != turret.GetInstanceID() ||
                _heldAimDirection.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            direction = _heldAimDirection;
            return true;
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
            return false;
        }
    }

    private static bool TryGetGroundVehicleInThirdPerson(
        PlayerController? controller,
        out Vehicle vehicle)
    {
        vehicle = null!;
        if (controller == null || !PlayerController.TPSEnabled)
            return false;

        vehicle = controller.ControlledVehicle;
        return vehicle != null && !vehicle.IsAirVehicle();
    }

    private static bool IsVehicleLookHeld()
    {
        var gamepad = GamepadsAPI.GetGamepad(0);
        return gamepad != null &&
               gamepad.GetButton(
                   GameInput.LookAroundInVehicle,
                   StickPressCondition.StickCentered);
    }

    private static void ReportFailure(Exception exception)
    {
        if (_loggedFailure)
            return;

        _loggedFailure = true;
        Plugin.LogSource.LogWarning(
            $"Ground-vehicle freelook disabled after an input failure: {exception.Message}");
    }
}

[HarmonyPatch(typeof(PlayerController), "Update")]
internal static class PlayerVehicleFreeLookStatePatch
{
    [HarmonyPrefix]
    private static void Prefix(PlayerController __instance)
    {
        if (__instance == PlayerController.currentController)
            PlayerVehicleFreeLook.UpdateState(__instance);
    }
}

[HarmonyPatch(typeof(Turret), nameof(Turret.RotateToward))]
internal static class PlayerVehicleFreeLookTurretPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Turret __instance, ref Vector3 direction)
    {
        if (PlayerVehicleFreeLook.TryGetHeldAimDirection(
                __instance,
                out var heldDirection))
        {
            direction = heldDirection;
        }
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.GetAimingFOV))]
internal static class PlayerHoldBreathZoomPatch
{
    // PlayerController.LateUpdate applies this native multiplier after calling
    // Soldier.GetAimingFOV whenever the player is holding their breath.
    internal const float NativeHoldBreathFovMultiplier = 0.7f;

    [HarmonyPostfix]
    private static void Postfix(Soldier __instance, ref float __result)
    {
        if (__instance != null &&
            PlayerController.fpsCamera &&
            PlayerViewFeaturesController.BinocularsActive)
        {
            var binocularFov = PlayerViewFeaturesController.GetBinocularFov(__result);

            // The game applies its fixed hold-breath multiplier after this method.
            // Cancel it here so binoculars remain an exact optical 10x (by default)
            // even if the player is also holding the breath key.
            if (Soldier.CurrentPlayerIsHoldingBreath())
                binocularFov /= NativeHoldBreathFovMultiplier;

            __result = Mathf.Clamp(binocularFov, 1f, 179f);
            return;
        }

        var strength = Settings.HoldBreathZoomMultiplier.Value;
        if (__instance == null ||
            !PlayerController.fpsCamera ||
            !Soldier.CurrentPlayerIsHoldingBreath() ||
            Mathf.Approximately(strength, 1f))
            return;

        // Scale only the extra zoom beyond the regular aimed FOV. The game still
        // performs its own frame-by-frame FOV interpolation after this postfix.
        var adjustedMultiplier = 1f - ((1f - NativeHoldBreathFovMultiplier) * strength);
        var compensation = adjustedMultiplier / NativeHoldBreathFovMultiplier;
        __result = Mathf.Clamp(__result * compensation, 1f, 179f);
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.GetAimingFOV))]
internal static class PlayerThirdPersonAimZoomPatch
{
    [HarmonyPostfix]
    private static void Postfix(Soldier __instance, ref float __result)
    {
        if (__instance == null || !PlayerController.TPSEnabled)
            return;

        var controller = PlayerController.currentController;
        var controlledSoldier = Soldier.CurrentControlledSoldierOrNull();
        if (controller == null ||
            controller.ControlledVehicle != null ||
            controlledSoldier == null ||
            __instance.GetInstanceID() != controlledSoldier.GetInstanceID())
        {
            return;
        }

        var magnification = Settings.ThirdPersonZoom.Value;
        if (Mathf.Approximately(magnification, 1f))
            return;

        // PlayerController.Update calls Soldier.GetAimingFOV directly while
        // transitioning into an infantry aim. Treat the setting as optical
        // magnification so its full range always produces a visible result.
        __result = Mathf.Clamp(__result / magnification, 1f, 179f);
    }
}

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.GetAimingFOVVehicle))]
internal static class PlayerVehicleHoldBreathZoomPatch
{
    private readonly struct ScopeFovState
    {
        internal ScopeFovState(Turret turret, float originalFov)
        {
            Turret = turret;
            OriginalFov = originalFov;
        }

        internal Turret? Turret { get; }
        internal float OriginalFov { get; }
    }

    [HarmonyPrefix]
    private static void Prefix(PlayerController __instance, out ScopeFovState __state)
    {
        __state = default;
        if (__instance == null ||
            !__instance.IsAiming ||
            !PlayerAimingInput.IsHoldBreathHeld())
            return;

        var vehicle = __instance.ControlledVehicle;
        if (vehicle == null ||
            (!vehicle.IsStatic() && vehicle.GetComponent<VehicleTank>() == null))
        {
            return;
        }

        var turret = vehicle.GetTurret(__instance.selectedVehicleTurret);
        if (turret == null)
            return;

        var strength = Settings.OpticsZoom.Value;
        var adjustedMultiplier = 1f -
            ((1f - PlayerHoldBreathZoomPatch.NativeHoldBreathFovMultiplier) * strength);
        __state = new ScopeFovState(turret, turret.scopeFOV);

        // Let the native method interpolate from the current camera FOV to the
        // adjusted gunsight target. Scaling its already-interpolated result would
        // feed a new value back every frame and make tank optics shake.
        turret.scopeFOV = Mathf.Clamp(
            turret.scopeFOV * adjustedMultiplier,
            1f,
            179f);
    }

    [HarmonyPostfix]
    private static void Postfix(ScopeFovState __state)
    {
        if (__state.Turret != null)
            __state.Turret.scopeFOV = __state.OriginalFov;
    }
}
