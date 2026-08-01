using System.Numerics;
using System.Runtime.CompilerServices;
using ER2RealismOverhaul;

internal static class AircraftMouseInstructorScenarios
{
    private const float DegreesToRadians = MathF.PI / 180f;

    [ModuleInitializer]
    internal static void RunAll()
    {
        var tests = new (string Name, Action Run)[]
        {
            (nameof(LateralDiveBlendsFromDiagonalPushIntoBankedPull),
                LateralDiveBlendsFromDiagonalPushIntoBankedPull),
            (nameof(DiagonalAimCommandsPitchAndRollTogether),
                DiagonalAimCommandsPitchAndRollTogether),
            (nameof(DescendingTurnPlaneHandoffKeepsPitchAndRollContinuous),
                DescendingTurnPlaneHandoffKeepsPitchAndRollContinuous),
            (nameof(DescendingTurnPullWaitsForTheCorrectBankSide),
                DescendingTurnPullWaitsForTheCorrectBankSide),
            (nameof(StraightAheadDiveUsesFullMousePushAcrossTheForwardView),
                StraightAheadDiveUsesFullMousePushAcrossTheForwardView),
            (nameof(VerticalFlightRetainsBodyRelativeNoseDownAuthority),
                VerticalFlightRetainsBodyRelativeNoseDownAuthority),
            (nameof(ShallowBankBodyDownAimRetainsNoseDownCommand),
                ShallowBankBodyDownAimRetainsNoseDownCommand),
            (nameof(BodyDownRouteBlendsAcrossLateralAndBankThresholds),
                BodyDownRouteBlendsAcrossLateralAndBankThresholds),
            (nameof(InvertedTopOfLoopKeepsPositiveGPull),
                InvertedTopOfLoopKeepsPositiveGPull),
            (nameof(RearRouteKeepsOneRollDirectionAcrossThePole),
                RearRouteKeepsOneRollDirectionAcrossThePole),
            (nameof(RearRouteReleasesWhileMouseInputContinues),
                RearRouteReleasesWhileMouseInputContinues),
            (nameof(FineLateralAimUsesRudderAndLevelsWithoutBanking),
                FineLateralAimUsesRudderAndLevelsWithoutBanking),
            (nameof(FineLateralAimLevelsBankDespiteVerticalOffset),
                FineLateralAimLevelsBankDespiteVerticalOffset),
            (nameof(HorizontalAimBlendsContinuouslyIntoBank),
                HorizontalAimBlendsContinuouslyIntoBank),
            (nameof(HorizontalTurnStartsBankAndPullTogether),
                HorizontalTurnStartsBankAndPullTogether),
            (nameof(ModerateRateErrorsReceivePromptDampedControl),
                ModerateRateErrorsReceivePromptDampedControl),
            (nameof(MaterialCircleErrorKeepsStrongControlWhileAlreadyTurning),
                MaterialCircleErrorKeepsStrongControlWhileAlreadyTurning),
            (nameof(FighterRollLimitsKeepFullAileronAtCombatRollRate),
                FighterRollLimitsKeepFullAileronAtCombatRollRate),
            (nameof(RollControllerBrakesBeforeCaptureWithoutFlutter),
                RollControllerBrakesBeforeCaptureWithoutFlutter),
            (nameof(AngleOfAttackProtectionOnlyUnloads),
                AngleOfAttackProtectionOnlyUnloads),
            (nameof(ContinuousSlowMouseInputDoesNotQuietCapture),
                ContinuousSlowMouseInputDoesNotQuietCapture),
            (nameof(NearCaptureBrakesPitchRateWithoutHorizonTarget),
                NearCaptureBrakesPitchRateWithoutHorizonTarget),
            (nameof(InvertedFineBodyPullIsNotReplacedByWorldLeveling),
                InvertedFineBodyPullIsNotReplacedByWorldLeveling),
            (nameof(MovingFineLeadRetainsBankAndLoad),
                MovingFineLeadRetainsBankAndLoad),
            (nameof(MaterialHeadingErrorKeepsPursuitBankAtCombatTurnRate),
                MaterialHeadingErrorKeepsPursuitBankAtCombatTurnRate),
            (nameof(MovingAimVelocityCannotCreateOrReverseBank),
                MovingAimVelocityCannotCreateOrReverseBank),
            (nameof(LargeOffsetAimReversalStartsRollingOutImmediately),
                LargeOffsetAimReversalStartsRollingOutImmediately),
            (nameof(QuietCaptureBrakesBodyRatesWithoutHorizonLeveling),
                QuietCaptureBrakesBodyRatesWithoutHorizonLeveling),
            (nameof(AllOutputsStayFiniteAndBounded),
                AllOutputsStayFiniteAndBounded)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Run();
                Console.WriteLine($"PASS MOUSE {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine(
                    $"FAIL MOUSE {test.Name}: {exception.Message}");
            }
        }

        if (failures > 0)
        {
            throw new InvalidOperationException(
                $"{tests.Length - failures}/{tests.Length} mouse-instructor scenarios passed.");
        }

        Console.WriteLine(
            $"{tests.Length}/{tests.Length} deterministic mouse-instructor scenarios passed.");
    }

    private static void LateralDiveBlendsFromDiagonalPushIntoBankedPull()
    {
        var aim = Vector3.Normalize(new Vector3(0.42f, -0.42f, 0.82f));
        var state = default(AircraftMouseInstructorState);
        var level = Step(
            ref state,
            forward: Vector3.UnitZ,
            up: Vector3.UnitY,
            aim: aim);

        True(level.Roll > 0.20f,
            "A down-right command did not begin with a right bank.");
        True(level.Pitch < -0.20f,
            "A down-right command rolled without simultaneously pitching toward the circle. " +
            $"Pitch={level.Pitch}; Roll={level.Roll}");
        True(level.WeakPushActive,
            "A body-down diagonal command did not engage direct pitch pursuit.");

        var bankedUp = Vector3.Normalize(new Vector3(1f, -1f, 0f));
        var banked = Step(
            ref state,
            forward: Vector3.UnitZ,
            up: bankedUp,
            aim: aim);

        True(banked.Pitch > 0.20f,
            "After banking into the descending plane, the instructor did not pull positive G. " +
            $"Pitch={banked.Pitch}; Roll={banked.Roll}; Lift={banked.DesiredLiftDirectionWorld}");
        True(MathF.Abs(banked.Roll) < MathF.Abs(level.Roll),
            "Roll demand did not reduce after the lift vector reached the commanded turn plane.");
    }

    private static void DiagonalAimCommandsPitchAndRollTogether()
    {
        foreach (var lateralSign in new[] { -1f, 1f })
        {
            const float lateralDegrees = 25f;
            const float verticalDegrees = 15f;
            var lateralRadians = lateralDegrees * DegreesToRadians;
            var verticalRadians = verticalDegrees * DegreesToRadians;

            foreach (var verticalSign in new[] { -1f, 1f })
            {
                var aim = Vector3.Normalize(new Vector3(
                    lateralSign * MathF.Sin(lateralRadians) *
                        MathF.Cos(verticalRadians),
                    verticalSign * MathF.Sin(verticalRadians),
                    MathF.Cos(lateralRadians) * MathF.Cos(verticalRadians)));
                var state = default(AircraftMouseInstructorState);
                var output = Step(
                    ref state,
                    Vector3.UnitZ,
                    Vector3.UnitY,
                    aim,
                    limits: AircraftMouseInstructorLimits.Fighter);

                True(output.Roll * lateralSign > 0.50f,
                    "A diagonal command did not begin rolling toward the circle. " +
                    $"VerticalSign={verticalSign}; Roll={output.Roll}");
                True(output.Pitch * verticalSign > 0.20f,
                    "A diagonal command did not pitch toward the circle while " +
                    $"rolling. VerticalSign={verticalSign}; Pitch={output.Pitch}; " +
                    $"Roll={output.Roll}");
            }
        }
    }

    private static void DescendingTurnPlaneHandoffKeepsPitchAndRollContinuous()
    {
        foreach (var turnSign in new[] { -1f, 1f })
        {
            var before = DescendingTurnOutput(
                turnSign * 65f,
                turnSign,
                pitchRate: 30f);
            var after = DescendingTurnOutput(
                turnSign * 66f,
                turnSign,
                pitchRate: 30f);

            True(after.Pitch > 0.25f,
                "A one-degree bank change stopped the positive-G pursuit of a held " +
                $"descending turn. Pitch={after.Pitch}; " +
                $"desiredRate={after.DesiredPitchRateDegreesPerSecond}");
            True(MathF.Abs(
                    after.DesiredPitchRateDegreesPerSecond -
                    before.DesiredPitchRateDegreesPerSecond) < 8f,
                "Pitch demand jumped at the descending maneuver-plane handoff. " +
                $"Before={before.DesiredPitchRateDegreesPerSecond}; " +
                $"After={after.DesiredPitchRateDegreesPerSecond}");
            True(MathF.Abs(after.RollErrorDegrees - before.RollErrorDegrees) < 8f,
                "The desired bank snapped to a different turn plane while the " +
                $"circle stayed fixed. Before={before.RollErrorDegrees}; " +
                $"After={after.RollErrorDegrees}");
            True(MathF.Abs(after.Roll - before.Roll) < 0.35f,
                "A one-degree bank change caused an aileron snap while pursuing " +
                $"the same circle. Before={before.Roll}; After={after.Roll}");
        }
    }

    private static void DescendingTurnPullWaitsForTheCorrectBankSide()
    {
        foreach (var turnSign in new[] { -1f, 1f })
        {
            var wrongWay = DescendingTurnOutput(
                -turnSign * 30f,
                turnSign,
                pitchRate: 0f);
            True(wrongWay.Roll * turnSign > 0.80f,
                "A descending lateral command did not strongly correct an " +
                $"opposite bank. Roll={wrongWay.Roll}");
            True(wrongWay.Pitch <= 0.10f,
                "The instructor pulled hard before the lift vector was on the " +
                $"correct side of the descending turn. Pitch={wrongWay.Pitch}");

            var established = DescendingTurnOutput(
                turnSign * 100f,
                turnSign,
                pitchRate: 30f);
            True(established.Pitch > 0.50f,
                "The instructor did not keep pulling once banked into the held " +
                $"descending turn. Pitch={established.Pitch}");
            True(established.Roll * turnSign > 0f,
                "The instructor stopped rolling toward the held descending " +
                $"maneuver plane too early. Roll={established.Roll}");
        }
    }

    private static AircraftMouseInstructorOutput DescendingTurnOutput(
        float bankDegrees,
        float turnSign,
        float pitchRate)
    {
        const float lateralDegrees = 25f;
        const float downDegrees = 15f;
        var lateralRadians = lateralDegrees * DegreesToRadians;
        var downRadians = downDegrees * DegreesToRadians;
        var aim = Vector3.Normalize(new Vector3(
            turnSign * MathF.Sin(lateralRadians) * MathF.Cos(downRadians),
            -MathF.Sin(downRadians),
            MathF.Cos(lateralRadians) * MathF.Cos(downRadians)));
        var state = default(AircraftMouseInstructorState);
        return Step(
            ref state,
            Vector3.UnitZ,
            RightBankedUp(Vector3.UnitZ, Vector3.UnitY, bankDegrees),
            aim,
            pitchRate: pitchRate,
            limits: AircraftMouseInstructorLimits.Fighter);
    }

    private static void StraightAheadDiveUsesFullMousePushAcrossTheForwardView()
    {
        var previousMagnitude = 0f;
        foreach (var downAngle in new[] { 5f, 29f, 70f, 88.5f })
        {
            var state = default(AircraftMouseInstructorState);
            var aim = RotateAroundAxis(
                Vector3.UnitZ,
                Vector3.UnitX,
                downAngle * DegreesToRadians);
            var output = Step(
                ref state,
                forward: Vector3.UnitZ,
                up: Vector3.UnitY,
                aim: aim);

            True(output.WeakPushActive,
                $"A {downAngle:0}-degree straight-down command left the direct-push path.");
            True(output.Pitch < -0.05f,
                $"A {downAngle:0}-degree straight-down command did not push the nose down.");
            True(MathF.Abs(output.Roll) < 0.001f,
                $"A {downAngle:0}-degree straight-down command added an unwanted roll.");
            True(MathF.Abs(output.Pitch) + 0.0001f >= previousMagnitude,
                "Nose-down authority decreased as the command moved farther below the nose.");
            previousMagnitude = MathF.Abs(output.Pitch);
        }

        True(previousMagnitude > 0.90f,
            "A deep straight-down mouse command still could not request full instructor push.");
    }

    private static void VerticalFlightRetainsBodyRelativeNoseDownAuthority()
    {
        var forward = Vector3.UnitY;
        var up = -Vector3.UnitZ;
        var aim = RotateAroundAxis(
            forward,
            Vector3.UnitX,
            88.5f * DegreesToRadians);
        var state = default(AircraftMouseInstructorState);

        var output = Step(
            ref state,
            forward,
            up,
            aim);

        True(output.WeakPushActive,
            "A body-relative push was disabled when the aircraft pointed vertically.");
        True(output.Pitch < -0.90f,
            "A steep downward aim during vertical flight did not request strong nose-down authority.");
        True(MathF.Abs(output.Roll) < 0.001f,
            "A straight body-relative push during vertical flight introduced an unwanted roll.");
    }

    private static void ShallowBankBodyDownAimRetainsNoseDownCommand()
    {
        const float bankDegrees = 12f;
        const float downDegrees = 35f;
        var forward = Vector3.UnitZ;
        var up = RightBankedUp(
            forward,
            Vector3.UnitY,
            bankDegrees);
        var right = Vector3.Normalize(Vector3.Cross(up, forward));
        var aim = RotateAroundAxis(
            forward,
            right,
            downDegrees * DegreesToRadians);
        var state = default(AircraftMouseInstructorState);

        var output = Step(
            ref state,
            forward,
            up,
            aim);

        True(output.WeakPushActive,
            "A body-down aim left the direct-push path during a shallow bank.");
        True(output.Pitch < -0.50f,
            "A shallow bank suppressed the requested body-relative nose-down command. " +
            $"Pitch={output.Pitch}; Roll={output.Roll}");
        True(output.Roll <= 0.001f,
            "A body-down aim increased the existing bank instead of leveling it.");
    }

    private static void BodyDownRouteBlendsAcrossLateralAndBankThresholds()
    {
        var belowLateral = BodyDownOutput(
            bankDegrees: 0f,
            lateralDegrees: 5.95f);
        var aboveLateral = BodyDownOutput(
            bankDegrees: 0f,
            lateralDegrees: 6.05f);
        AssertBodyDownRouteContinuity(
            belowLateral,
            aboveLateral,
            "six-degree lateral");

        var belowBank = BodyDownOutput(
            bankDegrees: 19.95f,
            lateralDegrees: 0f);
        var aboveBank = BodyDownOutput(
            bankDegrees: 20.05f,
            lateralDegrees: 0f);
        AssertBodyDownRouteContinuity(
            belowBank,
            aboveBank,
            "twenty-degree bank");
    }

    private static AircraftMouseInstructorOutput BodyDownOutput(
        float bankDegrees,
        float lateralDegrees)
    {
        const float downDegrees = 35f;
        var forward = Vector3.UnitZ;
        var up = RightBankedUp(
            forward,
            Vector3.UnitY,
            bankDegrees);
        var right = Vector3.Normalize(Vector3.Cross(up, forward));
        var downRadians = downDegrees * DegreesToRadians;
        var lateralRadians = lateralDegrees * DegreesToRadians;
        var aim = Vector3.Normalize(
            forward *
            (MathF.Cos(downRadians) * MathF.Cos(lateralRadians)) +
            right *
            (MathF.Cos(downRadians) * MathF.Sin(lateralRadians)) -
            up * MathF.Sin(downRadians));
        var state = default(AircraftMouseInstructorState);
        return Step(
            ref state,
            forward,
            up,
            aim);
    }

    private static void AssertBodyDownRouteContinuity(
        AircraftMouseInstructorOutput below,
        AircraftMouseInstructorOutput above,
        string boundary)
    {
        True(MathF.Abs(above.Pitch - below.Pitch) < 0.15f,
            $"Pitch snapped across the {boundary} handoff. " +
            $"Below={below.Pitch}; Above={above.Pitch}");
        True(MathF.Abs(above.Roll - below.Roll) < 0.20f,
            $"Roll snapped across the {boundary} handoff. " +
            $"Below={below.Roll}; Above={above.Roll}");
        True(MathF.Abs(
                 above.DesiredPitchRateDegreesPerSecond -
                 below.DesiredPitchRateDegreesPerSecond) < 8f,
            $"Desired pitch rate snapped across the {boundary} handoff. " +
            $"Below={below.DesiredPitchRateDegreesPerSecond}; " +
            $"Above={above.DesiredPitchRateDegreesPerSecond}");
        True(AngleDegrees(
                 below.DesiredLiftDirectionWorld,
                 above.DesiredLiftDirectionWorld) < 8f,
            $"Maneuver plane snapped across the {boundary} handoff.");
    }

    private static void InvertedTopOfLoopKeepsPositiveGPull()
    {
        var forward = -Vector3.UnitZ;
        var up = -Vector3.UnitY;
        var aim = Vector3.Normalize(
            forward * 0.82f +
            up * 0.57f);
        var state = default(AircraftMouseInstructorState);

        var output = Step(
            ref state,
            forward,
            up,
            aim);

        True(output.Pitch > 0.15f,
            "At the inverted top of a loop, a body-up aim did not retain a positive-G pull. " +
            $"Pitch={output.Pitch}; Roll={output.Roll}; Lift={output.DesiredLiftDirectionWorld}");
        False(output.WeakPushActive,
            "The top-of-loop continuation was mistaken for a world-horizon nose-down command.");
        True(output.Pitch >= 0f,
            "The instructor pushed forward while continuing an inverted positive-G loop.");
    }

    private static void RearRouteKeepsOneRollDirectionAcrossThePole()
    {
        var state = default(AircraftMouseInstructorState);
        var samples = new[] { 150f, 165f, 176f, 179f, 181f, 184f, 195f };
        var observedSigns = new List<int>();
        var routeWasLatched = false;

        foreach (var angle in samples)
        {
            var aim = RotateAroundAxis(
                Vector3.UnitZ,
                Vector3.UnitY,
                angle * DegreesToRadians);
            var output = Step(
                ref state,
                Vector3.UnitZ,
                Vector3.UnitY,
                aim,
                aimAngularVelocity: Vector3.UnitY * 25f);

            routeWasLatched |= output.RouteLatched;
            if (MathF.Abs(output.DesiredRollRateDegreesPerSecond) > 0.5f)
            {
                observedSigns.Add(
                    MathF.Sign(output.DesiredRollRateDegreesPerSecond));
            }
        }

        True(routeWasLatched,
            "The instructor did not latch a deliberate route near the rear hemisphere.");
        True(observedSigns.Count >= 4,
            "The rear-route samples did not produce enough meaningful roll commands.");
        Equal(1, observedSigns.Distinct().Count(),
            "Roll direction chattered or reversed while the aim crossed directly behind.");
    }

    private static void RearRouteReleasesWhileMouseInputContinues()
    {
        var state = default(AircraftMouseInstructorState);
        var rearAim = RotateAroundAxis(
            Vector3.UnitZ,
            Vector3.UnitY,
            150f * DegreesToRadians);
        var latched = Step(
            ref state,
            Vector3.UnitZ,
            Vector3.UnitY,
            rearAim,
            aimAngularVelocity: Vector3.UnitY * 20f);

        True(latched.RouteLatched,
            "A deliberate rear-hemisphere command did not latch a stable route.");

        var forwardAim = RotateAroundAxis(
            Vector3.UnitZ,
            Vector3.UnitY,
            20f * DegreesToRadians);
        var released = Step(
            ref state,
            Vector3.UnitZ,
            Vector3.UnitY,
            forwardAim,
            aimAngularVelocity: Vector3.UnitY * 20f);

        False(released.RouteLatched,
            "The rear-route latch waited for mouse input to stop after the command returned forward.");
    }

    private static void FineLateralAimUsesRudderAndLevelsWithoutBanking()
    {
        var aim = RotateAroundAxis(
            Vector3.UnitZ,
            Vector3.UnitY,
            3f * DegreesToRadians);
        var levelState = default(AircraftMouseInstructorState);
        var level = Step(
            ref levelState,
            Vector3.UnitZ,
            Vector3.UnitY,
            aim,
            aimAngularVelocity: Vector3.UnitY * 3f);

        True(MathF.Abs(level.RollErrorDegrees) < 0.1f,
            "A three-degree fine-aim correction created a bank target.");
        True(MathF.Abs(level.Roll) < 0.01f,
            "A three-degree fine-aim correction used aileron instead of rudder.");
        True(level.Yaw > 0.05f,
            "A three-degree fine-aim correction did not use right rudder.");
        Near(0f, level.Pitch, 0.0001f,
            "A level fine-aim correction introduced an elevator command.");

        var bankedState = default(AircraftMouseInstructorState);
        var banked = Step(
            ref bankedState,
            Vector3.UnitZ,
            RightBankedUp(Vector3.UnitZ, Vector3.UnitY, 20f),
            aim);
        True(banked.Roll < -0.30f,
            "Fine aim did not actively level an existing right bank.");
        True(banked.Yaw > 0.05f,
            "Leveling an existing bank removed the fine rudder correction.");
    }

    private static void FineLateralAimLevelsBankDespiteVerticalOffset()
    {
        const float horizontalDegrees = 3f;
        const float verticalDegrees = 9f;
        var horizontalRadians = horizontalDegrees * DegreesToRadians;
        var verticalRadians = verticalDegrees * DegreesToRadians;
        var aim = Vector3.Normalize(new Vector3(
            MathF.Sin(horizontalRadians) * MathF.Cos(verticalRadians),
            MathF.Sin(verticalRadians),
            MathF.Cos(horizontalRadians) * MathF.Cos(verticalRadians)));
        var state = default(AircraftMouseInstructorState);
        var output = Step(
            ref state,
            Vector3.UnitZ,
            RightBankedUp(Vector3.UnitZ, Vector3.UnitY, 45f),
            aim);

        True(output.RollErrorDegrees < -40f,
            "A fine horizon-relative lateral aim retained the existing bank when " +
            "the aim also had a vertical offset.");
        True(output.Roll < -0.50f,
            "The instructor did not apply enough opposite aileron to level the bank.");
        True(Vector3.Dot(output.DesiredLiftDirectionWorld, Vector3.UnitY) > 0.995f,
            "Fine lateral correction did not command a horizon-level lift direction.");
    }

    private static void HorizontalAimBlendsContinuouslyIntoBank()
    {
        var aimAngles = new[] { 4f, 8f, 12f, 16f, 24f, 30f };
        var expectedBanks = new[] { 0f, 7.1f, 23.9f, 44.1f, 68f, 68f };
        var previousBank = float.NegativeInfinity;

        for (var index = 0; index < aimAngles.Length; index++)
        {
            var state = default(AircraftMouseInstructorState);
            var output = Step(
                ref state,
                Vector3.UnitZ,
                Vector3.UnitY,
                RotateAroundAxis(
                    Vector3.UnitZ,
                    Vector3.UnitY,
                    aimAngles[index] * DegreesToRadians));

            Near(
                expectedBanks[index],
                output.RollErrorDegrees,
                0.35f,
                $"The {aimAngles[index]}-degree lateral aim used the wrong bank schedule.");
            True(output.RollErrorDegrees + 0.001f >= previousBank,
                "Increasing lateral aim reduced the requested bank.");
            True(output.RollErrorDegrees <= 68.1f,
                "A near-level lateral turn exceeded the pursuit-bank limit.");
            previousBank = output.RollErrorDegrees;
        }

        var belowState = default(AircraftMouseInstructorState);
        var aboveState = default(AircraftMouseInstructorState);
        var below = Step(
            ref belowState,
            Vector3.UnitZ,
            Vector3.UnitY,
            RotateAroundAxis(
                Vector3.UnitZ,
                Vector3.UnitY,
                3.9f * DegreesToRadians));
        var above = Step(
            ref aboveState,
            Vector3.UnitZ,
            Vector3.UnitY,
            RotateAroundAxis(
                Vector3.UnitZ,
                Vector3.UnitY,
                4.1f * DegreesToRadians));
        True(MathF.Abs(above.RollErrorDegrees - below.RollErrorDegrees) < 0.1f,
            "The rudder-to-bank crossover introduced a threshold snap.");
    }

    private static void HorizontalTurnStartsBankAndPullTogether()
    {
        var aim = RotateAroundAxis(
            Vector3.UnitZ,
            Vector3.UnitY,
            12f * DegreesToRadians);
        var levelState = default(AircraftMouseInstructorState);
        var level = Step(
            ref levelState,
            Vector3.UnitZ,
            Vector3.UnitY,
            aim);

        True(level.Roll > 0.50f,
            "A twelve-degree horizontal command did not begin banking.");
        True(level.Yaw > 0.05f,
            "A twelve-degree horizontal command did not coordinate rudder.");
        True(level.Pitch > 0.02f && level.Pitch < 0.20f,
            "A horizontal turn did not begin a bounded pull alongside its bank. " +
            $"Pitch={level.Pitch}; Roll={level.Roll}");

        var bankedState = default(AircraftMouseInstructorState);
        var banked = Step(
            ref bankedState,
            Vector3.UnitZ,
            RightBankedUp(Vector3.UnitZ, Vector3.UnitY, 24f),
            aim);
        True(banked.Pitch > 0.15f,
            "The instructor did not pull after reaching the commanded turn plane.");

        var largeAim = RotateAroundAxis(
            Vector3.UnitZ,
            Vector3.UnitY,
            60f * DegreesToRadians);
        var largeState = default(AircraftMouseInstructorState);
        var large = Step(
            ref largeState,
            Vector3.UnitZ,
            Vector3.UnitY,
            largeAim,
            limits: AircraftMouseInstructorLimits.Fighter);
        True(large.Roll > 0.95f,
            "A large horizontal command did not retain full initial aileron.");
        True(large.Pitch > 0.15f && large.Pitch < 0.35f,
            "The bank-acquisition pull was absent or large enough to create a climb. " +
            $"Pitch={large.Pitch}; Roll={large.Roll}");

        var invertedState = default(AircraftMouseInstructorState);
        var inverted = Step(
            ref invertedState,
            Vector3.UnitZ,
            -Vector3.UnitY,
            largeAim,
            limits: AircraftMouseInstructorLimits.Fighter);
        Near(0f, inverted.Pitch, 0.0001f,
            "The upright bank-acquisition preload also pulled while inverted.");
    }

    private static void ModerateRateErrorsReceivePromptDampedControl()
    {
        var pitchAim = RotateAroundAxis(
            Vector3.UnitZ,
            -Vector3.UnitX,
            5f * DegreesToRadians);
        var pitchState = default(AircraftMouseInstructorState);
        var pitch = Step(
            ref pitchState,
            Vector3.UnitZ,
            Vector3.UnitY,
            pitchAim,
            limits: AircraftMouseInstructorLimits.Fighter);
        True(pitch.Pitch > 0.70f && pitch.Pitch < 0.80f,
            "A moderate pitch error still produced a sluggish or saturated response. " +
            $"Pitch={pitch.Pitch}");

        var levelState = default(AircraftMouseInstructorState);
        var leveling = Step(
            ref levelState,
            Vector3.UnitZ,
            RightBankedUp(Vector3.UnitZ, Vector3.UnitY, 10f),
            Vector3.UnitZ,
            limits: AircraftMouseInstructorLimits.Fighter);
        True(leveling.Roll <= -0.95f && leveling.Roll >= -1f,
            "A moderate bank did not receive prompt full leveling aileron. " +
            $"Roll={leveling.Roll}");
    }

    private static void MaterialCircleErrorKeepsStrongControlWhileAlreadyTurning()
    {
        var pitchAim = RotateAroundAxis(
            Vector3.UnitZ,
            -Vector3.UnitX,
            8f * DegreesToRadians);
        var pitchState = default(AircraftMouseInstructorState);
        var pitching = Step(
            ref pitchState,
            Vector3.UnitZ,
            Vector3.UnitY,
            pitchAim,
            pitchRate: 7f,
            limits: AircraftMouseInstructorLimits.Fighter);
        True(pitching.Pitch > 0.75f && pitching.Pitch < 0.90f,
            "The instructor backed off elevator while the nose was still " +
            "materially short of the circle. " +
            $"Pitch={pitching.Pitch}; DesiredRate={pitching.DesiredPitchRateDegreesPerSecond}");

        var turnAim = RotateAroundAxis(
            Vector3.UnitZ,
            Vector3.UnitY,
            12f * DegreesToRadians);
        var turnState = default(AircraftMouseInstructorState);
        var turning = Step(
            ref turnState,
            Vector3.UnitZ,
            RightBankedUp(Vector3.UnitZ, Vector3.UnitY, 8f),
            turnAim,
            rollRate: 18f,
            limits: AircraftMouseInstructorLimits.Fighter);
        True(turning.Roll > 0.80f,
            "The instructor backed off aileron while a banked aircraft was " +
            "still materially short of the circle. " +
            $"Roll={turning.Roll}; Error={turning.RollErrorDegrees}; " +
            $"DesiredRate={turning.DesiredRollRateDegreesPerSecond}");
    }

    private static void RollControllerBrakesBeforeCaptureWithoutFlutter()
    {
        var aim = RotateAroundAxis(
            Vector3.UnitZ,
            Vector3.UnitY,
            16f * DegreesToRadians);
        var state = default(AircraftMouseInstructorState);
        // A sixteen-degree horizon-relative aim requests about 44 degrees of
        // bank. Sample the final approach to that fixed target; the controller
        // should counter-roll before crossing it instead of chasing a bank
        // schedule that changes with the aircraft's current attitude.
        var bankAngles = new[] { 37f, 39f, 41f, 42.5f, 43f };
        var rollRates = new[] { 28f, 22f, 16f, 10f, 5f };
        var brakingCommands = new List<float>();

        for (var index = 0; index < bankAngles.Length; index++)
        {
            var up = RightBankedUp(
                Vector3.UnitZ,
                Vector3.UnitY,
                bankAngles[index]);
            var output = Step(
                ref state,
                Vector3.UnitZ,
                up,
                aim,
                rollRate: rollRates[index]);

            True(output.RollErrorDegrees > 0f,
                $"The aircraft crossed the target bank before braking sample {index}: " +
                $"bank={bankAngles[index]:0.0}, error={output.RollErrorDegrees:0.00}.");
            True(output.DesiredRollRateDegreesPerSecond >= 0f,
                "The position planner reversed its route before reaching the target bank.");
            brakingCommands.Add(output.Roll);
        }

        True(brakingCommands.All(command => command < 0f),
            "The rate controller did not apply counter-aileron before bank capture. " +
            $"Commands={string.Join(", ", brakingCommands.Select(value => value.ToString("0.000")))}");
        True(brakingCommands.All(command => command <= 0f),
            "Near-capture counter-aileron alternated sides and would create visible flutter.");
    }

    private static void FighterRollLimitsKeepFullAileronAtCombatRollRate()
    {
        var aim = Vector3.UnitX;
        var fighterState = default(AircraftMouseInstructorState);
        var bomberState = default(AircraftMouseInstructorState);
        var fighter = Step(
            ref fighterState,
            Vector3.UnitZ,
            Vector3.UnitY,
            aim,
            rollRate: 50f,
            limits: AircraftMouseInstructorLimits.Fighter);
        var bomber = Step(
            ref bomberState,
            Vector3.UnitZ,
            Vector3.UnitY,
            aim,
            rollRate: 50f,
            limits: AircraftMouseInstructorLimits.Default);

        True(fighter.Roll > 0.95f,
            "A large fighter bank command unloaded aileron at only fifty degrees per second.");
        True(bomber.Roll < 0.35f,
            "The fighter regression did not distinguish the retained bomber roll limits.");
    }

    private static void AngleOfAttackProtectionOnlyUnloads()
    {
        var aim = RotateAroundAxis(
            Vector3.UnitZ,
            -Vector3.UnitX,
            32f * DegreesToRadians);
        var protectedState = default(AircraftMouseInstructorState);
        var unprotectedState = default(AircraftMouseInstructorState);

        var unprotected = Step(
            ref unprotectedState,
            Vector3.UnitZ,
            Vector3.UnitY,
            aim,
            angleOfAttack: 0f,
            criticalAngleOfAttack: 16f);
        True(unprotected.Pitch > 0.15f,
            "The positive-AoA scenario did not establish a pull command.");

        var previousPitch = float.PositiveInfinity;
        foreach (var angleOfAttack in new[] { 12f, 14f, 16f, 18f, 22f, 40f })
        {
            var output = Step(
                ref protectedState,
                Vector3.UnitZ,
                Vector3.UnitY,
                aim,
                angleOfAttack: angleOfAttack,
                criticalAngleOfAttack: 16f);

            True(output.Pitch >= -0.000001f,
                "Positive-AoA protection converted unloading into a forward-stick push.");
            True(output.Pitch <= previousPitch + 0.0001f,
                "Positive-AoA protection increased pull as the wing approached separation.");
            previousPitch = output.Pitch;
        }

        Near(0f, previousPitch, 0.0001f,
            "Far beyond critical AoA, the instructor continued adding positive pull.");
    }

    private static void ContinuousSlowMouseInputDoesNotQuietCapture()
    {
        var aim = RotateAroundAxis(
            Vector3.UnitZ,
            Vector3.UnitY,
            DegreesToRadians);
        var state = default(AircraftMouseInstructorState);
        var output = default(AircraftMouseInstructorOutput);

        for (var sample = 0; sample < 12; sample++)
        {
            output = Step(
                ref state,
                Vector3.UnitZ,
                Vector3.UnitY,
                aim,
                aimAngularVelocity: Vector3.UnitY * 0.75f);
            False(output.AimCaptured,
                "A slow but deliberate mouse command was mislabeled as quiet capture.");
        }

        for (var sample = 0; sample < 6; sample++)
        {
            output = Step(
                ref state,
                Vector3.UnitZ,
                Vector3.UnitY,
                aim);
        }

        True(output.AimCaptured,
            "The same near-nose command did not capture after mouse input actually stopped.");
    }

    private static void NearCaptureBrakesPitchRateWithoutHorizonTarget()
    {
        var climbAngle = 22f * DegreesToRadians;
        var forward = Vector3.Normalize(new Vector3(
            0f,
            MathF.Sin(climbAngle),
            MathF.Cos(climbAngle)));
        var levelUp = Vector3.Normalize(new Vector3(
            0f,
            MathF.Cos(climbAngle),
            -MathF.Sin(climbAngle)));
        var bankedUp = RightBankedUp(forward, levelUp, 24f);
        var aim = Vector3.Normalize(
            forward * MathF.Cos(2f * DegreesToRadians) +
            bankedUp * MathF.Sin(2f * DegreesToRadians));
        var state = default(AircraftMouseInstructorState);

        var output = Step(
            ref state,
            forward,
            bankedUp,
            aim,
            pitchRate: 14f,
            rollRate: 11f,
            yawRate: -8f);

        False(output.AimCaptured,
            "A fresh near-nose command captured before its quiet interval.");
        True(output.DesiredPitchRateDegreesPerSecond > 0f,
            "The near-nose command discarded its small positive-G pull target.");
        True(output.Pitch < 0f && output.Pitch >= -0.3501f,
            "The instructor did not apply bounded counter-elevator before overshooting the aim ray.");
        True(output.Roll < 0f,
            "The instructor did not begin arresting the existing right bank.");

        var descendingForward = new Vector3(
            forward.X,
            -forward.Y,
            forward.Z);
        var descendingLevelUp = new Vector3(
            levelUp.X,
            levelUp.Y,
            -levelUp.Z);
        var descendingBankedUp = RightBankedUp(
            descendingForward,
            descendingLevelUp,
            24f);
        var descendingAim = Vector3.Normalize(
            descendingForward * MathF.Cos(2f * DegreesToRadians) +
            descendingBankedUp * MathF.Sin(2f * DegreesToRadians));
        var descendingState = default(AircraftMouseInstructorState);
        var descending = Step(
            ref descendingState,
            descendingForward,
            descendingBankedUp,
            descendingAim,
            pitchRate: 14f,
            rollRate: 11f,
            yawRate: -8f);

        Near(output.Pitch, descending.Pitch, 0.0001f,
            "Near-capture pitch braking changed with world elevation instead of body-relative error.");
    }

    private static void InvertedFineBodyPullIsNotReplacedByWorldLeveling()
    {
        foreach (var pullDegrees in new[] { 2f, 3f, 5f, 8f, 12f })
        {
            var state = default(AircraftMouseInstructorState);
            var invertedUp = -Vector3.UnitY;
            var aim = Vector3.Normalize(
                Vector3.UnitZ *
                MathF.Cos(pullDegrees * DegreesToRadians) +
                invertedUp *
                MathF.Sin(pullDegrees * DegreesToRadians));
            var output = Step(
                ref state,
                Vector3.UnitZ,
                invertedUp,
                aim,
                aimAngularVelocity: Vector3.Zero);

            False(output.AimCaptured,
                "An active inverted body pull entered quiet capture.");
            True(output.Pitch > 0.10f,
                $"An inverted {pullDegrees:0.#}-degree body pull was suppressed by world leveling.");
            True(MathF.Abs(output.RollErrorDegrees) < 5f,
                $"An inverted {pullDegrees:0.#}-degree body pull introduced an upright roll target.");
            True(Vector3.Dot(
                     output.DesiredLiftDirectionWorld,
                     invertedUp) > 0.90f,
                $"An inverted {pullDegrees:0.#}-degree body pull lost its body-up maneuver plane.");
            False(output.WeakPushActive,
                "An inverted positive-G pull entered the negative-G push path.");

            for (var sample = 0; sample < 6; sample++)
            {
                output = Step(
                    ref state,
                    Vector3.UnitZ,
                    invertedUp,
                    aim);
            }

            False(output.AimCaptured,
                "An unfulfilled inverted body pull entered quiet capture.");
            True(output.Pitch > 0.10f,
                $"A captured inverted {pullDegrees:0.#}-degree body pull lost elevator response.");
            True(MathF.Abs(output.RollErrorDegrees) < 5f,
                $"A captured inverted {pullDegrees:0.#}-degree body pull commanded upright roll.");
            True(Vector3.Dot(
                     output.DesiredLiftDirectionWorld,
                     invertedUp) > 0.90f,
                $"A captured inverted {pullDegrees:0.#}-degree body pull lost its body-up maneuver plane.");
        }

        var levelingState = default(AircraftMouseInstructorState);
        var leveling = default(AircraftMouseInstructorOutput);
        for (var sample = 0; sample < 6; sample++)
        {
            leveling = Step(
                ref levelingState,
                Vector3.UnitZ,
                -Vector3.UnitY,
                Vector3.UnitZ);
        }

        True(leveling.AimCaptured,
            "A centered inverted command did not enter quiet capture.");
        Near(0f, leveling.Pitch, 0.000001f,
            "A centered inverted command introduced an elevator target.");
        True(MathF.Abs(leveling.RollErrorDegrees) > 170f,
            "Removing inverted pull suppression also disabled centered upright recovery.");
        True(Vector3.Dot(
                 leveling.DesiredLiftDirectionWorld,
                 Vector3.UnitY) > 0.90f,
            "A centered inverted command no longer targets upright recovery.");
    }

    private static void MovingFineLeadRetainsBankAndLoad()
    {
        var aim = RotateAroundAxis(
            Vector3.UnitZ,
            Vector3.UnitY,
            3f * DegreesToRadians);
        var bankedUp = RightBankedUp(
            Vector3.UnitZ,
            Vector3.UnitY,
            40f);
        var movingState = default(AircraftMouseInstructorState);
        var moving = Step(
            ref movingState,
            Vector3.UnitZ,
            bankedUp,
            aim,
            aimAngularVelocity: Vector3.UnitY * 20f);

        Near(40f, 40f + moving.RollErrorDegrees, 0.5f,
            "A steadily moving lead command did not preserve the established pursuit bank.");
        True(MathF.Abs(moving.Roll) < 0.02f,
            "A steadily moving lead command kept adding aileron after reaching its established bank.");
        True(moving.Pitch > 0.10f,
            "A steadily moving lead command discarded the load needed to keep pursuing the circle.");
        False(moving.AimCaptured,
            "An actively moving lead command entered quiet capture.");

        var stationaryState = default(AircraftMouseInstructorState);
        var stationary = Step(
            ref stationaryState,
            Vector3.UnitZ,
            bankedUp,
            aim);

        True(stationary.RollErrorDegrees < -30f &&
             stationary.Roll < -0.30f,
            "A stationary fine correction stopped leveling an established bank.");
        Near(0f, stationary.Pitch, 0.0001f,
            "A stationary fine lateral correction added an unnecessary pull.");
    }

    private static void MaterialHeadingErrorKeepsPursuitBankAtCombatTurnRate()
    {
        var aim = RotateAroundAxis(
            Vector3.UnitZ,
            Vector3.UnitY,
            25f * DegreesToRadians);
        var bankedUp = RightBankedUp(
            Vector3.UnitZ,
            Vector3.UnitY,
            45f);
        var state = default(AircraftMouseInstructorState);
        var output = Step(
            ref state,
            Vector3.UnitZ,
            bankedUp,
            aim,
            pitchRate: 72f);

        True(output.RollErrorDegrees > 15f && output.Roll > 0.10f,
            "A material current heading error rolled out early at an ordinary combat pitch rate.");
        True(output.DesiredPitchRateDegreesPerSecond > 0f,
            "A material current heading error stopped requesting a positive-G pursuit path.");
    }

    private static void MovingAimVelocityCannotCreateOrReverseBank()
    {
        var aim = RotateAroundAxis(
            Vector3.UnitZ,
            Vector3.UnitY,
            3f * DegreesToRadians);
        var levelState = default(AircraftMouseInstructorState);
        var level = Step(
            ref levelState,
            Vector3.UnitZ,
            Vector3.UnitY,
            aim,
            aimAngularVelocity: Vector3.UnitY * 12f);

        True(MathF.Abs(level.RollErrorDegrees) < 0.1f &&
             MathF.Abs(level.Roll) < 0.01f,
            "Fast fine-aim motion created a bank from level flight.");

        var bankedUp = RightBankedUp(
            Vector3.UnitZ,
            Vector3.UnitY,
            35f);
        var rates = new[] { 0f, 12f, 0f, -12f };
        var desiredBanks = new float[rates.Length];
        var state = default(AircraftMouseInstructorState);

        for (var index = 0; index < rates.Length; index++)
        {
            var output = Step(
                ref state,
                Vector3.UnitZ,
                bankedUp,
                aim,
                aimAngularVelocity: Vector3.UnitY * rates[index]);
            desiredBanks[index] =
                35f + output.RollErrorDegrees;
        }

        True(desiredBanks.All(bank => bank >= -0.1f && bank <= 35.1f),
            "Changing mouse velocity demanded more than the established bank or cross-banked the aircraft.");
        Near(35f, desiredBanks[1], 0.5f,
            "Mouse motion into the turn failed to preserve the established bank.");
        True(desiredBanks[3] >= -0.1f && desiredBanks[3] < 1f,
            "Reversing mouse motion cross-banked before the circle crossed the nose.");
    }

    private static void LargeOffsetAimReversalStartsRollingOutImmediately()
    {
        var aim = RotateAroundAxis(
            Vector3.UnitZ,
            Vector3.UnitY,
            60f * DegreesToRadians);
        var bankedUp = RightBankedUp(
            Vector3.UnitZ,
            Vector3.UnitY,
            45f);
        var state = default(AircraftMouseInstructorState);
        var output = Step(
            ref state,
            Vector3.UnitZ,
            bankedUp,
            aim,
            aimAngularVelocity: -Vector3.UnitY * 8f);

        True(output.RollErrorDegrees < -5f,
            "Reversing a far-offset command retained the old full-bank target.");
        True(output.Roll < -0.10f,
            "A far-offset command reversal did not begin rolling out immediately.");
    }

    private static void QuietCaptureBrakesBodyRatesWithoutHorizonLeveling()
    {
        var climbAngle = 22f * DegreesToRadians;
        var forward = Vector3.Normalize(new Vector3(
            0f,
            MathF.Sin(climbAngle),
            MathF.Cos(climbAngle)));
        var levelUp = Vector3.Normalize(new Vector3(
            0f,
            MathF.Cos(climbAngle),
            -MathF.Sin(climbAngle)));
        var bankedUp = RightBankedUp(forward, levelUp, 24f);
        var state = default(AircraftMouseInstructorState);
        var output = default(AircraftMouseInstructorOutput);

        for (var sample = 0; sample < 8; sample++)
        {
            output = Step(
                ref state,
                forward,
                bankedUp,
                forward,
                pitchRate: 14f,
                rollRate: 11f,
                yawRate: -8f);
        }

        True(output.AimCaptured,
            "A motionless on-nose aim did not enter quiet capture.");
        True(output.Pitch < 0f && output.Pitch >= -0.3501f,
            "Quiet capture did not brake the residual positive pitch rate.");
        True(output.Roll < 0f,
            "Quiet capture did not oppose the right bank and positive roll rate.");
        True(output.Yaw > 0f,
            "Quiet capture did not oppose the negative yaw rate.");

        var settledState = default(AircraftMouseInstructorState);
        var settled = default(AircraftMouseInstructorOutput);
        for (var sample = 0; sample < 8; sample++)
        {
            settled = Step(
                ref settledState,
                forward,
                levelUp,
                forward);
        }

        True(settled.AimCaptured,
            "A settled on-nose climb did not enter quiet capture.");
        Near(0f, settled.Pitch, 0.000001f,
            "Quiet capture pulled a settled climb toward the world horizon.");
    }

    private static void AllOutputsStayFiniteAndBounded()
    {
        var state = default(AircraftMouseInstructorState);
        var limits = new AircraftMouseInstructorLimits(
            float.NaN,
            float.PositiveInfinity,
            -12f,
            0f,
            float.NaN,
            float.NegativeInfinity);
        var values = new[]
        {
            -720f, -181f, -90f, -1f, 0f, 0.001f, 45f, 179f, 540f,
            float.NaN, float.PositiveInfinity, float.NegativeInfinity
        };

        for (var index = 0; index < values.Length * 8; index++)
        {
            var angle = index * 47f * DegreesToRadians;
            var forward = Vector3.Normalize(new Vector3(
                MathF.Sin(angle) * 0.4f,
                MathF.Sin(angle * 0.37f) * 0.7f,
                MathF.Cos(angle)));
            var upCandidate = new Vector3(
                MathF.Cos(angle * 0.29f),
                1f,
                MathF.Sin(angle * 0.53f));
            var up = Vector3.Normalize(
                upCandidate -
                forward * Vector3.Dot(upCandidate, forward));
            var right = Vector3.Normalize(Vector3.Cross(up, forward));
            var aim = Vector3.Normalize(new Vector3(
                MathF.Sin(angle * 1.31f),
                MathF.Cos(angle * 0.73f),
                MathF.Cos(angle * 1.11f)));
            var value = values[index % values.Length];

            if (index % 19 == 0)
                aim = new Vector3(float.NaN, 0f, 1f);
            if (index % 23 == 0)
                up = Vector3.Zero;

            var output = AircraftMouseInstructorCore.Step(
                ref state,
                new AircraftMouseInstructorInput(
                    forward,
                    up,
                    right,
                    index % 7 == 0
                        ? new Vector3(float.PositiveInfinity, 1f, 0f)
                        : Vector3.UnitY,
                    aim,
                    index % 5 == 0
                        ? new Vector3(float.NaN, 0f, 0f)
                        : new Vector3(0f, value, 0f),
                    index % 5 != 0,
                    value,
                    values[(index + 2) % values.Length],
                    values[(index + 4) % values.Length],
                    values[(index + 6) % values.Length],
                    values[(index + 8) % values.Length],
                    values[(index + 10) % values.Length],
                    index % 3 == 0 ? float.NaN : 0.02f,
                    limits));

            Finite(output.Pitch, "Pitch output became non-finite.");
            Finite(output.Roll, "Roll output became non-finite.");
            Finite(output.Yaw, "Yaw output became non-finite.");
            InControlRange(output.Pitch, "Pitch output exceeded the surface range.");
            InControlRange(output.Roll, "Roll output exceeded the surface range.");
            InControlRange(output.Yaw, "Yaw output exceeded the surface range.");
            Finite(output.AimErrorDegrees, "Aim error became non-finite.");
            Finite(output.RollErrorDegrees, "Roll error became non-finite.");
            Finite(
                output.DesiredPitchRateDegreesPerSecond,
                "Desired pitch rate became non-finite.");
            Finite(
                output.DesiredRollRateDegreesPerSecond,
                "Desired roll rate became non-finite.");
            Finite(
                output.DesiredYawRateDegreesPerSecond,
                "Desired yaw rate became non-finite.");
            True(IsFinite(output.DesiredLiftDirectionWorld),
                "Desired lift direction became non-finite.");
        }
    }

    private static AircraftMouseInstructorOutput Step(
        ref AircraftMouseInstructorState state,
        Vector3 forward,
        Vector3 up,
        Vector3 aim,
        Vector3? aimAngularVelocity = null,
        float pitchRate = 0f,
        float rollRate = 0f,
        float yawRate = 0f,
        float sideslip = 0f,
        float angleOfAttack = 0f,
        float criticalAngleOfAttack = 16f,
        float deltaTime = 0.02f,
        AircraftMouseInstructorLimits? limits = null)
    {
        var right = Vector3.Normalize(Vector3.Cross(up, forward));
        return AircraftMouseInstructorCore.Step(
            ref state,
            new AircraftMouseInstructorInput(
                forward,
                up,
                right,
                Vector3.UnitY,
                aim,
                aimAngularVelocity ?? Vector3.Zero,
                aimAngularVelocity.HasValue,
                pitchRate,
                rollRate,
                yawRate,
                sideslip,
                angleOfAttack,
                criticalAngleOfAttack,
                deltaTime,
                limits ?? AircraftMouseInstructorLimits.Default));
    }

    private static Vector3 RightBankedUp(
        Vector3 forward,
        Vector3 levelUp,
        float bankDegrees)
        => Vector3.Normalize(RotateAroundAxis(
            levelUp,
            forward,
            -bankDegrees * DegreesToRadians));

    private static Vector3 RotateAroundAxis(
        Vector3 value,
        Vector3 axis,
        float radians)
    {
        axis = Vector3.Normalize(axis);
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        return value * cosine +
               Vector3.Cross(axis, value) * sine +
               axis * Vector3.Dot(axis, value) * (1f - cosine);
    }

    private static float AngleDegrees(Vector3 first, Vector3 second)
        => MathF.Acos(Math.Clamp(
               Vector3.Dot(
                   Vector3.Normalize(first),
                   Vector3.Normalize(second)),
               -1f,
               1f)) *
           180f / MathF.PI;

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) &&
           float.IsFinite(value.Y) &&
           float.IsFinite(value.Z);

    private static void InControlRange(float value, string message)
    {
        if (value < -1.000001f || value > 1.000001f)
        {
            throw new InvalidOperationException(
                $"{message} Actual={value}");
        }
    }

    private static void Finite(float value, string message)
    {
        if (!float.IsFinite(value))
            throw new InvalidOperationException(message);
    }

    private static void Near(
        float expected,
        float actual,
        float tolerance,
        string message)
    {
        if (!float.IsFinite(actual) ||
            MathF.Abs(expected - actual) > tolerance)
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

    private static void False(bool condition, string message)
        => True(!condition, message);

    private static void Equal<T>(
        T expected,
        T actual,
        string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{message} Expected={expected}; Actual={actual}");
        }
    }
}
