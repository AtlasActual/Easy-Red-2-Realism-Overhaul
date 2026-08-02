using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ER2RealismOverhaul;

internal static class DirectTurretAiming
{
    private const float ZeroingAngleEpsilon = 0.0001f;
    private const float MousePixelsPerSecondForFullTraverse = 600f;
    private const float MouseDeltaEpsilon = 0.001f;

    private static Quaternion _lastGunsightRotation = Quaternion.identity;
    private static int _lastGunsightRotationFrame = -1;
    private static int _lastDirectTurretId = int.MinValue;
    private static int _lastDirectHandledFrame = -2;
    private static float _lastZeroingAngle;
    private static bool _lastDirectRotationComplete;
    private static bool _loggedFailure;

    internal static bool TryGetGunsightRotation(
        PlayerController controller,
        Quaternion nativeLookRotation,
        out Quaternion rotation)
    {
        rotation = Quaternion.identity;

        if (!TryGetSelectedPlayerTurret(controller, out _, out var turret))
            return false;

        try
        {
            if (PlayerVehicleFreeLook.IsCameraDetached)
                return false;

            Vector3 cameraForward;
            if (controller.IsAiming &&
                Settings.UnstabilizedGunsightEnabled.Value)
            {
                // The optical sight follows the physical gun. Stopping the input
                // therefore holds the gun relative to the vehicle instead of
                // maintaining an artificial center-screen world aim point.
                var firePosition = turret.GetFirePosTransform();
                cameraForward = firePosition != null
                    ? firePosition.forward
                    : turret.GetFireDir();
            }
            else
            {
                // Exterior view keeps War Thunder-style point aiming: the camera
                // moves independently while the physical weapon catches up.
                cameraForward = nativeLookRotation * Vector3.forward;
            }

            if (cameraForward.sqrMagnitude <= 0.000001f)
                return false;

            var leveledRotation = Quaternion.LookRotation(
                cameraForward.normalized,
                Vector3.up);
            rotation = PlayerVehicleFreeLook.ResolveCameraRotation(leveledRotation);
            _lastGunsightRotation = rotation;
            _lastGunsightRotationFrame = Time.frameCount;
            return true;
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
            return false;
        }
    }

    internal static bool TryGetLastGunsightRotation(
        PlayerController controller,
        out Quaternion rotation)
    {
        rotation = _lastGunsightRotation;
        return _lastGunsightRotationFrame == Time.frameCount &&
               TryGetSelectedPlayerTurret(controller, out _, out _);
    }

    internal static bool TryHandleDirectGunnerControl(
        Turret turret,
        float targetAngleAdjust,
        out bool rotationComplete)
    {
        rotationComplete = false;

        try
        {
            var controller = PlayerController.currentController;
            if (turret == null ||
                controller == null ||
                !TryGetSelectedPlayerTurret(controller, out _, out var selectedTurret) ||
                turret.GetInstanceID() != selectedTurret.GetInstanceID())
            {
                return false;
            }

            var directOpticalSight =
                Settings.UnstabilizedGunsightEnabled.Value &&
                controller.IsAiming &&
                !PlayerVehicleFreeLook.IsCameraDetached;
            var lockGunnerViewElevation =
                Settings.GunnerViewElevationLockEnabled.Value &&
                IsUnzoomedGunnerPeriscopeView(controller);
            if (!directOpticalSight && !lockGunnerViewElevation)
                return false;

            var turretId = turret.GetInstanceID();
            var frame = Time.frameCount;
            if (turretId == _lastDirectTurretId &&
                frame == _lastDirectHandledFrame)
            {
                rotationComplete = _lastDirectRotationComplete;
                return true;
            }

            PreserveAdjustableZeroing(
                turret,
                turretId,
                frame,
                targetAngleAdjust);

            if (!TryGetDirectInput(out var input))
                return false;

            rotationComplete = turret.ManualRotate(
                input.x,
                lockGunnerViewElevation ? 0f : input.y);
            _lastDirectTurretId = turretId;
            _lastDirectHandledFrame = frame;
            _lastDirectRotationComplete = rotationComplete;
            return true;
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
            return false;
        }
    }

    private static bool IsUnzoomedGunnerPeriscopeView(
        PlayerController controller)
    {
        if (controller.IsAiming ||
            PlayerController.TPSEnabled ||
            !PlayerController.fpsCamera)
        {
            return false;
        }

        var soldier = controller.GetControlledCharacter();
        var seat = soldier?.GetCurrentVehicleSeat();
        return seat != null && seat.HasPeriscope();
    }

    internal static bool TryGetThirdPersonAimDirection(
        Turret turret,
        out Vector3 direction)
    {
        direction = Vector3.zero;

        try
        {
            var controller = PlayerController.currentController;
            if (turret == null ||
                controller == null ||
                !PlayerController.TPSEnabled ||
                controller.IsAiming ||
                !TryGetSelectedPlayerTurret(controller, out _, out var selectedTurret) ||
                turret.GetInstanceID() != selectedTurret.GetInstanceID())
            {
                return false;
            }

            if (PlayerVehicleFreeLook.TryGetHeldAimDirection(
                    turret,
                    out direction))
            {
                return true;
            }

            var camera = ResourcesManager.mainCamera;
            if (camera == null)
                return false;

            // Treat the exterior camera and bore as parallel sight lines. Using a
            // nearby convergence point from the elevated camera makes the gun
            // visibly elevate even when the player is looking at the horizon.
            direction = camera.transform.forward;
            return direction.sqrMagnitude > 0.000001f;
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
            return false;
        }
    }

    internal static bool IsOperatingSeatTurret(PlayerController? controller)
    {
        var soldier = controller?.GetControlledCharacter();
        if (soldier == null || !soldier.IsUsingSeatTurret)
            return false;

        var seat = soldier.GetCurrentVehicleSeat();
        return seat?.connectedTurret?.TryCast<TurretGun>() != null;
    }

    internal static bool TryGetSelectedPlayerTurret(
        PlayerController? controller,
        out Vehicle vehicle,
        out TurretGun turret)
    {
        vehicle = null!;
        turret = null!;

        if (!Settings.DirectTurretAimingEnabled.Value ||
            controller == null ||
            controller != PlayerController.currentController)
        {
            return false;
        }

        vehicle = controller.ControlledVehicle;
        if (vehicle == null ||
            !vehicle.IsActive() ||
            vehicle.IsAirVehicle())
        {
            return false;
        }

        if (!IsOperatingSeatTurret(controller))
            return false;

        var selectedTurret = vehicle.GetTurret(controller.selectedVehicleTurret);
        turret = selectedTurret?.TryCast<TurretGun>()!;
        return turret != null &&
               !turret.IsUpIndirectFire() &&
               turret.GetLookTargetRotation() != null;
    }

    private static bool TryGetDirectInput(out Vector2 input)
    {
        input = Vector2.zero;

        var mouse = Mouse.current;
        if (mouse != null)
        {
            var mouseDelta = mouse.delta.ReadValue();
            if (mouseDelta.sqrMagnitude >
                MouseDeltaEpsilon * MouseDeltaEpsilon)
            {
                var deltaTime = Mathf.Max(
                    Time.unscaledDeltaTime,
                    1f / 240f);
                var scale =
                    1f /
                    (MousePixelsPerSecondForFullTraverse * deltaTime);
                var verticalDirection =
                    SavableData.Settings?.controls?.YAxisDirection_tank ?? 1;

                input = new Vector2(
                    Mathf.Clamp(mouseDelta.x * scale, -1f, 1f),
                    Mathf.Clamp(
                        -mouseDelta.y * scale * verticalDirection,
                        -1f,
                        1f));
                return true;
            }
        }

        if (GamepadsAPI.GetGamepad() != null)
        {
            input = PlayerController.GetCameraRotationInput_Tank();
            input.y = -input.y;
        }

        return true;
    }

    private static void PreserveAdjustableZeroing(
        Turret turret,
        int turretId,
        int frame,
        float targetAngleAdjust)
    {
        if (turretId != _lastDirectTurretId ||
            frame > _lastDirectHandledFrame + 1)
        {
            _lastZeroingAngle = targetAngleAdjust;
            return;
        }

        var zeroingDelta = targetAngleAdjust - _lastZeroingAngle;
        _lastZeroingAngle = targetAngleAdjust;
        if (Mathf.Abs(zeroingDelta) <= ZeroingAngleEpsilon)
            return;

        var targetRotation = turret.GetLookTargetRotation();
        if (targetRotation == null)
            return;

        turret.ForceTargetRotation(new LookParameters(
            targetRotation.leftRight,
            targetRotation.upDown - zeroingDelta));
    }

    private static void ReportFailure(Exception exception)
    {
        if (_loggedFailure)
            return;

        _loggedFailure = true;
        Plugin.LogSource.LogWarning(
            $"Ground-vehicle aiming fell back to native behavior: {exception.Message}");
    }
}

[HarmonyPatch(typeof(PlayerController), "GetCameraRotationsVehicle")]
internal static class DirectTurretGunsightCameraPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void Postfix(
        PlayerController __instance,
        ref Quaternion __result)
    {
        if (DirectTurretAiming.TryGetGunsightRotation(
                __instance,
                __result,
                out var rotation))
        {
            __result = rotation;
        }
    }
}

[HarmonyPatch(typeof(PlayerController), "LateUpdate")]
internal static class DirectTurretFinalCameraPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void Postfix(PlayerController __instance)
    {
        if (!DirectTurretAiming.TryGetLastGunsightRotation(
                __instance,
                out var rotation))
        {
            return;
        }

        var camera = ResourcesManager.mainCamera;
        if (camera != null)
            camera.transform.rotation = rotation;
    }
}

[HarmonyPatch(typeof(Turret), nameof(Turret.RotateToward))]
internal static class ThirdPersonTurretParallelAimPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(
        Turret __instance,
        ref Vector3 direction,
        float targetAngleAdjust,
        ref bool __result)
    {
        if (DirectTurretAiming.TryHandleDirectGunnerControl(
                __instance,
                targetAngleAdjust,
                out var rotationComplete))
        {
            __result = rotationComplete;
            return false;
        }

        if (DirectTurretAiming.TryGetThirdPersonAimDirection(
                __instance,
                out var cameraDirection))
        {
            direction = cameraDirection;
        }

        return true;
    }
}

[HarmonyPatch(typeof(TurretCommander), nameof(TurretCommander.RotateToward))]
internal static class CommanderTurretUnstabilizedGunsightPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(
        TurretCommander __instance,
        float targetAngleAdjust,
        ref bool __result)
    {
        if (!DirectTurretAiming.TryHandleDirectGunnerControl(
                __instance,
                targetAngleAdjust,
                out var rotationComplete))
        {
            return true;
        }

        __result = rotationComplete;
        return false;
    }
}
