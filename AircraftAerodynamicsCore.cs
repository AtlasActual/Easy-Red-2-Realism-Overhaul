using System.Numerics;

namespace ER2RealismOverhaul;

/// <summary>
/// Pilot-facing surface commands. Positive pitch means pull/nose-up, positive roll
/// means right wing down, and positive yaw means nose-right.
/// </summary>
internal readonly record struct AircraftSurfaceCommands(float Pitch, float Roll, float Yaw)
{
    internal static readonly AircraftSurfaceCommands Neutral = new(0f, 0f, 0f);

    internal AircraftSurfaceCommands Clamped()
        => new(
            AircraftAerodynamicsCore.Clamp(Pitch, -1f, 1f),
            AircraftAerodynamicsCore.Clamp(Roll, -1f, 1f),
            AircraftAerodynamicsCore.Clamp(Yaw, -1f, 1f));
}

internal readonly record struct AircraftKinematicState(
    /// <summary>Aircraft velocity through the air, not the incoming-wind vector.</summary>
    Vector3 AirVelocity,
    Vector3 AngularVelocity,
    Vector3 Forward,
    Vector3 Up);

internal readonly record struct AircraftAerodynamicsParameters(
    float AirDensity,
    float WingArea,
    float WingSpan,
    float MeanAerodynamicChord,
    float ZeroLiftAngleOfAttackDegrees,
    float LiftCurveSlopePerRadian,
    float MaximumAttachedLiftCoefficient,
    float CriticalAngleOfAttackDegrees,
    float SeparationWidthDegrees,
    float PostStallLiftCoefficient,
    float ParasiteDragCoefficient,
    float InducedDragFactor,
    float SeparatedDragCoefficient,
    float AileronAngleOfAttackDegrees,
    float ReferenceControlSpeed,
    float ElevatorPropwashAuthority,
    float RudderPropwashAuthority,
    float PitchMomentCoefficient,
    float YawMomentCoefficient,
    float NoseDownMomentFraction)
{
    internal static readonly AircraftAerodynamicsParameters DefaultFighter = new(
        AirDensity: 1.225f,
        WingArea: 22f,
        WingSpan: 11f,
        MeanAerodynamicChord: 2f,
        ZeroLiftAngleOfAttackDegrees: -2.5f,
        LiftCurveSlopePerRadian: 5.2f,
        MaximumAttachedLiftCoefficient: 1.35f,
        CriticalAngleOfAttackDegrees: 16f,
        SeparationWidthDegrees: 9f,
        PostStallLiftCoefficient: 0.90f,
        ParasiteDragCoefficient: 0.028f,
        InducedDragFactor: 0.055f,
        SeparatedDragCoefficient: 0.72f,
        AileronAngleOfAttackDegrees: 7.5f,
        ReferenceControlSpeed: 55f,
        ElevatorPropwashAuthority: 0.30f,
        RudderPropwashAuthority: 0.42f,
        PitchMomentCoefficient: 0.70f,
        YawMomentCoefficient: 0.16f,
        NoseDownMomentFraction: 0.58f);
}

internal readonly record struct AircraftEngineParameters(
    float MinimumStaticThrustToWeight,
    float MaximumStaticThrustToWeight,
    float PowerTransitionSpeed,
    float SpoolUpTimeConstant,
    float SpoolDownTimeConstant)
{
    internal static readonly AircraftEngineParameters Default = new(
        MinimumStaticThrustToWeight: 0.18f,
        MaximumStaticThrustToWeight: 0.78f,
        PowerTransitionSpeed: 45f,
        SpoolUpTimeConstant: 0.24f,
        SpoolDownTimeConstant: 0.44f);
}

internal readonly record struct AircraftForceInput(
    AircraftKinematicState Kinematics,
    AircraftSurfaceCommands SurfaceCommands,
    float MassKilograms,
    float EngineRating,
    float EngineSpool,
    float LeftWingEffectiveness,
    float RightWingEffectiveness)
{
    internal AircraftForceInput(
        AircraftKinematicState kinematics,
        AircraftSurfaceCommands surfaceCommands,
        float massKilograms,
        float engineRating,
        float engineSpool)
        : this(
            kinematics,
            surfaceCommands,
            massKilograms,
            engineRating,
            engineSpool,
            1f,
            1f)
    {
    }
}

internal readonly record struct AircraftLiftState(
    float AngleOfAttackDegrees,
    float EffectiveAngleOfAttackDegrees,
    float LiftCoefficient,
    float Separation)
{
    internal bool IsSeparated => Separation >= 0.5f;
}

internal readonly record struct AircraftControlAuthority(
    float Aileron,
    float Elevator,
    float Rudder);

/// <summary>
/// Fraction of the filtered virtual control-surface travel that the pilot can
/// attain at the current speed. The native flight model remains responsible
/// for converting that deflection and the available airflow into motion.
/// </summary>
internal readonly record struct AircraftNativeControlSchedule(
    float Pitch,
    float Roll,
    float Rudder)
{
    internal static readonly AircraftNativeControlSchedule FullTravel =
        new(1f, 1f, 1f);
}

internal readonly record struct AircraftWingForce(
    Vector3 Position,
    Vector3 AirVelocity,
    float DynamicPressure,
    AircraftLiftState LiftState,
    float DragCoefficient,
    Vector3 Lift,
    Vector3 Drag,
    Vector3 Force,
    Vector3 Moment);

/// <summary>
/// Gravity is deliberately not included. The Unity adapter should apply the
/// rigidbody's normal gravity once, alongside this force and moment result.
/// </summary>
internal readonly record struct AircraftForceEvaluation(
    AircraftWingForce LeftWing,
    AircraftWingForce RightWing,
    AircraftControlAuthority ControlAuthority,
    Vector3 AerodynamicForce,
    Vector3 ThrustForce,
    Vector3 TotalForce,
    Vector3 AerodynamicMoment,
    Vector3 ControlMoment,
    Vector3 TotalMoment);

/// <summary>
/// Deterministic, Unity-free aerodynamics shared by direct controls and the mouse
/// instructor after either controller has produced semantic surface commands.
/// </summary>
internal static class AircraftAerodynamicsCore
{
    internal const float StandardGravity = 9.80665f;
    internal const float RecommendedNativeDragFraction = 0.12f;
    private const float DegreesToRadians = MathF.PI / 180f;
    private const float RadiansToDegrees = 180f / MathF.PI;
    private const float VectorEpsilonSquared = 1e-10f;

    internal static float LandingGearExtensionSpeedLimit(
        float maximumSpeedMetersPerSecond,
        float stallSpeedMetersPerSecond,
        bool isBomber)
    {
        if (!float.IsFinite(maximumSpeedMetersPerSecond) ||
            maximumSpeedMetersPerSecond <= 0f ||
            !float.IsFinite(stallSpeedMetersPerSecond) ||
            stallSpeedMetersPerSecond <= 0f)
        {
            return 0f;
        }

        var stallMultiplier = isBomber ? 1.55f : 1.70f;
        var candidate = MathF.Min(
            maximumSpeedMetersPerSecond * 0.62f,
            stallSpeedMetersPerSecond * stallMultiplier);
        return Clamp(
            candidate,
            stallSpeedMetersPerSecond * 1.35f,
            maximumSpeedMetersPerSecond * 0.70f);
    }

    internal static bool LandingGearExtensionAllowed(
        bool ownsAircraftFlight,
        bool isGrounded,
        float airspeedMetersPerSecond,
        float extensionLimitMetersPerSecond)
    {
        if (!ownsAircraftFlight || isGrounded)
            return true;

        // Preserve the native call if an aircraft is despawning or reports
        // invalid kinematics instead of trapping its gear in an unknown state.
        if (!float.IsFinite(airspeedMetersPerSecond) ||
            airspeedMetersPerSecond < 0f ||
            !float.IsFinite(extensionLimitMetersPerSecond) ||
            extensionLimitMetersPerSecond <= 0f)
        {
            return true;
        }

        return airspeedMetersPerSecond <= extensionLimitMetersPerSecond;
    }

    internal static float DynamicPressure(float speed, float airDensity)
    {
        if (!float.IsFinite(speed) || !float.IsFinite(airDensity))
            return 0f;

        speed = MathF.Max(0f, speed);
        airDensity = MathF.Max(0f, airDensity);
        return 0.5f * airDensity * speed * speed;
    }

    internal static float AngleOfAttackDegrees(
        Vector3 airVelocity,
        Vector3 forward,
        Vector3 up)
    {
        if (!IsFinite(airVelocity))
            return 0f;

        var axes = BuildAxes(forward, up);
        var axialSpeed = Vector3.Dot(airVelocity, axes.Forward);
        var verticalSpeed = Vector3.Dot(airVelocity, axes.Up);
        if (MathF.Abs(axialSpeed) + MathF.Abs(verticalSpeed) <= 1e-6f)
            return 0f;

        // Positive AoA means the nose/chord points above the flight path.
        return WrapDegrees(MathF.Atan2(-verticalSpeed, axialSpeed) * RadiansToDegrees);
    }

    /// <summary>
    /// Separation is a function of AoA only. Low dynamic pressure removes force and
    /// control authority, but does not manufacture a stall for an unloaded wing.
    /// </summary>
    internal static AircraftLiftState EvaluateLift(
        float angleOfAttackDegrees,
        in AircraftAerodynamicsParameters parameters)
    {
        angleOfAttackDegrees = float.IsFinite(angleOfAttackDegrees)
            ? WrapDegrees(angleOfAttackDegrees)
            : 0f;
        var zeroLift = FiniteOr(parameters.ZeroLiftAngleOfAttackDegrees, -2.5f);
        var effectiveAngle = WrapDegrees(angleOfAttackDegrees - zeroLift);
        var effectiveRadians = effectiveAngle * DegreesToRadians;
        var criticalAngle = PositiveOr(parameters.CriticalAngleOfAttackDegrees, 16f);
        var separationWidth = PositiveOr(parameters.SeparationWidthDegrees, 9f);
        var separationCoordinate =
            (MathF.Abs(effectiveAngle) - criticalAngle) / separationWidth;
        var separation = SmoothStep01(separationCoordinate);

        var maximumAttachedLift =
            PositiveOr(parameters.MaximumAttachedLiftCoefficient, 1.35f);
        var liftSlope = PositiveOr(parameters.LiftCurveSlopePerRadian, 5.2f);
        var attachedLift = maximumAttachedLift *
                           MathF.Tanh(liftSlope * effectiveRadians / maximumAttachedLift);
        var postStallLift = MathF.Max(0f, FiniteOr(parameters.PostStallLiftCoefficient, 0.90f)) *
                            MathF.Sin(2f * effectiveRadians);
        var liftCoefficient = Lerp(attachedLift, postStallLift, separation);

        return new AircraftLiftState(
            angleOfAttackDegrees,
            effectiveAngle,
            FiniteOr(liftCoefficient, 0f),
            separation);
    }

    internal static float EvaluateDragCoefficient(
        float liftCoefficient,
        float separation,
        in AircraftAerodynamicsParameters parameters)
    {
        liftCoefficient = FiniteOr(liftCoefficient, 0f);
        separation = Clamp01(FiniteOr(separation, 0f));
        var parasite = MathF.Max(0f, FiniteOr(parameters.ParasiteDragCoefficient, 0.028f));
        var induced = MathF.Max(0f, FiniteOr(parameters.InducedDragFactor, 0.055f));
        var separated = MathF.Max(0f, FiniteOr(parameters.SeparatedDragCoefficient, 0.72f));
        return parasite +
               induced * liftCoefficient * liftCoefficient +
               separated * separation * separation;
    }

    internal static Vector3 LiftForce(
        Vector3 airVelocity,
        Vector3 spanAxis,
        float magnitude)
    {
        if (!IsFinite(airVelocity) || !IsFinite(spanAxis) || !float.IsFinite(magnitude))
            return Vector3.Zero;

        var velocityDirection = NormalizeOrZero(airVelocity);
        var spanDirection = NormalizeOrZero(spanAxis);
        var liftDirection = NormalizeOrZero(Vector3.Cross(velocityDirection, spanDirection));
        return liftDirection * magnitude;
    }

    internal static Vector3 DragForce(Vector3 airVelocity, float magnitude)
    {
        if (!IsFinite(airVelocity) || !float.IsFinite(magnitude))
            return Vector3.Zero;

        return -NormalizeOrZero(airVelocity) * MathF.Max(0f, magnitude);
    }

    /// <summary>
    /// Ailerons receive freestream dynamic pressure only. Propwash can retain only
    /// the configured fraction of elevator/rudder authority at zero airspeed.
    /// </summary>
    internal static AircraftControlAuthority EvaluateControlAuthority(
        float airSpeed,
        float engineSpool,
        in AircraftAerodynamicsParameters parameters)
    {
        airSpeed = MathF.Max(0f, FiniteOr(airSpeed, 0f));
        engineSpool = Clamp01(FiniteOr(engineSpool, 0f));
        var referenceSpeed = PositiveOr(parameters.ReferenceControlSpeed, 55f);
        var freeStreamAuthority = Clamp01(
            airSpeed * airSpeed / (referenceSpeed * referenceSpeed));
        var elevatorPropwash = Clamp01(
            FiniteOr(parameters.ElevatorPropwashAuthority, 0.30f));
        var rudderPropwash = Clamp01(
            FiniteOr(parameters.RudderPropwashAuthority, 0.42f));

        var elevator = 1f - (1f - freeStreamAuthority) *
                       (1f - elevatorPropwash * engineSpool);
        var rudder = 1f - (1f - freeStreamAuthority) *
                     (1f - rudderPropwash * engineSpool);
        return new AircraftControlAuthority(
            freeStreamAuthority,
            Clamp01(elevator),
            Clamp01(rudder));
    }

    /// <summary>
    /// Returns a static-stability error with the same small-angle slope as an
    /// ordinary angle difference, but remains continuous through a tailslide.
    /// A raw +/-180 degree error can flip a very large pitch moment when the
    /// airflow crosses the rear pole; sine naturally fades that moment to zero
    /// in fully reversed flow.
    /// </summary>
    internal static float BoundedStabilityErrorDegrees(
        float angleDegrees,
        float neutralAngleDegrees)
    {
        var error = WrapDegrees(
            FiniteOr(angleDegrees, 0f) -
            FiniteOr(neutralAngleDegrees, 0f));
        return MathF.Sin(error * DegreesToRadians) / DegreesToRadians;
    }

    /// <summary>
    /// Maps the player-facing 1..10 rating linearly. The default 0.18..0.78 static
    /// thrust-to-weight range is 4.33x while remaining below one-g rocket thrust.
    /// </summary>
    internal static float MapEngineRatingToStaticThrustToWeight(
        float engineRating,
        in AircraftEngineParameters parameters)
    {
        engineRating = Clamp(FiniteOr(engineRating, 1f), 1f, 10f);
        var minimum = Clamp(FiniteOr(parameters.MinimumStaticThrustToWeight, 0.18f), 0f, 0.78f);
        var maximum = Clamp(
            FiniteOr(parameters.MaximumStaticThrustToWeight, 0.78f),
            minimum,
            0.78f);
        return Lerp(minimum, maximum, (engineRating - 1f) / 9f);
    }

    internal static float MapAerodynamicDragToRigidbodyDrag(
        float originalRigidbodyDrag,
        float aerodynamicDragSetting)
    {
        originalRigidbodyDrag = MathF.Max(
            0f,
            FiniteOr(originalRigidbodyDrag, 0f));
        aerodynamicDragSetting = Clamp(
            FiniteOr(aerodynamicDragSetting, 1f),
            0.60f,
            1.60f);
        return originalRigidbodyDrag *
               RecommendedNativeDragFraction *
               aerodynamicDragSetting;
    }

    /// <summary>
    /// Models the high-speed pedal-force limit of a manually controlled
    /// aircraft. Elevator and aileron response remains on the native model;
    /// applying another all-axis multiplier here made the mouse instructor lag
    /// behind its target. Rudder travel remains unrestricted through the low
    /// and middle envelope, then becomes progressively harder to reach as
    /// dynamic pressure rises.
    /// </summary>
    internal static AircraftNativeControlSchedule EvaluateNativeControlSchedule(
        float airspeedMs,
        float heavyControlSpeedMs)
    {
        if (!float.IsFinite(airspeedMs) ||
            airspeedMs < 0f ||
            !float.IsFinite(heavyControlSpeedMs) ||
            heavyControlSpeedMs <= 0f)
        {
            return AircraftNativeControlSchedule.FullTravel;
        }

        var speedRatio = airspeedMs / heavyControlSpeedMs;
        if (!float.IsFinite(speedRatio) || speedRatio <= 1f)
            return AircraftNativeControlSchedule.FullTravel;

        // Two times the heavy-control reference is already beyond the normal
        // speed envelope. Capping the ratio keeps a finite amount of recovery
        // control during extreme overspeed while the native model applies its
        // own additional overspeed attenuation.
        speedRatio = MathF.Min(speedRatio, 2f);
        var dynamicPressureExcess =
            speedRatio * speedRatio - 1f;

        // The native model already schedules pitch and roll against airflow.
        // Restrict only maximum pedal travel here; the caller preserves small
        // rudder corrections instead of multiplying every filtered command.
        return new AircraftNativeControlSchedule(
            Pitch: 1f,
            Roll: 1f,
            Rudder: 1f / (1f + 1.50f * dynamicPressureExcess));
    }

    /// <summary>
    /// Caps only the attainable magnitude of an already-filtered control
    /// command. Inputs inside the available travel pass through unchanged, so
    /// fine instructor corrections stay immediate while a large command meets
    /// the physical high-speed travel limit.
    /// </summary>
    internal static float LimitNativeControlTravel(
        float nativeCommand,
        float availableTravel)
    {
        nativeCommand = Clamp(
            FiniteOr(nativeCommand, 0f),
            -1f,
            1f);
        availableTravel = Clamp(
            FiniteOr(availableTravel, 1f),
            0f,
            1f);
        return Clamp(
            nativeCommand,
            -availableTravel,
            availableTravel);
    }

    /// <summary>
    /// Scales the already-filtered native aileron position for the one render
    /// update in which VehiclePlane applies attitude. The caller restores the
    /// stored surface value immediately afterward.
    /// </summary>
    internal static float ScaleNativeRollCommand(
        float nativeRollCommand,
        bool isBomber,
        float rollAuthoritySetting)
    {
        nativeRollCommand = Clamp(
            FiniteOr(nativeRollCommand, 0f),
            -1f,
            1f);
        rollAuthoritySetting = Clamp(
            FiniteOr(rollAuthoritySetting, 1f),
            0.25f,
            2f);
        var classGain = isBomber ? 1.15f : 1.50f;
        return nativeRollCommand * classGain * rollAuthoritySetting;
    }

    /// <summary>
    /// Scales the already-filtered native rudder position. Applying this at the
    /// same physical boundary as pitch and roll makes the player-facing slider
    /// affect complete rudder travel instead of only reaching saturation sooner.
    /// </summary>
    internal static float ScaleNativeYawCommand(
        float nativeYawCommand,
        float rudderAuthoritySetting)
    {
        nativeYawCommand = Clamp(
            FiniteOr(nativeYawCommand, 0f),
            -1f,
            1f);
        rudderAuthoritySetting = Clamp(
            FiniteOr(rudderAuthoritySetting, 1f),
            0.25f,
            3.75f);
        return nativeYawCommand * rudderAuthoritySetting;
    }

    /// <summary>
    /// Scales the finite native elevator input at the control boundary. Native
    /// positive pitch is a nose-down push, which deliberately retains only
    /// 58 percent of the matching nose-up authority.
    /// </summary>
    internal static float ScaleNativePitchCommand(
        float nativePitchCommand,
        float pitchAuthoritySetting)
    {
        nativePitchCommand = Clamp(
            FiniteOr(nativePitchCommand, 0f),
            -1f,
            1f);
        pitchAuthoritySetting = Clamp(
            FiniteOr(pitchAuthoritySetting, 1f),
            0.25f,
            2f);
        var scaledCommand =
            nativePitchCommand * pitchAuthoritySetting;
        return scaledCommand > 0f
            ? scaledCommand * 0.58f
            : scaledCommand;
    }

    /// <summary>
    /// Returns the angle swept by the flight-path velocity direction per second.
    /// Axial acceleration and pure roll therefore produce no false turn rate.
    /// </summary>
    internal static float FlightPathTurnRateRadiansPerSecond(
        Vector3 previousVelocity,
        Vector3 currentVelocity,
        float deltaTime)
    {
        if (!IsFinite(previousVelocity) ||
            !IsFinite(currentVelocity) ||
            !float.IsFinite(deltaTime) ||
            deltaTime <= 0f ||
            previousVelocity.LengthSquared() <= VectorEpsilonSquared ||
            currentVelocity.LengthSquared() <= VectorEpsilonSquared)
        {
            return 0f;
        }

        var previousDirection = Vector3.Normalize(previousVelocity);
        var currentDirection = Vector3.Normalize(currentVelocity);
        var directionDot = Clamp(
            Vector3.Dot(previousDirection, currentDirection),
            -1f,
            1f);
        var directionCross = Vector3.Cross(previousDirection, currentDirection);
        var sweptAngle = MathF.Atan2(directionCross.Length(), directionDot);
        return FiniteOr(sweptAngle / deltaTime, 0f);
    }

    /// <summary>
    /// Keeps native direction steering but removes only its small, hidden speed
    /// subtraction. Ineligible, large, divergent, or speed-increasing writes are
    /// returned exactly as requested so collisions and network corrections remain
    /// authoritative.
    /// </summary>
    internal static Vector3 PreserveRequestedVelocityDirection(
        Vector3 requestedVelocity,
        Vector3 currentVelocity,
        bool eligible)
    {
        if (!eligible ||
            !IsFinite(requestedVelocity) ||
            !IsFinite(currentVelocity))
            return requestedVelocity;

        var requestedSpeedSquared = requestedVelocity.LengthSquared();
        var currentSpeedSquared = currentVelocity.LengthSquared();
        if (requestedSpeedSquared <= VectorEpsilonSquared ||
            currentSpeedSquared <= VectorEpsilonSquared)
            return requestedVelocity;

        var requestedSpeed = MathF.Sqrt(requestedSpeedSquared);
        var currentSpeed = MathF.Sqrt(currentSpeedSquared);
        if (currentSpeed < 1.5f ||
            requestedSpeed < 0.1f ||
            requestedSpeed >= currentSpeed - 0.001f)
            return requestedVelocity;

        var lostFraction =
            (currentSpeed - requestedSpeed) / currentSpeed;
        if (lostFraction > 0.20f)
            return requestedVelocity;

        var requestedDirection = requestedVelocity / requestedSpeed;
        var currentDirection = currentVelocity / currentSpeed;
        if (Vector3.Dot(requestedDirection, currentDirection) < 0.85f)
            return requestedVelocity;

        return requestedDirection * currentSpeed;
    }

    /// <summary>
    /// Calculates only the excess induced drag needed on top of native flight.
    /// Pitch and yaw rate bend the flight path and cost energy quadratically;
    /// axial roll is deliberately excluded by the Unity adapter.
    /// </summary>
    internal static float NativeManeuverDragAcceleration(
        float massKilograms,
        float nonRollAngularSpeedRadiansPerSecond,
        float airDensity,
        float wingArea,
        float aspectRatio,
        float oswaldEfficiency,
        float airspeed,
        float stallSpeed,
        float aerodynamicDragSetting)
    {
        if (!float.IsFinite(massKilograms) ||
            massKilograms <= 0f ||
            !float.IsFinite(nonRollAngularSpeedRadiansPerSecond) ||
            nonRollAngularSpeedRadiansPerSecond <= 0f ||
            !float.IsFinite(airDensity) ||
            airDensity <= 0f ||
            !float.IsFinite(wingArea) ||
            wingArea <= 0f ||
            !float.IsFinite(aspectRatio) ||
            aspectRatio <= 0f ||
            !float.IsFinite(oswaldEfficiency) ||
            oswaldEfficiency <= 0f ||
            !float.IsFinite(airspeed) ||
            airspeed <= 0f ||
            !float.IsFinite(stallSpeed) ||
            stallSpeed <= 0f)
        {
            return 0f;
        }

        aerodynamicDragSetting = Clamp(
            FiniteOr(aerodynamicDragSetting, 1f),
            0.60f,
            1.60f);
        var fadeStart = stallSpeed * 0.55f;
        var fadeEnd = stallSpeed * 0.90f;
        var lowSpeedConfidence = SmoothStep01(
            (airspeed - fadeStart) /
            MathF.Max(0.001f, fadeEnd - fadeStart));
        var acceleration =
            3f *
            massKilograms *
            nonRollAngularSpeedRadiansPerSecond *
            nonRollAngularSpeedRadiansPerSecond /
            (airDensity *
             wingArea *
             MathF.PI *
             aspectRatio *
             oswaldEfficiency);
        acceleration *=
            aerodynamicDragSetting *
            lowSpeedConfidence;
        return Clamp(
            FiniteOr(acceleration, 0f),
            0f,
            0.75f * StandardGravity);
    }

    /// <summary>
    /// Exact first-order response, so a fixed throttle command gives the same spool
    /// after a duration regardless of the caller's fixed-step size.
    /// </summary>
    internal static float AdvanceEngineSpool(
        float currentSpool,
        float targetThrottle,
        float deltaTime,
        in AircraftEngineParameters parameters)
    {
        currentSpool = Clamp01(FiniteOr(currentSpool, 0f));
        targetThrottle = Clamp01(FiniteOr(targetThrottle, 0f));
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f)
            return currentSpool;

        var timeConstant = targetThrottle > currentSpool
            ? PositiveOr(parameters.SpoolUpTimeConstant, 0.24f)
            : PositiveOr(parameters.SpoolDownTimeConstant, 0.44f);
        var blend = 1f - MathF.Exp(-deltaTime / timeConstant);
        return Clamp01(currentSpool + (targetThrottle - currentSpool) * blend);
    }

    /// <summary>
    /// Static thrust is available at zero or negative axial speed. Above the power
    /// transition speed, thrust falls as P/V and can never exceed static thrust.
    /// </summary>
    internal static float PowerLimitedThrust(
        float massKilograms,
        float engineRating,
        float engineSpool,
        float axialSpeed,
        in AircraftEngineParameters parameters)
    {
        massKilograms = MathF.Max(0f, FiniteOr(massKilograms, 0f));
        engineSpool = Clamp01(FiniteOr(engineSpool, 0f));
        var staticThrust = massKilograms * StandardGravity *
                           MapEngineRatingToStaticThrustToWeight(engineRating, parameters) *
                           engineSpool;
        if (staticThrust <= 0f)
            return 0f;

        var positiveAxialSpeed = MathF.Max(0f, FiniteOr(axialSpeed, 0f));
        var transitionSpeed = PositiveOr(parameters.PowerTransitionSpeed, 45f);
        if (positiveAxialSpeed <= transitionSpeed)
            return staticThrust;

        var maximumPropulsivePower = staticThrust * transitionSpeed;
        return MathF.Min(staticThrust, maximumPropulsivePower / positiveAxialSpeed);
    }

    /// <summary>
    /// Caps the native per-throttle-unit thrust request to the physical engine
    /// envelope while leaving a weaker authored request untouched. Native applies
    /// density after this multiplier; density below one still reduces thrust, while
    /// density above one is included in the ceiling denominator.
    /// </summary>
    internal static float NativeThrustForceMultiplier(
        float authoredThrustForceMultiplier,
        float nativeMaximumThrottle,
        float massKilograms,
        float engineRating,
        float axialSpeed,
        float airDensityMultiplier,
        float powerTransitionSpeed,
        in AircraftEngineParameters parameters)
    {
        authoredThrustForceMultiplier = MathF.Max(
            0f,
            FiniteOr(authoredThrustForceMultiplier, 0f));
        nativeMaximumThrottle = MathF.Max(
            0f,
            FiniteOr(nativeMaximumThrottle, 0f));
        massKilograms = MathF.Max(0f, FiniteOr(massKilograms, 0f));
        if (authoredThrustForceMultiplier <= 0f ||
            nativeMaximumThrottle <= 0f ||
            massKilograms <= 0f)
        {
            return 0f;
        }

        var density = MathF.Max(0f, FiniteOr(airDensityMultiplier, 1f));
        var guardedDensity = MathF.Max(1f, density);
        var engine = parameters with
        {
            PowerTransitionSpeed = PositiveOr(
                powerTransitionSpeed,
                PositiveOr(parameters.PowerTransitionSpeed, 45f))
        };
        var thrustCeiling = PowerLimitedThrust(
            massKilograms,
            engineRating,
            1f,
            axialSpeed,
            engine);
        var multiplierCeiling =
            thrustCeiling /
            (nativeMaximumThrottle * guardedDensity);
        return MathF.Min(
            authoredThrustForceMultiplier,
            MathF.Max(0f, FiniteOr(multiplierCeiling, 0f)));
    }

    internal static AircraftForceEvaluation EvaluateForces(
        in AircraftForceInput input,
        in AircraftAerodynamicsParameters aerodynamics,
        in AircraftEngineParameters engine)
    {
        var axes = BuildAxes(input.Kinematics.Forward, input.Kinematics.Up);
        var commands = input.SurfaceCommands.Clamped();
        var wingSpan = PositiveOr(aerodynamics.WingSpan, 11f);
        var wingArm = wingSpan * 0.25f;
        var leftPosition = -axes.Right * wingArm;
        var rightPosition = axes.Right * wingArm;
        var aileronAngle = MathF.Max(
            0f, FiniteOr(aerodynamics.AileronAngleOfAttackDegrees, 7.5f));
        var leftWing = EvaluateHalfWing(
            input.Kinematics,
            axes,
            leftPosition,
            commands.Roll * aileronAngle,
            input.LeftWingEffectiveness,
            aerodynamics);
        var rightWing = EvaluateHalfWing(
            input.Kinematics,
            axes,
            rightPosition,
            -commands.Roll * aileronAngle,
            input.RightWingEffectiveness,
            aerodynamics);

        var airSpeed = IsFinite(input.Kinematics.AirVelocity)
            ? input.Kinematics.AirVelocity.Length()
            : 0f;
        var authority = EvaluateControlAuthority(
            airSpeed, input.EngineSpool, aerodynamics);
        var referenceSpeed = PositiveOr(aerodynamics.ReferenceControlSpeed, 55f);
        var referencePressure = DynamicPressure(referenceSpeed, aerodynamics.AirDensity);
        var wingArea = PositiveOr(aerodynamics.WingArea, 22f);
        var chord = PositiveOr(aerodynamics.MeanAerodynamicChord, 2f);
        var pitchCoefficient = MathF.Max(
            0f, FiniteOr(aerodynamics.PitchMomentCoefficient, 0.70f));
        var yawCoefficient = MathF.Max(
            0f, FiniteOr(aerodynamics.YawMomentCoefficient, 0.16f));
        var noseDownFraction = Clamp01(
            FiniteOr(aerodynamics.NoseDownMomentFraction, 0.58f));
        var pitchDirectionScale = commands.Pitch >= 0f ? 1f : noseDownFraction;
        var pitchMomentMagnitude = referencePressure * wingArea * chord *
                                   pitchCoefficient * authority.Elevator *
                                   commands.Pitch * pitchDirectionScale;
        var yawMomentMagnitude = referencePressure * wingArea * wingSpan *
                                 yawCoefficient * authority.Rudder * commands.Yaw;
        var controlMoment = -axes.Right * pitchMomentMagnitude +
                            axes.Up * yawMomentMagnitude;

        var aerodynamicForce = leftWing.Force + rightWing.Force;
        var aerodynamicMoment = leftWing.Moment + rightWing.Moment;
        var axialSpeed = IsFinite(input.Kinematics.AirVelocity)
            ? Vector3.Dot(input.Kinematics.AirVelocity, axes.Forward)
            : 0f;
        var thrustMagnitude = PowerLimitedThrust(
            input.MassKilograms,
            input.EngineRating,
            input.EngineSpool,
            axialSpeed,
            engine);
        var thrustForce = axes.Forward * thrustMagnitude;
        return new AircraftForceEvaluation(
            leftWing,
            rightWing,
            authority,
            aerodynamicForce,
            thrustForce,
            aerodynamicForce + thrustForce,
            aerodynamicMoment,
            controlMoment,
            aerodynamicMoment + controlMoment);
    }

    private static AircraftWingForce EvaluateHalfWing(
        in AircraftKinematicState kinematics,
        in AircraftAxes axes,
        Vector3 position,
        float aileronAngleOfAttackDegrees,
        float effectiveness,
        in AircraftAerodynamicsParameters parameters)
    {
        effectiveness = Clamp01(FiniteOr(effectiveness, 0f));
        var pointVelocity = IsFinite(kinematics.AirVelocity) &&
                            IsFinite(kinematics.AngularVelocity)
            ? kinematics.AirVelocity + Vector3.Cross(kinematics.AngularVelocity, position)
            : Vector3.Zero;
        // Spanwise flow does not create two-dimensional section lift.
        var sectionVelocity = pointVelocity -
                              axes.Right * Vector3.Dot(pointVelocity, axes.Right);
        var speedSquared = IsFinite(sectionVelocity)
            ? sectionVelocity.LengthSquared()
            : 0f;
        if (speedSquared <= VectorEpsilonSquared || effectiveness <= 0f)
        {
            return new AircraftWingForce(
                position,
                sectionVelocity,
                0f,
                EvaluateLift(0f, parameters),
                EvaluateDragCoefficient(0f, 0f, parameters),
                Vector3.Zero,
                Vector3.Zero,
                Vector3.Zero,
                Vector3.Zero);
        }

        var speed = MathF.Sqrt(speedSquared);
        var geometricAngle = AngleOfAttackDegrees(
            sectionVelocity, axes.Forward, axes.Up);
        var liftState = EvaluateLift(
            geometricAngle + aileronAngleOfAttackDegrees, parameters);
        var dragCoefficient = EvaluateDragCoefficient(
            liftState.LiftCoefficient, liftState.Separation, parameters);
        var pressure = DynamicPressure(speed, parameters.AirDensity);
        var halfWingArea = PositiveOr(parameters.WingArea, 22f) * 0.5f * effectiveness;
        var liftMagnitude = pressure * halfWingArea * liftState.LiftCoefficient;
        var dragMagnitude = pressure * halfWingArea * dragCoefficient;
        var lift = LiftForce(sectionVelocity, axes.Right, liftMagnitude);
        var drag = DragForce(sectionVelocity, dragMagnitude);
        var force = lift + drag;
        return new AircraftWingForce(
            position,
            sectionVelocity,
            pressure,
            liftState,
            dragCoefficient,
            lift,
            drag,
            force,
            Vector3.Cross(position, force));
    }

    private static AircraftAxes BuildAxes(Vector3 forward, Vector3 up)
    {
        forward = NormalizeOrFallback(forward, Vector3.UnitZ);
        up = IsFinite(up) ? up : Vector3.UnitY;
        var right = NormalizeOrZero(Vector3.Cross(up, forward));
        if (right.LengthSquared() <= VectorEpsilonSquared)
        {
            var fallbackUp = MathF.Abs(Vector3.Dot(forward, Vector3.UnitY)) < 0.95f
                ? Vector3.UnitY
                : Vector3.UnitX;
            right = NormalizeOrFallback(Vector3.Cross(fallbackUp, forward), Vector3.UnitX);
        }

        up = NormalizeOrFallback(Vector3.Cross(forward, right), Vector3.UnitY);
        return new AircraftAxes(forward, up, right);
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
    {
        var normalized = NormalizeOrZero(value);
        return normalized.LengthSquared() > VectorEpsilonSquared ? normalized : fallback;
    }

    private static Vector3 NormalizeOrZero(Vector3 value)
    {
        if (!IsFinite(value))
            return Vector3.Zero;
        var lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= VectorEpsilonSquared)
            return Vector3.Zero;
        return value / MathF.Sqrt(lengthSquared);
    }

    internal static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) &&
           float.IsFinite(value.Y) &&
           float.IsFinite(value.Z);

    internal static float Clamp(float value, float minimum, float maximum)
        => MathF.Min(maximum, MathF.Max(minimum, value));

    private static float Clamp01(float value) => Clamp(value, 0f, 1f);

    private static float SmoothStep01(float value)
    {
        value = Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static float Lerp(float left, float right, float amount)
        => left + (right - left) * Clamp01(amount);

    private static float FiniteOr(float value, float fallback)
        => float.IsFinite(value) ? value : fallback;

    private static float PositiveOr(float value, float fallback)
        => float.IsFinite(value) && value > 1e-5f ? value : fallback;

    private static float WrapDegrees(float degrees)
    {
        if (!float.IsFinite(degrees))
            return 0f;
        degrees %= 360f;
        if (degrees > 180f)
            degrees -= 360f;
        else if (degrees < -180f)
            degrees += 360f;
        return degrees;
    }

    private readonly record struct AircraftAxes(Vector3 Forward, Vector3 Up, Vector3 Right);
}
