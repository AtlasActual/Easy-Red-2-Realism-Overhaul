using System.Numerics;
using System.Runtime.CompilerServices;
using ER2RealismOverhaul;

internal static class AircraftAerodynamicsScenarios
{
    [ModuleInitializer]
    internal static void RunAll()
    {
        var tests = new (string Name, Action Run)[]
        {
            (nameof(MultiplayerFlightRequiresExactLocalNetworkOwnership),
                MultiplayerFlightRequiresExactLocalNetworkOwnership),
            (nameof(ExperimentalAiFlightIsOptInAndHostAuthoritative),
                ExperimentalAiFlightIsOptInAndHostAuthoritative),
            (nameof(LandingGearInterlockRequiresSafeSpeedOnlyForOwnedAirbornePlane),
                LandingGearInterlockRequiresSafeSpeedOnlyForOwnedAirbornePlane),
            (nameof(AerodynamicVectorsStayFinitePerpendicularAndPassive),
                AerodynamicVectorsStayFinitePerpendicularAndPassive),
            (nameof(ZeroSpeedProducesNoAerodynamicOrEngineForce),
                ZeroSpeedProducesNoAerodynamicOrEngineForce),
            (nameof(LowSpeedAlignedFlowIsNotAStall),
                LowSpeedAlignedFlowIsNotAStall),
            (nameof(StallSeparationAndCoefficientsAreContinuous),
                StallSeparationAndCoefficientsAreContinuous),
            (nameof(TailslidePitchStabilityIsContinuousAndBounded),
                TailslidePitchStabilityIsContinuousAndBounded),
            (nameof(ControlAuthorityUsesFreestreamAndBoundedPropwash),
                ControlAuthorityUsesFreestreamAndBoundedPropwash),
            (nameof(EngineRatingHasUsefulRangeWithoutRocketThrust),
                EngineRatingHasUsefulRangeWithoutRocketThrust),
            (nameof(AerodynamicDragUsesEnergyRetainingAirborneBaseline),
                AerodynamicDragUsesEnergyRetainingAirborneBaseline),
            (nameof(NativeControlSchedulePreservesImmediateCorrectionsAndLimitsRudderTravel),
                NativeControlSchedulePreservesImmediateCorrectionsAndLimitsRudderTravel),
            (nameof(NativeRollGainChangesPhysicalRateAfterSurfaceFiltering),
                NativeRollGainChangesPhysicalRateAfterSurfaceFiltering),
            (nameof(NativePitchAuthorityPreservesNoseDownAsymmetry),
                NativePitchAuthorityPreservesNoseDownAsymmetry),
            (nameof(NativeThrustMultiplierPreservesAuthoredPowerAndCapsRocketThrust),
                NativeThrustMultiplierPreservesAuthoredPowerAndCapsRocketThrust),
            (nameof(NativeRecoveryVerticalPowerCapsReachFiniteApexAtRatingsFourAndTen),
                NativeRecoveryVerticalPowerCapsReachFiniteApexAtRatingsFourAndTen),
            (nameof(FlightPathTurnRateMeasuresCurvatureWithoutAxialFalsePositives),
                FlightPathTurnRateMeasuresCurvatureWithoutAxialFalsePositives),
            (nameof(NativeVelocityDirectionPreservationIsStrictlyScoped),
                NativeVelocityDirectionPreservationIsStrictlyScoped),
            (nameof(NativeRecoveryCoastAndDiveRetainGravityDrivenEnergy),
                NativeRecoveryCoastAndDiveRetainGravityDrivenEnergy),
            (nameof(ManeuverDragIsQuadraticBoundedAndLeavesStraightFlightAlone),
                ManeuverDragIsQuadraticBoundedAndLeavesStraightFlightAlone),
            (nameof(MaximumRatingHardTurnBurnsEnergyWhileStraightFlightAccelerates),
                MaximumRatingHardTurnBurnsEnergyWhileStraightFlightAccelerates),
            (nameof(PowerLimitedThrustUsesOnlyPositiveAxialSpeed),
                PowerLimitedThrustUsesOnlyPositiveAxialSpeed),
            (nameof(EngineSpoolIsMonotonicAsymmetricAndStepStable),
                EngineSpoolIsMonotonicAsymmetricAndStepStable),
            (nameof(VerticalClimbReachesFiniteApexThenDescends),
                VerticalClimbReachesFiniteApexThenDescends),
            (nameof(PowerOffCoastRetainsPlausibleFlightEnergy),
                PowerOffCoastRetainsPlausibleFlightEnergy),
            (nameof(FlightIntegrationIsStableAtFiftyAndOneHundredHertz),
                FlightIntegrationIsStableAtFiftyAndOneHundredHertz),
            (nameof(DirectAndMouseCommandsShareIdenticalPhysics),
                DirectAndMouseCommandsShareIdenticalPhysics),
            (nameof(TwoHalfWingsCreateDifferentialRollAndPassiveRollDamping),
                TwoHalfWingsCreateDifferentialRollAndPassiveRollDamping)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Run();
                Console.WriteLine($"PASS AIR {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL AIR {test.Name}: {exception.Message}");
            }
        }

        if (failures > 0)
        {
            throw new InvalidOperationException(
                $"{tests.Length - failures}/{tests.Length} aircraft scenarios passed.");
        }

        Console.WriteLine($"{tests.Length}/{tests.Length} deterministic aircraft scenarios passed.");
    }

    private static void MultiplayerFlightRequiresExactLocalNetworkOwnership()
    {
        True(
            CanOwnFlight(),
            "The local human pilot should own physical flight offline.");
        False(
            CanOwnFlight(enabled: false),
            "The master aircraft switch should disable physical flight.");
        False(
            CanOwnFlight(isLocallyControlledVehicle: false),
            "A remote aircraft must never receive local physical flight.");
        False(
            CanOwnFlight(hasHumanDriver: false),
            "An AI aircraft must retain native flight.");
        False(
            CanOwnFlight(usesRealisticControls: false),
            "Simplified controls must retain native flight.");
        False(
            CanOwnFlight(multiplayerIntent: true),
            "Flight must fail closed while a multiplayer room is still joining.");
        False(
            CanOwnFlight(
                multiplayerIntent: true,
                inNetworkRoom: true),
            "A multiplayer client must not simulate an aircraft it does not own.");
        True(
            CanOwnFlight(
                multiplayerIntent: true,
                inNetworkRoom: true,
                ownsNetworkSynchronizer: true),
            "A multiplayer client should simulate its own network-owned aircraft.");
        True(
            CanOwnFlight(
                inNetworkRoom: true,
                ownsNetworkSynchronizer: true),
            "The active network room must require local ownership even if match intent is late.");
    }

    private static void LandingGearInterlockRequiresSafeSpeedOnlyForOwnedAirbornePlane()
    {
        const float maximumSpeed = 150f;
        const float stallSpeed = 40f;
        var fighterLimit =
            AircraftAerodynamicsCore.LandingGearExtensionSpeedLimit(
                maximumSpeed,
                stallSpeed,
                isBomber: false);
        var bomberLimit =
            AircraftAerodynamicsCore.LandingGearExtensionSpeedLimit(
                maximumSpeed,
                stallSpeed,
                isBomber: true);

        Near(68f, fighterLimit, 0.0001f,
            "The fighter gear-extension limit no longer follows its stall-speed envelope.");
        Near(62f, bomberLimit, 0.0001f,
            "The bomber gear-extension limit no longer follows its stall-speed envelope.");
        True(
            AircraftAerodynamicsCore.LandingGearExtensionAllowed(
                true,
                false,
                fighterLimit,
                fighterLimit),
            "An airborne aircraft was blocked at its safe extension speed.");
        False(
            AircraftAerodynamicsCore.LandingGearExtensionAllowed(
                true,
                false,
                fighterLimit + 0.01f,
                fighterLimit),
            "Ground proximity alone was allowed to extend gear above its safe speed.");
        True(
            AircraftAerodynamicsCore.LandingGearExtensionAllowed(
                true,
                true,
                fighterLimit + 50f,
                fighterLimit),
            "A grounded aircraft could not use its landing gear.");
        True(
            AircraftAerodynamicsCore.LandingGearExtensionAllowed(
                false,
                false,
                fighterLimit + 50f,
                fighterLimit),
            "An aircraft outside local flight ownership did not retain native gear behavior.");
        True(
            AircraftAerodynamicsCore.LandingGearExtensionAllowed(
                true,
                false,
                float.NaN,
                fighterLimit),
            "Invalid despawn-time kinematics trapped the landing gear closed.");
    }

    private static void ExperimentalAiFlightIsOptInAndHostAuthoritative()
    {
        False(CanOwnAiFlight(experimentalAiEnabled: false),
            "AI flight must remain native by default.");
        False(CanOwnAiFlight(enabled: false),
            "The master flight-model switch must disable experimental AI flight.");
        False(CanOwnAiFlight(hasAiDriver: false),
            "An aircraft without an AI pilot must not enter experimental AI flight.");
        True(CanOwnAiFlight(),
            "An opted-in AI aircraft should use the flight model offline.");
        False(CanOwnAiFlight(multiplayerIntent: true),
            "AI flight must fail closed while a multiplayer room is joining.");
        False(CanOwnAiFlight(
                multiplayerIntent: true,
                inNetworkRoom: true),
            "A non-master client must not simulate shared AI aircraft.");
        True(CanOwnAiFlight(
                multiplayerIntent: true,
                inNetworkRoom: true,
                isMasterClient: true),
            "The multiplayer host should simulate opted-in AI aircraft.");
    }

    private static bool CanOwnFlight(
        bool enabled = true,
        bool isLocallyControlledVehicle = true,
        bool hasHumanDriver = true,
        bool usesRealisticControls = true,
        bool multiplayerIntent = false,
        bool inNetworkRoom = false,
        bool ownsNetworkSynchronizer = false)
        => AircraftFlightOwnershipCore.CanSimulate(
            enabled,
            isLocallyControlledVehicle,
            hasHumanDriver,
            usesRealisticControls,
            multiplayerIntent,
            inNetworkRoom,
            ownsNetworkSynchronizer);

    private static bool CanOwnAiFlight(
        bool enabled = true,
        bool experimentalAiEnabled = true,
        bool hasAiDriver = true,
        bool multiplayerIntent = false,
        bool inNetworkRoom = false,
        bool isMasterClient = false)
        => AircraftFlightOwnershipCore.CanSimulateAi(
            enabled,
            experimentalAiEnabled,
            hasAiDriver,
            multiplayerIntent,
            inNetworkRoom,
            isMasterClient);

    private static void AerodynamicVectorsStayFinitePerpendicularAndPassive()
    {
        var aerodynamics = AircraftAerodynamicsParameters.DefaultFighter;
        var engine = AircraftEngineParameters.Default;
        for (var angle = -179f; angle <= 179f; angle += 3.25f)
        {
            const float speed = 72f;
            var radians = angle * MathF.PI / 180f;
            var velocity = new Vector3(
                0f,
                -MathF.Sin(radians) * speed,
                MathF.Cos(radians) * speed);
            var liftState = AircraftAerodynamicsCore.EvaluateLift(angle, aerodynamics);
            var dragCoefficient = AircraftAerodynamicsCore.EvaluateDragCoefficient(
                liftState.LiftCoefficient, liftState.Separation, aerodynamics);
            var pressure = AircraftAerodynamicsCore.DynamicPressure(
                speed, aerodynamics.AirDensity);
            var lift = AircraftAerodynamicsCore.LiftForce(
                velocity,
                Vector3.UnitX,
                pressure * aerodynamics.WingArea * liftState.LiftCoefficient);
            var drag = AircraftAerodynamicsCore.DragForce(
                velocity,
                pressure * aerodynamics.WingArea * dragCoefficient);

            Finite(liftState.LiftCoefficient, "Lift coefficient became non-finite.");
            Finite(liftState.Separation, "Separation became non-finite.");
            True(dragCoefficient >= 0f && float.IsFinite(dragCoefficient),
                "Drag coefficient became negative or non-finite.");
            Finite(lift, "Lift vector became non-finite.");
            Finite(drag, "Drag vector became non-finite.");
            Orthogonal(lift, velocity, "Lift was not perpendicular to local airflow.");
            True(Vector3.Dot(drag, velocity) <= 0.01f,
                "Drag added energy to the local airflow.");

            var evaluation = Evaluate(
                velocity,
                new Vector3(0.25f, -0.14f, 0.32f),
                AircraftSurfaceCommands.Neutral,
                engineSpool: 0f);
            Finite(evaluation.AerodynamicForce, "Aerodynamic force became non-finite.");
            Finite(evaluation.AerodynamicMoment, "Aerodynamic moment became non-finite.");
            var aerodynamicPower =
                Vector3.Dot(evaluation.AerodynamicForce, velocity) +
                Vector3.Dot(
                    evaluation.AerodynamicMoment,
                    new Vector3(0.25f, -0.14f, 0.32f));
            var powerTolerance =
                0.00002f *
                (evaluation.AerodynamicForce.Length() * velocity.Length() +
                 evaluation.AerodynamicMoment.Length() * 0.45f) +
                0.02f;
            True(aerodynamicPower <= powerTolerance,
                "Neutral aerodynamic forces injected translational/rotational energy.");
        }
    }

    private static void ZeroSpeedProducesNoAerodynamicOrEngineForce()
    {
        var evaluation = Evaluate(
            Vector3.Zero,
            Vector3.Zero,
            new AircraftSurfaceCommands(1f, 1f, 1f),
            engineSpool: 0f);

        Near(Vector3.Zero, evaluation.LeftWing.Force, 0.00001f,
            "The left wing produced force at zero airflow.");
        Near(Vector3.Zero, evaluation.RightWing.Force, 0.00001f,
            "The right wing produced force at zero airflow.");
        Near(Vector3.Zero, evaluation.AerodynamicForce, 0.00001f,
            "Aerodynamics produced force at zero airflow.");
        Near(Vector3.Zero, evaluation.ThrustForce, 0.00001f,
            "An unspooled engine produced thrust.");
        Near(Vector3.Zero, evaluation.TotalMoment, 0.00001f,
            "Full surface commands produced a moment without freestream or propwash.");
        Near(0f, evaluation.ControlAuthority.Aileron, 0.000001f,
            "Ailerons retained authority without freestream.");
        Near(0f, evaluation.ControlAuthority.Elevator, 0.000001f,
            "An unspooled engine manufactured elevator authority.");
        Near(0f, evaluation.ControlAuthority.Rudder, 0.000001f,
            "An unspooled engine manufactured rudder authority.");
        False(evaluation.LeftWing.LiftState.IsSeparated,
            "An undefined zero-speed AoA was classified as a stall.");
    }

    private static void LowSpeedAlignedFlowIsNotAStall()
    {
        var lowSpeed = Evaluate(
            Vector3.UnitZ * 1.5f,
            Vector3.Zero,
            AircraftSurfaceCommands.Neutral,
            engineSpool: 0f);
        var highSpeed = Evaluate(
            Vector3.UnitZ * 90f,
            Vector3.Zero,
            AircraftSurfaceCommands.Neutral,
            engineSpool: 0f);
        const float separatedAngle = 26f;
        var separatedRadians = separatedAngle * MathF.PI / 180f;
        var separatedDirection = new Vector3(
            0f,
            -MathF.Sin(separatedRadians),
            MathF.Cos(separatedRadians));
        var lowSpeedSeparated = Evaluate(
            separatedDirection * 2f,
            Vector3.Zero,
            AircraftSurfaceCommands.Neutral,
            engineSpool: 0f);
        var highSpeedSeparated = Evaluate(
            separatedDirection * 80f,
            Vector3.Zero,
            AircraftSurfaceCommands.Neutral,
            engineSpool: 0f);

        False(lowSpeed.LeftWing.LiftState.IsSeparated,
            "Low dynamic pressure alone stalled the left wing.");
        False(lowSpeed.RightWing.LiftState.IsSeparated,
            "Low dynamic pressure alone stalled the right wing.");
        Near(
            highSpeed.LeftWing.LiftState.Separation,
            lowSpeed.LeftWing.LiftState.Separation,
            0.000001f,
            "Separation changed with speed at the same AoA.");
        True(
            lowSpeed.AerodynamicForce.Length() <
            highSpeed.AerodynamicForce.Length() * 0.001f,
            "Low-speed aligned flow retained implausibly large aerodynamic force.");
        Near(
            highSpeedSeparated.LeftWing.LiftState.Separation,
            lowSpeedSeparated.LeftWing.LiftState.Separation,
            0.000001f,
            "A separated wing changed stall state with dynamic pressure.");
    }

    private static void StallSeparationAndCoefficientsAreContinuous()
    {
        var parameters = AircraftAerodynamicsParameters.DefaultFighter;
        var startAngle =
            parameters.ZeroLiftAngleOfAttackDegrees +
            parameters.CriticalAngleOfAttackDegrees;
        var endAngle = startAngle + parameters.SeparationWidthDegrees;
        var beforeStart = AircraftAerodynamicsCore.EvaluateLift(
            startAngle - 0.001f, parameters);
        var afterStart = AircraftAerodynamicsCore.EvaluateLift(
            startAngle + 0.001f, parameters);
        var beforeEnd = AircraftAerodynamicsCore.EvaluateLift(
            endAngle - 0.001f, parameters);
        var afterEnd = AircraftAerodynamicsCore.EvaluateLift(
            endAngle + 0.001f, parameters);

        Near(beforeStart.LiftCoefficient, afterStart.LiftCoefficient, 0.001f,
            "Lift snapped at separation onset.");
        Near(beforeStart.Separation, afterStart.Separation, 0.001f,
            "Separation snapped at its onset.");
        Near(beforeEnd.LiftCoefficient, afterEnd.LiftCoefficient, 0.001f,
            "Lift snapped when the wing became fully separated.");
        Near(beforeEnd.Separation, afterEnd.Separation, 0.001f,
            "Separation snapped at its full-separation boundary.");

        var previous = AircraftAerodynamicsCore.EvaluateLift(
            startAngle - 4f, parameters);
        var maximumLiftStep = 0f;
        for (var angle = startAngle - 3.99f; angle <= endAngle + 4f; angle += 0.01f)
        {
            var current = AircraftAerodynamicsCore.EvaluateLift(angle, parameters);
            maximumLiftStep = MathF.Max(
                maximumLiftStep,
                MathF.Abs(current.LiftCoefficient - previous.LiftCoefficient));
            True(current.Separation + 0.000001f >= previous.Separation,
                "Positive-AoA separation was not monotonic through the stall.");
            previous = current;
        }

        True(maximumLiftStep < 0.004f,
            $"Lift curve contained a discontinuity; maximum 0.01-degree step was {maximumLiftStep}.");
        True(AircraftAerodynamicsCore.EvaluateLift(90f, parameters).Separation > 0.999f,
            "A broadside wing was not fully separated.");
    }

    private static void ControlAuthorityUsesFreestreamAndBoundedPropwash()
    {
        var parameters = AircraftAerodynamicsParameters.DefaultFighter;
        var stoppedIdle = AircraftAerodynamicsCore.EvaluateControlAuthority(
            0f, 0f, parameters);
        var stoppedPowered = AircraftAerodynamicsCore.EvaluateControlAuthority(
            0f, 1f, parameters);
        var lowIdle = AircraftAerodynamicsCore.EvaluateControlAuthority(
            20f, 0f, parameters);
        var lowPowered = AircraftAerodynamicsCore.EvaluateControlAuthority(
            20f, 1f, parameters);
        var referencePowered = AircraftAerodynamicsCore.EvaluateControlAuthority(
            parameters.ReferenceControlSpeed, 1f, parameters);

        Near(0f, stoppedIdle.Aileron, 0.000001f,
            "Stopped ailerons retained a hidden authority floor.");
        Near(stoppedIdle.Aileron, stoppedPowered.Aileron, 0.000001f,
            "Propwash incorrectly increased aileron authority.");
        Near(lowIdle.Aileron, lowPowered.Aileron, 0.000001f,
            "Throttle changed freestream-only aileron authority.");
        True(stoppedPowered.Elevator > 0f && stoppedPowered.Rudder > 0f,
            "Powered propwash did not reach the tail.");
        True(stoppedPowered.Elevator <= parameters.ElevatorPropwashAuthority + 0.000001f,
            "Elevator propwash exceeded its configured bound.");
        True(stoppedPowered.Rudder <= parameters.RudderPropwashAuthority + 0.000001f,
            "Rudder propwash exceeded its configured bound.");
        True(lowPowered.Elevator - lowIdle.Elevator <=
             parameters.ElevatorPropwashAuthority + 0.000001f,
            "Elevator propwash contribution exceeded its configured bound in motion.");
        True(lowPowered.Rudder - lowIdle.Rudder <=
             parameters.RudderPropwashAuthority + 0.000001f,
            "Rudder propwash contribution exceeded its configured bound in motion.");
        Near(1f, referencePowered.Aileron, 0.000001f,
            "Ailerons did not reach reference authority at reference speed.");
        Near(1f, referencePowered.Elevator, 0.000001f,
            "Elevator authority exceeded or missed its normalized ceiling.");
        Near(1f, referencePowered.Rudder, 0.000001f,
            "Rudder authority exceeded or missed its normalized ceiling.");
    }

    private static void TailslidePitchStabilityIsContinuousAndBounded()
    {
        const float neutralAngle = 1.25f;
        var beforeRearPole =
            AircraftAerodynamicsCore.BoundedStabilityErrorDegrees(
                179.999f,
                neutralAngle);
        var afterRearPole =
            AircraftAerodynamicsCore.BoundedStabilityErrorDegrees(
                -179.999f,
                neutralAngle);

        Finite(beforeRearPole,
            "Pitch stability became non-finite before the rear airflow pole.");
        Finite(afterRearPole,
            "Pitch stability became non-finite after the rear airflow pole.");
        True(MathF.Abs(beforeRearPole - afterRearPole) < 0.01f,
            $"Pitch stability jumped across a tailslide: {beforeRearPole} vs {afterRearPole}.");

        for (var angle = -180f; angle <= 180f; angle += 0.25f)
        {
            var error =
                AircraftAerodynamicsCore.BoundedStabilityErrorDegrees(
                    angle,
                    neutralAngle);
            Finite(error,
                $"Pitch stability was non-finite at {angle} degrees.");
            True(MathF.Abs(error) <= 180f / MathF.PI + 0.001f,
                $"Pitch stability exceeded its continuous bound at {angle} degrees: {error}.");
        }

        var smallPositive =
            AircraftAerodynamicsCore.BoundedStabilityErrorDegrees(
                neutralAngle + 5f,
                neutralAngle);
        var smallNegative =
            AircraftAerodynamicsCore.BoundedStabilityErrorDegrees(
                neutralAngle - 5f,
                neutralAngle);
        Near(5f, smallPositive, 0.01f,
            "Bounded stability changed the positive small-angle response.");
        Near(-5f, smallNegative, 0.01f,
            "Bounded stability changed the negative small-angle response.");
    }

    private static void EngineRatingHasUsefulRangeWithoutRocketThrust()
    {
        var engine = AircraftEngineParameters.Default;
        var minimum = AircraftAerodynamicsCore.MapEngineRatingToStaticThrustToWeight(
            1f, engine);
        var maximum = AircraftAerodynamicsCore.MapEngineRatingToStaticThrustToWeight(
            10f, engine);
        True(maximum / minimum > 4f,
            "The 1..10 engine rating did not span more than four times static thrust.");
        True(maximum <= 0.78f + 0.000001f,
            "Maximum engine rating exceeded the non-rocket static thrust limit.");

        var previous = minimum;
        for (var rating = 2; rating <= 10; rating++)
        {
            var current = AircraftAerodynamicsCore.MapEngineRatingToStaticThrustToWeight(
                rating, engine);
            True(current > previous, "Engine rating was not strictly monotonic.");
            previous = current;
        }

        const float mass = 3200f;
        var thrust = AircraftAerodynamicsCore.PowerLimitedThrust(
            mass, 10f, 1f, 0f, engine);
        var weight = mass * AircraftAerodynamicsCore.StandardGravity;
        Near(maximum, thrust / weight, 0.000001f,
            "Static thrust did not match the rating's thrust-to-weight mapping.");

        var vertical = AircraftAerodynamicsCore.EvaluateForces(
            new AircraftForceInput(
                new AircraftKinematicState(
                    Vector3.Zero,
                    Vector3.Zero,
                    Vector3.UnitY,
                    Vector3.UnitZ),
                AircraftSurfaceCommands.Neutral,
                mass,
                10f,
                1f,
                1f,
                1f),
            AircraftAerodynamicsParameters.DefaultFighter,
            engine);
        var netWithGravity = vertical.TotalForce - Vector3.UnitY * weight;
        True(Vector3.Dot(netWithGravity, Vector3.UnitY) < 0f,
            "Maximum engine rating overcame gravity from a static vertical start.");
    }

    private static void AerodynamicDragUsesEnergyRetainingAirborneBaseline()
    {
        const float nativeDrag = 0.075f;
        var minimum =
            AircraftAerodynamicsCore.MapAerodynamicDragToRigidbodyDrag(
                nativeDrag,
                0.60f);
        var recommended =
            AircraftAerodynamicsCore.MapAerodynamicDragToRigidbodyDrag(
                nativeDrag,
                1f);
        var maximum =
            AircraftAerodynamicsCore.MapAerodynamicDragToRigidbodyDrag(
                nativeDrag,
                1.60f);

        Near(0.00540f, minimum, 0.000001f,
            "Minimum coasting drag did not retain the expected energy.");
        Near(0.00900f, recommended, 0.000001f,
            "The recommended coasting baseline was not twelve percent of native damping.");
        Near(0.01440f, maximum, 0.000001f,
            "Maximum slider drag did not preserve the tuned airborne range.");
        True(minimum < recommended && recommended < maximum,
            "The aerodynamic drag slider was not strictly monotonic.");
        Near(
            recommended,
            AircraftAerodynamicsCore.MapAerodynamicDragToRigidbodyDrag(
                nativeDrag,
                float.NaN),
            0.000001f,
            "A non-finite drag setting did not fall back to the recommended baseline.");
    }

    private static void NativeRollGainChangesPhysicalRateAfterSurfaceFiltering()
    {
        Near(
            0.375f,
            AircraftAerodynamicsCore.ScaleNativeRollCommand(
                1f,
                isBomber: false,
                rollAuthoritySetting: 0.25f),
            0.000001f,
            "Minimum fighter Roll Authority did not produce one quarter of its class baseline.");
        Near(
            1.50f,
            AircraftAerodynamicsCore.ScaleNativeRollCommand(
                1f,
                isBomber: false,
                rollAuthoritySetting: 1f),
            0.000001f,
            "Default fighter roll did not receive the intended physical rate gain.");
        Near(
            -1.50f,
            AircraftAerodynamicsCore.ScaleNativeRollCommand(
                -1f,
                isBomber: false,
                rollAuthoritySetting: 1f),
            0.000001f,
            "Fighter roll gain was not sign-symmetric.");
        Near(
            3f,
            AircraftAerodynamicsCore.ScaleNativeRollCommand(
                1f,
                isBomber: false,
                rollAuthoritySetting: 2f),
            0.000001f,
            "Maximum fighter Roll Authority was clipped below twice its class baseline.");
        Near(
            1.15f,
            AircraftAerodynamicsCore.ScaleNativeRollCommand(
                1f,
                isBomber: true,
                rollAuthoritySetting: 1f),
            0.000001f,
            "Bomber roll did not retain its heavier class response.");
        Near(
            0.75f,
            AircraftAerodynamicsCore.ScaleNativeRollCommand(
                0.5f,
                isBomber: false,
                rollAuthoritySetting: 1f),
            0.000001f,
            "Partial filtered aileron was not scaled proportionally.");
        Near(
            2.30f,
            AircraftAerodynamicsCore.ScaleNativeRollCommand(
                1f,
                isBomber: true,
                rollAuthoritySetting: 2f),
            0.000001f,
            "Maximum bomber Roll Authority was clipped below twice its class baseline.");
        Near(
            3f,
            AircraftAerodynamicsCore.ScaleNativeRollCommand(
                4f,
                isBomber: false,
                rollAuthoritySetting: 2f),
            0.000001f,
            "An out-of-range native aileron input escaped the finite surface clamp.");
        Near(
            0f,
            AircraftAerodynamicsCore.ScaleNativeRollCommand(
                float.NaN,
                isBomber: false,
                rollAuthoritySetting: 2f),
            0.000001f,
            "A non-finite native aileron command manufactured roll.");
    }

    private static void NativeControlSchedulePreservesImmediateCorrectionsAndLimitsRudderTravel()
    {
        const float heavyControlSpeed = 80f;
        var low =
            AircraftAerodynamicsCore.EvaluateNativeControlSchedule(
                heavyControlSpeed * 0.35f,
                heavyControlSpeed);
        var nominal =
            AircraftAerodynamicsCore.EvaluateNativeControlSchedule(
                heavyControlSpeed,
                heavyControlSpeed);
        var fast =
            AircraftAerodynamicsCore.EvaluateNativeControlSchedule(
                heavyControlSpeed * 1.5f,
                heavyControlSpeed);
        var veryFast =
            AircraftAerodynamicsCore.EvaluateNativeControlSchedule(
                heavyControlSpeed * 2f,
                heavyControlSpeed);

        Near(1f, low.Pitch, 0.000001f,
            "Low-speed elevator did not retain complete available travel.");
        Near(1f, low.Roll, 0.000001f,
            "Low-speed aileron did not retain complete available travel.");
        Near(1f, low.Rudder, 0.000001f,
            "Low-speed rudder did not retain complete available travel.");
        Near(1f, nominal.Pitch, 0.000001f,
            "Elevator travel changed below the heavy-control threshold.");
        Near(1f, nominal.Roll, 0.000001f,
            "Aileron travel changed below the heavy-control threshold.");
        Near(1f, nominal.Rudder, 0.000001f,
            "Rudder travel changed below the heavy-control threshold.");

        Near(1f, fast.Pitch, 0.00001f,
            "The extra speed schedule weakened elevator response.");
        Near(1f, fast.Roll, 0.00001f,
            "The extra speed schedule weakened aileron response.");
        Near(0.3478261f, fast.Rudder, 0.00001f,
            "Fast-flight rudder heaviness left its dynamic-pressure curve.");
        Near(1f, veryFast.Pitch, 0.00001f,
            "Extreme speed added a second elevator attenuation layer.");
        Near(1f, veryFast.Roll, 0.00001f,
            "Extreme speed added a second aileron attenuation layer.");
        Near(0.1818182f, veryFast.Rudder, 0.00001f,
            "Extreme-speed rudder recovery travel left its lower bound.");
        True(
            nominal.Rudder > fast.Rudder && fast.Rudder > veryFast.Rudder,
            "Rudder heaviness was not progressive with speed.");

        var justBelow =
            AircraftAerodynamicsCore.EvaluateNativeControlSchedule(
                heavyControlSpeed - 0.001f,
                heavyControlSpeed);
        var justAbove =
            AircraftAerodynamicsCore.EvaluateNativeControlSchedule(
                heavyControlSpeed + 0.001f,
                heavyControlSpeed);
        True(
            MathF.Abs(justBelow.Pitch - justAbove.Pitch) < 0.0001f &&
            MathF.Abs(justBelow.Roll - justAbove.Roll) < 0.0001f &&
            MathF.Abs(justBelow.Rudder - justAbove.Rudder) < 0.0001f,
            "The heavy-control threshold introduced a control snap.");

        var invalidSpeed =
            AircraftAerodynamicsCore.EvaluateNativeControlSchedule(
                float.NaN,
                heavyControlSpeed);
        var invalidReference =
            AircraftAerodynamicsCore.EvaluateNativeControlSchedule(
                heavyControlSpeed,
                0f);
        Near(1f, invalidSpeed.Pitch, 0.000001f,
            "Invalid airspeed did not fail open to native elevator travel.");
        Near(1f, invalidSpeed.Roll, 0.000001f,
            "Invalid airspeed did not fail open to native aileron travel.");
        Near(1f, invalidSpeed.Rudder, 0.000001f,
            "Invalid airspeed did not fail open to native rudder travel.");
        Near(1f, invalidReference.Pitch, 0.000001f,
            "Invalid reference speed did not fail open to native controls.");

        var extreme =
            AircraftAerodynamicsCore.EvaluateNativeControlSchedule(
                float.MaxValue,
                heavyControlSpeed);
        True(
            float.IsFinite(extreme.Pitch) && extreme.Pitch > 0f &&
            float.IsFinite(extreme.Roll) && extreme.Roll > 0f &&
            float.IsFinite(extreme.Rudder) && extreme.Rudder > 0f,
            "Extreme overspeed removed finite recovery control.");

        Near(
            0.095f,
            AircraftAerodynamicsCore.LimitNativeControlTravel(
                0.095f,
                fast.Rudder),
            0.000001f,
            "High-speed loading attenuated a fine instructor rudder correction.");
        Near(
            fast.Rudder,
            AircraftAerodynamicsCore.LimitNativeControlTravel(
                0.8f,
                fast.Rudder),
            0.000001f,
            "A large rudder command escaped its high-speed travel limit.");
        Near(
            -fast.Rudder,
            AircraftAerodynamicsCore.LimitNativeControlTravel(
                -0.8f,
                fast.Rudder),
            0.000001f,
            "The high-speed rudder travel limit was not symmetric.");
        Near(
            fast.Rudder * 2f,
            AircraftAerodynamicsCore.ScaleNativeYawCommand(
                AircraftAerodynamicsCore.LimitNativeControlTravel(
                    0.8f,
                    fast.Rudder),
                rudderAuthoritySetting: 2f),
            0.000001f,
            "Rudder Authority did not apply after the high-speed travel cap.");
        Near(
            0.4f,
            AircraftAerodynamicsCore.LimitNativeControlTravel(
                0.4f,
                float.NaN),
            0.000001f,
            "Invalid available travel did not fail open.");

        Near(
            1f,
            AircraftAerodynamicsCore.ScaleNativeYawCommand(
                0.5f,
                rudderAuthoritySetting: 2f),
            0.000001f,
            "Rudder Authority did not scale the filtered physical surface.");
        Near(
            3.75f,
            AircraftAerodynamicsCore.ScaleNativeYawCommand(
                1f,
                rudderAuthoritySetting: 3.75f),
            0.000001f,
            "The recentered Rudder Authority range was still clipped at its old ceiling.");
        Near(
            -0.25f,
            AircraftAerodynamicsCore.ScaleNativeYawCommand(
                -1f,
                rudderAuthoritySetting: 0.25f),
            0.000001f,
            "Minimum Rudder Authority did not preserve command sign.");
        Near(
            0f,
            AircraftAerodynamicsCore.ScaleNativeYawCommand(
                float.PositiveInfinity,
                rudderAuthoritySetting: 2f),
            0.000001f,
            "A non-finite native rudder command manufactured yaw.");
    }

    private static void NativePitchAuthorityPreservesNoseDownAsymmetry()
    {
        var minimumPush =
            AircraftAerodynamicsCore.ScaleNativePitchCommand(
                1f,
                pitchAuthoritySetting: 0.25f);
        var defaultPush =
            AircraftAerodynamicsCore.ScaleNativePitchCommand(
                1f,
                pitchAuthoritySetting: 1f);
        var maximumPush =
            AircraftAerodynamicsCore.ScaleNativePitchCommand(
                1f,
                pitchAuthoritySetting: 2f);

        Near(0.145f, minimumPush, 0.000001f,
            "Minimum Pitch Authority did not retain the 0.58 nose-down asymmetry.");
        Near(0.58f, defaultPush, 0.000001f,
            "Default native nose-down command was not exactly 0.58.");
        Near(1.16f, maximumPush, 0.000001f,
            "Maximum Pitch Authority was clipped before applying nose-down asymmetry.");
        True(minimumPush < defaultPush && defaultPush < maximumPush,
            "Native nose-down authority was not monotonic across the Pitch Authority range.");
        Near(
            -1f,
            AircraftAerodynamicsCore.ScaleNativePitchCommand(
                -1f,
                pitchAuthoritySetting: 1f),
            0.000001f,
            "The nose-down asymmetry incorrectly weakened a nose-up pull.");
        Near(
            0f,
            AircraftAerodynamicsCore.ScaleNativePitchCommand(
                float.PositiveInfinity,
                pitchAuthoritySetting: 2f),
            0.000001f,
            "A non-finite native elevator input manufactured pitch.");
    }

    private static void NativeThrustMultiplierPreservesAuthoredPowerAndCapsRocketThrust()
    {
        const float mass = 3200f;
        const float nativeMaximumThrottle = 100f;
        const float transitionSpeed = 50f;
        var engine = AircraftEngineParameters.Default;
        var weakAuthored =
            AircraftAerodynamicsCore.NativeThrustForceMultiplier(
                1f,
                nativeMaximumThrottle,
                mass,
                10f,
                0f,
                1f,
                transitionSpeed,
                engine);
        Near(1f, weakAuthored, 0.000001f,
            "A weak authored engine request was unnecessarily increased or replaced.");

        var staticMultiplier =
            AircraftAerodynamicsCore.NativeThrustForceMultiplier(
                1000f,
                nativeMaximumThrottle,
                mass,
                10f,
                0f,
                1f,
                transitionSpeed,
                engine);
        var weight = mass * AircraftAerodynamicsCore.StandardGravity;
        var staticThrust = staticMultiplier * nativeMaximumThrottle;
        var ratingCeiling =
            AircraftAerodynamicsCore.MapEngineRatingToStaticThrustToWeight(10f, engine);
        Near(ratingCeiling, staticThrust / weight, 0.000001f,
            "The native multiplier did not cap full-throttle thrust to the engine rating.");
        True(staticThrust < weight,
            "The capped native engine could still hover vertically like a rocket.");

        const float denseAir = 1.3f;
        var denseMultiplier =
            AircraftAerodynamicsCore.NativeThrustForceMultiplier(
                1000f,
                nativeMaximumThrottle,
                mass,
                10f,
                0f,
                denseAir,
                transitionSpeed,
                engine);
        True(
            denseMultiplier * nativeMaximumThrottle * denseAir <=
            staticThrust + 0.01f,
            "Density above one defeated the physical thrust-to-weight ceiling.");

        const float thinAir = 0.6f;
        var thinMultiplier =
            AircraftAerodynamicsCore.NativeThrustForceMultiplier(
                1000f,
                nativeMaximumThrottle,
                mass,
                10f,
                0f,
                thinAir,
                transitionSpeed,
                engine);
        Near(staticMultiplier, thinMultiplier, 0.000001f,
            "Thin air was divided out of the native thrust multiplier.");
        Near(
            staticThrust * thinAir,
            thinMultiplier * nativeMaximumThrottle * thinAir,
            0.001f,
            "Thin air did not reduce physically applied native thrust.");

        var highSpeedMultiplier =
            AircraftAerodynamicsCore.NativeThrustForceMultiplier(
                1000f,
                nativeMaximumThrottle,
                mass,
                10f,
                transitionSpeed * 2f,
                1f,
                transitionSpeed,
                engine);
        Near(staticMultiplier * 0.5f, highSpeedMultiplier, 0.00001f,
            "Native thrust was not power-limited to half at twice transition speed.");

        const float initialClimbSpeed = 80f;
        var verticalAcceleration =
            staticThrust / mass -
            AircraftAerodynamicsCore.StandardGravity;
        True(verticalAcceleration < 0f,
            "Maximum-rating vertical flight did not retain negative net acceleration.");
        var apexTime = -initialClimbSpeed / verticalAcceleration;
        True(float.IsFinite(apexTime) && apexTime > 0f && apexTime < 60f,
            $"The maximum-rating vertical climb did not have a finite apex: {apexTime}.");
    }

    private static void NativeRecoveryVerticalPowerCapsReachFiniteApexAtRatingsFourAndTen()
    {
        var ratingFour = IntegrateNativeRecoveryVerticalClimb(4f);
        var ratingTen = IntegrateNativeRecoveryVerticalClimb(10f);

        True(ratingFour.ReachedApex && ratingTen.ReachedApex,
            "A full-throttle shipped-path vertical climb failed to reach an apex.");
        Near(11.79f, ratingFour.ApexTime, 0.05f,
            "Rating four left the expected P-over-V vertical-climb envelope.");
        Near(444.53f, ratingFour.MaximumAltitude, 1f,
            "Rating four produced an unexpected vertical apex.");
        Near(26.98f, ratingTen.ApexTime, 0.08f,
            "Rating ten left the expected P-over-V vertical-climb envelope.");
        Near(889.50f, ratingTen.MaximumAltitude, 2f,
            "Rating ten produced an unexpected vertical apex.");

        True(ratingFour.Velocity.Y < -170f,
            $"Rating four did not establish a strong descent: Vy={ratingFour.Velocity.Y} m/s.");
        True(ratingTen.Velocity.Y < -35f,
            $"Rating ten did not descend after its finite apex: Vy={ratingTen.Velocity.Y} m/s.");
        True(
            ratingFour.Position.Y < ratingFour.MaximumAltitude - 3000f &&
            ratingTen.Position.Y < ratingTen.MaximumAltitude - 300f,
            "A capped vertical climb did not give altitude back after its apex.");
        True(ratingTen.MaximumAltitude > ratingFour.MaximumAltitude,
            "The engine-rating range did not provide more vertical energy at rating ten.");
    }

    private static void FlightPathTurnRateMeasuresCurvatureWithoutAxialFalsePositives()
    {
        const float deltaTime = 0.02f;
        var straightRate =
            AircraftAerodynamicsCore.FlightPathTurnRateRadiansPerSecond(
                Vector3.UnitZ * 40f,
                Vector3.UnitZ * 80f,
                deltaTime);
        Near(0f, straightRate, 0.000001f,
            "Pure axial acceleration produced a false flight-path turn.");

        const float degreesPerSecond = 20f;
        var angle = degreesPerSecond * deltaTime * MathF.PI / 180f;
        var turnedVelocity = new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * 70f;
        var measured =
            AircraftAerodynamicsCore.FlightPathTurnRateRadiansPerSecond(
                Vector3.UnitZ * 70f,
                turnedVelocity,
                deltaTime);
        Near(degreesPerSecond * MathF.PI / 180f, measured, 0.001f,
            "Flight-path curvature did not recover a known twenty-degree-per-second turn.");

        const float mass = 3000f;
        var turnDrag =
            AircraftAerodynamicsCore.NativeManeuverDragAcceleration(
                mass,
                measured,
                1.225f,
                22f,
                5.5f,
                0.82f,
                70f,
                24f,
                1f);
        var thrustAcceleration =
            AircraftAerodynamicsCore.PowerLimitedThrust(
                mass,
                4f,
                1f,
                70f,
                AircraftEngineParameters.Default) /
            mass;
        True(thrustAcceleration - turnDrag < 0f,
            "A representative sustained twenty-degree-per-second turn still gained energy.");
    }

    private static void NativeVelocityDirectionPreservationIsStrictlyScoped()
    {
        var current = Vector3.Normalize(new Vector3(0.1f, 0f, 1f)) * 60f;
        var requested = Vector3.Normalize(new Vector3(0.2f, 0f, 1f)) * 55f;
        var preserved =
            AircraftAerodynamicsCore.PreserveRequestedVelocityDirection(
                requested,
                current,
                eligible: true);
        Near(current.Length(), preserved.Length(), 0.00001f,
            "Velocity direction preservation changed native speed.");
        Near(Vector3.Normalize(requested), Vector3.Normalize(preserved), 0.00001f,
            "Eligible velocity direction preservation did not restore the requested direction.");

        Equal(
            requested,
            AircraftAerodynamicsCore.PreserveRequestedVelocityDirection(
                requested,
                current,
                eligible: false),
            "An ineligible aircraft received native velocity direction preservation.");
        var excessiveLoss = Vector3.Normalize(requested) * 47f;
        Equal(
            excessiveLoss,
            AircraftAerodynamicsCore.PreserveRequestedVelocityDirection(
                excessiveLoss,
                current,
                eligible: true),
            "Velocity preservation masked a speed loss greater than twenty percent.");
        var divergent = -Vector3.Normalize(current) * 55f;
        Equal(
            divergent,
            AircraftAerodynamicsCore.PreserveRequestedVelocityDirection(
                divergent,
                current,
                eligible: true),
            "Velocity preservation masked a major native direction change.");
        var speedIncrease = Vector3.Normalize(requested) * 65f;
        Equal(
            speedIncrease,
            AircraftAerodynamicsCore.PreserveRequestedVelocityDirection(
                speedIncrease,
                current,
                eligible: true),
            "Velocity preservation intercepted a native speed increase.");
    }

    private static void NativeRecoveryCoastAndDiveRetainGravityDrivenEnergy()
    {
        var levelCoast = IntegrateNativeRecoveryCoast(
            Vector3.UnitZ * 70f,
            duration: 8f,
            gravityEnabled: false);
        var diveDirection = Vector3.Normalize(
            Vector3.UnitZ * MathF.Cos(30f * MathF.PI / 180f) -
            Vector3.UnitY * MathF.Sin(30f * MathF.PI / 180f));
        var dive = IntegrateNativeRecoveryCoast(
            diveDirection * 70f,
            duration: 5f,
            gravityEnabled: true);

        Near(65.14f, levelCoast.Length(), 0.10f,
            "Mapped airborne drag or native steering deleted too much coasting speed.");
        True(levelCoast.Length() > 0.92f * 70f,
            $"An eight-second native-recovery coast lost excessive speed: {levelCoast.Length()} m/s.");
        Near(99.93f, dive.Length(), 0.20f,
            "The native-recovery dive left its gravity-driven energy envelope.");
        True(dive.Length() > 95f && dive.Y < -80f,
            $"A power-off dive slowed instead of converting altitude to speed: {dive}.");
    }

    private static void ManeuverDragIsQuadraticBoundedAndLeavesStraightFlightAlone()
    {
        const float mass = 3000f;
        const float density = 1.225f;
        const float wingArea = 22f;
        const float aspectRatio = 6f;
        const float efficiency = 0.8f;
        const float stallSpeed = 35f;
        const float airspeed = 80f;
        var tenDegreesPerSecond = 10f * MathF.PI / 180f;
        var twentyDegreesPerSecond = 2f * tenDegreesPerSecond;
        var tenDegreeTurn =
            AircraftAerodynamicsCore.NativeManeuverDragAcceleration(
                mass,
                tenDegreesPerSecond,
                density,
                wingArea,
                aspectRatio,
                efficiency,
                airspeed,
                stallSpeed,
                1f);
        var twentyDegreeTurn =
            AircraftAerodynamicsCore.NativeManeuverDragAcceleration(
                mass,
                twentyDegreesPerSecond,
                density,
                wingArea,
                aspectRatio,
                efficiency,
                airspeed,
                stallSpeed,
                1f);

        Near(0.675f, tenDegreeTurn, 0.01f,
            "Representative maneuver drag left its physical calibration.");
        Near(tenDegreeTurn * 4f, twentyDegreeTurn, 0.001f,
            "Maneuver drag did not grow with turn-rate squared.");
        Near(
            0f,
            AircraftAerodynamicsCore.NativeManeuverDragAcceleration(
                mass,
                0f,
                density,
                wingArea,
                aspectRatio,
                efficiency,
                airspeed,
                stallSpeed,
                1f),
            0.000001f,
            "Straight flight or pure axial roll received maneuver drag.");

        var minimumSetting =
            AircraftAerodynamicsCore.NativeManeuverDragAcceleration(
                mass,
                tenDegreesPerSecond,
                density,
                wingArea,
                aspectRatio,
                efficiency,
                airspeed,
                stallSpeed,
                0.60f);
        var maximumSetting =
            AircraftAerodynamicsCore.NativeManeuverDragAcceleration(
                mass,
                tenDegreesPerSecond,
                density,
                wingArea,
                aspectRatio,
                efficiency,
                airspeed,
                stallSpeed,
                1.60f);
        Near(
            tenDegreeTurn * 0.60f,
            minimumSetting,
            0.0001f,
            "The drag setting did not scale maneuver energy loss down.");
        Near(
            tenDegreeTurn * 1.60f,
            maximumSetting,
            0.0001f,
            "The drag setting did not scale maneuver energy loss up.");

        Near(
            0f,
            AircraftAerodynamicsCore.NativeManeuverDragAcceleration(
                mass,
                tenDegreesPerSecond,
                density,
                wingArea,
                aspectRatio,
                efficiency,
                stallSpeed * 0.55f,
                stallSpeed,
                1f),
            0.000001f,
            "Near-stall maneuver drag did not fade out cleanly.");
        Near(
            tenDegreeTurn,
            AircraftAerodynamicsCore.NativeManeuverDragAcceleration(
                mass,
                tenDegreesPerSecond,
                density,
                wingArea,
                aspectRatio,
                efficiency,
                stallSpeed * 0.90f,
                stallSpeed,
                1f),
            0.0001f,
            "Maneuver drag did not reach full confidence above stall.");
        Near(
            0.75f * AircraftAerodynamicsCore.StandardGravity,
            AircraftAerodynamicsCore.NativeManeuverDragAcceleration(
                mass,
                10f,
                density,
                wingArea,
                aspectRatio,
                efficiency,
                airspeed,
                stallSpeed,
                1.60f),
            0.0001f,
            "Extreme maneuver drag exceeded or missed its acceleration cap.");
    }

    private static void MaximumRatingHardTurnBurnsEnergyWhileStraightFlightAccelerates()
    {
        const float initialSpeed = 70f;
        var straightSpeed = IntegrateNativeRecoveryPoweredFlight(
            initialSpeed,
            turnRateDegreesPerSecond: 0f,
            duration: 8f);
        var hardTurnSpeed = IntegrateNativeRecoveryPoweredFlight(
            initialSpeed,
            turnRateDegreesPerSecond: 35f,
            duration: 8f);

        Near(96.81f, straightSpeed, 0.25f,
            "Default straight flight lost the expected maximum-rating acceleration.");
        Near(52.84f, hardTurnSpeed, 0.25f,
            "A hard maximum-rating turn left the bounded maneuver-loss envelope.");
        True(straightSpeed > initialSpeed + 20f,
            "Straight flight was harmed by maneuver drag despite zero curvature.");
        True(hardTurnSpeed < initialSpeed - 15f,
            $"A hard turn still retained or gained speed at rating ten: {hardTurnSpeed} m/s.");
    }

    private static void PowerLimitedThrustUsesOnlyPositiveAxialSpeed()
    {
        var engine = AircraftEngineParameters.Default;
        const float mass = 3100f;
        var staticThrust = AircraftAerodynamicsCore.PowerLimitedThrust(
            mass, 7f, 1f, 0f, engine);
        var reverseThrust = AircraftAerodynamicsCore.PowerLimitedThrust(
            mass, 7f, 1f, -80f, engine);
        var transitionThrust = AircraftAerodynamicsCore.PowerLimitedThrust(
            mass, 7f, 1f, engine.PowerTransitionSpeed, engine);
        var highSpeed = engine.PowerTransitionSpeed * 3f;
        var highSpeedThrust = AircraftAerodynamicsCore.PowerLimitedThrust(
            mass, 7f, 1f, highSpeed, engine);

        Near(staticThrust, reverseThrust, 0.001f,
            "Negative axial speed entered the propulsive power divisor.");
        Near(staticThrust, transitionThrust, 0.001f,
            "Thrust was discontinuous at the power transition speed.");
        True(highSpeedThrust < staticThrust,
            "Positive high axial speed did not power-limit thrust.");
        True(highSpeedThrust * highSpeed <=
             staticThrust * engine.PowerTransitionSpeed + 0.01f,
            "High-speed propulsive power exceeded the configured power budget.");
    }

    private static void EngineSpoolIsMonotonicAsymmetricAndStepStable()
    {
        var engine = AircraftEngineParameters.Default;
        var coarseUp = IntegrateSpool(0f, 1f, 2f, 0.02f, engine, increasing: true);
        var fineUp = IntegrateSpool(0f, 1f, 2f, 0.005f, engine, increasing: true);
        Near(coarseUp, fineUp, 0.00003f,
            "Spool-up response changed with fixed-step size.");
        True(coarseUp > 0.99f && coarseUp < 1.000001f,
            "The fast spool-up response was too slow or overshot.");

        var coarseDown = IntegrateSpool(1f, 0f, 2f, 0.02f, engine, increasing: false);
        var fineDown = IntegrateSpool(1f, 0f, 2f, 0.005f, engine, increasing: false);
        Near(coarseDown, fineDown, 0.00003f,
            "Spool-down response changed with fixed-step size.");
        True(coarseDown >= 0f && coarseDown < 1f,
            "Spool-down response overshot its target.");

        var shortRise = AircraftAerodynamicsCore.AdvanceEngineSpool(
            0f, 1f, 0.25f, engine);
        var shortFall = 1f - AircraftAerodynamicsCore.AdvanceEngineSpool(
            1f, 0f, 0.25f, engine);
        True(shortRise > shortFall,
            "Spool-up was not faster than spool-down.");
    }

    private static void VerticalClimbReachesFiniteApexThenDescends()
    {
        const float initialVerticalSpeed = 80f;
        var flight = IntegrateTranslation(
            Vector3.UnitY * initialVerticalSpeed,
            Vector3.UnitY,
            Vector3.UnitZ,
            duration: 15f,
            deltaTime: 0.01f,
            engineSpool: 0f);
        var vacuumApex =
            initialVerticalSpeed * initialVerticalSpeed /
            (2f * AircraftAerodynamicsCore.StandardGravity);

        True(flight.ReachedApex,
            "A power-off vertical climb never transitioned from rising to falling.");
        Finite(flight.ApexTime, "The vertical climb produced a non-finite apex time.");
        Finite(flight.MaximumAltitude,
            "The vertical climb produced a non-finite apex altitude.");
        True(flight.ApexTime > 0f && flight.ApexTime < 15f,
            $"The vertical climb apex time was outside the integration window: {flight.ApexTime}.");
        True(flight.MaximumAltitude > 10f &&
             flight.MaximumAltitude <= vacuumApex + 0.5f,
            $"The vertical climb apex was implausible: {flight.MaximumAltitude} m.");
        True(flight.Velocity.Y < -1f,
            $"Gravity did not establish a descent after the apex: Vy={flight.Velocity.Y} m/s.");
        True(flight.Position.Y < flight.MaximumAltitude - 10f,
            "The aircraft did not lose meaningful altitude after reaching its apex.");
    }

    private static void PowerOffCoastRetainsPlausibleFlightEnergy()
    {
        var flight = IntegrateTranslation(
            Vector3.UnitZ * 70f,
            Vector3.UnitZ,
            Vector3.UnitY,
            duration: 8f,
            deltaTime: 0.01f,
            engineSpool: 0f);

        Finite(flight.Position, "The power-off coast produced a non-finite position.");
        Finite(flight.Velocity, "The power-off coast produced a non-finite velocity.");
        Finite(flight.FinalSpecificEnergy,
            "The power-off coast produced non-finite mechanical energy.");
        True(flight.MaximumSpecificEnergy <= flight.InitialSpecificEnergy + 0.5f,
            $"A power-off coast gained mechanical energy: initial={flight.InitialSpecificEnergy}, " +
            $"maximum={flight.MaximumSpecificEnergy} J/kg.");
        True(flight.FinalSpecificEnergy > flight.InitialSpecificEnergy * 0.80f,
            $"Power-off flight energy collapsed unrealistically in eight seconds: " +
            $"initial={flight.InitialSpecificEnergy}, final={flight.FinalSpecificEnergy} J/kg.");
        True(flight.Velocity.Length() > 45f,
            $"A short power-off coast destroyed too much airspeed: {flight.Velocity.Length()} m/s.");
    }

    private static void FlightIntegrationIsStableAtFiftyAndOneHundredHertz()
    {
        var fiftyHertz = IntegrateTranslation(
            new Vector3(0f, 0f, 70f),
            Vector3.UnitZ,
            Vector3.UnitY,
            duration: 8f,
            deltaTime: 0.02f,
            engineSpool: 0f);
        var oneHundredHertz = IntegrateTranslation(
            new Vector3(0f, 0f, 70f),
            Vector3.UnitZ,
            Vector3.UnitY,
            duration: 8f,
            deltaTime: 0.01f,
            engineSpool: 0f);

        Near(oneHundredHertz.Position, fiftyHertz.Position, 0.75f,
            "Power-off trajectory changed materially between 50 and 100 Hz.");
        Near(oneHundredHertz.Velocity, fiftyHertz.Velocity, 0.15f,
            "Power-off velocity changed materially between 50 and 100 Hz.");
        Near(
            oneHundredHertz.FinalSpecificEnergy,
            fiftyHertz.FinalSpecificEnergy,
            5f,
            "Power-off energy changed materially between 50 and 100 Hz.");
    }

    private static void DirectAndMouseCommandsShareIdenticalPhysics()
    {
        var directSurfaceCommands = new AircraftSurfaceCommands(0.48f, 0.62f, 0.31f);
        var mouseInstructorSurfaceCommands = new AircraftSurfaceCommands(0.48f, 0.62f, 0.31f);
        var kinematics = new AircraftKinematicState(
            new Vector3(2f, -4f, 52f),
            new Vector3(0.08f, -0.04f, 0.12f),
            Vector3.UnitZ,
            Vector3.UnitY);
        var direct = AircraftAerodynamicsCore.EvaluateForces(
            new AircraftForceInput(
                kinematics,
                directSurfaceCommands,
                2950f,
                6.5f,
                0.78f,
                1f,
                1f),
            AircraftAerodynamicsParameters.DefaultFighter,
            AircraftEngineParameters.Default);
        var mouse = AircraftAerodynamicsCore.EvaluateForces(
            new AircraftForceInput(
                kinematics,
                mouseInstructorSurfaceCommands,
                2950f,
                6.5f,
                0.78f,
                1f,
                1f),
            AircraftAerodynamicsParameters.DefaultFighter,
            AircraftEngineParameters.Default);

        Equal(direct, mouse,
            "Control-source identity changed physics after both paths produced the same surfaces.");
        True(Vector3.Dot(direct.ControlMoment, Vector3.UnitX) < 0f,
            "Positive semantic pitch did not command a nose-up moment.");
        True(Vector3.Dot(direct.ControlMoment, Vector3.UnitY) > 0f,
            "Positive semantic yaw did not command a nose-right moment.");
    }

    private static void TwoHalfWingsCreateDifferentialRollAndPassiveRollDamping()
    {
        var commandedRoll = Evaluate(
            Vector3.UnitZ * 48f,
            Vector3.Zero,
            new AircraftSurfaceCommands(0f, 0.65f, 0f),
            engineSpool: 0f);
        True(
            commandedRoll.LeftWing.LiftState.LiftCoefficient >
            commandedRoll.RightWing.LiftState.LiftCoefficient,
            "Positive roll did not raise left-wing lift and lower right-wing lift.");
        True(Vector3.Dot(commandedRoll.AerodynamicMoment, Vector3.UnitZ) < 0f,
            "Positive semantic roll did not command right-wing-down torque.");

        var rightRollAngularVelocity = -Vector3.UnitZ * 0.75f;
        var damping = Evaluate(
            Vector3.UnitZ * 48f,
            rightRollAngularVelocity,
            AircraftSurfaceCommands.Neutral,
            engineSpool: 0f);
        True(Vector3.Dot(damping.AerodynamicMoment, rightRollAngularVelocity) < 0f,
            "Two-half-wing airflow amplified roll rate instead of damping it.");
        True(
            damping.LeftWing.LiftState.LiftCoefficient <
            damping.RightWing.LiftState.LiftCoefficient,
            "Rolling motion did not create the expected differential half-wing AoA.");
    }

    private static AircraftForceEvaluation Evaluate(
        Vector3 velocity,
        Vector3 angularVelocity,
        AircraftSurfaceCommands commands,
        float engineSpool)
        => AircraftAerodynamicsCore.EvaluateForces(
            new AircraftForceInput(
                new AircraftKinematicState(
                    velocity,
                    angularVelocity,
                    Vector3.UnitZ,
                    Vector3.UnitY),
                commands,
                3000f,
                5f,
                engineSpool,
                1f,
                1f),
            AircraftAerodynamicsParameters.DefaultFighter,
            AircraftEngineParameters.Default);

    private static float IntegrateSpool(
        float current,
        float target,
        float duration,
        float deltaTime,
        in AircraftEngineParameters parameters,
        bool increasing)
    {
        var stepCount = (int)MathF.Round(duration / deltaTime);
        for (var step = 0; step < stepCount; step++)
        {
            var previous = current;
            current = AircraftAerodynamicsCore.AdvanceEngineSpool(
                current, target, deltaTime, parameters);
            if (increasing)
                True(current >= previous && current <= target,
                    "Spool-up was non-monotonic or overshot.");
            else
                True(current <= previous && current >= target,
                    "Spool-down was non-monotonic or overshot.");
        }

        return current;
    }

    private static FlightSnapshot IntegrateNativeRecoveryVerticalClimb(float engineRating)
    {
        const float mass = 3200f;
        const float nativeMaximumThrottle = 100f;
        const float densityMultiplier = 1f;
        const float duration = 45f;
        const float deltaTime = 0.01f;
        var engine = AircraftEngineParameters.Default;
        var drag = AircraftAerodynamicsCore.MapAerodynamicDragToRigidbodyDrag(
            0.075f,
            1f);
        var position = Vector3.Zero;
        var velocity = Vector3.UnitY * 80f;
        var maximumAltitude = 0f;
        var initialSpecificEnergy = SpecificMechanicalEnergy(position, velocity);
        var maximumSpecificEnergy = initialSpecificEnergy;
        var apexTime = float.NaN;
        var reachedApex = false;
        var stepCount = (int)MathF.Round(duration / deltaTime);

        for (var step = 0; step < stepCount; step++)
        {
            var thrustMultiplier =
                AircraftAerodynamicsCore.NativeThrustForceMultiplier(
                    authoredThrustForceMultiplier: 1000f,
                    nativeMaximumThrottle,
                    mass,
                    engineRating,
                    axialSpeed: velocity.Y,
                    densityMultiplier,
                    powerTransitionSpeed: 45f,
                    engine);
            var thrustAcceleration =
                thrustMultiplier *
                nativeMaximumThrottle *
                densityMultiplier /
                mass;
            var previousVerticalSpeed = velocity.Y;
            velocity += Vector3.UnitY *
                        (thrustAcceleration -
                         AircraftAerodynamicsCore.StandardGravity) *
                        deltaTime;
            velocity /= 1f + drag * deltaTime;
            position += velocity * deltaTime;

            Finite(position,
                "Native-recovery vertical integration produced a non-finite position.");
            Finite(velocity,
                "Native-recovery vertical integration produced a non-finite velocity.");
            maximumAltitude = MathF.Max(maximumAltitude, position.Y);
            maximumSpecificEnergy = MathF.Max(
                maximumSpecificEnergy,
                SpecificMechanicalEnergy(position, velocity));
            if (!reachedApex && previousVerticalSpeed > 0f && velocity.Y <= 0f)
            {
                reachedApex = true;
                apexTime = (step + 1) * deltaTime;
            }
        }

        return new FlightSnapshot(
            position,
            velocity,
            maximumAltitude,
            apexTime,
            reachedApex,
            initialSpecificEnergy,
            SpecificMechanicalEnergy(position, velocity),
            maximumSpecificEnergy);
    }

    private static Vector3 IntegrateNativeRecoveryCoast(
        Vector3 initialVelocity,
        float duration,
        bool gravityEnabled)
    {
        const float deltaTime = 0.01f;
        var drag = AircraftAerodynamicsCore.MapAerodynamicDragToRigidbodyDrag(
            0.075f,
            1f);
        var velocity = initialVelocity;
        var stepCount = (int)MathF.Round(duration / deltaTime);
        for (var step = 0; step < stepCount; step++)
        {
            var nativeRequestedVelocity = velocity * 0.98f;
            velocity = AircraftAerodynamicsCore.PreserveRequestedVelocityDirection(
                nativeRequestedVelocity,
                velocity,
                eligible: true);
            if (gravityEnabled)
            {
                velocity -=
                    Vector3.UnitY *
                    AircraftAerodynamicsCore.StandardGravity *
                    deltaTime;
            }

            velocity /= 1f + drag * deltaTime;
            Finite(velocity,
                "Native-recovery coast integration produced a non-finite velocity.");
        }

        return velocity;
    }

    private static float IntegrateNativeRecoveryPoweredFlight(
        float initialSpeed,
        float turnRateDegreesPerSecond,
        float duration)
    {
        const float mass = 3000f;
        const float density = 1.225f;
        const float densityMultiplier = 1f;
        const float wingArea = 22f;
        const float aspectRatio = 6f;
        const float efficiency = 0.8f;
        const float stallSpeed = 35f;
        const float nativeMaximumThrottle = 100f;
        const float deltaTime = 0.01f;
        var engine = AircraftEngineParameters.Default;
        var drag = AircraftAerodynamicsCore.MapAerodynamicDragToRigidbodyDrag(
            0.075f,
            1f);
        var direction = Vector3.UnitZ;
        var speed = initialSpeed;
        var turnStepRadians =
            turnRateDegreesPerSecond *
            MathF.PI /
            180f *
            deltaTime;
        var stepCount = (int)MathF.Round(duration / deltaTime);

        for (var step = 0; step < stepCount; step++)
        {
            var previousVelocity = direction * speed;
            direction = Vector3.Normalize(
                Vector3.Transform(
                    direction,
                    Quaternion.CreateFromAxisAngle(
                        Vector3.UnitY,
                        turnStepRadians)));
            var currentVelocity = direction * speed;
            var measuredTurnRate =
                AircraftAerodynamicsCore.FlightPathTurnRateRadiansPerSecond(
                    previousVelocity,
                    currentVelocity,
                    deltaTime);
            var maneuverDrag =
                AircraftAerodynamicsCore.NativeManeuverDragAcceleration(
                    mass,
                    measuredTurnRate,
                    density,
                    wingArea,
                    aspectRatio,
                    efficiency,
                    speed,
                    stallSpeed,
                    1f);
            var thrustMultiplier =
                AircraftAerodynamicsCore.NativeThrustForceMultiplier(
                    authoredThrustForceMultiplier: 1000f,
                    nativeMaximumThrottle,
                    mass,
                    engineRating: 10f,
                    axialSpeed: speed,
                    densityMultiplier,
                    powerTransitionSpeed: 45f,
                    engine);
            var thrustAcceleration =
                thrustMultiplier *
                nativeMaximumThrottle *
                densityMultiplier /
                mass;

            speed += (thrustAcceleration - maneuverDrag) * deltaTime;
            speed /= 1f + drag * deltaTime;
            Finite(speed,
                "Native-recovery powered-flight integration produced non-finite speed.");
            True(speed > 0f,
                "Native-recovery powered-flight integration reversed its speed.");
        }

        return speed;
    }

    private static FlightSnapshot IntegrateTranslation(
        Vector3 initialVelocity,
        Vector3 forward,
        Vector3 up,
        float duration,
        float deltaTime,
        float engineSpool)
    {
        const float mass = 3000f;
        var position = Vector3.Zero;
        var velocity = initialVelocity;
        var maximumAltitude = position.Y;
        var initialSpecificEnergy = SpecificMechanicalEnergy(position, velocity);
        var maximumSpecificEnergy = initialSpecificEnergy;
        var apexTime = float.NaN;
        var reachedApex = false;
        var stepCount = (int)MathF.Round(duration / deltaTime);

        for (var step = 0; step < stepCount; step++)
        {
            var evaluation = AircraftAerodynamicsCore.EvaluateForces(
                new AircraftForceInput(
                    new AircraftKinematicState(
                        velocity,
                        Vector3.Zero,
                        forward,
                        up),
                    AircraftSurfaceCommands.Neutral,
                    mass,
                    5f,
                    engineSpool,
                    1f,
                    1f),
                AircraftAerodynamicsParameters.DefaultFighter,
                AircraftEngineParameters.Default);
            var previousVerticalSpeed = velocity.Y;
            var acceleration =
                evaluation.TotalForce / mass -
                Vector3.UnitY * AircraftAerodynamicsCore.StandardGravity;
            velocity += acceleration * deltaTime;
            position += velocity * deltaTime;

            Finite(position, "Flight integration produced a non-finite position.");
            Finite(velocity, "Flight integration produced a non-finite velocity.");
            maximumAltitude = MathF.Max(maximumAltitude, position.Y);
            maximumSpecificEnergy = MathF.Max(
                maximumSpecificEnergy,
                SpecificMechanicalEnergy(position, velocity));
            if (!reachedApex && previousVerticalSpeed > 0f && velocity.Y <= 0f)
            {
                reachedApex = true;
                apexTime = (step + 1) * deltaTime;
            }
        }

        return new FlightSnapshot(
            position,
            velocity,
            maximumAltitude,
            apexTime,
            reachedApex,
            initialSpecificEnergy,
            SpecificMechanicalEnergy(position, velocity),
            maximumSpecificEnergy);
    }

    private static float SpecificMechanicalEnergy(Vector3 position, Vector3 velocity)
        => 0.5f * velocity.LengthSquared() +
           AircraftAerodynamicsCore.StandardGravity * position.Y;

    private readonly record struct FlightSnapshot(
        Vector3 Position,
        Vector3 Velocity,
        float MaximumAltitude,
        float ApexTime,
        bool ReachedApex,
        float InitialSpecificEnergy,
        float FinalSpecificEnergy,
        float MaximumSpecificEnergy);

    private static void Orthogonal(Vector3 left, Vector3 right, string message)
    {
        var scale = left.Length() * right.Length();
        if (scale <= 0.000001f)
            return;
        True(MathF.Abs(Vector3.Dot(left, right)) <= scale * 0.00001f + 0.001f,
            message);
    }

    private static void Finite(float value, string message)
    {
        if (!float.IsFinite(value))
            throw new InvalidOperationException(message);
    }

    private static void Finite(Vector3 value, string message)
    {
        if (!AircraftAerodynamicsCore.IsFinite(value))
            throw new InvalidOperationException(message);
    }

    private static void Near(float expected, float actual, float tolerance, string message)
    {
        if (!float.IsFinite(actual) || MathF.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"{message} Expected={expected}; Actual={actual}");
        }
    }

    private static void Near(Vector3 expected, Vector3 actual, float tolerance, string message)
    {
        if (!AircraftAerodynamicsCore.IsFinite(actual) ||
            Vector3.Distance(expected, actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"{message} Expected={expected}; Actual={actual}");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void False(bool condition, string message) => True(!condition, message);

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{message} Expected={expected}; Actual={actual}");
        }
    }
}
