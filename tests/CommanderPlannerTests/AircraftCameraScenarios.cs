using System.Numerics;
using System.Runtime.CompilerServices;
using ER2RealismOverhaul;

internal static class AircraftCameraScenarios
{
    private const float DegreesToRadians = MathF.PI / 180f;

    [ModuleInitializer]
    internal static void RunAll()
    {
        var tests = new (string Name, Action Run)[]
        {
            (nameof(InitializationUsesTheRenderedPose),
                InitializationUsesTheRenderedPose),
            (nameof(ChaseOrbitKeepsTheAircraftCenteredAtEveryAttitude),
                ChaseOrbitKeepsTheAircraftCenteredAtEveryAttitude),
            (nameof(NativeOrbitCenterKeepsZoomStableAcrossAircraftAttitudes),
                NativeOrbitCenterKeepsZoomStableAcrossAircraftAttitudes),
            (nameof(FreeLookBeginsAtTheRenderedPoseWithoutAJump),
                FreeLookBeginsAtTheRenderedPoseWithoutAJump),
            (nameof(FreeLookReconcilesNearVerticalEntryAfterThePreservedFrame),
                FreeLookReconcilesNearVerticalEntryAfterThePreservedFrame),
            (nameof(FreeLookLevelsInheritedRollSmoothlyWhileHeld),
                FreeLookLevelsInheritedRollSmoothlyWhileHeld),
            (nameof(FreeLookYawKeepsAStableHorizonAndElevation),
                FreeLookYawKeepsAStableHorizonAndElevation),
            (nameof(AimFreezesOnlyWhileFreeLookIsHeld),
                AimFreezesOnlyWhileFreeLookIsHeld),
            (nameof(ForwardHemisphereAllowsEveryVisibleScreenEdgeAndCorner),
                ForwardHemisphereAllowsEveryVisibleScreenEdgeAndCorner),
            (nameof(ForwardHemispherePreventsRearwardAimAndSaturatesSmoothly),
                ForwardHemispherePreventsRearwardAimAndSaturatesSmoothly),
            (nameof(ForwardHemisphereTravelsWithTheAircraftThroughACompleteLoop),
                ForwardHemisphereTravelsWithTheAircraftThroughACompleteLoop),
            (nameof(PointAimCameraSmoothlyFollowsWithoutSnappingTheCircle),
                PointAimCameraSmoothlyFollowsWithoutSnappingTheCircle),
            (nameof(VisibleAimRingBoundaryMatchesThePerspectiveFrame),
                VisibleAimRingBoundaryMatchesThePerspectiveFrame),
            (nameof(MovingAimIsPursuedContinuouslyAtEveryFrameRate),
                MovingAimIsPursuedContinuouslyAtEveryFrameRate),
            (nameof(FastAimFlickCanReachTheFrameEdgeThenRecentersSmoothly),
                FastAimFlickCanReachTheFrameEdgeThenRecentersSmoothly),
            (nameof(ChaseForwardAndHorizonMovementHavePerFrameRateCeilings),
                ChaseForwardAndHorizonMovementHavePerFrameRateCeilings),
            (nameof(EdgeAimRemainsContinuousThroughTurnsAndAFrameHitch),
                EdgeAimRemainsContinuousThroughTurnsAndAFrameHitch),
            (nameof(AntipodalHorizonRecoveryKeepsItsEstablishedTurnBranch),
                AntipodalHorizonRecoveryKeepsItsEstablishedTurnBranch),
            (nameof(HorizonBasisStaysFiniteAndContinuousAcrossBothPoles),
                HorizonBasisStaysFiniteAndContinuousAcrossBothPoles),
            (nameof(FreeLookKeepsTheHeldAimLegalWithoutMovingTheRenderedView),
                FreeLookKeepsTheHeldAimLegalWithoutMovingTheRenderedView),
            (nameof(ReturnUsesTheSmoothShortestRotation),
                ReturnUsesTheSmoothShortestRotation),
            (nameof(ReturnRemainsRateLimitedAcrossLargeHitches),
                ReturnRemainsRateLimitedAcrossLargeHitches),
            (nameof(HorizonLevelRecoversWithoutAnAttitudeSnap),
                HorizonLevelRecoversWithoutAnAttitudeSnap),
            (nameof(MouseInputUsesRenderedScreenAxesAfterHorizonRecovery),
                MouseInputUsesRenderedScreenAxesAfterHorizonRecovery),
            (nameof(OwnershipReleaseStopsEveryCameraMutation),
                OwnershipReleaseStopsEveryCameraMutation)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Run();
                Console.WriteLine($"PASS CAMERA {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine(
                    $"FAIL CAMERA {test.Name}: {exception.Message}");
            }
        }

        if (failures > 0)
        {
            throw new InvalidOperationException(
                $"{tests.Length - failures}/{tests.Length} aircraft camera scenarios passed.");
        }

        Console.WriteLine(
            $"{tests.Length}/{tests.Length} deterministic aircraft camera scenarios passed.");
    }

    private static void InitializationUsesTheRenderedPose()
    {
        var rendered = Quaternion.Normalize(
            Quaternion.CreateFromYawPitchRoll(
                73f * DegreesToRadians,
                -28f * DegreesToRadians,
                41f * DegreesToRadians));

        var state = AircraftCameraCore.Initialize(1847, rendered, Vector3.UnitY);

        True(state.Ownership.IsOwned,
            "Initialization did not claim camera ownership.");
        Equal(1847, state.Ownership.OwnerToken,
            "Initialization lost the supplied owner token.");
        SameRotation(rendered, state.Aim.Orientation, 0.000001f,
            "Point aim did not initialize from the rendered rotation.");
        SameRotation(rendered, state.FreeLook.Rotation, 0.000001f,
            "Free-look did not initialize from the rendered rotation.");
        SameRotation(rendered, state.Return.Rotation, 0.000001f,
            "Return did not initialize from the rendered rotation.");
        SameRotation(rendered, state.Chase.Rotation, 0.000001f,
            "Chase did not initialize from the rendered rotation.");
        SameRotation(rendered, AircraftCameraCore.GetRenderedRotation(state),
            0.000001f,
            "Initialization changed the pose presented to the renderer.");
        Near(
            Vector3.Transform(Vector3.UnitZ, rendered),
            state.Aim.Direction,
            0.000001f,
            "Point-aim direction did not match the rendered camera forward.");
        ValidBasis(state.Chase, "initialized");
    }

    private static void ChaseOrbitKeepsTheAircraftCenteredAtEveryAttitude()
    {
        var pivot = new Vector3(41f, -7f, 203f);
        var nativeCameraPositions = new[]
        {
            new Vector3(32f, 4f, 180f),
            new Vector3(40.2f, -6.7f, 200.6f)
        };
        var forwards = new[]
        {
            Vector3.UnitZ,
            -Vector3.UnitZ,
            Vector3.UnitX,
            -Vector3.UnitX,
            Vector3.UnitY,
            -Vector3.UnitY,
            Vector3.Normalize(new Vector3(0.4f, -0.7f, 0.58f))
        };

        foreach (var nativeCameraPosition in nativeCameraPositions)
        {
            var nativeDistance = Vector3.Distance(
                pivot,
                nativeCameraPosition);
            foreach (var forward in forwards)
            {
                var cameraPosition = AircraftCameraCore.RecenterChasePosition(
                    pivot,
                    nativeCameraPosition,
                    forward);

                Finite(cameraPosition,
                    "Centered chase orbit produced a non-finite position.");
                Near(nativeDistance, Vector3.Distance(pivot, cameraPosition),
                    0.0001f,
                    "Centered chase orbit changed native zoom or collision distance.");
                Near(forward, Vector3.Normalize(pivot - cameraPosition),
                    0.00001f,
                    "Aircraft pivot did not remain on the exact center view ray.");
            }
        }
    }

    private static void NativeOrbitCenterKeepsZoomStableAcrossAircraftAttitudes()
    {
        var aircraftPosition = new Vector3(53f, 700f, -91f);
        const float cameraHeight = 2.8f;
        const float nativeRadius = 18f;
        var attitudes = new[]
        {
            Quaternion.Identity,
            Quaternion.CreateFromYawPitchRoll(
                35f * DegreesToRadians,
                -27f * DegreesToRadians,
                61f * DegreesToRadians),
            Quaternion.CreateFromYawPitchRoll(
                -105f * DegreesToRadians,
                72f * DegreesToRadians,
                179f * DegreesToRadians)
        };
        var cameraForwards = new[]
        {
            Vector3.UnitZ,
            Vector3.Normalize(new Vector3(0.75f, -0.2f, 0.63f)),
            Vector3.Normalize(new Vector3(-0.3f, 0.82f, -0.49f))
        };

        foreach (var attitude in attitudes)
        {
            var aircraftUp = Vector3.Transform(Vector3.UnitY, attitude);
            var pivot = AircraftCameraCore.GetNativeChaseOrbitCenter(
                aircraftPosition,
                aircraftUp,
                cameraHeight);
            Near(
                aircraftPosition + Vector3.Normalize(aircraftUp) * cameraHeight,
                pivot,
                0.00001f,
                "Native orbit center did not follow the aircraft-scaled camera height.");

            foreach (var cameraForward in cameraForwards)
            {
                var nativeDirection = Vector3.Normalize(
                    aircraftUp * 0.1f - cameraForward);
                var nativePosition = pivot + nativeDirection * nativeRadius;
                var centeredPosition = AircraftCameraCore.RecenterChasePosition(
                    pivot,
                    nativePosition,
                    cameraForward);

                Near(nativeRadius, Vector3.Distance(pivot, centeredPosition),
                    0.0001f,
                    "Aircraft attitude changed the native chase zoom radius.");
                Near(cameraForward,
                    Vector3.Normalize(pivot - centeredPosition),
                    0.00001f,
                    "The aircraft-scaled orbit center left the camera center ray.");
            }
        }
    }

    private static void FreeLookBeginsAtTheRenderedPoseWithoutAJump()
    {
        var state = AircraftCameraCore.Initialize(
            1,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 15f * DegreesToRadians),
            Vector3.UnitY);
        state = AircraftCameraCore.UpdateHorizonChase(
            state,
            Vector3.Normalize(new Vector3(0.6f, 0.25f, 0.75f)),
            Vector3.UnitY,
            0.25f,
            20f,
            5f);
        var actuallyRendered = Quaternion.Normalize(
            Quaternion.CreateFromYawPitchRoll(
                -96f * DegreesToRadians,
                22f * DegreesToRadians,
                -17f * DegreesToRadians));

        state = AircraftCameraCore.EnterFreeLook(state, actuallyRendered);

        True(state.FreeLook.IsActive,
            "Entering free-look did not activate its state.");
        True(state.Aim.IsHeld,
            "Entering free-look did not hold point aim.");
        False(state.Return.IsActive,
            "Entering free-look left a stale camera return active.");
        SameRotation(actuallyRendered, state.FreeLook.Rotation, 0.000001f,
            "Free-look started from an internal chase pose instead of the rendered pose.");
        SameRotation(actuallyRendered, AircraftCameraCore.GetRenderedRotation(state),
            0.000001f,
            "Entering free-look caused a visible first-frame camera jump.");
    }

    private static void FreeLookReconcilesNearVerticalEntryAfterThePreservedFrame()
    {
        foreach (var elevationSign in new[] { -1f, 1f })
        {
            var entryElevation = elevationSign * 89f * DegreesToRadians;
            var entryForward = Vector3.Normalize(new Vector3(
                0f,
                MathF.Sin(entryElevation),
                MathF.Cos(entryElevation)));
            var rendered = RotationFromForward(entryForward);
            var state = AircraftCameraCore.Initialize(
                109,
                Quaternion.Identity,
                Vector3.UnitY);

            state = AircraftCameraCore.EnterFreeLook(state, rendered);
            SameRotation(rendered, state.FreeLook.Rotation, 0.000001f,
                "Near-vertical free-look entry did not preserve its exact first frame.");

            state = AircraftCameraCore.UpdateFreeLook(
                state,
                0f,
                0f,
                Vector3.UnitY,
                1f / 60f);
            var rotation = state.FreeLook.Rotation;
            var forward = Vector3.Normalize(
                Vector3.Transform(Vector3.UnitZ, rotation));
            var up = Vector3.Normalize(
                Vector3.Transform(Vector3.UnitY, rotation));
            var elevation = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));

            Near(
                elevationSign *
                AircraftCameraCore.DefaultMaximumFreeLookPitchRadians,
                elevation,
                0.00001f,
                "The second held free-look frame did not reconcile the actual near-vertical pose to +/-85 degrees.");
            Finite(rotation,
                "Near-vertical free-look reconciliation produced a non-finite rotation.");
            Near(0f, Vector3.Dot(forward, up), 0.00001f,
                "Near-vertical free-look reconciliation lost an orthogonal horizon basis.");
            True(Vector3.Dot(up, ProjectedUp(forward)) > 0.99f,
                "Horizon leveling was invalid after near-vertical free-look reconciliation.");
        }
    }

    private static void FreeLookLevelsInheritedRollSmoothlyWhileHeld()
    {
        const float deltaTime = 1f / 60f;
        var rendered = Quaternion.Normalize(
            Quaternion.CreateFromYawPitchRoll(
                31f * DegreesToRadians,
                -18f * DegreesToRadians,
                74f * DegreesToRadians));
        var state = AircraftCameraCore.Initialize(
            110,
            Quaternion.Identity,
            Vector3.UnitY);
        state = AircraftCameraCore.EnterFreeLook(state, rendered);
        var heldForward = Vector3.Normalize(
            Vector3.Transform(Vector3.UnitZ, rendered));
        var targetUp = ProjectedUp(heldForward);
        var previousRotation = state.FreeLook.Rotation;
        var previousAlignment = Vector3.Dot(
            Vector3.Transform(Vector3.UnitY, previousRotation),
            targetUp);

        for (var frame = 0; frame < 120; frame++)
        {
            state = AircraftCameraCore.UpdateFreeLook(
                state,
                0f,
                0f,
                Vector3.UnitY,
                deltaTime);
            var rotation = state.FreeLook.Rotation;
            var forward = Vector3.Normalize(
                Vector3.Transform(Vector3.UnitZ, rotation));
            var up = Vector3.Normalize(
                Vector3.Transform(Vector3.UnitY, rotation));
            var alignment = Vector3.Dot(up, targetUp);

            Near(heldForward, forward, 0.00001f,
                "Free-look horizon leveling changed the held look direction.");
            True(alignment + 0.000001f >= previousAlignment,
                "Free-look horizon leveling reversed direction.");
            True(QuaternionAngle(previousRotation, rotation) <=
                 6.01f * DegreesToRadians,
                "Free-look horizon leveling exceeded its per-frame rate limit.");

            previousAlignment = alignment;
            previousRotation = rotation;
        }

        True(previousAlignment > 0.9999f,
            "Free look did not converge to a level horizon while held.");
    }

    private static void FreeLookYawKeepsAStableHorizonAndElevation()
    {
        const float deltaTime = 1f / 60f;
        var forward = Vector3.Normalize(new Vector3(0.35f, 0.57f, 0.74f));
        var state = AircraftCameraCore.Initialize(
            111,
            RotationFromForward(forward),
            Vector3.UnitY);
        state = AircraftCameraCore.EnterFreeLook(
            state,
            state.Chase.Rotation);

        for (var frame = 0; frame < 120; frame++)
        {
            state = AircraftCameraCore.UpdateFreeLook(
                state, 0f, 0f, Vector3.UnitY, deltaTime);
        }

        var initialForward = Vector3.Normalize(
            Vector3.Transform(Vector3.UnitZ, state.FreeLook.Rotation));
        var initialElevation = Vector3.Dot(initialForward, Vector3.UnitY);
        for (var frame = 0; frame < 120; frame++)
        {
            state = AircraftCameraCore.UpdateFreeLook(
                state,
                0f,
                1f * DegreesToRadians,
                Vector3.UnitY,
                deltaTime);
            var rotation = state.FreeLook.Rotation;
            var yawedForward = Vector3.Normalize(
                Vector3.Transform(Vector3.UnitZ, rotation));
            var yawedUp = Vector3.Normalize(
                Vector3.Transform(Vector3.UnitY, rotation));

            Near(initialElevation,
                Vector3.Dot(yawedForward, Vector3.UnitY),
                0.00002f,
                "World-relative free-look yaw changed camera elevation.");
            var horizonAlignment =
                Vector3.Dot(yawedUp, ProjectedUp(yawedForward));
            True(horizonAlignment > 0.9999f,
                $"Free-look yaw accumulated screen roll instead of staying level: {horizonAlignment}.");
        }
    }

    private static void AimFreezesOnlyWhileFreeLookIsHeld()
    {
        var state = AircraftCameraCore.Initialize(
            2, Quaternion.Identity, Vector3.UnitY);
        state = AircraftCameraCore.UpdatePointAim(
            state, 9f * DegreesToRadians, 24f * DegreesToRadians);
        var aimBeforeFreeLook = state.Aim.Orientation;

        state = AircraftCameraCore.EnterFreeLook(
            state,
            Quaternion.CreateFromYawPitchRoll(
                31f * DegreesToRadians,
                -12f * DegreesToRadians,
                4f * DegreesToRadians));
        state = AircraftCameraCore.UpdateFreeLook(
            state,
            -19f * DegreesToRadians,
            47f * DegreesToRadians,
            Vector3.UnitY,
            1f / 60f);
        state = AircraftCameraCore.UpdatePointAim(
            state, 35f * DegreesToRadians, -72f * DegreesToRadians);

        SameRotation(aimBeforeFreeLook, state.Aim.Orientation, 0.000001f,
            "Point aim moved while free-look was held.");

        state = AircraftCameraCore.ReleaseFreeLook(state);
        True(state.Return.IsActive,
            "Releasing free-look did not begin a camera return.");
        False(state.Aim.IsHeld,
            "Releasing free-look kept point aim frozen.");
        var returningCamera = state.Return.Rotation;
        state = AircraftCameraCore.UpdatePointAim(
            state, -6f * DegreesToRadians, 18f * DegreesToRadians);

        True(QuaternionAngle(aimBeforeFreeLook, state.Aim.Orientation) >
             1f * DegreesToRadians,
            "Point aim did not resume while the camera was returning.");
        Equal(returningCamera, state.Return.Rotation,
            "Updating point aim directly moved the returning camera.");
    }

    private static void ForwardHemisphereAllowsEveryVisibleScreenEdgeAndCorner()
    {
        const float aspect = 16f / 9f;
        var halfVerticalTangent = MathF.Tan(30f * DegreesToRadians);
        var visibleDirections = new[]
        {
            Vector3.Normalize(new Vector3(
                halfVerticalTangent * aspect, 0f, 1f)),
            Vector3.Normalize(new Vector3(
                -halfVerticalTangent * aspect, 0f, 1f)),
            Vector3.Normalize(new Vector3(
                0f, halfVerticalTangent, 1f)),
            Vector3.Normalize(new Vector3(
                0f, -halfVerticalTangent, 1f)),
            Vector3.Normalize(new Vector3(
                halfVerticalTangent * aspect,
                halfVerticalTangent,
                1f)),
            Vector3.Normalize(new Vector3(
                -halfVerticalTangent * aspect,
                -halfVerticalTangent,
                1f))
        };

        foreach (var direction in visibleDirections)
        {
            var state = AircraftCameraCore.Initialize(
                20,
                RotationFromForward(direction),
                Vector3.UnitY);
            var before = state.Aim.Direction;
            state = AircraftCameraCore.ConstrainPointAimToAircraftCone(
                state,
                Vector3.UnitZ,
                Vector3.UnitY,
                AircraftCameraCore.MaximumPointAimConeRadians);

            Near(before, state.Aim.Direction, 0.00001f,
                "The forward-hemisphere limit clipped a visible screen edge or corner.");
        }

        True(
            DirectionAngle(Vector3.UnitZ, visibleDirections[0]) >
            44f * DegreesToRadians,
            "The horizontal screen-edge sample did not exceed the removed small circular limit.");
    }

    private static void ForwardHemispherePreventsRearwardAimAndSaturatesSmoothly()
    {
        var limit = AircraftCameraCore.MaximumPointAimConeRadians;
        var state = AircraftCameraCore.Initialize(
            21,
            Quaternion.Identity,
            Vector3.UnitY);
        state = AircraftCameraCore.UpdatePointAim(
            state,
            0f,
            140f * DegreesToRadians);
        state = AircraftCameraCore.ConstrainPointAimToAircraftCone(
            state,
            Vector3.UnitZ,
            Vector3.UnitY,
            limit);

        Near(
            limit,
            DirectionAngle(Vector3.UnitZ, state.Aim.Direction),
            0.00001f,
            "A rearward command was not held at the forward-travel boundary.");
        True(Vector3.Dot(Vector3.UnitZ, state.Aim.Direction) > 0f,
            "The bounded point-aim command reached behind the aircraft.");

        var saturatedDirection = state.Aim.Direction;
        state = AircraftCameraCore.UpdatePointAim(
            state,
            0f,
            15f * DegreesToRadians);
        state = AircraftCameraCore.ConstrainPointAimToAircraftCone(
            state,
            Vector3.UnitZ,
            Vector3.UnitY,
            limit);
        var appliedRate =
            AircraftCameraCore.DirectionAngularVelocityDegreesPerSecond(
                saturatedDirection,
                state.Aim.Direction,
                1f / 60f,
                120f);

        Near(saturatedDirection, state.Aim.Direction, 0.00001f,
            "Repeated outward input moved or snapped a saturated aim direction.");
        Near(Vector3.Zero, appliedRate, 0.00001f,
            "Saturated outward input retained false instructor feed-forward.");
    }

    private static void ForwardHemisphereTravelsWithTheAircraftThroughACompleteLoop()
    {
        var limit = AircraftCameraCore.MaximumPointAimConeRadians;
        var state = AircraftCameraCore.Initialize(
            22,
            Quaternion.Identity,
            Vector3.UnitY);
        state = AircraftCameraCore.UpdatePointAim(
            state,
            20f * DegreesToRadians,
            0f);

        for (var step = 1; step <= 360; step++)
        {
            state = AircraftCameraCore.UpdatePointAim(
                state,
                DegreesToRadians,
                0f);
            var aircraftRotation = Quaternion.CreateFromAxisAngle(
                -Vector3.UnitX,
                step * DegreesToRadians);
            var aircraftForward = Vector3.Transform(
                Vector3.UnitZ,
                aircraftRotation);
            var aircraftUp = Vector3.Transform(
                Vector3.UnitY,
                aircraftRotation);
            state = AircraftCameraCore.ConstrainPointAimToAircraftCone(
                state,
                aircraftForward,
                aircraftUp,
                limit);

            True(
                DirectionAngle(aircraftForward, state.Aim.Direction) <=
                limit + 0.00001f,
                $"The moving forward-travel hemisphere failed at loop step {step}.");
        }

        True(Vector3.Dot(
                 Vector3.UnitZ,
                 Vector3.Transform(
                     Vector3.UnitZ,
                     Quaternion.CreateFromAxisAngle(
                         -Vector3.UnitX,
                         360f * DegreesToRadians))) > 0.99999f,
            "The deterministic aircraft path did not complete the loop.");
        Near(
            20f * DegreesToRadians,
            DirectionAngle(Vector3.UnitZ, state.Aim.Direction),
            0.0001f,
            "The forward-travel hemisphere imposed a world-pitch limit during the loop.");
    }

    private static void PointAimCameraSmoothlyFollowsWithoutSnappingTheCircle()
    {
        var rolled = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ,
            28f * DegreesToRadians);
        var state = AircraftCameraCore.Initialize(
            23,
            rolled,
            Vector3.UnitY);
        var aim = Vector3.Normalize(new Vector3(0.31f, -0.22f, 0.92f));

        state = AircraftCameraCore.UpdateHorizonChase(
            state,
            aim,
            Vector3.UnitY,
            1f / 30f,
            AircraftCameraCore.DefaultForwardFollowSharpness,
            10f,
            lockForwardToTarget: false);

        True(DirectionAngle(state.Chase.Forward, aim) <
             DirectionAngle(
                 Vector3.Transform(Vector3.UnitZ, rolled),
                 aim),
            "The camera did not begin following the large-circle aim ray.");
        True(DirectionAngle(state.Chase.Forward, aim) >
             0.1f * DegreesToRadians,
            "The camera instantly centered the large circle instead of allowing screen travel.");

        for (var frame = 0; frame < 240; frame++)
        {
            state = AircraftCameraCore.UpdateHorizonChase(
                state,
                aim,
                Vector3.UnitY,
                1f / 60f,
                AircraftCameraCore.DefaultForwardFollowSharpness,
                10f,
                lockForwardToTarget: false);
        }

        True(DirectionAngle(state.Chase.Forward, aim) <
             0.10f * DegreesToRadians,
            "The camera did not settle onto an idle point-aim ray.");
    }

    private static void VisibleAimRingBoundaryMatchesThePerspectiveFrame()
    {
        var state = AircraftCameraCore.Initialize(
            231,
            Quaternion.Identity,
            Vector3.UnitY);
        const float verticalFieldOfViewDegrees = 60f;
        const float aspect = 16f / 9f;
        var verticalAim = Vector3.Normalize(new Vector3(0f, 1f, 1f));
        var horizontalAim = Vector3.Normalize(new Vector3(1f, 0f, 1f));
        var diagonalAim = Vector3.Normalize(new Vector3(1f, 1f, 1f));
        var verticalHalfAngle =
            verticalFieldOfViewDegrees * 0.5f * DegreesToRadians;
        foreach (var resolution in new[]
                 {
                     (Width: 1280f, Height: 720f),
                     (Width: 1920f, Height: 1080f),
                     (Width: 3840f, Height: 2160f)
                 })
        {
            var fractions =
                AircraftCameraCore.CameraAimWorkspaceScreenFractions(
                    resolution.Width,
                    resolution.Height);
            var horizontalFraction = fractions.X;
            var verticalFraction = fractions.Y;
            var verticalTangent =
                MathF.Tan(verticalHalfAngle) * verticalFraction;
            var horizontalTangent =
                MathF.Tan(verticalHalfAngle) *
                aspect *
                horizontalFraction;

            var verticalWorkspace =
                AircraftCameraCore.VisibleForwardWorkspaceRadians(
                    state.Chase,
                    verticalAim,
                    verticalFieldOfViewDegrees,
                    aspect,
                    horizontalFraction,
                    verticalFraction);
            var horizontalWorkspace =
                AircraftCameraCore.VisibleForwardWorkspaceRadians(
                    state.Chase,
                    horizontalAim,
                    verticalFieldOfViewDegrees,
                    aspect,
                    horizontalFraction,
                    verticalFraction);
            var diagonalWorkspace =
                AircraftCameraCore.VisibleForwardWorkspaceRadians(
                    state.Chase,
                    diagonalAim,
                    verticalFieldOfViewDegrees,
                    aspect,
                    horizontalFraction,
                    verticalFraction);

            Near(
                MathF.Atan(verticalTangent),
                verticalWorkspace,
                0.00001f,
                $"Vertical aim workspace did not match the rendered ring edge at {resolution.Width}x{resolution.Height}.");
            Near(
                MathF.Atan(horizontalTangent),
                horizontalWorkspace,
                0.00001f,
                $"Horizontal aim workspace did not match the rendered ring edge at {resolution.Width}x{resolution.Height}.");
            Near(
                MathF.Atan(verticalTangent * MathF.Sqrt(2f)),
                diagonalWorkspace,
                0.00001f,
                $"Diagonal aim workspace did not meet the rendered ring corner at {resolution.Width}x{resolution.Height}.");
            True(horizontalWorkspace > diagonalWorkspace &&
                 diagonalWorkspace > verticalWorkspace,
                "The command workspace remained a radial cone instead of matching the camera frame.");
        }
    }

    private static void MovingAimIsPursuedContinuouslyAtEveryFrameRate()
    {
        var finalSeparations = new float[3];
        var frameRates = new[] { 30f, 60f, 120f };
        for (var rateIndex = 0; rateIndex < frameRates.Length; rateIndex++)
        {
            var framesPerSecond = frameRates[rateIndex];
            var deltaTime = 1f / framesPerSecond;
            var state = AircraftCameraCore.Initialize(
                232 + (int)framesPerSecond,
                Quaternion.Identity,
                Vector3.UnitY);
            var aircraftPivot = new Vector3(73f, 12f, -41f);
            var nativeCameraPosition =
                aircraftPivot + new Vector3(3f, 4f, -16f);
            var followSharpness =
                AircraftCameraCore.DefaultForwardFollowSharpness;

            for (var frame = 0; frame < (int)(framesPerSecond * 5f); frame++)
            {
                var previousForward = state.Chase.Forward;
                state = AircraftCameraCore.UpdatePointAim(
                    state,
                    0f,
                    6f * DegreesToRadians * deltaTime);
                var separationBefore = Vector3.Distance(
                    previousForward,
                    state.Aim.Direction);
                followSharpness =
                    AircraftCameraCore.UpdateForwardFollowSharpness(
                        followSharpness,
                        aimInputActive: true,
                        deltaTime);
                state = AircraftCameraCore.UpdateHorizonChase(
                    state,
                    state.Aim.Direction,
                    Vector3.UnitY,
                    deltaTime,
                    followSharpness,
                    AircraftCameraCore.DefaultHorizonLevelSharpness,
                    lockForwardToTarget: false,
                    maximumForwardRateRadiansPerSecond:
                        AircraftCameraCore
                            .DefaultMaximumForwardFollowRateRadiansPerSecond);
                var cameraStep = Vector3.Distance(
                    previousForward,
                    state.Chase.Forward);
                var separationAfter = Vector3.Distance(
                    state.Chase.Forward,
                    state.Aim.Direction);
                var cameraPosition = AircraftCameraCore.RecenterChasePosition(
                    aircraftPivot,
                    nativeCameraPosition,
                    state.Chase.Forward);
                var centeredDirection = Vector3.Normalize(
                    aircraftPivot - cameraPosition);

                True(cameraStep > 0.0000001f,
                    $"The camera stopped pursuing active mouse input at {framesPerSecond:0} Hz.");
                True(separationAfter < separationBefore,
                    $"The camera let active aim move away without chasing it at {framesPerSecond:0} Hz.");
                True(cameraStep <=
                     2f * MathF.Sin(
                         AircraftCameraCore
                             .DefaultMaximumForwardFollowRateRadiansPerSecond *
                          deltaTime * 0.5f) + 0.00001f,
                    $"Active camera pursuit exceeded its rate ceiling at {framesPerSecond:0} Hz.");
                Near(state.Chase.Forward, centeredDirection, 0.00001f,
                    $"Moving camera pursuit displaced the aircraft from screen center at {framesPerSecond:0} Hz.");
            }

            var cameraTravel = DirectionAngle(
                Vector3.UnitZ,
                state.Chase.Forward);
            finalSeparations[rateIndex] = DirectionAngle(
                state.Chase.Forward,
                state.Aim.Direction);
            True(cameraTravel > 28f * DegreesToRadians,
                $"Sustained mouse input left the camera behind instead of following at {framesPerSecond:0} Hz.");
            True(finalSeparations[rateIndex] > 1.15f * DegreesToRadians,
                $"Sustained mouse input was swallowed inside the command ring at {framesPerSecond:0} Hz.");
            True(finalSeparations[rateIndex] < 1.75f * DegreesToRadians,
                $"Sustained mouse input left excessive camera lag at {framesPerSecond:0} Hz.");

            var previousSeparation = finalSeparations[rateIndex];
            for (var frame = 0; frame < (int)(framesPerSecond * 0.70f); frame++)
            {
                followSharpness =
                    AircraftCameraCore.UpdateForwardFollowSharpness(
                        followSharpness,
                        aimInputActive: false,
                        deltaTime);
                state = AircraftCameraCore.UpdateHorizonChase(
                    state,
                    state.Aim.Direction,
                    Vector3.UnitY,
                    deltaTime,
                    followSharpness,
                    AircraftCameraCore.DefaultHorizonLevelSharpness,
                    lockForwardToTarget: false);
                var separation = DirectionAngle(
                    state.Chase.Forward,
                    state.Aim.Direction);
                True(separation <= previousSeparation + 0.00001f,
                    $"Idle camera recovery reversed after active input at {framesPerSecond:0} Hz.");
                previousSeparation = separation;
            }

            True(previousSeparation < 0.10f * DegreesToRadians,
                $"The camera did not smoothly recenter after active input at {framesPerSecond:0} Hz.");
        }

        var minimumSeparation = MathF.Min(
            finalSeparations[0],
            MathF.Min(finalSeparations[1], finalSeparations[2]));
        var maximumSeparation = MathF.Max(
            finalSeparations[0],
            MathF.Max(finalSeparations[1], finalSeparations[2]));
        True(maximumSeparation - minimumSeparation <
             0.12f * DegreesToRadians,
            "Continuous camera pursuit changed materially with frame rate.");
    }

    private static void FastAimFlickCanReachTheFrameEdgeThenRecentersSmoothly()
    {
        var fractions =
            AircraftCameraCore.CameraAimWorkspaceScreenFractions(
                1920f,
                1080f);
        foreach (var framesPerSecond in new[] { 30f, 60f, 120f })
        {
            var deltaTime = 1f / framesPerSecond;
            var state = AircraftCameraCore.Initialize(
                240 + (int)framesPerSecond,
                Quaternion.Identity,
                Vector3.UnitY);
            state = AircraftCameraCore.UpdatePointAim(
                state,
                0f,
                60f * DegreesToRadians);
            var visibleBoundary =
                AircraftCameraCore.VisibleForwardWorkspaceRadians(
                    state.Chase,
                    state.Aim.Direction,
                    60f,
                    16f / 9f,
                    fractions.X,
                    fractions.Y);

            var previousForward = state.Chase.Forward;
            state = AircraftCameraCore.UpdateHorizonChase(
                state,
                state.Aim.Direction,
                Vector3.UnitY,
                deltaTime);
            var firstStep = DirectionAngle(
                previousForward,
                state.Chase.Forward);
            var separation = DirectionAngle(
                state.Chase.Forward,
                state.Aim.Direction);

            True(separation > visibleBoundary,
                $"A fast flick could not transiently move the circle to the frame edge at {framesPerSecond:0} Hz.");
            True(firstStep <=
                 AircraftCameraCore
                     .DefaultMaximumForwardFollowRateRadiansPerSecond *
                 deltaTime + 0.00001f,
                $"The first fast-flick chase frame snapped at {framesPerSecond:0} Hz.");

            var previousSeparation = separation;
            var settleFrames = (int)MathF.Ceiling(framesPerSecond * 0.70f);
            for (var frame = 0; frame < settleFrames; frame++)
            {
                previousForward = state.Chase.Forward;
                state = AircraftCameraCore.UpdateHorizonChase(
                    state,
                    state.Aim.Direction,
                    Vector3.UnitY,
                    deltaTime);
                separation = DirectionAngle(
                    state.Chase.Forward,
                    state.Aim.Direction);
                True(separation <= previousSeparation + 0.00001f,
                    $"Fast-flick recentering reversed at {framesPerSecond:0} Hz.");
                True(DirectionAngle(previousForward, state.Chase.Forward) <=
                     AircraftCameraCore
                         .DefaultMaximumForwardFollowRateRadiansPerSecond *
                     deltaTime + 0.00001f,
                    $"Fast-flick recentering exceeded its rate ceiling at {framesPerSecond:0} Hz.");
                previousSeparation = separation;
            }

            True(separation < 0.10f * DegreesToRadians,
                $"The camera did not smoothly recenter after a fast flick at {framesPerSecond:0} Hz.");
        }
    }

    private static void ChaseForwardAndHorizonMovementHavePerFrameRateCeilings()
    {
        const float deltaTime = 1f / 60f;
        var state = AircraftCameraCore.Initialize(
            24,
            Quaternion.Identity,
            Vector3.UnitY);
        state = AircraftCameraCore.UpdateHorizonChase(
            state,
            -Vector3.UnitZ,
            Vector3.UnitY,
            deltaTime,
            1000f,
            1000f);

        Near(
            AircraftCameraCore.DefaultMaximumForwardFollowRateRadiansPerSecond *
            deltaTime,
            DirectionAngle(Vector3.UnitZ, state.Chase.Forward),
            0.00001f,
            "A large aim change exceeded the chase forward rate ceiling.");

        var upsideDown = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ,
            MathF.PI);
        state = AircraftCameraCore.Initialize(
            25,
            upsideDown,
            Vector3.UnitY);
        var oldUp = state.Chase.Up;
        state = AircraftCameraCore.UpdateHorizonChase(
            state,
            Vector3.UnitZ,
            Vector3.UnitY,
            deltaTime,
            1000f,
            1000f,
            lockForwardToTarget: true);

        Near(
            AircraftCameraCore.DefaultMaximumHorizonLevelRateRadiansPerSecond *
            deltaTime,
            DirectionAngle(oldUp, state.Chase.Up),
            0.00001f,
            "Upside-down horizon recovery exceeded its rate ceiling.");

        state = AircraftCameraCore.UpdateHorizonChase(
            state,
            -Vector3.UnitZ,
            Vector3.UnitY,
            deltaTime,
            1000f,
            1000f,
            lockForwardToTarget: true);
        Near(-Vector3.UnitZ, state.Chase.Forward, 0.000001f,
            "The explicit forward lock stopped being exact after adding rate ceilings.");
    }

    private static void EdgeAimRemainsContinuousThroughTurnsAndAFrameHitch()
    {
        var state = AircraftCameraCore.Initialize(
            261,
            Quaternion.Identity,
            Vector3.UnitY);
        var previousForward = state.Chase.Forward;
        var accumulatedTravelYaw = 0f;
        var establishedTurnSign = 0f;
        var hitchStepRadians = 0f;

        for (var frame = 0; frame < 120; frame++)
        {
            var deltaTime = frame == 38 ? 0.1f : 1f / 60f;
            accumulatedTravelYaw +=
                18f * DegreesToRadians * deltaTime;
            var travelRotation = Quaternion.CreateFromAxisAngle(
                Vector3.UnitY,
                accumulatedTravelYaw);
            var travelForward = Vector3.Transform(
                Vector3.UnitZ,
                travelRotation);
            var travelUp = Vector3.Transform(
                Vector3.UnitY,
                travelRotation);

            // Alternate just inside and just outside the 89-degree boundary
            // while the aircraft's travel direction continues turning. This
            // reproduces edge-held mouse input with small per-frame noise.
            var noisyConeAngle =
                (frame & 1) == 0
                    ? 89.06f * DegreesToRadians
                    : 88.90f * DegreesToRadians;
            var aimRotation = Quaternion.CreateFromAxisAngle(
                Vector3.UnitY,
                accumulatedTravelYaw + noisyConeAngle);
            state = state with
            {
                Aim = state.Aim with
                {
                    Orientation = aimRotation
                }
            };
            state = AircraftCameraCore.ConstrainPointAimToAircraftCone(
                state,
                travelForward,
                travelUp,
                AircraftCameraCore.MaximumPointAimConeRadians);

            var constrainedAimAngle = DirectionAngle(
                travelForward,
                state.Aim.Direction);
            True(
                constrainedAimAngle <=
                AircraftCameraCore.MaximumPointAimConeRadians + 0.0001f,
                $"Noisy edge aim escaped the travel cone at frame {frame}; angle={constrainedAimAngle / DegreesToRadians:F5} degrees.");

            state = AircraftCameraCore.UpdateHorizonChase(
                state,
                state.Aim.Direction,
                Vector3.UnitY,
                deltaTime,
                AircraftCameraCore.DefaultForwardFollowSharpness,
                AircraftCameraCore.DefaultHorizonLevelSharpness);

            var stepRadians = DirectionAngle(
                previousForward,
                state.Chase.Forward);
            var integrationDeltaTime = MathF.Min(
                deltaTime,
                AircraftCameraCore.MaximumChaseIntegrationDeltaTime);
            var maximumStep =
                AircraftCameraCore.DefaultMaximumForwardFollowRateRadiansPerSecond *
                integrationDeltaTime;
            True(stepRadians <= maximumStep + 0.00001f,
                $"Edge-follow camera exceeded its visual frame bound at frame {frame}.");

            var signedTurn = Vector3.Dot(
                Vector3.UnitY,
                Vector3.Cross(previousForward, state.Chase.Forward));
            if (MathF.Abs(signedTurn) > 0.000001f)
            {
                var turnSign = MathF.Sign(signedTurn);
                if (establishedTurnSign == 0f)
                    establishedTurnSign = turnSign;
                Equal(establishedTurnSign, turnSign,
                    $"Noisy edge aim reversed the chase turn branch at frame {frame}.");
            }

            if (frame == 38)
                hitchStepRadians = stepRadians;
            ValidBasis(state.Chase, $"edge aim frame {frame}");
            previousForward = state.Chase.Forward;
        }

        True(establishedTurnSign > 0f,
            "The edge-follow trajectory never established its expected turn.");
        True(
            hitchStepRadians <=
            AircraftCameraCore.DefaultMaximumForwardFollowRateRadiansPerSecond *
            AircraftCameraCore.MaximumChaseIntegrationDeltaTime +
            0.00001f,
            "A 100 ms hitch produced an objectionable one-frame chase jump.");

        state = AircraftCameraCore.UpdateHorizonChase(
            state,
            -Vector3.UnitZ,
            Vector3.UnitY,
            0.1f,
            lockForwardToTarget: true);
        Near(-Vector3.UnitZ, state.Chase.Forward, 0.000001f,
            "Hitch protection weakened the explicit forward-lock contract.");
    }

    private static void AntipodalHorizonRecoveryKeepsItsEstablishedTurnBranch()
    {
        var forwardBaseline = AircraftCameraCore.Initialize(
            260,
            Quaternion.Identity,
            Vector3.UnitY);
        forwardBaseline = forwardBaseline with
        {
            Chase = forwardBaseline.Chase with { LevelTurnSign = -1f }
        };
        var leftRearward = Vector3.Normalize(
            new Vector3(-0.0001f, 0f, -1f));
        var rightRearward = Vector3.Normalize(
            new Vector3(0.0001f, 0f, -1f));
        var leftForward = AircraftCameraCore.UpdateHorizonChase(
            forwardBaseline,
            leftRearward,
            Vector3.UnitY,
            1f / 60f,
            1000f,
            0f);
        var rightForward = AircraftCameraCore.UpdateHorizonChase(
            forwardBaseline,
            rightRearward,
            Vector3.UnitY,
            1f / 60f,
            1000f,
            0f);
        True(Vector3.Dot(
                 leftForward.Chase.Forward,
                 rightForward.Chase.Forward) > 0.999999f,
            "Rearward aim noise sent the chase forward down opposite turn arcs.");

        var upsideDown = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ,
            MathF.PI);
        var baseline = AircraftCameraCore.Initialize(
            26,
            upsideDown,
            Vector3.UnitY);
        baseline = baseline with
        {
            Chase = baseline.Chase with { LevelTurnSign = -1f }
        };
        var leftPerturbation = Vector3.Normalize(
            new Vector3(-0.0001f, 1f, 0f));
        var rightPerturbation = Vector3.Normalize(
            new Vector3(0.0001f, 1f, 0f));

        var left = AircraftCameraCore.UpdateHorizonChase(
            baseline,
            Vector3.UnitZ,
            leftPerturbation,
            1f / 60f,
            1000f,
            1000f);
        var right = AircraftCameraCore.UpdateHorizonChase(
            baseline,
            Vector3.UnitZ,
            rightPerturbation,
            1f / 60f,
            1000f,
            1000f);

        True(left.Chase.LevelTurnSign < 0f &&
             right.Chase.LevelTurnSign < 0f,
            "Tiny noise around the antipodal horizon reversed the stored turn branch.");
        True(Vector3.Dot(left.Chase.Up, right.Chase.Up) > 0.999999f,
            "Antipodal horizon noise sent equivalent frames down opposite recovery arcs.");
    }

    private static void FreeLookKeepsTheHeldAimLegalWithoutMovingTheRenderedView()
    {
        var state = AircraftCameraCore.Initialize(
            28,
            Quaternion.Identity,
            Vector3.UnitY);
        var freeLookRotation = Quaternion.CreateFromYawPitchRoll(
            67f * DegreesToRadians,
            -31f * DegreesToRadians,
            12f * DegreesToRadians);
        state = AircraftCameraCore.EnterFreeLook(state, freeLookRotation);

        state = AircraftCameraCore.ConstrainPointAimToAircraftCone(
            state,
            -Vector3.UnitZ,
            Vector3.UnitY,
            AircraftCameraCore.MaximumPointAimConeRadians);

        True(state.Aim.IsHeld,
            "Applying the safety cone released the held aim during free-look.");
        True(DirectionAngle(-Vector3.UnitZ, state.Aim.Direction) <=
             AircraftCameraCore.MaximumPointAimConeRadians + 0.00001f,
            "The held command became rearward while the player was free-looking.");
        SameRotation(
            freeLookRotation,
            AircraftCameraCore.GetRenderedRotation(state),
            0.000001f,
            "Keeping the held aim legal moved the rendered free-look camera.");
    }

    private static void HorizonBasisStaysFiniteAndContinuousAcrossBothPoles()
    {
        TraverseVerticalArc(1f, "zenith");
        TraverseVerticalArc(-1f, "nadir");
    }

    private static void TraverseVerticalArc(float verticalSign, string poleName)
    {
        var state = AircraftCameraCore.Initialize(
            verticalSign > 0f ? 3 : 4,
            Quaternion.Identity,
            Vector3.UnitY);
        var previousUp = state.Chase.Up;
        var enteredPole = false;
        var exitedPole = false;

        for (var step = 1; step <= 180; step++)
        {
            var radians = step * DegreesToRadians;
            var desiredForward = Vector3.Normalize(
                new Vector3(
                    0f,
                    verticalSign * MathF.Sin(radians),
                    MathF.Cos(radians)));
            var wasInsidePole = state.Chase.IsInsidePole;
            state = AircraftCameraCore.UpdateHorizonChase(
                state,
                desiredForward,
                Vector3.UnitY,
                1f / 120f,
                1000f,
                AircraftCameraCore.DefaultHorizonLevelSharpness);

            ValidBasis(state.Chase, $"{poleName} step {step}");
            True(Vector3.Dot(previousUp, state.Chase.Up) > 0.5f,
                $"Camera up flipped while traversing the {poleName}.");
            if (!wasInsidePole && state.Chase.IsInsidePole)
                enteredPole = true;
            if (enteredPole && wasInsidePole && !state.Chase.IsInsidePole)
                exitedPole = true;
            previousUp = state.Chase.Up;
        }

        True(enteredPole,
            $"The {poleName} traversal never entered pole protection.");
        True(exitedPole,
            $"The {poleName} traversal never exited pole protection.");
        True(Vector3.Dot(state.Chase.Forward, -Vector3.UnitZ) > 0.999f,
            $"The chase camera did not follow through the {poleName}.");
    }

    private static void ReturnUsesTheSmoothShortestRotation()
    {
        var target = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY, -170f * DegreesToRadians);
        var released = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY, 170f * DegreesToRadians);
        var state = AircraftCameraCore.Initialize(5, target, Vector3.UnitY);
        state = AircraftCameraCore.EnterFreeLook(state, released);
        state = AircraftCameraCore.ReleaseFreeLook(state);
        var initialAngle = QuaternionAngle(released, target);

        state = AircraftCameraCore.UpdateReturn(
            state, 1f / 60f, 4f, 0f);
        var firstRotation = state.Return.Rotation;
        var firstStep = SignedAngleAroundWorldUp(released, firstRotation);
        var firstRemaining = QuaternionAngle(firstRotation, target);

        True(firstStep > 0f,
            "Return rotated along the long path at the ±180-degree boundary.");
        True(firstStep < initialAngle * 0.25f,
            "Return snapped through too much of the shortest path in one frame.");
        True(firstRemaining < initialAngle,
            "Return did not reduce the shortest angular distance.");
        True(state.Return.IsActive,
            "Return completed in one frame instead of moving smoothly.");

        var previousRemainingChord =
            QuaternionChordDistance(firstRotation, target);
        var previousRotation = firstRotation;
        for (var frame = 0; frame < 600 && state.Return.IsActive; frame++)
        {
            var wasActive = state.Return.IsActive;
            state = AircraftCameraCore.UpdateReturn(
                state, 1f / 60f, 4f);
            var remainingChord =
                QuaternionChordDistance(state.Return.Rotation, target);
            True(remainingChord <= previousRemainingChord + 0.0000001f,
                "Return angular error increased between frames.");
            if (wasActive && !state.Return.IsActive)
            {
                var maximumCompletionChord =
                    2f * MathF.Sin(
                        0.0011f * DegreesToRadians * 0.25f);
                True(
                    QuaternionChordDistance(
                        previousRotation,
                        state.Return.Rotation) <= maximumCompletionChord,
                    "Free-look return ended with a visible completion snap.");
            }

            previousRemainingChord = remainingChord;
            previousRotation = state.Return.Rotation;
        }

        False(state.Return.IsActive,
            "Camera return did not eventually complete.");
        SameRotation(target, state.Return.Rotation, 0.000001f,
            "Completed return did not land on the chase orientation.");
    }

    private static void ReturnRemainsRateLimitedAcrossLargeHitches()
    {
        foreach (var releaseDegrees in new[] { 170f, 180f })
        {
            float? referenceStep = null;
            foreach (var deltaTime in new[] { 0.1f, 0.2f })
            {
                var target = Quaternion.Identity;
                var released = Quaternion.CreateFromAxisAngle(
                    Vector3.UnitY,
                    releaseDegrees * DegreesToRadians);
                var state = AircraftCameraCore.Initialize(
                    510,
                    target,
                    Vector3.UnitY);
                state = AircraftCameraCore.EnterFreeLook(state, released);
                state = AircraftCameraCore.ReleaseFreeLook(state);

                state = AircraftCameraCore.UpdateReturn(state, deltaTime);
                var step = QuaternionAngle(
                    released,
                    state.Return.Rotation);
                var maximumStep =
                    AircraftCameraCore
                        .DefaultMaximumReturnRateRadiansPerSecond *
                    AircraftCameraCore.MaximumChaseIntegrationDeltaTime;

                True(step > 0f,
                    $"A {releaseDegrees:0}-degree free-look release did not begin returning after a {deltaTime:0.0}s hitch.");
                True(step <= maximumStep + 0.00001f,
                    $"A {deltaTime:0.0}s hitch let a {releaseDegrees:0}-degree free-look return exceed its angular rate ceiling.");
                True(QuaternionAngle(state.Return.Rotation, target) <
                     QuaternionAngle(released, target),
                    "Hitch-safe free-look return did not reduce angular error.");
                Finite(state.Return.Rotation,
                    "Hitch-safe free-look return produced a non-finite rotation.");

                if (referenceStep.HasValue)
                {
                    Near(referenceStep.Value, step, 0.00001f,
                        "A longer render hitch changed the capped free-look return step.");
                }
                else
                {
                    referenceStep = step;
                }
            }
        }
    }

    private static void HorizonLevelRecoversWithoutAnAttitudeSnap()
    {
        var rolled = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ, 90f * DegreesToRadians);
        var state = AircraftCameraCore.Initialize(6, rolled, Vector3.UnitY);
        var initialUp = state.Chase.Up;

        state = AircraftCameraCore.UpdateHorizonChase(
            state,
            Vector3.UnitZ,
            Vector3.UnitY,
            1f / 60f,
            AircraftCameraCore.DefaultForwardFollowSharpness,
            AircraftCameraCore.DefaultHorizonLevelSharpness);
        var firstUp = state.Chase.Up;

        True(Vector3.Dot(firstUp, Vector3.UnitY) >
             Vector3.Dot(initialUp, Vector3.UnitY),
            "Horizon leveling did not begin recovering the rolled camera.");
        True(Vector3.Dot(firstUp, Vector3.UnitY) < 0.25f,
            "Horizon leveling snapped upright in its first frame.");

        var previousAlignment = Vector3.Dot(firstUp, Vector3.UnitY);
        for (var frame = 0; frame < 360; frame++)
        {
            state = AircraftCameraCore.UpdateHorizonChase(
                state,
                Vector3.UnitZ,
                Vector3.UnitY,
                1f / 60f,
                AircraftCameraCore.DefaultForwardFollowSharpness,
                AircraftCameraCore.DefaultHorizonLevelSharpness);
            var alignment = Vector3.Dot(state.Chase.Up, Vector3.UnitY);
            True(alignment + 0.000001f >= previousAlignment,
                "Horizon recovery reversed direction.");
            previousAlignment = alignment;
        }

        True(Vector3.Dot(state.Chase.Up, Vector3.UnitY) > 0.9999f,
            "The chase camera did not eventually recover the world horizon.");
        ValidBasis(state.Chase, "recovered horizon");
    }

    private static void MouseInputUsesRenderedScreenAxesAfterHorizonRecovery()
    {
        var rolled = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ, 90f * DegreesToRadians);
        var state = AircraftCameraCore.Initialize(27, rolled, Vector3.UnitY);
        for (var frame = 0; frame < 360; frame++)
        {
            state = AircraftCameraCore.UpdateHorizonChase(
                state,
                Vector3.UnitZ,
                Vector3.UnitY,
                1f / 60f,
                AircraftCameraCore.DefaultForwardFollowSharpness,
                AircraftCameraCore.DefaultHorizonLevelSharpness);
        }

        var screenForward = state.Chase.Forward;
        var screenUp = state.Chase.Up;
        var screenRight = Vector3.Normalize(
            Vector3.Cross(screenUp, screenForward));
        state = AircraftCameraCore.UpdatePointAim(
            state,
            0f,
            5f * DegreesToRadians);
        var aim = state.Aim.Direction;

        True(Vector3.Dot(aim, screenRight) > 0.08f,
            "Horizontal mouse input did not move the command toward screen-right after horizon recovery.");
        True(MathF.Abs(Vector3.Dot(aim, screenUp)) < 0.001f,
            "Horizontal mouse input inherited stale aim roll and moved vertically on screen.");
    }

    private static void OwnershipReleaseStopsEveryCameraMutation()
    {
        var state = AircraftCameraCore.Initialize(
            913,
            Quaternion.CreateFromYawPitchRoll(
                33f * DegreesToRadians,
                11f * DegreesToRadians,
                -8f * DegreesToRadians),
            Vector3.UnitY);
        state = AircraftCameraCore.EnterFreeLook(state, state.Chase.Rotation);
        state = AircraftCameraCore.UpdateFreeLook(
            state,
            7f * DegreesToRadians,
            16f * DegreesToRadians,
            Vector3.UnitY,
            1f / 60f);
        state = AircraftCameraCore.ReleaseOwnership(state);

        False(state.Ownership.IsOwned,
            "Explicit release left camera ownership active.");
        Equal(913, state.Ownership.OwnerToken,
            "Explicit release changed the ownership identity.");
        False(state.Aim.IsHeld,
            "Explicit release left point aim held.");
        False(state.FreeLook.IsActive,
            "Explicit release left free-look active.");
        False(state.Return.IsActive,
            "Explicit release left camera return active.");

        var released = state;
        state = AircraftCameraCore.UpdatePointAim(state, 1f, 1f);
        state = AircraftCameraCore.EnterFreeLook(
            state, Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1f));
        state = AircraftCameraCore.UpdateFreeLook(
            state, 1f, 1f, Vector3.UnitY, 0.1f);
        state = AircraftCameraCore.ReleaseFreeLook(state);
        state = AircraftCameraCore.UpdateHorizonChase(
            state, Vector3.UnitX, Vector3.UnitY, 0.1f);
        state = AircraftCameraCore.UpdateReturn(state, 0.1f);

        Equal(released, state,
            "A camera update mutated state after ownership was released.");
    }

    private static void ValidBasis(
        AircraftHorizonChaseState chase,
        string context)
    {
        Finite(chase.Rotation, $"Rotation became non-finite at {context}.");
        Finite(chase.Forward, $"Forward became non-finite at {context}.");
        Finite(chase.Up, $"Up became non-finite at {context}.");
        Near(1f, chase.Forward.Length(), 0.00001f,
            $"Forward was not normalized at {context}.");
        Near(1f, chase.Up.Length(), 0.00001f,
            $"Up was not normalized at {context}.");
        Near(0f, Vector3.Dot(chase.Forward, chase.Up), 0.00001f,
            $"Forward and up were not perpendicular at {context}.");
        var rotationForward = Vector3.Transform(Vector3.UnitZ, chase.Rotation);
        var rotationUp = Vector3.Transform(Vector3.UnitY, chase.Rotation);
        Near(chase.Forward, rotationForward, 0.00005f,
            $"Rotation and cached forward disagreed at {context}.");
        Near(chase.Up, rotationUp, 0.00005f,
            $"Rotation and cached up disagreed at {context}.");
    }

    private static Vector3 ProjectedUp(Vector3 forward)
    {
        forward = Vector3.Normalize(forward);
        return Vector3.Normalize(
            Vector3.UnitY -
            forward * Vector3.Dot(Vector3.UnitY, forward));
    }

    private static float SignedAngleAroundWorldUp(
        Quaternion from,
        Quaternion to)
    {
        var fromForward = Vector3.Transform(Vector3.UnitZ, from);
        var toForward = Vector3.Transform(Vector3.UnitZ, to);
        var sine = Vector3.Dot(
            Vector3.UnitY,
            Vector3.Cross(fromForward, toForward));
        var cosine = Math.Clamp(
            Vector3.Dot(fromForward, toForward), -1f, 1f);
        return MathF.Atan2(sine, cosine);
    }

    private static float QuaternionAngle(Quaternion left, Quaternion right)
    {
        var dot = Math.Clamp(MathF.Abs(Quaternion.Dot(
            Quaternion.Normalize(left),
            Quaternion.Normalize(right))), 0f, 1f);
        return 2f * MathF.Acos(dot);
    }

    private static float QuaternionChordDistance(
        Quaternion left,
        Quaternion right)
    {
        left = Quaternion.Normalize(left);
        right = Quaternion.Normalize(right);
        if (Quaternion.Dot(left, right) < 0f)
        {
            right = new Quaternion(
                -right.X,
                -right.Y,
                -right.Z,
                -right.W);
        }

        return (left - right).Length();
    }

    private static float DirectionAngle(Vector3 left, Vector3 right)
    {
        var dot = Math.Clamp(
            Vector3.Dot(
                Vector3.Normalize(left),
                Vector3.Normalize(right)),
            -1f,
            1f);
        return MathF.Acos(dot);
    }

    private static Quaternion RotationFromForward(Vector3 direction)
    {
        direction = Vector3.Normalize(direction);
        var dot = Math.Clamp(Vector3.Dot(Vector3.UnitZ, direction), -1f, 1f);
        if (dot > 0.999999f)
            return Quaternion.Identity;
        if (dot < -0.999999f)
        {
            return Quaternion.CreateFromAxisAngle(
                Vector3.UnitY,
                MathF.PI);
        }

        return Quaternion.Normalize(
            new Quaternion(
                Vector3.Cross(Vector3.UnitZ, direction),
                1f + dot));
    }

    private static void SameRotation(
        Quaternion expected,
        Quaternion actual,
        float tolerance,
        string message)
    {
        if (!IsFinite(actual) ||
            QuaternionAngle(expected, actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"{message} AngularError={QuaternionAngle(expected, actual)}");
        }
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

    private static void Near(
        Vector3 expected,
        Vector3 actual,
        float tolerance,
        string message)
    {
        if (!IsFinite(actual) ||
            Vector3.Distance(expected, actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"{message} Expected={expected}; Actual={actual}");
        }
    }

    private static void Finite(Vector3 value, string message)
    {
        if (!IsFinite(value))
            throw new InvalidOperationException(message);
    }

    private static void Finite(Quaternion value, string message)
    {
        if (!IsFinite(value))
            throw new InvalidOperationException(message);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void False(bool condition, string message) =>
        True(!condition, message);

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{message} Expected={expected}; Actual={actual}");
        }
    }
}
