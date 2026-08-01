using System;
using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Unity adapter for the deterministic aircraft camera and mouse instructor.
/// The adapter owns input/camera state, but it returns ordinary native virtual
/// surface inputs and never rotates or redirects the aircraft itself.
/// </summary>
internal static class AircraftMousePointAiming
{
    private const float MaximumCommandPitchRateDegreesPerSecond = 85f;
    private const float MaximumCommandYawRateDegreesPerSecond = 70f;
    private const float AimInputDeadZoneDegrees = 0.0001f;
    private const int AimInputActiveGraceFrames = 2;
    private const int AimVelocityGraceFrames = 1;
    private const float ManualRollDeadZone = 0.01f;
    private const float PointAimHorizonLevelSharpness = 10f;
    private const float MinimumReliableTravelSpeedSquared = 100f;

    private enum CameraOwnershipMode
    {
        Uninitialized,
        NativeFirstPerson,
        AwaitingThirdPersonSeed,
        ThirdPersonOwned
    }

    private static int _planeId = int.MinValue;
    private static int _lastCameraFrame = -1;
    private static int _lastResolvedFrame = -1;
    private static int _lastMouseCaptureFrame = -1;
    private static int _lastAimInputFrame = -1;
    private static int _lastFreeLookInputFrame = -1;
    private static int _lastGuidanceFrame = -1;

    private static float _manualRollInput;
    private static float _lastGuidedYaw;
    private static float _lastGuidedPitch;
    private static float _lastGuidedRoll;
    private static float _forwardFollowSharpness =
        AircraftCameraCore.DefaultForwardFollowSharpness;
    private static Vector2 _freeLookInput;
    private static Vector3 _lastReliableTravelDirection;
    private static System.Numerics.Vector3 _aimAngularVelocityWorld;
    private static Quaternion _resolvedCameraRotation = Quaternion.identity;
    private static AircraftMouseInstructorState _instructorState;
    private static AircraftCameraCoreState _cameraState;
    private static Rigidbody? _cameraInterpolatedRigidbody;
    private static RigidbodyInterpolation
        _originalCameraRigidbodyInterpolation;
    private static bool _cameraStateInitialized;
    private static CameraOwnershipMode _cameraOwnershipMode;

    private static bool _loggedActivation;
    private static bool _loggedMouseInput;
    private static bool _loggedFailure;

    /// <summary>
    /// Strict active-ownership gate. Input can consume camera state only after
    /// the third-person camera has explicitly acquired and seeded that state.
    /// Aircraft activity/stall flags do not revoke established ownership.
    /// </summary>
    internal static bool TryGetPlayerPlane(
        PlayerController? controller,
        out VehiclePlane plane)
    {
        plane = null!;
        if (_cameraOwnershipMode != CameraOwnershipMode.ThirdPersonOwned ||
            !_cameraStateInitialized ||
            !TryGetStablePlayerPlane(controller, out plane) ||
            plane.GetInstanceID() != _planeId ||
            !PlayerController.TPSEnabled ||
            IsNativeBombSightActive(controller))
        {
            return false;
        }

        return true;
    }

    internal static void ObserveNativeViewSwitch(
        PlayerController controller,
        bool thirdPersonBefore,
        bool thirdPersonAfter)
    {
        if (controller == null ||
            controller != PlayerController.currentController ||
            thirdPersonBefore == thirdPersonAfter)
        {
            return;
        }

        Reset(
            thirdPersonAfter
                ? CameraOwnershipMode.AwaitingThirdPersonSeed
                : CameraOwnershipMode.NativeFirstPerson);
    }

    internal static void ReleaseLostCameraOwnership(
        PlayerController controller)
    {
        if (_cameraOwnershipMode != CameraOwnershipMode.ThirdPersonOwned)
            return;

        var currentController = PlayerController.currentController;
        if (currentController == null)
        {
            Reset();
            return;
        }

        if (controller != currentController)
            return;

        try
        {
            if (!TryGetStablePlayerPlane(controller, out var plane) ||
                plane.GetInstanceID() != _planeId)
            {
                Reset();
            }
        }
        catch
        {
            // Dismount and destruction can invalidate the old vehicle during
            // LateUpdate. Reset still safely restores any live Rigidbody.
            Reset();
        }
    }

    internal static void CaptureNativeMouseInput(
        VehiclePlane plane,
        float yawInput,
        float pitchInput,
        float rollInput)
    {
        try
        {
            var controller = PlayerController.currentController;
            if (plane == null ||
                !TryGetPlayerPlane(controller, out var playerPlane) ||
                playerPlane.GetInstanceID() != plane.GetInstanceID())
            {
                return;
            }

            var frame = Time.frameCount;
            if (_lastMouseCaptureFrame == frame)
                return;
            _lastMouseCaptureFrame = frame;

            // RotateRealisticMouse exposes A/D through yawInput even though it
            // is the native roll channel. A material key input directly owns
            // roll for this frame; the native joystick path filters it once.
            _manualRollInput = Mathf.Clamp(yawInput, -1f, 1f);

            // Native positive pitch points down. Core camera pitch is positive
            // up; native add_roll is the horizontal mouse/yaw delta.
            var pitchDeltaDegrees = -pitchInput;
            var yawDeltaDegrees = rollInput;
            var hasAimInput =
                Mathf.Abs(pitchDeltaDegrees) > AimInputDeadZoneDegrees ||
                Mathf.Abs(yawDeltaDegrees) > AimInputDeadZoneDegrees;

            if (!_cameraState.Aim.IsHeld && hasAimInput)
            {
                var oldAimDirection = _cameraState.Aim.Direction;
                _cameraState = AircraftCameraCore.UpdatePointAim(
                    _cameraState,
                    pitchDeltaDegrees * Mathf.Deg2Rad,
                    yawDeltaDegrees * Mathf.Deg2Rad);
                ConstrainPointAimToPlane(plane);

                var deltaTime = Mathf.Max(
                    0.001f,
                    Time.unscaledDeltaTime);
                _aimAngularVelocityWorld =
                    AircraftCameraCore
                        .DirectionAngularVelocityDegreesPerSecond(
                            oldAimDirection,
                            _cameraState.Aim.Direction,
                            deltaTime,
                            Mathf.Sqrt(
                                MaximumCommandPitchRateDegreesPerSecond *
                                MaximumCommandPitchRateDegreesPerSecond +
                                MaximumCommandYawRateDegreesPerSecond *
                                MaximumCommandYawRateDegreesPerSecond));
                // Input activity is the player's physical mouse intent, not
                // only the part of that intent which survives the travel-cone
                // constraint. Slow movement and outward edge pressure must
                // keep capture disengaged even when their measured direction
                // rate is below the feed-forward threshold.
                _lastAimInputFrame = frame;
            }
            else if (_lastAimInputFrame < 0 ||
                     frame - _lastAimInputFrame >
                         AimInputActiveGraceFrames)
            {
                _aimAngularVelocityWorld =
                    System.Numerics.Vector3.Zero;
            }

            if (!_loggedMouseInput)
            {
                _loggedMouseInput = true;
                Plugin.LogSource.LogInfo(
                    "Aircraft point aiming is receiving native mouse flight input.");
            }
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    internal static void CaptureNativeVehicleLookInput(
        PlayerController controller,
        Vehicle? vehicle,
        Vector2 input)
    {
        try
        {
            if (!IsVehicleLookHeld() ||
                vehicle == null ||
                !TryGetPlayerPlane(controller, out var plane) ||
                vehicle.GetInstanceID() != plane.GetInstanceID())
            {
                return;
            }

            _freeLookInput = input;
            _lastFreeLookInputFrame = Time.frameCount;
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    internal static void SampleNativeFreeLookInput(
        PlayerController controller)
    {
        try
        {
            if (!IsVehicleLookHeld() ||
                !TryGetPlayerPlane(controller, out var plane))
            {
                return;
            }

            _freeLookInput = controller.GetRotationInput(plane);
            _lastFreeLookInputFrame = Time.frameCount;
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    /// <summary>
    /// Resolves the one rotation used by both native chase position and final
    /// camera orientation. Free look enters from the actual rendered pose
    /// supplied by the Harmony adapter.
    /// </summary>
    internal static bool TryResolveCameraRotation(
        PlayerController controller,
        Quaternion nativeRotation,
        bool canAcquireFromRenderedRotation,
        out Quaternion rotation)
    {
        rotation = nativeRotation;

        try
        {
            if (!PlayerController.TPSEnabled)
            {
                Reset(CameraOwnershipMode.NativeFirstPerson);
                return false;
            }

            // Bomb sights are a distinct native aircraft optic, not the
            // generic right-click aiming flag. Relinquish both control and
            // camera ownership only for that explicit view, then seed a fresh
            // third-person chase pose from the rendered camera on return.
            if (IsNativeBombSightActive(controller))
            {
                Reset(CameraOwnershipMode.AwaitingThirdPersonSeed);
                return false;
            }

            VehiclePlane plane;
            if (_cameraOwnershipMode == CameraOwnershipMode.ThirdPersonOwned &&
                _cameraStateInitialized)
            {
                // Once acquired, transient driver-alive or native
                // realistic-control false negatives must not hand one frame
                // back to the stock chase camera. Release only when the real
                // owner, vehicle, view mode, setting, or input device changes.
                if (!TryGetStablePlayerPlane(controller, out plane) ||
                    plane.GetInstanceID() != _planeId)
                {
                    Reset();
                    return false;
                }
            }
            else if (!TryGetEligiblePlayerPlane(controller, out plane))
            {
                Reset();
                return false;
            }

            var frame = Time.frameCount;
            var planeId = plane.GetInstanceID();
            if (_cameraOwnershipMode == CameraOwnershipMode.ThirdPersonOwned &&
                (!_cameraStateInitialized || planeId != _planeId))
            {
                Reset(CameraOwnershipMode.AwaitingThirdPersonSeed);
            }

            if (_cameraOwnershipMode !=
                    CameraOwnershipMode.ThirdPersonOwned ||
                !_cameraStateInitialized ||
                planeId != _planeId)
            {
                _cameraOwnershipMode =
                    CameraOwnershipMode.AwaitingThirdPersonSeed;
                if (!canAcquireFromRenderedRotation)
                    return false;

                InitializePlaneState(plane, nativeRotation);
            }

            if (_lastResolvedFrame == frame)
            {
                rotation = _resolvedCameraRotation;
                return true;
            }

            if (_lastCameraFrame != frame)
            {
                var deltaTime = Mathf.Max(
                    0.001f,
                    Time.unscaledDeltaTime);
                var lookHeld = IsVehicleLookHeld();
                if (lookHeld)
                {
                    var enteredFreeLook = false;
                    if (!_cameraState.FreeLook.IsActive)
                    {
                        // This seed is ResourcesManager.mainCamera's rendered
                        // pose in the prefix, not the aircraft/chase target.
                        _cameraState = AircraftCameraCore.EnterFreeLook(
                            _cameraState,
                            ToNumerics(nativeRotation));
                        enteredFreeLook = true;
                    }

                    var appliedPitchDegrees = 0f;
                    var appliedYawDegrees = 0f;
                    if (_lastFreeLookInputFrame == frame)
                    {
                        appliedPitchDegrees = -_freeLookInput.x;
                        appliedYawDegrees = _freeLookInput.y;
                    }

                    // Keep updating with zero input so a view inherited from
                    // a bank settles smoothly toward a level horizon while C
                    // remains held. Pitch is clamped in absolute world
                    // elevation, so entering free look while steep cannot
                    // cross a pole by inheriting another relative 85 degrees.
                    if (!enteredFreeLook)
                    {
                        _cameraState = AircraftCameraCore.UpdateFreeLook(
                            _cameraState,
                            appliedPitchDegrees * Mathf.Deg2Rad,
                            appliedYawDegrees * Mathf.Deg2Rad,
                            System.Numerics.Vector3.UnitY,
                            deltaTime);
                    }
                }
                else if (_cameraState.FreeLook.IsActive)
                {
                    _cameraState =
                        AircraftCameraCore.ReleaseFreeLook(_cameraState);
                }

                // The command may use the whole forward travel hemisphere.
                // Reapply after freelook because the aircraft may have turned
                // while its world-space aim direction was held.
                ConstrainPointAimToPlane(plane);
                var aimInputActive =
                    _lastAimInputFrame >= 0 &&
                    frame - _lastAimInputFrame <=
                        AimInputActiveGraceFrames &&
                    !_cameraState.Aim.IsHeld;
                _forwardFollowSharpness =
                    AircraftCameraCore.UpdateForwardFollowSharpness(
                        _forwardFollowSharpness,
                        aimInputActive,
                        deltaTime);
                _cameraState = AircraftCameraCore.UpdateHorizonChase(
                    _cameraState,
                    _cameraState.Aim.Direction,
                    System.Numerics.Vector3.UnitY,
                    deltaTime,
                    _forwardFollowSharpness,
                    PointAimHorizonLevelSharpness,
                    lockForwardToTarget: false,
                    maximumForwardRateRadiansPerSecond:
                        AircraftCameraCore
                            .DefaultMaximumForwardFollowRateRadiansPerSecond);
                _cameraState = AircraftCameraCore.UpdateReturn(
                    _cameraState,
                    deltaTime);
                _lastCameraFrame = frame;
            }

            rotation = ToUnity(
                AircraftCameraCore.GetRenderedRotation(_cameraState));
            _resolvedCameraRotation = rotation;
            _lastResolvedFrame = frame;

            if (!_loggedActivation)
            {
                _loggedActivation = true;
                Plugin.LogSource.LogInfo(
                    "Third-person aircraft mouse point aiming is active; physical controllers remain native.");
            }

            return true;
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
            if (_cameraOwnershipMode == CameraOwnershipMode.ThirdPersonOwned &&
                _cameraStateInitialized)
            {
                rotation = ToUnity(
                    AircraftCameraCore.GetRenderedRotation(_cameraState));
                return true;
            }

            return false;
        }
    }

    internal static bool TryGetAimDirection(out Vector3 direction)
    {
        direction =
            _cameraOwnershipMode == CameraOwnershipMode.ThirdPersonOwned &&
            _cameraStateInitialized
            ? ToUnity(_cameraState.Aim.Direction)
            : Vector3.zero;
        return _planeId != int.MinValue &&
               direction.sqrMagnitude > 0.000001f;
    }

    internal static bool TryGetDetachedAimDirection(out Vector3 direction)
    {
        direction =
            _cameraOwnershipMode == CameraOwnershipMode.ThirdPersonOwned &&
            _cameraStateInitialized
            ? ToUnity(_cameraState.Aim.Direction)
            : Vector3.zero;
        return _planeId != int.MinValue &&
               (_cameraState.FreeLook.IsActive ||
                _cameraState.Return.IsActive) &&
               direction.sqrMagnitude > 0.000001f;
    }

    internal static bool TryGetGuidanceInputs(
        VehiclePlane plane,
        out float yaw,
        out float pitch,
        out float roll)
    {
        yaw = 0f;
        pitch = 0f;
        roll = 0f;

        try
        {
            var controller = PlayerController.currentController;
            if (plane == null ||
                !TryGetPlayerPlane(controller, out var playerPlane) ||
                playerPlane.GetInstanceID() != plane.GetInstanceID())
            {
                return false;
            }

            var frame = Time.frameCount;
            ConstrainPointAimToPlane(plane);
            if (_lastGuidanceFrame == frame)
            {
                yaw = _lastGuidedYaw;
                pitch = _lastGuidedPitch;
                roll = _lastGuidedRoll;
                return true;
            }

            var rigidbody = plane.GetRigidbody();
            var localAngularDegrees = rigidbody != null
                ? plane.transform.InverseTransformDirection(
                      rigidbody.angularVelocity) *
                  Mathf.Rad2Deg
                : Vector3.zero;

            var sideslipDegrees = 0f;
            var angleOfAttackDegrees = 0f;
            var criticalAngleOfAttackDegrees = 16f;
            if (AircraftFlightPhysics.TryGetState(
                    plane,
                    out var flightState))
            {
                sideslipDegrees = flightState.SideslipAngle;
                angleOfAttackDegrees = flightState.AngleOfAttack;
                criticalAngleOfAttackDegrees =
                    flightState.Profile.PositiveCriticalAngle;
            }

            var aimInputActive =
                _lastAimInputFrame >= 0 &&
                frame - _lastAimInputFrame <=
                    AimInputActiveGraceFrames &&
                !_cameraState.Aim.IsHeld;
            var aimVelocityActive =
                _lastAimInputFrame >= 0 &&
                frame - _lastAimInputFrame <=
                    AimVelocityGraceFrames &&
                !_cameraState.Aim.IsHeld;
            var instructorLimits =
                plane.planeType == PlaneType.Bomber
                    ? AircraftMouseInstructorLimits.Default
                    : AircraftMouseInstructorLimits.Fighter;
            var instructorInput = new AircraftMouseInstructorInput(
                AircraftForward: ToNumerics(plane.transform.forward),
                AircraftUp: ToNumerics(plane.transform.up),
                AircraftRight: ToNumerics(plane.transform.right),
                WorldUp: System.Numerics.Vector3.UnitY,
                AimDirection: _cameraState.Aim.Direction,
                AimAngularVelocityWorldDegreesPerSecond:
                    aimVelocityActive
                        ? _aimAngularVelocityWorld
                        : System.Numerics.Vector3.Zero,
                AimInputActive: aimInputActive,
                // Unity local +X is nose-down and +Z is left bank.
                PitchRateDegreesPerSecond: -localAngularDegrees.x,
                RollRateDegreesPerSecond: -localAngularDegrees.z,
                YawRateDegreesPerSecond: localAngularDegrees.y,
                SideslipDegrees: sideslipDegrees,
                AngleOfAttackDegrees: angleOfAttackDegrees,
                CriticalAngleOfAttackDegrees:
                    criticalAngleOfAttackDegrees,
                DeltaTimeSeconds: Mathf.Max(0.001f, Time.deltaTime),
                Limits: instructorLimits);
            var output = AircraftMouseInstructorCore.Step(
                ref _instructorState,
                in instructorInput);

            // This is the only semantic/native sign boundary and the only
            // virtual-surface filter is VehiclePlane.RotateRealisticJoystick:
            // native pitch is positive nose-down; yaw and roll already match.
            yaw = output.Yaw;
            var manualRollActive =
                _lastMouseCaptureFrame == frame &&
                Mathf.Abs(_manualRollInput) > ManualRollDeadZone;
            // A/D is a deliberate axial-roll override in point-aim mode. It
            // must not retain instructor elevator and turn a pure roll into a
            // climbing barrel roll.
            pitch = manualRollActive
                ? 0f
                : -output.Pitch;
            roll = manualRollActive
                ? _manualRollInput
                : output.Roll;
            _lastGuidedYaw = yaw;
            _lastGuidedPitch = pitch;
            _lastGuidedRoll = roll;
            _lastGuidanceFrame = frame;
            return true;
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
            return false;
        }
    }

    internal static bool TryGetBoreDirection(
        VehiclePlane plane,
        out Vector3 direction)
    {
        direction = Vector3.zero;
        if (plane == null)
            return false;

        var fallback = plane.transform.forward;
        direction = fallback;
        try
        {
            // Native crosshair direction follows the currently selected pilot
            // weapon, its first muzzle, and the game's own ballistic zero.
            var pilotTurret = plane.GetPilotTurret();
            if (pilotTurret != null)
            {
                var candidate = pilotTurret.GetCrosshairDirection();
                if (float.IsFinite(candidate.x) &&
                    float.IsFinite(candidate.y) &&
                    float.IsFinite(candidate.z) &&
                    candidate.sqrMagnitude > 0.000001f)
                {
                    direction = candidate.normalized;
                }
            }
        }
        catch
        {
            // Aircraft without a usable pilot-turret path still receive the
            // stable native nose-axis marker.
        }

        return direction.sqrMagnitude > 0.000001f;
    }

    private static void ConstrainPointAimToPlane(VehiclePlane plane)
    {
        if (!_cameraStateInitialized || plane == null)
            return;

        var travelDirection =
            _lastReliableTravelDirection.sqrMagnitude > 0.5f
                ? _lastReliableTravelDirection
                : plane.transform.forward;
        try
        {
            var rigidbody = plane.GetRigidbody();
            if (rigidbody != null)
            {
                var velocity = rigidbody.velocity;
                if (float.IsFinite(velocity.x) &&
                    float.IsFinite(velocity.y) &&
                    float.IsFinite(velocity.z) &&
                    velocity.sqrMagnitude >=
                    MinimumReliableTravelSpeedSquared)
                {
                    travelDirection = velocity.normalized;
                    _lastReliableTravelDirection = travelDirection;
                }
            }
        }
        catch
        {
            // At rest or during a native vehicle transition, the aircraft nose
            // is the only stable forward-hemisphere reference.
        }

        _cameraState =
            AircraftCameraCore.ConstrainPointAimToAircraftCone(
                _cameraState,
                ToNumerics(travelDirection),
                ToNumerics(plane.transform.up),
                AircraftCameraCore.MaximumPointAimConeRadians);
    }

    private static void InitializePlaneState(
        VehiclePlane plane,
        Quaternion renderedRotation)
    {
        Reset(CameraOwnershipMode.AwaitingThirdPersonSeed);

        var planeId = plane.GetInstanceID();
        _planeId = planeId;
        _lastCameraFrame = -1;
        _lastResolvedFrame = -1;
        _lastMouseCaptureFrame = -1;
        _lastAimInputFrame = -1;
        _lastFreeLookInputFrame = -1;
        _lastGuidanceFrame = -1;
        _manualRollInput = 0f;
        _lastGuidedYaw = 0f;
        _lastGuidedPitch = 0f;
        _lastGuidedRoll = 0f;
        _forwardFollowSharpness =
            AircraftCameraCore.DefaultForwardFollowSharpness;
        _freeLookInput = Vector2.zero;
        _lastReliableTravelDirection = Vector3.zero;
        _aimAngularVelocityWorld =
            System.Numerics.Vector3.Zero;
        _instructorState = default;

        _resolvedCameraRotation = renderedRotation;
        _cameraState = AircraftCameraCore.Initialize(
            planeId,
            ToNumerics(renderedRotation),
            System.Numerics.Vector3.UnitY);
        EnableCameraInterpolation(plane);
        _cameraStateInitialized = true;
        _cameraOwnershipMode = CameraOwnershipMode.ThirdPersonOwned;
    }

    private static bool IsPhysicalControllerAssigned()
    {
        var gamepad = GamepadsAPI.GetGamepad(0);
        return gamepad != null && gamepad.IsGamepad;
    }

    private static bool IsVehicleLookHeld()
    {
        var gamepad = GamepadsAPI.GetGamepad(0);
        return gamepad != null &&
               gamepad.GetButton(
                   GameInput.LookAroundInVehicle,
                   StickPressCondition.StickCentered);
    }

    private static bool IsNativeBombSightActive(
        PlayerController? controller)
        => controller != null && controller.IsUsingBombSights;

    private static bool TryGetEligiblePlayerPlane(
        PlayerController? controller,
        out VehiclePlane plane)
    {
        if (!TryGetStablePlayerPlane(controller, out plane))
            return false;

        return plane.HasDriverAlive &&
               plane.PlayerIsDrivingWithRealisticControls();
    }

    private static bool TryGetStablePlayerPlane(
        PlayerController? controller,
        out VehiclePlane plane)
    {
        plane = null!;
        if (!Settings.AircraftMousePointAimingEnabled.Value ||
            controller == null ||
            controller != PlayerController.currentController ||
            IsPhysicalControllerAssigned() ||
            controller.ControlledVehicle is not VehiclePlane controlledPlane)
        {
            return false;
        }

        plane = controlledPlane;
        return true;
    }

    internal static void RecenterNativeChasePosition(
        Vehicle vehicle,
        Vector3 cameraForward,
        ref Vector3 cameraPosition)
    {
        try
        {
            if (!TryGetPlayerPlane(
                    PlayerController.currentController,
                    out var plane) ||
                vehicle.GetInstanceID() != plane.GetInstanceID())
            {
                return;
            }

            // This is TPSCamPos's exact aircraft-scaled orbit center. Using
            // the obstruction ray's lower 0.5 m origin here made the measured
            // radius breathe with aircraft attitude and placed the rear view
            // unnecessarily low. Native zoom and collision clipping remain
            // intact on the corrected centered ray.
            var pivot = ToUnity(
                AircraftCameraCore.GetNativeChaseOrbitCenter(
                    ToNumerics(plane.transform.position),
                    ToNumerics(plane.transform.up),
                    plane.tpsCamDist));
            cameraPosition = ToUnity(
                AircraftCameraCore.RecenterChasePosition(
                    ToNumerics(pivot),
                    ToNumerics(cameraPosition),
                    ToNumerics(cameraForward)));
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private static System.Numerics.Vector3 ToNumerics(Vector3 value)
        => new(value.x, value.y, value.z);

    private static System.Numerics.Quaternion ToNumerics(Quaternion value)
        => new(value.x, value.y, value.z, value.w);

    private static Vector3 ToUnity(System.Numerics.Vector3 value)
        => new(value.X, value.Y, value.Z);

    private static Quaternion ToUnity(System.Numerics.Quaternion value)
        => new(value.X, value.Y, value.Z, value.W);

    private static void EnableCameraInterpolation(VehiclePlane plane)
    {
        try
        {
            var rigidbody = plane.GetRigidbody();
            if (rigidbody == null)
                return;

            _cameraInterpolatedRigidbody = rigidbody;
            _originalCameraRigidbodyInterpolation =
                rigidbody.interpolation;
            if (rigidbody.interpolation !=
                RigidbodyInterpolation.Interpolate)
            {
                rigidbody.interpolation =
                    RigidbodyInterpolation.Interpolate;
            }
        }
        catch
        {
            _cameraInterpolatedRigidbody = null;
            _originalCameraRigidbodyInterpolation = default;
        }
    }

    private static void RestoreCameraInterpolation()
    {
        var rigidbody = _cameraInterpolatedRigidbody;
        var original = _originalCameraRigidbodyInterpolation;
        _cameraInterpolatedRigidbody = null;
        _originalCameraRigidbodyInterpolation = default;

        try
        {
            // Do not overwrite a later decision made by the game or another
            // system while this camera owned the aircraft.
            if (rigidbody != null &&
                rigidbody.interpolation ==
                    RigidbodyInterpolation.Interpolate)
            {
                rigidbody.interpolation = original;
            }
        }
        catch
        {
            // The Rigidbody can disappear during vehicle destruction or a
            // scene handoff; ownership cleanup must remain harmless.
        }
    }

    private static void Reset(
        CameraOwnershipMode nextMode =
            CameraOwnershipMode.Uninitialized)
    {
        RestoreCameraInterpolation();

        if (_cameraStateInitialized)
        {
            _cameraState =
                AircraftCameraCore.ReleaseOwnership(_cameraState);
        }

        _planeId = int.MinValue;
        _lastCameraFrame = -1;
        _lastResolvedFrame = -1;
        _lastMouseCaptureFrame = -1;
        _lastAimInputFrame = -1;
        _lastFreeLookInputFrame = -1;
        _lastGuidanceFrame = -1;
        _manualRollInput = 0f;
        _lastGuidedYaw = 0f;
        _lastGuidedPitch = 0f;
        _lastGuidedRoll = 0f;
        _forwardFollowSharpness =
            AircraftCameraCore.DefaultForwardFollowSharpness;
        _freeLookInput = Vector2.zero;
        _aimAngularVelocityWorld =
            System.Numerics.Vector3.Zero;
        _resolvedCameraRotation = Quaternion.identity;
        _instructorState = default;
        _cameraState = default;
        _cameraStateInitialized = false;
        _cameraOwnershipMode = nextMode;
    }

    private static void ReportFailure(Exception exception)
    {
        if (_loggedFailure)
            return;

        _loggedFailure = true;
        Plugin.LogSource.LogWarning(
            $"Aircraft mouse point aiming fell back to native controls: {exception.Message}");
    }
}

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.GetRotationInput))]
internal static class AircraftVehicleLookInputPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        PlayerController __instance,
        Vehicle __0,
        Vector2 __result)
    {
        AircraftMousePointAiming.CaptureNativeVehicleLookInput(
            __instance,
            __0,
            __result);
    }
}

[HarmonyPatch(typeof(PlayerController), "GetCameraRotationsVehicle")]
internal static class AircraftPointAimVehicleCameraPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        PlayerController __instance,
        ref Quaternion __result)
    {
        // Read ordinary free-look axes, then own the complete native chase
        // calculation so no behind-plane recenter state writes this frame.
        AircraftMousePointAiming.SampleNativeFreeLookInput(__instance);

        var camera = ResourcesManager.mainCamera;
        var seedRotation = camera != null
            ? camera.transform.rotation
            : Quaternion.identity;
        if (!AircraftMousePointAiming.TryResolveCameraRotation(
                __instance,
                seedRotation,
                camera != null,
                out var rotation))
        {
            return true;
        }

        __result = rotation;

        // The native chase-camera calculation normally consumes this one-shot
        // delta. We skip that calculation while point aim owns the camera, so
        // discard it here instead of replaying it on the ownership handoff.
        __instance.vehTpsCameraAngleDelta = 0f;
        return false;
    }
}

[HarmonyPatch(typeof(PlayerController), "LateUpdate")]
internal static class AircraftPointAimOwnershipCleanupPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerController __instance)
    {
        // Native LateUpdate no longer enters the vehicle-camera path after a
        // dismount, so clean up the former plane's temporary interpolation at
        // the one lifecycle point that still runs on the handoff frame.
        AircraftMousePointAiming.ReleaseLostCameraOwnership(__instance);
    }
}

[HarmonyPatch(typeof(Vehicle), nameof(Vehicle.TPSCamPos))]
internal static class AircraftPointAimVehicleCameraPositionPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        Vehicle __instance,
        Vector3 __0,
        ref Vector3 __result)
    {
        AircraftMousePointAiming.RecenterNativeChasePosition(
            __instance,
            __0,
            ref __result);
    }
}

[HarmonyPatch(typeof(PlayerController), "GetVehicleCameraShake")]
internal static class AircraftPointAimVehicleSpeedShakePatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref Vector3 __result)
    {
        // The stock vehicle-position update adds continuous speed-driven
        // Perlin vibration after applying our stable chase rotation. Suppress
        // only that positional layer while the point-aim camera owns the local
        // aircraft. Native ShakeCameraRoutine still runs afterward, so impacts,
        // explosions, and destruction retain their deliberate feedback.
        if (!AircraftMousePointAiming.TryGetPlayerPlane(
                PlayerController.currentController,
                out _))
        {
            return true;
        }

        __result = Vector3.zero;
        return false;
    }
}

[HarmonyPatch(typeof(PlayerController), "TrySwitchFpsTps")]
internal static class AircraftPointAimViewSwitchPatch
{
    [HarmonyPrefix]
    private static void Prefix(out bool __state)
    {
        __state = PlayerController.TPSEnabled;
    }

    [HarmonyPostfix]
    private static void Postfix(
        PlayerController __instance,
        bool __state)
    {
        AircraftMousePointAiming.ObserveNativeViewSwitch(
            __instance,
            __state,
            PlayerController.TPSEnabled);
    }
}

[HarmonyPatch(
    typeof(GenericGun),
    nameof(GenericGun.Fire),
    new[]
    {
        typeof(Creature),
        typeof(Il2CppSystem.Nullable<Vector3>)
    })]
internal static class AircraftPointAimSelfGunShakePatch
{
    private readonly record struct ShakeTimerState(
        bool IsArmed,
        float PreviousEndTime);

    [HarmonyPrefix]
    private static void Prefix(
        GenericGun __instance,
        Creature __0,
        out ShakeTimerState __state)
    {
        __state = default;

        try
        {
            var controller = PlayerController.currentController;
            var controlledCharacter = controller?.ControlledCharacter;
            if (__instance == null ||
                __0 == null ||
                controlledCharacter == null ||
                __0.GetInstanceID() != controlledCharacter.GetInstanceID() ||
                !AircraftMousePointAiming.TryGetPlayerPlane(
                    controller,
                    out _))
            {
                return;
            }

            // Native GenericGun.Fire extends the shared shake timer only for
            // player-fired armor-piercing rounds. Mirror that narrow source
            // check so explosions and destruction keep their ordinary shake.
            var ammo = __instance.GetAmmoItem();
            var bullet = ammo?.GetBulletData();
            if (bullet == null || !bullet.IsAPullet())
                return;

            __state = new ShakeTimerState(
                IsArmed: true,
                PreviousEndTime: PlayerController.camShakeEnd);
        }
        catch
        {
            // Metadata or ownership changes must never interfere with firing.
            __state = default;
        }
    }

    [HarmonyPostfix]
    private static void Postfix(
        bool __result,
        ShakeTimerState __state)
    {
        if (!__result || !__state.IsArmed)
            return;

        try
        {
            // Preserve shake that was already active before this local gun
            // fired; remove only this shot's timer extension.
            PlayerController.camShakeEnd = __state.PreviousEndTime;
        }
        catch
        {
            // Camera feedback is optional and must fail open.
        }
    }
}
