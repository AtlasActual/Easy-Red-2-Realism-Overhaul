using System;
using System.Numerics;

namespace ER2RealismOverhaul;

// Coordinate convention: +X is screen-right, +Y is up, and +Z is forward.
// Quaternions rotate those basis vectors into world space. Positive mouse yaw
// turns forward toward local +X; positive mouse pitch turns it toward local +Y.
// A Unity adapter can copy quaternion (x,y,z,w) and vector (x,y,z) components.
internal readonly record struct AircraftAimState(
    Quaternion Orientation,
    bool IsHeld)
{
    // This is the only value the flight controller should consume. Free-look
    // holds this orientation; camera return never changes or blocks it.
    internal Vector3 Direction => Vector3.Transform(Vector3.UnitZ, Orientation);
}

internal readonly record struct AircraftFreeLookState(
    bool IsActive,
    Quaternion Rotation);

internal readonly record struct AircraftCameraReturnState(
    bool IsActive,
    Quaternion Rotation);

internal readonly record struct AircraftCameraOwnershipState(
    bool IsOwned,
    int OwnerToken);

internal readonly record struct AircraftHorizonChaseState(
    Quaternion Rotation,
    Vector3 Forward,
    Vector3 Up,
    bool IsInsidePole,
    float LevelTurnSign);

internal readonly record struct AircraftCameraCoreState(
    AircraftAimState Aim,
    AircraftFreeLookState FreeLook,
    AircraftCameraReturnState Return,
    AircraftCameraOwnershipState Ownership,
    AircraftHorizonChaseState Chase);

/// <summary>
/// Deterministic aircraft aim and chase-camera state. This core owns no input,
/// timing, Unity objects, or flight guidance; the adapter supplies radians and
/// an explicit delta time once per simulation frame.
/// </summary>
internal static class AircraftCameraCore
{
    internal const float DefaultForwardFollowSharpness = 18f;
    // While the mouse is moving, keep a visible command lead instead of
    // letting the chase camera consume ordinary slow input inside the ring.
    // The camera still pursues on every frame; only its active-input gain is
    // lower. Once input stops, the gain eases back to the normal recenter rate.
    internal const float ActiveInputForwardFollowSharpness = 4f;
    internal const float ForwardFollowSharpnessRecovery = 8f;
    internal const float DefaultHorizonLevelSharpness = 5f;
    internal const float DefaultMaximumForwardFollowRateRadiansPerSecond =
        150f * (MathF.PI / 180f);
    internal const float DefaultMaximumHorizonLevelRateRadiansPerSecond =
        120f * (MathF.PI / 180f);
    internal const float DefaultFreeLookHorizonSharpness = 20f;
    internal const float DefaultMaximumFreeLookHorizonRateRadiansPerSecond =
        360f * (MathF.PI / 180f);
    internal const float DefaultMaximumFreeLookPitchRadians =
        85f * (MathF.PI / 180f);
    internal const float MaximumChaseIntegrationDeltaTime = 1f / 30f;
    internal const float DefaultReturnSharpness = 8f;
    internal const float DefaultMaximumReturnRateRadiansPerSecond =
        360f * (MathF.PI / 180f);
    internal const float DefaultReturnCompletionRadians =
        0.001f * (MathF.PI / 180f);
    internal const float MaximumPointAimConeRadians =
        89f * (MathF.PI / 180f);
    internal const float CameraAimRingDiameterAt1080P = 42f;
    internal const float CameraAimRingEdgePaddingPixels = 6f;

    // Hysteresis keeps the world-up projection from changing sign at zenith or
    // nadir. Inside this cone the last valid up is parallel-transported.
    private const float PoleEnterDot = 0.985f;
    private const float PoleExitDot = 0.960f;
    private const float FreeLookPoleDot = 0.999f;
    private const float AntipodalSineThreshold = 0.001f;
    private const float Epsilon = 1e-6f;

    internal static float UpdateForwardFollowSharpness(
        float currentSharpness,
        bool aimInputActive,
        float deltaTime)
    {
        if (aimInputActive)
            return ActiveInputForwardFollowSharpness;

        if (!float.IsFinite(currentSharpness) || currentSharpness <= 0f)
            currentSharpness = DefaultForwardFollowSharpness;
        if (!ValidDeltaTime(deltaTime))
            return currentSharpness;

        return currentSharpness +
               (DefaultForwardFollowSharpness - currentSharpness) *
               SmoothingFactor(
                   ForwardFollowSharpnessRecovery,
                   MathF.Min(deltaTime, MaximumChaseIntegrationDeltaTime));
    }

    internal static AircraftCameraCoreState Initialize(
        int ownerToken,
        Quaternion renderedRotation,
        Vector3 worldUp)
    {
        var rotation = NormalizeRotation(renderedRotation);
        var forward = NormalizeOr(
            Vector3.Transform(Vector3.UnitZ, rotation),
            Vector3.UnitZ);
        var up = PerpendicularUnit(
            Vector3.Transform(Vector3.UnitY, rotation),
            forward);
        var vertical = NormalizeOr(worldUp, Vector3.UnitY);
        var insidePole = MathF.Abs(Vector3.Dot(forward, vertical)) >= PoleEnterDot;

        return new AircraftCameraCoreState(
            new AircraftAimState(rotation, false),
            new AircraftFreeLookState(false, rotation),
            new AircraftCameraReturnState(false, rotation),
            new AircraftCameraOwnershipState(true, ownerToken),
            new AircraftHorizonChaseState(
                rotation,
                forward,
                up,
                insidePole,
                1f));
    }

    internal static AircraftCameraCoreState ReleaseOwnership(
        AircraftCameraCoreState state)
    {
        return state with
        {
            Aim = state.Aim with { IsHeld = false },
            FreeLook = state.FreeLook with { IsActive = false },
            Return = state.Return with { IsActive = false },
            Ownership = state.Ownership with { IsOwned = false }
        };
    }

    internal static AircraftCameraCoreState UpdatePointAim(
        AircraftCameraCoreState state,
        float pitchDeltaRadians,
        float yawDeltaRadians)
    {
        if (!state.Ownership.IsOwned || state.Aim.IsHeld ||
            !IsFinite(pitchDeltaRadians) || !IsFinite(yawDeltaRadians))
        {
            return state;
        }

        // Mouse deltas belong to the view the player is actually looking
        // through. The persistent aim orientation can retain old twist after
        // an inverted maneuver or horizon recovery, so using its local axes
        // would turn horizontal input into diagonal or vertical cursor motion.
        return state with
        {
            Aim = state.Aim with
            {
                Orientation = ApplyPitchYawAroundReference(
                    state.Aim.Orientation,
                    GetRenderedRotation(state),
                    pitchDeltaRadians,
                    yawDeltaRadians)
            }
        };
    }

    /// <summary>
    /// Keeps the point-aim ray inside the aircraft's forward travel
    /// hemisphere. The adapter supplies the flight-path direction, so the
    /// command can use the whole forward 178-degree field without ever
    /// crossing behind the moving aircraft.
    /// </summary>
    internal static AircraftCameraCoreState ConstrainPointAimToAircraftCone(
        AircraftCameraCoreState state,
        Vector3 aircraftForward,
        Vector3 aircraftUp,
        float maximumAngleRadians)
    {
        if (!state.Ownership.IsOwned || !IsFinite(maximumAngleRadians))
        {
            return state;
        }

        var orientation = NormalizeRotation(state.Aim.Orientation);
        var aimDirection = NormalizeOr(
            state.Aim.Direction,
            Vector3.UnitZ);
        var forward = NormalizeOr(aircraftForward, aimDirection);
        var limit = Math.Clamp(
            maximumAngleRadians,
            0f,
            MaximumPointAimConeRadians);
        var dot = Math.Clamp(
            Vector3.Dot(forward, aimDirection),
            -1f,
            1f);
        var angle = MathF.Acos(dot);
        if (angle <= limit + Epsilon)
            return state;

        var tangent =
            aimDirection - forward * dot;
        if (!IsFinite(tangent) ||
            tangent.LengthSquared() <= Epsilon * Epsilon)
        {
            var aimUp = Vector3.Transform(Vector3.UnitY, orientation);
            tangent = PerpendicularUnit(
                IsFinite(aimUp) ? aimUp : aircraftUp,
                forward);
        }
        else
        {
            tangent = Vector3.Normalize(tangent);
        }

        var constrainedDirection = NormalizeOr(
            forward * MathF.Cos(limit) +
            tangent * MathF.Sin(limit),
            forward);
        var rotationDelta = FromToRotation(
            aimDirection,
            constrainedDirection,
            Vector3.Transform(Vector3.UnitY, orientation));
        return state with
        {
            Aim = state.Aim with
            {
                // Apply the shortest direction correction to the complete
                // orientation so existing camera twist remains continuous.
                Orientation = NormalizeRotation(
                    Quaternion.Multiply(rotationDelta, orientation))
            }
        };
    }

    /// <summary>
    /// Measures only the direction change that survived any aim constraint.
    /// A saturated outward mouse input therefore produces zero feed-forward.
    /// </summary>
    internal static Vector3 DirectionAngularVelocityDegreesPerSecond(
        Vector3 previousDirection,
        Vector3 currentDirection,
        float deltaTime,
        float maximumRateDegreesPerSecond)
    {
        if (!ValidDeltaTime(deltaTime))
            return Vector3.Zero;

        var previous = NormalizeOr(previousDirection, Vector3.UnitZ);
        var current = NormalizeOr(currentDirection, previous);
        var cross = Vector3.Cross(previous, current);
        var sine = cross.Length();
        var cosine = Math.Clamp(Vector3.Dot(previous, current), -1f, 1f);
        if (!IsFinite(sine) || sine <= Epsilon)
            return Vector3.Zero;

        var rate =
            MathF.Atan2(sine, cosine) *
            (180f / MathF.PI) /
            deltaTime;
        if (IsFinite(maximumRateDegreesPerSecond) &&
            maximumRateDegreesPerSecond > 0f)
        {
            rate = MathF.Min(rate, maximumRateDegreesPerSecond);
        }

        return cross / sine * rate;
    }

    /// <summary>
    /// Returns the angular room available from the current chase direction to
    /// the inset edge of a perspective camera in the aim ray's screen
    /// direction. Horizontal, vertical, and diagonal commands therefore meet
    /// the actual visible frame instead of a fixed radial cone.
    /// </summary>
    internal static float VisibleForwardWorkspaceRadians(
        AircraftHorizonChaseState chase,
        Vector3 desiredForward,
        float verticalFieldOfViewDegrees,
        float aspect,
        float horizontalScreenFraction = 1f,
        float verticalScreenFraction = 1f)
    {
        var rotation = NormalizeRotation(chase.Rotation);
        var cameraForward = NormalizeOr(
            Vector3.Transform(Vector3.UnitZ, rotation),
            Vector3.UnitZ);
        var cameraRight = NormalizeOr(
            Vector3.Transform(Vector3.UnitX, rotation),
            Vector3.UnitX);
        var cameraUp = NormalizeOr(
            Vector3.Transform(Vector3.UnitY, rotation),
            Vector3.UnitY);
        var direction = NormalizeOr(desiredForward, cameraForward);
        var screenX = Vector3.Dot(direction, cameraRight);
        var screenY = Vector3.Dot(direction, cameraUp);
        var screenLength = MathF.Sqrt(
            screenX * screenX + screenY * screenY);

        var sanitizedVerticalFieldOfView = IsFinite(verticalFieldOfViewDegrees)
            ? Math.Clamp(verticalFieldOfViewDegrees, 2f, 178f)
            : 60f;
        var sanitizedAspect = IsFinite(aspect) && aspect > Epsilon
            ? aspect
            : 16f / 9f;
        var sanitizedHorizontalScreenFraction =
            IsFinite(horizontalScreenFraction)
                ? Math.Clamp(horizontalScreenFraction, 0.10f, 1f)
                : 1f;
        var sanitizedVerticalScreenFraction =
            IsFinite(verticalScreenFraction)
                ? Math.Clamp(verticalScreenFraction, 0.10f, 1f)
                : 1f;
        var verticalHalfAngle =
            sanitizedVerticalFieldOfView * 0.5f * (MathF.PI / 180f);
        var verticalTangent =
            MathF.Tan(verticalHalfAngle) *
            sanitizedVerticalScreenFraction;
        var horizontalTangent =
            MathF.Tan(verticalHalfAngle) *
            sanitizedAspect *
            sanitizedHorizontalScreenFraction;

        if (!IsFinite(screenLength) || screenLength <= Epsilon)
        {
            return MathF.Atan(MathF.Min(
                horizontalTangent,
                verticalTangent));
        }

        var directionX = MathF.Abs(screenX) / screenLength;
        var directionY = MathF.Abs(screenY) / screenLength;
        var horizontalBoundary = directionX > Epsilon
            ? horizontalTangent / directionX
            : float.PositiveInfinity;
        var verticalBoundary = directionY > Epsilon
            ? verticalTangent / directionY
            : float.PositiveInfinity;
        return MathF.Atan(MathF.Min(
            horizontalBoundary,
            verticalBoundary));
    }

    /// <summary>
    /// Returns the usable half-screen fractions inside the rendered command
    /// ring's rectangular clamp. Keeping this geometry in the deterministic
    /// core prevents the camera workspace and visible marker from drifting
    /// apart at different resolutions.
    /// </summary>
    internal static Vector2 CameraAimWorkspaceScreenFractions(
        float screenWidthPixels,
        float screenHeightPixels)
    {
        var width =
            IsFinite(screenWidthPixels) && screenWidthPixels > 0f
                ? screenWidthPixels
                : 1f;
        var height =
            IsFinite(screenHeightPixels) && screenHeightPixels > 0f
                ? screenHeightPixels
                : 1f;
        var scale = Math.Clamp(height / 1080f, 0.75f, 1.5f);
        var inset =
            CameraAimRingDiameterAt1080P * scale * 0.5f +
            CameraAimRingEdgePaddingPixels;
        var halfWidth = MathF.Max(1f, width * 0.5f);
        var halfHeight = MathF.Max(1f, height * 0.5f);
        return new Vector2(
            Math.Clamp((halfWidth - inset) / halfWidth, 0.10f, 1f),
            Math.Clamp((halfHeight - inset) / halfHeight, 0.10f, 1f));
    }

    internal static AircraftCameraCoreState EnterFreeLook(
        AircraftCameraCoreState state,
        Quaternion renderedRotation)
    {
        if (!state.Ownership.IsOwned)
            return state;

        // Start at the pose actually rendered last frame, not at the aircraft
        // attitude or an internal chase target.
        var rotation = NormalizeRotation(renderedRotation);
        return state with
        {
            Aim = state.Aim with { IsHeld = true },
            FreeLook = new AircraftFreeLookState(true, rotation),
            Return = new AircraftCameraReturnState(false, rotation)
        };
    }

    internal static AircraftCameraCoreState UpdateFreeLook(
        AircraftCameraCoreState state,
        float pitchDeltaRadians,
        float yawDeltaRadians,
        Vector3 worldUp,
        float deltaTime,
        float horizonLevelSharpness = DefaultFreeLookHorizonSharpness,
        float maximumHorizonRateRadiansPerSecond =
            DefaultMaximumFreeLookHorizonRateRadiansPerSecond,
        float maximumAbsolutePitchRadians =
            DefaultMaximumFreeLookPitchRadians)
    {
        if (!state.Ownership.IsOwned || !state.FreeLook.IsActive ||
            !IsFinite(pitchDeltaRadians) || !IsFinite(yawDeltaRadians) ||
            !IsFinite(worldUp) || !ValidDeltaTime(deltaTime))
        {
            return state;
        }

        var rotation = NormalizeRotation(state.FreeLook.Rotation);
        var oldForward = NormalizeOr(
            Vector3.Transform(Vector3.UnitZ, rotation),
            Vector3.UnitZ);
        var oldUp = PerpendicularUnit(
            Vector3.Transform(Vector3.UnitY, rotation),
            oldForward);
        var vertical = NormalizeOr(worldUp, Vector3.UnitY);

        // Native vehicle free look is an absolute horizon-relative yaw/pitch
        // view. Yawing around camera-local up inherited aircraft roll and then
        // compounded that tilt every time the view was moved while pitched.
        var yawRotation = Quaternion.CreateFromAxisAngle(
            vertical,
            yawDeltaRadians);
        var forward = NormalizeOr(
            Vector3.Transform(oldForward, yawRotation),
            oldForward);
        var up = PerpendicularUnit(
            Vector3.Transform(oldUp, yawRotation),
            forward);
        var screenRight = NormalizeOr(
            Vector3.Cross(vertical, forward),
            Vector3.Transform(Vector3.UnitX, rotation));

        // The entry frame deliberately preserves the exact rendered pose.
        // Every later held frame reconciles that actual pose into the absolute
        // elevation envelope before applying further input. This matters when
        // C is pressed while the chase camera is already near +/-90 degrees.
        var maximumPitch = IsFinite(maximumAbsolutePitchRadians)
            ? Math.Clamp(
                MathF.Abs(maximumAbsolutePitchRadians),
                0f,
                MathF.PI * 0.5f - Epsilon)
            : DefaultMaximumFreeLookPitchRadians;
        var currentElevation = MathF.Asin(Math.Clamp(
            Vector3.Dot(forward, vertical),
            -1f,
            1f));
        var targetElevation = Math.Clamp(
            currentElevation + pitchDeltaRadians,
            -maximumPitch,
            maximumPitch);
        var appliedPitchRadians = targetElevation - currentElevation;
        var pitchRotation = Quaternion.CreateFromAxisAngle(
            screenRight,
            -appliedPitchRadians);
        forward = NormalizeOr(
            Vector3.Transform(forward, pitchRotation),
            forward);
        up = PerpendicularUnit(
            Vector3.Transform(up, pitchRotation),
            forward);

        // Carry screen-up through the same world-yaw and screen-pitch
        // rotations as the look ray, then remove inherited roll at a bounded
        // rate. A level view therefore stays level while moving, while the
        // first C frame still remains the exact rendered pose. At an exact
        // pole there is no unique horizon, so the carried up direction is
        // retained until the view moves away again.
        if (MathF.Abs(Vector3.Dot(forward, vertical)) < FreeLookPoleDot)
        {
            var targetUp = PerpendicularUnit(vertical, forward);
            var levelAngle = SignedAngleAround(
                up,
                targetUp,
                forward,
                1f,
                out _);
            var integrationDeltaTime = MathF.Min(
                deltaTime,
                MaximumChaseIntegrationDeltaTime);
            var requestedLevelAngle = levelAngle * SmoothingFactor(
                horizonLevelSharpness,
                integrationDeltaTime);
            var maximumLevelAngle = MaximumFrameAngle(
                maximumHorizonRateRadiansPerSecond,
                integrationDeltaTime);
            var appliedLevelAngle = Math.Clamp(
                requestedLevelAngle,
                -maximumLevelAngle,
                maximumLevelAngle);
            up = PerpendicularUnit(
                Vector3.Transform(
                    up,
                    Quaternion.CreateFromAxisAngle(
                        forward,
                        appliedLevelAngle)),
                forward);
        }

        return state with
        {
            FreeLook = state.FreeLook with
            {
                Rotation = LookRotation(forward, up)
            }
        };
    }

    internal static AircraftCameraCoreState ReleaseFreeLook(
        AircraftCameraCoreState state)
    {
        if (!state.Ownership.IsOwned || !state.FreeLook.IsActive)
            return state;

        var releasedRotation = state.FreeLook.Rotation;
        return state with
        {
            Aim = state.Aim with { IsHeld = false },
            FreeLook = state.FreeLook with { IsActive = false },
            Return = new AircraftCameraReturnState(true, releasedRotation)
        };
    }

    internal static AircraftCameraCoreState UpdateHorizonChase(
        AircraftCameraCoreState state,
        Vector3 desiredForward,
        Vector3 worldUp,
        float deltaTime,
        float forwardFollowSharpness = DefaultForwardFollowSharpness,
        float horizonLevelSharpness = DefaultHorizonLevelSharpness,
        bool lockForwardToTarget = false,
        float maximumForwardRateRadiansPerSecond =
            DefaultMaximumForwardFollowRateRadiansPerSecond,
        float maximumHorizonRateRadiansPerSecond =
            DefaultMaximumHorizonLevelRateRadiansPerSecond)
    {
        if (!state.Ownership.IsOwned || !ValidDeltaTime(deltaTime))
            return state;

        var oldForward = NormalizeOr(state.Chase.Forward, Vector3.UnitZ);
        var oldUp = PerpendicularUnit(state.Chase.Up, oldForward);
        var targetForward = NormalizeOr(desiredForward, oldForward);
        // A render hitch must not turn a rate limit into a visible one-frame
        // jump while the aim is far from center. Catch up over later frames;
        // ordinary 30+ FPS follow and explicit forward locks remain unchanged.
        var integrationDeltaTime = MathF.Min(
            deltaTime,
            MaximumChaseIntegrationDeltaTime);
        var levelTurnSign = state.Chase.LevelTurnSign == 0f
            ? 1f
            : MathF.Sign(state.Chase.LevelTurnSign);
        // Ordinary point aim uses a finite follow so the command circle can
        // move across the view while the camera catches up. Callers may still
        // request an exact lock for modes whose marker must remain centered.
        Vector3 forward;
        if (lockForwardToTarget)
        {
            forward = targetForward;
        }
        else
        {
            var follow = SmoothingFactor(
                forwardFollowSharpness,
                integrationDeltaTime);
            var targetAngle = DirectionAngle(oldForward, targetForward);
            // The camera always pursues the command ray. A finite follow rate
            // still lets a fast mouse flick move the circle toward an edge,
            // but physical input never opens a dead zone that suspends chase.
            var requestedAngle = targetAngle * follow;
            var maximumAngle = MaximumFrameAngle(
                maximumForwardRateRadiansPerSecond,
                integrationDeltaTime);
            var appliedAngle = MathF.Min(requestedAngle, maximumAngle);
            var partialDelta = FromToRotationLimited(
                oldForward,
                targetForward,
                oldUp,
                levelTurnSign,
                appliedAngle);
            forward = NormalizeOr(
                Vector3.Transform(oldForward, partialDelta),
                targetForward);
        }

        // Minimal rotation transports the cached up through vertical without
        // consulting the sign-ambiguous world-up projection.
        var transport = FromToRotation(oldForward, forward, oldUp);
        var up = PerpendicularUnit(Vector3.Transform(oldUp, transport), forward);
        var vertical = NormalizeOr(worldUp, Vector3.UnitY);
        var verticalDot = MathF.Abs(Vector3.Dot(forward, vertical));
        var insidePole = state.Chase.IsInsidePole
            ? verticalDot >= PoleExitDot
            : verticalDot >= PoleEnterDot;
        if (!insidePole)
        {
            var levelUp = PerpendicularUnit(vertical, forward);
            var signedAngle = SignedAngleAround(
                up,
                levelUp,
                forward,
                levelTurnSign,
                out var measuredSign);
            if (measuredSign != 0f)
                levelTurnSign = measuredSign;

            var level = SmoothingFactor(
                horizonLevelSharpness,
                integrationDeltaTime);
            var requestedLevelAngle = signedAngle * level;
            var maximumLevelAngle = MaximumFrameAngle(
                maximumHorizonRateRadiansPerSecond,
                integrationDeltaTime);
            var appliedLevelAngle = Math.Clamp(
                requestedLevelAngle,
                -maximumLevelAngle,
                maximumLevelAngle);
            up = PerpendicularUnit(
                Vector3.Transform(
                    up,
                    Quaternion.CreateFromAxisAngle(forward, appliedLevelAngle)),
                forward);
        }

        var rotation = LookRotation(forward, up);
        return state with
        {
            Chase = new AircraftHorizonChaseState(
                rotation,
                forward,
                up,
                insidePole,
                levelTurnSign)
        };
    }

    internal static AircraftCameraCoreState UpdateReturn(
        AircraftCameraCoreState state,
        float deltaTime,
        float returnSharpness = DefaultReturnSharpness,
        float completionAngleRadians = DefaultReturnCompletionRadians,
        float maximumReturnRateRadiansPerSecond =
            DefaultMaximumReturnRateRadiansPerSecond)
    {
        if (!state.Ownership.IsOwned || !state.Return.IsActive ||
            !ValidDeltaTime(deltaTime))
        {
            return state;
        }

        var current = NormalizeRotation(state.Return.Rotation);
        var target = SameHemisphere(current, NormalizeRotation(state.Chase.Rotation));
        var completionAngle = MathF.Max(0f, completionAngleRadians);
        var completionChord =
            2f * MathF.Sin(completionAngle * 0.25f);
        var quaternionDelta = current - target;
        // Complete only below a sub-pixel tolerance. This prevents the visible
        // final jump that a quarter-degree completion threshold produced.
        if (completionAngle > 0f &&
            quaternionDelta.LengthSquared() <=
            completionChord * completionChord)
        {
            return state with
            {
                Return = new AircraftCameraReturnState(false, target)
            };
        }

        // Use the same hitch-safe integration pattern as chase pursuit. The
        // higher return ceiling keeps C release prompt without allowing a
        // 100-200 ms render hitch to become a one-frame camera snap.
        var integrationDeltaTime = MathF.Min(
            deltaTime,
            MaximumChaseIntegrationDeltaTime);
        var targetAngle = QuaternionAngle(current, target);
        var requestedAngle = targetAngle * SmoothingFactor(
            returnSharpness,
            integrationDeltaTime);
        var maximumAngle = MaximumFrameAngle(
            maximumReturnRateRadiansPerSecond,
            integrationDeltaTime);
        var appliedAngle = MathF.Min(requestedAngle, maximumAngle);
        var blend = targetAngle > Epsilon
            ? appliedAngle / targetAngle
            : 1f;
        var rotation = NormalizeRotation(Quaternion.Slerp(current, target, blend));
        return state with
        {
            Return = state.Return with { Rotation = rotation }
        };
    }

    internal static Quaternion GetRenderedRotation(
        AircraftCameraCoreState state)
    {
        if (state.FreeLook.IsActive)
            return state.FreeLook.Rotation;
        if (state.Return.IsActive)
            return state.Return.Rotation;
        return state.Chase.Rotation;
    }

    /// <summary>
    /// Reproduces Vehicle.TPSCamPos's aircraft-scaled orbit center. Keeping
    /// this separate from the lower obstruction-ray origin prevents apparent
    /// zoom changes as the aircraft pitches and rolls.
    /// </summary>
    internal static Vector3 GetNativeChaseOrbitCenter(
        Vector3 aircraftPosition,
        Vector3 aircraftUp,
        float cameraHeight)
    {
        if (!IsFinite(aircraftPosition) ||
            !IsFinite(aircraftUp) ||
            !IsFinite(cameraHeight))
        {
            return aircraftPosition;
        }

        return aircraftPosition +
               NormalizeOr(aircraftUp, Vector3.UnitY) * cameraHeight;
    }

    /// <summary>
    /// Keeps the native chase distance while placing the camera on the same
    /// forward ray as its rendered rotation. The aircraft pivot therefore
    /// remains at the center of the view at every attitude.
    /// </summary>
    internal static Vector3 RecenterChasePosition(
        Vector3 aircraftPivot,
        Vector3 nativeCameraPosition,
        Vector3 cameraForward)
    {
        if (!IsFinite(aircraftPivot) ||
            !IsFinite(nativeCameraPosition) ||
            !IsFinite(cameraForward) ||
            cameraForward.LengthSquared() <= Epsilon * Epsilon)
        {
            return nativeCameraPosition;
        }

        var distance = Vector3.Distance(
            aircraftPivot,
            nativeCameraPosition);
        if (!IsFinite(distance))
            return nativeCameraPosition;

        return aircraftPivot - Vector3.Normalize(cameraForward) * distance;
    }

    private static Quaternion ApplyPitchYawAroundReference(
        Quaternion orientation,
        Quaternion reference,
        float pitchRadians,
        float yawRadians)
    {
        var rotation = NormalizeRotation(orientation);
        var screen = NormalizeRotation(reference);
        var screenUp = NormalizeOr(
            Vector3.Transform(Vector3.UnitY, screen),
            Vector3.UnitY);
        var screenRight = NormalizeOr(
            Vector3.Transform(Vector3.UnitX, screen),
            Vector3.UnitX);

        rotation = NormalizeRotation(
            Quaternion.Multiply(
                Quaternion.CreateFromAxisAngle(screenUp, yawRadians),
                rotation));
        return NormalizeRotation(
            Quaternion.Multiply(
                Quaternion.CreateFromAxisAngle(screenRight, -pitchRadians),
                rotation));
    }

    private static Quaternion LookRotation(Vector3 forward, Vector3 up)
    {
        forward = NormalizeOr(forward, Vector3.UnitZ);
        up = PerpendicularUnit(up, forward);
        var swing = FromToRotation(Vector3.UnitZ, forward, Vector3.UnitY);
        var baseUp = PerpendicularUnit(
            Vector3.Transform(Vector3.UnitY, swing),
            forward);
        var twistAngle = SignedAngleAround(
            baseUp,
            up,
            forward,
            1f,
            out _);
        var twist = Quaternion.CreateFromAxisAngle(forward, twistAngle);
        return NormalizeRotation(Quaternion.Multiply(twist, swing));
    }

    private static Quaternion FromToRotation(
        Vector3 from,
        Vector3 to,
        Vector3 oppositeAxisHint)
    {
        from = NormalizeOr(from, Vector3.UnitZ);
        to = NormalizeOr(to, from);
        var dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        var cross = Vector3.Cross(from, to);
        // A dot-only parallel test creates an approximately 0.08-degree
        // dead zone in single precision. At the point-aim boundary that lets
        // small overshoots accumulate and then correct as visible steps.
        if (dot >= 0f &&
            cross.LengthSquared() <= Epsilon * Epsilon)
        {
            return Quaternion.Identity;
        }

        if (dot <= -1f + Epsilon)
        {
            var axis = PerpendicularUnit(oppositeAxisHint, from);
            return Quaternion.CreateFromAxisAngle(axis, MathF.PI);
        }

        return NormalizeRotation(new Quaternion(cross, 1f + dot));
    }

    private static Quaternion FromToRotationLimited(
        Vector3 from,
        Vector3 to,
        Vector3 oppositeAxisHint,
        float oppositeSign,
        float maximumAngle)
    {
        from = NormalizeOr(from, Vector3.UnitZ);
        to = NormalizeOr(to, from);
        var dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        var angle = MathF.Min(MathF.Acos(dot), MathF.Max(0f, maximumAngle));
        if (angle <= Epsilon)
            return Quaternion.Identity;

        var cross = Vector3.Cross(from, to);
        Vector3 axis;
        if (dot < 0f &&
            cross.LengthSquared() <=
            AntipodalSineThreshold * AntipodalSineThreshold)
        {
            // Tiny changes around a rearward target otherwise alternate
            // between equally short left/right arcs. Retain the previously
            // established turn sign until the target leaves that branch.
            axis = PerpendicularUnit(oppositeAxisHint, from) *
                   (oppositeSign < 0f ? -1f : 1f);
        }
        else
        {
            axis = NormalizeOr(cross, oppositeAxisHint);
        }

        return Quaternion.CreateFromAxisAngle(axis, angle);
    }

    private static float SignedAngleAround(
        Vector3 from,
        Vector3 to,
        Vector3 axis,
        float oppositeSign,
        out float measuredSign)
    {
        from = PerpendicularUnit(from, axis);
        to = PerpendicularUnit(to, axis);
        var sine = Vector3.Dot(axis, Vector3.Cross(from, to));
        var cosine = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        if (cosine >= 0f || MathF.Abs(sine) > AntipodalSineThreshold)
        {
            measuredSign = MathF.Sign(sine);
            return MathF.Atan2(sine, cosine);
        }

        measuredSign = 0f;
        return cosine < 0f
            ? MathF.CopySign(MathF.PI, oppositeSign == 0f ? 1f : oppositeSign)
            : 0f;
    }

    private static float DirectionAngle(Vector3 from, Vector3 to)
    {
        from = NormalizeOr(from, Vector3.UnitZ);
        to = NormalizeOr(to, from);
        return MathF.Acos(Math.Clamp(Vector3.Dot(from, to), -1f, 1f));
    }

    private static float MaximumFrameAngle(
        float rateRadiansPerSecond,
        float deltaTime)
    {
        if (!IsFinite(rateRadiansPerSecond) || rateRadiansPerSecond <= 0f)
            return MathF.PI;
        return MathF.Min(MathF.PI, rateRadiansPerSecond * deltaTime);
    }

    private static Vector3 PerpendicularUnit(Vector3 value, Vector3 normal)
    {
        normal = NormalizeOr(normal, Vector3.UnitZ);
        var projected = value - normal * Vector3.Dot(value, normal);
        if (IsFinite(projected) && projected.LengthSquared() > Epsilon * Epsilon)
            return Vector3.Normalize(projected);

        var fallback = MathF.Abs(normal.Y) < 0.9f
            ? Vector3.UnitY
            : Vector3.UnitX;
        projected = fallback - normal * Vector3.Dot(fallback, normal);
        return Vector3.Normalize(projected);
    }

    private static Vector3 NormalizeOr(Vector3 value, Vector3 fallback)
    {
        if (IsFinite(value) && value.LengthSquared() > Epsilon * Epsilon)
            return Vector3.Normalize(value);
        if (IsFinite(fallback) && fallback.LengthSquared() > Epsilon * Epsilon)
            return Vector3.Normalize(fallback);
        return Vector3.UnitZ;
    }

    private static Quaternion NormalizeRotation(Quaternion rotation)
    {
        if (!IsFinite(rotation) || rotation.LengthSquared() <= Epsilon * Epsilon)
            return Quaternion.Identity;
        return Quaternion.Normalize(rotation);
    }

    private static Quaternion SameHemisphere(
        Quaternion reference,
        Quaternion rotation)
    {
        return Quaternion.Dot(reference, rotation) < 0f
            ? new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W)
            : rotation;
    }

    private static float QuaternionAngle(Quaternion a, Quaternion b)
    {
        var dot = Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0f, 1f);
        return 2f * MathF.Acos(dot);
    }

    private static float SmoothingFactor(float sharpness, float deltaTime)
    {
        if (!IsFinite(sharpness) || sharpness <= 0f)
            return 0f;
        return 1f - MathF.Exp(-sharpness * deltaTime);
    }

    private static bool ValidDeltaTime(float deltaTime) =>
        IsFinite(deltaTime) && deltaTime > 0f;

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsFinite(Vector3 value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        IsFinite(value.X) && IsFinite(value.Y) &&
        IsFinite(value.Z) && IsFinite(value.W);
}
