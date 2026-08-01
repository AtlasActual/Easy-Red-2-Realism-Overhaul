using System;
using System.Numerics;

namespace ER2RealismOverhaul;

/// <summary>
/// Aircraft-specific rate limits supplied by the Unity adapter. All rates use
/// semantic aircraft axes in degrees per second:
/// pitch is positive nose-up/pull, roll is positive right bank, and yaw is
/// positive nose-right.
/// </summary>
internal readonly record struct AircraftMouseInstructorLimits(
    float MaximumPitchRateDegreesPerSecond,
    float MaximumRollRateDegreesPerSecond,
    float MaximumYawRateDegreesPerSecond,
    float PitchRateErrorForFullControl,
    float RollRateErrorForFullControl,
    float YawRateErrorForFullControl)
{
    internal static readonly AircraftMouseInstructorLimits Default = new(
        MaximumPitchRateDegreesPerSecond: 72f,
        MaximumRollRateDegreesPerSecond: 62f,
        MaximumYawRateDegreesPerSecond: 30f,
        PitchRateErrorForFullControl: 24f,
        RollRateErrorForFullControl: 42f,
        YawRateErrorForFullControl: 30f);

    internal static readonly AircraftMouseInstructorLimits Fighter = new(
        MaximumPitchRateDegreesPerSecond: 72f,
        MaximumRollRateDegreesPerSecond: 110f,
        MaximumYawRateDegreesPerSecond: 30f,
        PitchRateErrorForFullControl: 18f,
        RollRateErrorForFullControl: 24f,
        YawRateErrorForFullControl: 26f);

    internal AircraftMouseInstructorLimits Sanitized()
        => new(
            PositiveOrDefault(
                MaximumPitchRateDegreesPerSecond,
                Default.MaximumPitchRateDegreesPerSecond),
            PositiveOrDefault(
                MaximumRollRateDegreesPerSecond,
                Default.MaximumRollRateDegreesPerSecond),
            PositiveOrDefault(
                MaximumYawRateDegreesPerSecond,
                Default.MaximumYawRateDegreesPerSecond),
            PositiveOrDefault(
                PitchRateErrorForFullControl,
                Default.PitchRateErrorForFullControl),
            PositiveOrDefault(
                RollRateErrorForFullControl,
                Default.RollRateErrorForFullControl),
            PositiveOrDefault(
                YawRateErrorForFullControl,
                Default.YawRateErrorForFullControl));

    private static float PositiveOrDefault(float value, float fallback)
        => AircraftMouseInstructorCore.IsFinite(value) && value > 0.001f
            ? value
            : fallback;
}

/// <summary>
/// One deterministic mouse-instructor sample. World vectors may come directly
/// from Unity transforms after conversion to <see cref="Vector3"/>.
///
/// <para>
/// Body rates and output controls use semantic axes, not Unity's raw local
/// angular-velocity signs:
/// <list type="bullet">
/// <item><description>pitch +1: back stick / positive-G pull;</description></item>
/// <item><description>roll +1: right wing down / right bank;</description></item>
/// <item><description>yaw +1: nose right.</description></item>
/// </list>
/// A Unity adapter therefore normally maps local angular velocity to
/// pitch=-local.x, roll=-local.z, yaw=local.y, and maps the returned pitch to
/// the game's native pitch argument with the opposite sign.
/// </para>
///
/// <para>
/// Positive sideslip means that the velocity vector is to the aircraft's
/// right (atan2(localVelocity.x, localVelocity.z)). Correcting that condition
/// requires positive/right yaw.
/// </para>
///
/// <para>
/// <paramref name="AimInputActive"/> describes player input, not world-space
/// motion of the aim ray. The angular velocity is authoritative when input is
/// active; pass zero with <c>false</c> when the mouse is idle.
/// </para>
/// </summary>
internal readonly record struct AircraftMouseInstructorInput(
    Vector3 AircraftForward,
    Vector3 AircraftUp,
    Vector3 AircraftRight,
    Vector3 WorldUp,
    Vector3 AimDirection,
    Vector3 AimAngularVelocityWorldDegreesPerSecond,
    bool AimInputActive,
    float PitchRateDegreesPerSecond,
    float RollRateDegreesPerSecond,
    float YawRateDegreesPerSecond,
    float SideslipDegrees,
    float AngleOfAttackDegrees,
    float CriticalAngleOfAttackDegrees,
    float DeltaTimeSeconds,
    AircraftMouseInstructorLimits Limits);

/// <summary>
/// Persistent geometric state. It contains no filtered surface positions; the
/// native aircraft control path remains the single source of actuator slew.
/// Reset point aiming by assigning <c>default</c> or calling <see cref="Reset"/>.
/// </summary>
internal struct AircraftMouseInstructorState
{
    internal bool Initialized;
    internal bool RouteLatched;
    internal Vector3 PreviousAimDirection;
    internal Vector3 PreviousAircraftForward;
    internal Vector3 LastAimTurnAxis;
    internal Vector3 RouteAxis;
    internal Vector3 PoleSafeLevelUp;
    internal float UnwrappedRouteErrorDegrees;
    internal float AimQuietSeconds;
    internal int RollTurnSign;

    internal void Reset() => this = default;
}

/// <summary>
/// Bounded semantic controls and useful diagnostics for an adapter or telemetry
/// overlay. Pitch, roll, and yaw are always finite and in [-1, 1].
/// </summary>
internal readonly record struct AircraftMouseInstructorOutput(
    float Pitch,
    float Roll,
    float Yaw,
    float AimErrorDegrees,
    float RollErrorDegrees,
    float DesiredPitchRateDegreesPerSecond,
    float DesiredRollRateDegreesPerSecond,
    float DesiredYawRateDegreesPerSecond,
    Vector3 DesiredLiftDirectionWorld,
    bool AimCaptured,
    bool WeakPushActive,
    bool RouteLatched);

/// <summary>
/// Unity-free mouse instructor. It plans a maneuver direction, converts that
/// plan into desired body rates, and returns ordinary bounded control inputs.
/// It never writes an attitude, angular velocity, or force directly.
/// </summary>
internal static class AircraftMouseInstructorCore
{
    private const float DegreesToRadians = MathF.PI / 180f;
    private const float RadiansToDegrees = 180f / MathF.PI;
    private const float VectorEpsilonSquared = 0.000001f;

    private const float AimMotionThresholdDegreesPerSecond = 1.5f;
    private const float AimCaptureAngleDegrees = 3f;
    private const float AimCaptureQuietSeconds = 0.08f;

    private const float RouteLatchAngleDegrees = 145f;
    private const float RouteBlendStartDegrees = 145f;
    private const float RouteBlendEndDegrees = 175f;
    private const float RouteReleaseAngleDegrees = 25f;
    private const float RouteReleaseErrorDegrees = 30f;

    private const float WeakPushMaximumAimAngleDegrees = 89f;
    private const float WeakPushMaximumDownAngleDegrees = 89f;
    private const float WeakPushFullLateralAngleDegrees = 6f;
    private const float WeakPushZeroLateralAngleDegrees = 10f;
    private const float WeakPushFullBankDegrees = 20f;
    private const float WeakPushZeroBankDegrees = 30f;
    private const float WeakPushMaximumControl = 1f;
    private const float WeakPushRateFraction = 1f;

    private const float MaximumPursuitBankDegrees = 68f;
    private const float LiftAlignmentStart = 0.30f;
    private const float LiftAlignmentFull = 0.94f;
    private const float BankAcquisitionPitchRateFraction = 0.08f;

    private const float PitchPositionGain = 2.65f;
    private const float RollPositionGain = 2.40f;
    private const float YawPositionGain = 1.25f;
    private const float PitchBrakingAcceleration = 220f;
    private const float RollBrakingAcceleration = 280f;
    private const float YawBrakingAcceleration = 110f;
    private const float PitchBrakeFullAimAngleDegrees = 3f;
    private const float PitchBrakeReleaseAimAngleDegrees = 14f;
    private const float MaximumCapturePitchBrakeControl = 0.35f;
    private const float MovingAimBankStartDegreesPerSecond = 4f;
    private const float MovingAimBankFullDegreesPerSecond = 12f;
    private const float InvertedLevelSuppressionStart = 0.15f;
    private const float InvertedLevelSuppressionFull = 0.65f;
    private const float InvertedPullIntentStartDegrees = 0.20f;
    private const float InvertedPullIntentFullDegrees = 1.50f;
    private const float InvertedPullAlignmentStart = 0.55f;
    private const float InvertedPullAlignmentFull = 0.90f;

    private const float FineYawFullAngleDegrees = 4f;
    private const float FineYawZeroAngleDegrees = 24f;
    private const float FineLateralLevelReleaseAngleDegrees = 8f;
    private const float FineLateralLevelFullPitchDegrees = 10f;
    private const float FineLateralLevelReleasePitchDegrees = 20f;
    private const float LevelTurnFullWeightPitchDegrees = 3f;
    private const float LevelTurnZeroWeightPitchDegrees = 10f;
    private const float SideslipYawRateGain = 0.45f;
    private const float MaximumRudderControl = 0.38f;

    private const float HorizonAvailabilityThreshold = 0.22f;
    private const float HorizonReacquireRateDegreesPerSecond = 120f;
    private const float AlphaProtectionStartFraction = 0.90f;
    private const float AlphaProtectionEndFraction = 1.10f;

    internal static AircraftMouseInstructorOutput Step(
        ref AircraftMouseInstructorState state,
        in AircraftMouseInstructorInput input)
    {
        var limits = input.Limits.Sanitized();
        var deltaTime = SanitizeDeltaTime(input.DeltaTimeSeconds);
        if (!TryBuildAircraftFrame(
                input.AircraftForward,
                input.AircraftUp,
                input.AircraftRight,
                out var forward,
                out var up,
                out var right) ||
            !TryNormalize(input.AimDirection, out var aimDirection))
        {
            return default;
        }

        var worldUp = TryNormalize(input.WorldUp, out var suppliedWorldUp)
            ? suppliedWorldUp
            : Vector3.UnitY;
        var wasInitialized = state.Initialized;
        if (!wasInitialized)
        {
            InitializeState(
                ref state,
                forward,
                up,
                worldUp,
                aimDirection);
        }

        var aimAngularVelocity = ResolveAimAngularVelocity(
            aimDirection,
            input.AimAngularVelocityWorldDegreesPerSecond,
            input.AimInputActive);
        var aimAngularSpeed = aimAngularVelocity.Length();
        if (!IsFinite(aimAngularSpeed))
        {
            aimAngularVelocity = Vector3.Zero;
            aimAngularSpeed = 0f;
        }

        if (input.AimInputActive &&
            aimAngularSpeed >= AimMotionThresholdDegreesPerSecond &&
            TryNormalize(aimAngularVelocity, out var commandAxis))
        {
            state.LastAimTurnAxis = commandAxis;
        }

        if (input.AimInputActive)
        {
            state.AimQuietSeconds = 0f;
        }
        else
        {
            state.AimQuietSeconds = Math.Clamp(
                state.AimQuietSeconds + deltaTime,
                0f,
                10f);
        }

        var aimDirectionVelocity =
            Vector3.Cross(aimAngularVelocity, aimDirection);
        var aimDot = Math.Clamp(Vector3.Dot(forward, aimDirection), -1f, 1f);
        var aimErrorDegrees = MathF.Acos(aimDot) * RadiansToDegrees;
        UpdateRoute(
            ref state,
            forward,
            up,
            aimDirection,
            aimErrorDegrees);

        var desiredTangent = ResolveDesiredTangent(
            in state,
            forward,
            up,
            aimDirection,
            aimErrorDegrees);
        var horizonAvailable = ResolveLevelUp(
            ref state,
            forward,
            up,
            worldUp,
            deltaTime,
            out var levelUp);
        var currentBankDegrees = horizonAvailable
            ? -SignedAngleDegrees(
                levelUp,
                up,
                forward,
                state.RollTurnSign)
            : 0f;
        var levelRight = horizonAvailable
            ? NormalizeOrFallback(
                Vector3.Cross(levelUp, forward),
                right)
            : right;
        var forwardComponent = Vector3.Dot(aimDirection, forward);
        var upComponent = Vector3.Dot(aimDirection, up);
        var rightComponent = Vector3.Dot(aimDirection, right);
        var horizonRightComponent =
            Vector3.Dot(aimDirection, levelRight);
        var horizonForwardComponent =
            MathF.Max(0.0001f, forwardComponent);
        var horizonLateralOffsetDegrees = MathF.Atan2(
                                                 horizonRightComponent,
                                                 horizonForwardComponent) *
                                             RadiansToDegrees;
        var bodyPitchOffsetDegrees = MathF.Atan2(
                                         upComponent,
                                         MathF.Max(
                                             0.0001f,
                                             MathF.Sqrt(
                                                 forwardComponent * forwardComponent +
                                                 rightComponent * rightComponent))) *
                                     RadiansToDegrees;
        var bodyLateralOffsetDegrees = MathF.Atan2(
                                           rightComponent,
                                           MathF.Max(0.0001f, forwardComponent)) *
                                       RadiansToDegrees;
        var aimCaptured =
            aimErrorDegrees <= AimCaptureAngleDegrees &&
            state.AimQuietSeconds >= AimCaptureQuietSeconds;
        var weakPushEligible =
            !aimCaptured &&
            !state.RouteLatched &&
            aimErrorDegrees <= WeakPushMaximumAimAngleDegrees &&
            bodyPitchOffsetDegrees < -0.20f &&
            bodyPitchOffsetDegrees >= -WeakPushMaximumDownAngleDegrees;
        var weakPushLateralWeight = 1f - SmoothStep(
            WeakPushFullLateralAngleDegrees,
            WeakPushZeroLateralAngleDegrees,
            MathF.Abs(bodyLateralOffsetDegrees));
        var bodyDownBankToManeuverWeight = SmoothStep(
            WeakPushFullBankDegrees,
            WeakPushZeroBankDegrees,
            MathF.Abs(currentBankDegrees));
        var weakPushBankWeight = 1f - bodyDownBankToManeuverWeight;
        // Every forward-hemisphere body-down command needs elevator while
        // aileron acquires its maneuver plane; otherwise a diagonal command
        // can produce full roll with exactly zero pitch. Only the separate
        // straight-push weight suppresses roll near the centerline. As bank
        // develops, bodyPitchOffsetDegrees naturally approaches zero and then
        // changes to the positive-G pull required by the banked turn.
        var bodyDownPitchWeight = weakPushEligible ? 1f : 0f;
        var straightPushWeight =
            bodyDownPitchWeight * weakPushLateralWeight * weakPushBankWeight;
        var weakPushActive = bodyDownPitchWeight > 0.0001f;
        var lateralBankWeight = SmoothStep(
            FineYawFullAngleDegrees,
            FineYawZeroAngleDegrees,
            MathF.Abs(horizonLateralOffsetDegrees));
        var lateralAimRateDegreesPerSecond =
            HorizonLateralOffsetRateDegreesPerSecond(
                aimDirectionVelocity,
                forward,
                levelRight,
                forwardComponent,
                horizonForwardComponent,
                horizonRightComponent);
        var movingAimBankWeight = SmoothStep(
            MovingAimBankStartDegreesPerSecond,
            MovingAimBankFullDegreesPerSecond,
            MathF.Abs(lateralAimRateDegreesPerSecond));
        var invertedLevelWeight = horizonAvailable
            ? SmoothStep(
                InvertedLevelSuppressionStart,
                InvertedLevelSuppressionFull,
                -Vector3.Dot(up, levelUp))
            : 0f;
        var invertedPullIntent =
            SmoothStep(
                InvertedPullIntentStartDegrees,
                InvertedPullIntentFullDegrees,
                MathF.Max(0f, bodyPitchOffsetDegrees)) *
            SmoothStep(
                InvertedPullAlignmentStart,
                InvertedPullAlignmentFull,
                Vector3.Dot(up, desiredTangent));
        var invertedPullPreservation =
            invertedLevelWeight * invertedPullIntent;
        aimCaptured &= invertedPullPreservation <= 0.0001f;

        Vector3 desiredLift;
        if (aimCaptured)
        {
            desiredLift = horizonAvailable ? levelUp : up;
        }
        else
        {
            // The projected command ray is one continuous maneuver plane.
            // Near-level turns and straight-ahead pushes blend away from it
            // below, but aircraft attitude must never select a different
            // plane: doing so made a one-degree bank change jump between a
            // capped upright bank and an inverted descending bank.
            desiredLift = desiredTangent;
        }

        if (weakPushEligible)
        {
            var pushLift = horizonAvailable ? levelUp : up;
            var pushBlendDegrees = SignedAngleDegrees(
                desiredLift,
                pushLift,
                forward,
                -state.RollTurnSign);
            desiredLift = RotateAroundAxis(
                desiredLift,
                forward,
                pushBlendDegrees *
                straightPushWeight *
                DegreesToRadians);
        }

        var levelTurnWeight = 0f;
        var levelTurnLift = desiredLift;
        if (!state.RouteLatched && horizonAvailable)
        {
            var horizonPitchErrorDegrees =
                MathF.Asin(
                    Math.Clamp(
                        Vector3.Dot(aimDirection, levelUp),
                        -1f,
                        1f)) *
                RadiansToDegrees;
            levelTurnWeight = 1f - SmoothStep(
                LevelTurnFullWeightPitchDegrees,
                LevelTurnZeroWeightPitchDegrees,
                MathF.Abs(horizonPitchErrorDegrees));
            var fineLateralLevelWeight = 1f - SmoothStep(
                FineYawFullAngleDegrees,
                FineLateralLevelReleaseAngleDegrees,
                MathF.Abs(horizonLateralOffsetDegrees));
            fineLateralLevelWeight *= 1f - SmoothStep(
                FineLateralLevelFullPitchDegrees,
                FineLateralLevelReleasePitchDegrees,
                MathF.Abs(horizonPitchErrorDegrees));
            var rollLevelTurnWeight = MathF.Max(
                levelTurnWeight,
                fineLateralLevelWeight);

            var positionBankDegrees =
                -MathF.Sign(horizonLateralOffsetDegrees) *
                MaximumPursuitBankDegrees *
                lateralBankWeight;
            var levelTurnBankDegrees = positionBankDegrees;
            var movingAimBankSign =
                -MathF.Sign(lateralAimRateDegreesPerSecond);
            if (movingAimBankWeight > 0f && movingAimBankSign != 0f)
            {
                // Cursor velocity is context, not an independent full-bank
                // command. Motion into the turn may preserve bank already on
                // the aircraft; motion back toward the nose may only roll that
                // positional target toward level. It cannot cross-bank before
                // the command ray itself crosses the nose.
                var positionBankSign = MathF.Sign(positionBankDegrees);
                var currentLevelTurnBankDegrees = -currentBankDegrees;
                if (positionBankSign == 0f ||
                    positionBankSign == movingAimBankSign)
                {
                    var existingBankMagnitude =
                        MathF.Sign(currentLevelTurnBankDegrees) ==
                        movingAimBankSign
                            ? MathF.Min(
                                MaximumPursuitBankDegrees,
                                MathF.Abs(currentLevelTurnBankDegrees))
                            : 0f;
                    var sustainedBankMagnitude = MathF.Max(
                        MathF.Abs(positionBankDegrees),
                        existingBankMagnitude);
                    var sustainedBankDegrees =
                        movingAimBankSign * sustainedBankMagnitude;
                    levelTurnBankDegrees = positionBankDegrees +
                        (sustainedBankDegrees - positionBankDegrees) *
                        movingAimBankWeight;
                }
                else
                {
                    levelTurnBankDegrees =
                        positionBankDegrees * (1f - movingAimBankWeight);
                }
            }

            levelTurnBankDegrees = Math.Clamp(
                levelTurnBankDegrees,
                -MaximumPursuitBankDegrees,
                MaximumPursuitBankDegrees);
            levelTurnLift = RotateAroundAxis(
                levelUp,
                forward,
                levelTurnBankDegrees * DegreesToRadians);

            // A body-up command while meaningfully inverted is a deliberate
            // positive-G pull. World-up roll leveling must not replace that
            // maneuver plane merely because its world pitch is near level.
            rollLevelTurnWeight *=
                1f - invertedPullPreservation;

            var rollLevelBlendDegrees = SignedAngleDegrees(
                desiredLift,
                levelTurnLift,
                forward,
                -state.RollTurnSign);
            desiredLift = RotateAroundAxis(
                desiredLift,
                forward,
                rollLevelBlendDegrees *
                rollLevelTurnWeight *
                DegreesToRadians);
        }

        if (!TryNormalize(
                ProjectOnPlane(desiredLift, forward),
                out desiredLift))
        {
            desiredLift = up;
        }

        var rollErrorDegrees = -SignedAngleDegrees(
            up,
            desiredLift,
            forward,
            -state.RollTurnSign);
        if (MathF.Abs(rollErrorDegrees) > 1f &&
            MathF.Abs(rollErrorDegrees) < 175f)
        {
            state.RollTurnSign = rollErrorDegrees >= 0f ? 1 : -1;
        }

        var desiredRollRate = CommandedRateForError(
            rollErrorDegrees,
            limits.MaximumRollRateDegreesPerSecond,
            RollPositionGain,
            RollBrakingAcceleration);
        var rollControl = RateControl(
            desiredRollRate,
            FiniteOrZero(input.RollRateDegreesPerSecond),
            limits.RollRateErrorForFullControl);

        var desiredPitchRate = 0f;
        float pitchControl;
        if (aimCaptured)
        {
            // Capture tracks only the held body-relative ray and brakes the
            // remaining pitch rate. It has no world-elevation target, so a
            // steady climb with no pitch rate receives no leveling command.
            desiredPitchRate = CommandedRateForError(
                bodyPitchOffsetDegrees,
                limits.MaximumPitchRateDegreesPerSecond,
                PitchPositionGain,
                PitchBrakingAcceleration);
            pitchControl = Math.Clamp(
                RateControl(
                    desiredPitchRate,
                    FiniteOrZero(input.PitchRateDegreesPerSecond),
                    limits.PitchRateErrorForFullControl),
                -MaximumCapturePitchBrakeControl,
                1f);
        }
        else
        {
            var maneuverErrorDegrees = aimErrorDegrees;
            if (state.RouteLatched &&
                MathF.Abs(state.UnwrappedRouteErrorDegrees) >
                maneuverErrorDegrees)
            {
                maneuverErrorDegrees = MathF.Min(
                    180f,
                    MathF.Abs(state.UnwrappedRouteErrorDegrees));
            }

            var pathProjection = Math.Clamp(
                Vector3.Dot(desiredLift, desiredTangent),
                0f,
                1f);
            var geometricPullErrorDegrees =
                maneuverErrorDegrees * pathProjection;
            var bodyPullErrorDegrees = MathF.Min(
                MathF.Max(0f, bodyPitchOffsetDegrees),
                geometricPullErrorDegrees);
            if (state.RouteLatched)
            {
                // A rear-hemisphere route needs its unwrapped geometric
                // error because the target may not yet have a unique body
                // pitch sign. Ordinary forward pursuit does: its positive
                // body-pitch error is exactly the elevator turn component.
                var liftAlignment = SmoothStep(
                    LiftAlignmentStart,
                    LiftAlignmentFull,
                    Vector3.Dot(up, desiredLift));
                desiredPitchRate = CommandedRateForError(
                                       geometricPullErrorDegrees,
                                       limits.MaximumPitchRateDegreesPerSecond,
                                       PitchPositionGain,
                                       PitchBrakingAcceleration) *
                                   liftAlignment;
            }
            else
            {
                desiredPitchRate = CommandedRateForError(
                    bodyPullErrorDegrees,
                    limits.MaximumPitchRateDegreesPerSecond,
                    PitchPositionGain,
                    PitchBrakingAcceleration);
            }
            // A level lateral command used to request exactly zero elevator
            // until aileron had already established enough bank. Because the
            // native roll and pitch surfaces each slew independently, that
            // became a conspicuous roll-then-pull sequence. Prime a small
            // positive-G rate while an upright aircraft acquires the commanded
            // bank, then let the body-pitch pursuit take over. The
            // bounded fraction cannot change the maximum pitch rate, is absent
            // while inverted or near the horizon poles, and remains subject to
            // the same rate feedback and angle-of-attack protection below.
            var bankAcquisitionUprightWeight = horizonAvailable
                ? Math.Clamp(Vector3.Dot(up, levelUp), 0f, 1f)
                : 0f;
            var bankAcquisitionPitchRate =
                CommandedRateForError(
                    geometricPullErrorDegrees,
                    limits.MaximumPitchRateDegreesPerSecond,
                    PitchPositionGain,
                    PitchBrakingAcceleration) *
                BankAcquisitionPitchRateFraction *
                levelTurnWeight *
                bankAcquisitionUprightWeight;
            desiredPitchRate = MathF.Max(
                desiredPitchRate,
                bankAcquisitionPitchRate);
            var aimPullFeedForward =
                Vector3.Dot(
                    aimDirectionVelocity,
                    state.RouteLatched ? desiredLift : up);
            var levelTurnPullFeedForward = MathF.Min(
                MathF.Max(
                    0f,
                    Vector3.Dot(aimDirectionVelocity, up)),
                MathF.Max(
                    0f,
                    Vector3.Dot(
                        aimDirectionVelocity,
                        levelTurnLift)));
            var levelTurnFeedForwardWeight =
                levelTurnWeight * (1f - movingAimBankWeight);
            aimPullFeedForward +=
                (levelTurnPullFeedForward - aimPullFeedForward) *
                levelTurnFeedForwardWeight;
            if (aimPullFeedForward > 0f)
            {
                var feedForwardAlignment = state.RouteLatched
                    ? SmoothStep(
                        LiftAlignmentStart,
                        LiftAlignmentFull,
                        Vector3.Dot(up, desiredLift))
                    : 1f;
                desiredPitchRate +=
                    aimPullFeedForward * feedForwardAlignment;
            }

            desiredPitchRate = Math.Clamp(
                desiredPitchRate,
                0f,
                limits.MaximumPitchRateDegreesPerSecond);
            var pitchBrakeWeight = state.RouteLatched
                ? 0f
                : 1f - SmoothStep(
                    PitchBrakeFullAimAngleDegrees,
                    PitchBrakeReleaseAimAngleDegrees,
                    aimErrorDegrees);
            // The ordinary positive-G route still never chooses a broad
            // negative-G shortcut. Close to capture only, bounded
            // counter-elevator may arrest an existing positive pitch rate.
            pitchControl = Math.Clamp(
                 RateControl(
                     desiredPitchRate,
                     FiniteOrZero(input.PitchRateDegreesPerSecond),
                     limits.PitchRateErrorForFullControl),
                 -MaximumCapturePitchBrakeControl * pitchBrakeWeight,
                 1f);

            if (bodyDownPitchWeight > 0f)
            {
                var maximumPushRate =
                    limits.MaximumPitchRateDegreesPerSecond *
                    WeakPushRateFraction;
                var pushPitchRate = CommandedRateForError(
                    bodyPitchOffsetDegrees,
                    maximumPushRate,
                    PitchPositionGain,
                    PitchBrakingAcceleration);
                pushPitchRate += Math.Clamp(
                    Vector3.Dot(aimDirectionVelocity, up),
                    -maximumPushRate,
                    maximumPushRate);
                pushPitchRate = Math.Clamp(
                    pushPitchRate,
                    -maximumPushRate,
                    limits.MaximumPitchRateDegreesPerSecond);
                var pushPitchControl = Math.Clamp(
                    RateControl(
                        pushPitchRate,
                        FiniteOrZero(input.PitchRateDegreesPerSecond),
                        limits.PitchRateErrorForFullControl),
                    -WeakPushMaximumControl,
                    1f);
                desiredPitchRate +=
                    (pushPitchRate - desiredPitchRate) *
                    bodyDownPitchWeight;
                pitchControl +=
                    (pushPitchControl - pitchControl) *
                    bodyDownPitchWeight;
            }
        }

        pitchControl = ApplyPositiveAngleOfAttackProtection(
            pitchControl,
            input.AngleOfAttackDegrees,
            input.CriticalAngleOfAttackDegrees);

        var fineYawWeight = 1f - lateralBankWeight;
        var desiredFineYawRate = CommandedRateForError(
                                     bodyLateralOffsetDegrees,
                                     limits.MaximumYawRateDegreesPerSecond,
                                     YawPositionGain,
                                     YawBrakingAcceleration) *
                                 fineYawWeight;
        desiredFineYawRate +=
            Vector3.Dot(aimDirectionVelocity, right) *
            fineYawWeight;
        // Positive beta means the velocity vector is right of the nose, so the
        // stable correction is positive/right yaw, not the opposite sign.
        desiredFineYawRate +=
            FiniteOrZero(input.SideslipDegrees) *
            SideslipYawRateGain;
        desiredFineYawRate = Math.Clamp(
            desiredFineYawRate,
            -limits.MaximumYawRateDegreesPerSecond,
            limits.MaximumYawRateDegreesPerSecond);
        var yawControl = Math.Clamp(
            RateControl(
                desiredFineYawRate,
                FiniteOrZero(input.YawRateDegreesPerSecond),
                limits.YawRateErrorForFullControl),
            -MaximumRudderControl,
            MaximumRudderControl);

        state.PreviousAimDirection = aimDirection;
        state.PreviousAircraftForward = forward;

        pitchControl = BoundedFinite(pitchControl);
        rollControl = BoundedFinite(rollControl);
        yawControl = BoundedFinite(yawControl);
        return new AircraftMouseInstructorOutput(
            Pitch: pitchControl,
            Roll: rollControl,
            Yaw: yawControl,
            AimErrorDegrees: FiniteOrZero(aimErrorDegrees),
            RollErrorDegrees: FiniteOrZero(rollErrorDegrees),
            DesiredPitchRateDegreesPerSecond:
                FiniteOrZero(desiredPitchRate),
            DesiredRollRateDegreesPerSecond:
                FiniteOrZero(desiredRollRate),
            DesiredYawRateDegreesPerSecond:
                FiniteOrZero(desiredFineYawRate),
            DesiredLiftDirectionWorld: desiredLift,
            AimCaptured: aimCaptured,
            WeakPushActive: weakPushActive,
            RouteLatched: state.RouteLatched);
    }

    private static void InitializeState(
        ref AircraftMouseInstructorState state,
        Vector3 forward,
        Vector3 up,
        Vector3 worldUp,
        Vector3 aimDirection)
    {
        state.Initialized = true;
        state.RouteLatched = false;
        state.PreviousAimDirection = aimDirection;
        state.PreviousAircraftForward = forward;
        state.AimQuietSeconds = 0f;
        state.RollTurnSign = 1;

        var initialTurnAxis = Vector3.Cross(forward, aimDirection);
        if (!TryNormalize(initialTurnAxis, out state.LastAimTurnAxis))
        {
            state.LastAimTurnAxis = NormalizeOrFallback(
                Vector3.Cross(forward, up),
                -Vector3.UnitX);
        }

        var level = ProjectOnPlane(worldUp, forward);
        state.PoleSafeLevelUp = NormalizeOrFallback(level, up);
    }

    private static Vector3 ResolveAimAngularVelocity(
        Vector3 aimDirection,
        Vector3 suppliedAngularVelocity,
        bool aimInputActive)
    {
        if (!aimInputActive || !IsFinite(suppliedAngularVelocity))
            return Vector3.Zero;

        // Rotation about the sightline cannot move the aim direction. Removing
        // it prevents camera roll from becoming a false maneuver-plane command.
        var directionRate = ProjectOnPlane(
            suppliedAngularVelocity,
            aimDirection);
        return LimitMagnitude(directionRate, 720f);
    }

    private static float HorizonLateralOffsetRateDegreesPerSecond(
        Vector3 aimDirectionVelocity,
        Vector3 forward,
        Vector3 levelRight,
        float unclampedForwardComponent,
        float forwardComponent,
        float rightComponent)
    {
        var rightRate = Vector3.Dot(
            aimDirectionVelocity,
            levelRight);
        var forwardRate = unclampedForwardComponent > 0.0001f
            ? Vector3.Dot(aimDirectionVelocity, forward)
            : 0f;
        var denominator =
            forwardComponent * forwardComponent +
            rightComponent * rightComponent;
        if (!IsFinite(denominator) ||
            denominator <= VectorEpsilonSquared)
        {
            return FiniteOrZero(rightRate);
        }

        // d/dt atan2(right, forward). Unlike the old right-axis projection,
        // this remains the actual horizontal command rate when the circle is
        // far from the nose, so reversing a moving lead does not keep a stale
        // full-bank target until it is nearly centered.
        return FiniteOrZero(
            (forwardComponent * rightRate -
             rightComponent * forwardRate) /
            denominator);
    }

    private static void UpdateRoute(
        ref AircraftMouseInstructorState state,
        Vector3 forward,
        Vector3 up,
        Vector3 aimDirection,
        float aimErrorDegrees)
    {
        if (!state.RouteLatched &&
            aimErrorDegrees >= RouteLatchAngleDegrees)
        {
            var routeAxis = state.LastAimTurnAxis;
            if (!TryNormalize(routeAxis, out routeAxis))
            {
                routeAxis = Vector3.Cross(forward, aimDirection);
            }

            if (!TryNormalize(routeAxis, out routeAxis))
            {
                routeAxis = Vector3.Cross(forward, up);
            }

            state.RouteAxis = NormalizeOrFallback(
                routeAxis,
                -Vector3.UnitX);
            state.RouteLatched = true;
            state.UnwrappedRouteErrorDegrees =
                SignedRouteErrorDegrees(
                    forward,
                    aimDirection,
                    state.RouteAxis,
                    180f);
        }
        else if (state.RouteLatched)
        {
            var wrappedError = SignedRouteErrorDegrees(
                forward,
                aimDirection,
                state.RouteAxis,
                state.UnwrappedRouteErrorDegrees);
            state.UnwrappedRouteErrorDegrees = UnwrapDegrees(
                state.UnwrappedRouteErrorDegrees,
                wrappedError);
        }

        if (state.RouteLatched &&
            aimErrorDegrees <= RouteReleaseAngleDegrees &&
            MathF.Abs(state.UnwrappedRouteErrorDegrees) <=
            RouteReleaseErrorDegrees)
        {
            state.RouteLatched = false;
            state.UnwrappedRouteErrorDegrees = 0f;
        }
    }

    private static Vector3 ResolveDesiredTangent(
        in AircraftMouseInstructorState state,
        Vector3 forward,
        Vector3 up,
        Vector3 aimDirection,
        float aimErrorDegrees)
    {
        var shortest = ProjectOnPlane(aimDirection, forward);
        var hasShortest = TryNormalize(shortest, out shortest);
        if (!state.RouteLatched)
            return hasShortest ? shortest : up;

        var routeSign =
            state.UnwrappedRouteErrorDegrees < 0f ? -1f : 1f;
        var routeTangent =
            Vector3.Cross(state.RouteAxis, forward) * routeSign;
        if (!TryNormalize(routeTangent, out routeTangent))
            return hasShortest ? shortest : up;
        if (!hasShortest)
            return routeTangent;

        var branchError =
            MathF.Abs(state.UnwrappedRouteErrorDegrees);
        if (branchError >= 179.5f ||
            Vector3.Dot(shortest, routeTangent) < -0.10f)
        {
            return routeTangent;
        }

        var routeWeight = SmoothStep(
            RouteBlendStartDegrees,
            RouteBlendEndDegrees,
            aimErrorDegrees);
        return NormalizeOrFallback(
            Vector3.Lerp(shortest, routeTangent, routeWeight),
            routeTangent);
    }

    private static bool ResolveLevelUp(
        ref AircraftMouseInstructorState state,
        Vector3 forward,
        Vector3 aircraftUp,
        Vector3 worldUp,
        float deltaTime,
        out Vector3 levelUp)
    {
        var transported = NormalizeOrFallback(
            ProjectOnPlane(state.PoleSafeLevelUp, forward),
            aircraftUp);
        var horizon = ProjectOnPlane(worldUp, forward);
        var availability = horizon.Length();
        if (IsFinite(availability) &&
            availability >= HorizonAvailabilityThreshold &&
            TryNormalize(horizon, out var rawLevelUp))
        {
            // The raw world-up projection changes branch at zenith/nadir.
            // Reacquire it at a bounded angular rate from the transported
            // branch so the roll target remains continuous, then converges to
            // the genuinely upright horizon reference.
            var reacquireError = SignedAngleDegrees(
                transported,
                rawLevelUp,
                forward,
                state.RollTurnSign);
            var correction = Math.Clamp(
                reacquireError,
                -HorizonReacquireRateDegreesPerSecond * deltaTime,
                HorizonReacquireRateDegreesPerSecond * deltaTime);
            levelUp = NormalizeOrFallback(
                RotateAroundAxis(
                    transported,
                    forward,
                    correction * DegreesToRadians),
                transported);
            state.PoleSafeLevelUp = levelUp;
            return true;
        }

        levelUp = transported;
        state.PoleSafeLevelUp = levelUp;
        return false;
    }

    private static float ApplyPositiveAngleOfAttackProtection(
        float pitchControl,
        float angleOfAttackDegrees,
        float criticalAngleOfAttackDegrees)
    {
        if (pitchControl <= 0f ||
            !IsFinite(angleOfAttackDegrees) ||
            !IsFinite(criticalAngleOfAttackDegrees) ||
            angleOfAttackDegrees <= 0f ||
            criticalAngleOfAttackDegrees <= 1f)
        {
            return pitchControl;
        }

        var remaining = 1f - SmoothStep(
            criticalAngleOfAttackDegrees *
            AlphaProtectionStartFraction,
            criticalAngleOfAttackDegrees *
            AlphaProtectionEndFraction,
            angleOfAttackDegrees);
        return pitchControl * remaining;
    }

    private static float CommandedRateForError(
        float errorDegrees,
        float maximumRateDegreesPerSecond,
        float positionGain,
        float brakingAccelerationDegreesPerSecondSquared)
    {
        if (!IsFinite(errorDegrees) ||
            MathF.Abs(errorDegrees) <= 0.0001f)
        {
            return 0f;
        }

        var magnitude = MathF.Abs(errorDegrees);
        var proportionalRate = magnitude * positionGain;
        var brakingRate = MathF.Sqrt(
            2f *
            MathF.Max(0.001f, brakingAccelerationDegreesPerSecondSquared) *
            magnitude);
        var rate = MathF.Min(
            MathF.Max(0f, maximumRateDegreesPerSecond),
            MathF.Min(proportionalRate, brakingRate));
        return errorDegrees < 0f ? -rate : rate;
    }

    private static float RateControl(
        float desiredRate,
        float currentRate,
        float rateErrorForFullControl)
        => Math.Clamp(
            (desiredRate - currentRate) /
            MathF.Max(0.001f, rateErrorForFullControl),
            -1f,
            1f);

    private static float SignedRouteErrorDegrees(
        Vector3 from,
        Vector3 to,
        Vector3 axis,
        float previousErrorDegrees)
    {
        var projectedFrom = ProjectOnPlane(from, axis);
        var projectedTo = ProjectOnPlane(to, axis);
        if (!TryNormalize(projectedFrom, out projectedFrom) ||
            !TryNormalize(projectedTo, out projectedTo))
        {
            return WrapDegrees(previousErrorDegrees);
        }

        var sine = Vector3.Dot(
            axis,
            Vector3.Cross(projectedFrom, projectedTo));
        var cosine = Math.Clamp(
            Vector3.Dot(projectedFrom, projectedTo),
            -1f,
            1f);
        if (MathF.Abs(sine) <= 0.00001f && cosine < 0f)
        {
            return previousErrorDegrees < 0f ? -180f : 180f;
        }

        return MathF.Atan2(sine, cosine) * RadiansToDegrees;
    }

    private static float SignedAngleDegrees(
        Vector3 from,
        Vector3 to,
        Vector3 axis,
        int fallbackSign)
    {
        var projectedFrom = ProjectOnPlane(from, axis);
        var projectedTo = ProjectOnPlane(to, axis);
        if (!TryNormalize(projectedFrom, out projectedFrom) ||
            !TryNormalize(projectedTo, out projectedTo))
        {
            return 0f;
        }

        var sine = Vector3.Dot(
            axis,
            Vector3.Cross(projectedFrom, projectedTo));
        var cosine = Math.Clamp(
            Vector3.Dot(projectedFrom, projectedTo),
            -1f,
            1f);
        if (MathF.Abs(sine) <= 0.00001f && cosine < 0f)
            return fallbackSign < 0 ? -180f : 180f;

        return MathF.Atan2(sine, cosine) * RadiansToDegrees;
    }

    private static float UnwrapDegrees(
        float previousUnwrapped,
        float newWrapped)
        => previousUnwrapped +
           WrapDegrees(newWrapped - WrapDegrees(previousUnwrapped));

    private static float WrapDegrees(float degrees)
    {
        if (!IsFinite(degrees))
            return 0f;

        degrees %= 360f;
        if (degrees <= -180f)
            degrees += 360f;
        else if (degrees > 180f)
            degrees -= 360f;
        return degrees;
    }

    private static Vector3 RotateAroundAxis(
        Vector3 value,
        Vector3 axis,
        float radians)
    {
        axis = NormalizeOrFallback(axis, Vector3.UnitZ);
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        return value * cosine +
               Vector3.Cross(axis, value) * sine +
               axis * Vector3.Dot(axis, value) * (1f - cosine);
    }

    private static Vector3 ProjectOnPlane(Vector3 value, Vector3 normal)
        => value - normal * Vector3.Dot(value, normal);

    private static bool TryBuildAircraftFrame(
        Vector3 suppliedForward,
        Vector3 suppliedUp,
        Vector3 suppliedRight,
        out Vector3 forward,
        out Vector3 up,
        out Vector3 right)
    {
        forward = Vector3.UnitZ;
        up = Vector3.UnitY;
        right = Vector3.UnitX;
        if (!TryNormalize(suppliedForward, out forward))
            return false;

        var projectedUp = ProjectOnPlane(suppliedUp, forward);
        if (!TryNormalize(projectedUp, out up))
        {
            var projectedRight =
                ProjectOnPlane(suppliedRight, forward);
            if (!TryNormalize(projectedRight, out right))
                return false;
            up = NormalizeOrFallback(
                Vector3.Cross(forward, right),
                Vector3.UnitY);
        }

        right = NormalizeOrFallback(
            Vector3.Cross(up, forward),
            suppliedRight);
        up = NormalizeOrFallback(
            Vector3.Cross(forward, right),
            up);
        return IsFinite(forward) && IsFinite(up) && IsFinite(right);
    }

    private static bool TryNormalize(
        Vector3 value,
        out Vector3 normalized)
    {
        normalized = Vector3.Zero;
        if (!IsFinite(value))
            return false;

        var lengthSquared = value.LengthSquared();
        if (!IsFinite(lengthSquared) ||
            lengthSquared <= VectorEpsilonSquared)
        {
            return false;
        }

        normalized = value / MathF.Sqrt(lengthSquared);
        return IsFinite(normalized);
    }

    private static Vector3 NormalizeOrFallback(
        Vector3 value,
        Vector3 fallback)
    {
        if (TryNormalize(value, out var normalized))
            return normalized;
        if (TryNormalize(fallback, out normalized))
            return normalized;
        return Vector3.UnitY;
    }

    private static Vector3 LimitMagnitude(
        Vector3 value,
        float maximumMagnitude)
    {
        if (!IsFinite(value))
            return Vector3.Zero;

        var lengthSquared = value.LengthSquared();
        var maximumSquared = maximumMagnitude * maximumMagnitude;
        if (!IsFinite(lengthSquared) || lengthSquared <= maximumSquared)
            return value;
        if (lengthSquared <= VectorEpsilonSquared)
            return Vector3.Zero;
        return value *
               (maximumMagnitude / MathF.Sqrt(lengthSquared));
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        if (!IsFinite(value))
            return 0f;
        if (edge1 <= edge0)
            return value >= edge1 ? 1f : 0f;

        var t = Math.Clamp(
            (value - edge0) / (edge1 - edge0),
            0f,
            1f);
        return t * t * (3f - 2f * t);
    }

    private static float SanitizeDeltaTime(float deltaTime)
        => IsFinite(deltaTime)
            ? Math.Clamp(deltaTime, 0.001f, 0.10f)
            : 1f / 60f;

    private static float BoundedFinite(float value)
        => IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0f;

    private static float FiniteOrZero(float value)
        => IsFinite(value) ? value : 0f;

    internal static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static bool IsFinite(Vector3 value)
        => IsFinite(value.X) &&
           IsFinite(value.Y) &&
           IsFinite(value.Z);
}
