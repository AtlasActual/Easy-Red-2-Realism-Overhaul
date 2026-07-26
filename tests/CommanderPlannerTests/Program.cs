using ER2RealismOverhaul;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            (nameof(CoverFsmHoldsUsefulCover), CoverFsmHoldsUsefulCover),
            (nameof(CoverFsmSuppressionOverridesUrgency), CoverFsmSuppressionOverridesUrgency),
            (nameof(CoverFsmUrgencyBypassesDeliberateWait), CoverFsmUrgencyBypassesDeliberateWait),
            (nameof(CoverSelectionRequiresProtectionAndSafeNormalRoute), CoverSelectionRequiresProtectionAndSafeNormalRoute),
            (nameof(DefensiveOccupationAllowsOneMoveFromOpenGround), DefensiveOccupationAllowsOneMoveFromOpenGround),
            (nameof(DeliberateCoverValuesFiringQualityMoreThanUrgency), DeliberateCoverValuesFiringQualityMoreThanUrgency),
            (nameof(DefensiveCoverValuesProtectionOverImmediateFireLine), DefensiveCoverValuesProtectionOverImmediateFireLine),
            (nameof(SpatialCoverReservationsRejectOverlappingSlots), SpatialCoverReservationsRejectOverlappingSlots),
            (nameof(CoverPostureRequiresWholeBodyProtection), CoverPostureRequiresWholeBodyProtection),
            (nameof(BallisticCoverRatesMaterialAndThickness), BallisticCoverRatesMaterialAndThickness),
            (nameof(VisualObstructionAloneIsNotProtectiveCover), VisualObstructionAloneIsNotProtectiveCover),
            (nameof(ProtectionWeightedScorePrefersSurvivableCover), ProtectionWeightedScorePrefersSurvivableCover),
            (nameof(CoverScoringSpreadsSoldiersWithoutOverridingProtection), CoverScoringSpreadsSoldiersWithoutOverridingProtection),
            (nameof(DispersionDegradesInsteadOfBlockingNearbyCover), DispersionDegradesInsteadOfBlockingNearbyCover),
            (nameof(AttackProgressHasMaximumCombatHalt), AttackProgressHasMaximumCombatHalt),
            (nameof(IdleSoldiersRemainUnderNativeControl), IdleSoldiersRemainUnderNativeControl),
            (nameof(ArrivedDefendersStayUnderPositionControl), ArrivedDefendersStayUnderPositionControl),
            (nameof(AutonomousDefendersSeekCoverEvenWithVisibleContact), AutonomousDefendersSeekCoverEvenWithVisibleContact),
            (nameof(DefensivePositionOwnershipStaysLatchedOutsideTheArrivalArea), DefensivePositionOwnershipStaysLatchedOutsideTheArrivalArea),
            (nameof(ReachedCoverCreatesAStableFightingHalt), ReachedCoverCreatesAStableFightingHalt),
            (nameof(AttackBoundsRequireSafetyAndTacticalAuthorization), AttackBoundsRequireSafetyAndTacticalAuthorization),
            (nameof(OpenFieldAttackBoundsIgnoreDirectFireButNotPinning), OpenFieldAttackBoundsIgnoreDirectFireButNotPinning),
            (nameof(SustainedDirectFireOnCoverEventuallyAuthorizesABound), SustainedDirectFireOnCoverEventuallyAuthorizesABound),
            (nameof(CoveringFireIsAcceptedFromAnyConfirmedEnemyToken), CoveringFireIsAcceptedFromAnyConfirmedEnemyToken),
            (nameof(MoverWithNoShotsFiredCanStillAdvanceOnCoveringFire), MoverWithNoShotsFiredCanStillAdvanceOnCoveringFire),
            (nameof(PinnedReleaseGrantsImmunityOnlyOnTimeCapRelease), PinnedReleaseGrantsImmunityOnlyOnTimeCapRelease),
            (nameof(DefensiveRelocationsRemainWithinHoldArea), DefensiveRelocationsRemainWithinHoldArea),
            (nameof(DefendersRequireProtectionBeforeAnchoringCover), DefendersRequireProtectionBeforeAnchoringCover),
            (nameof(DefensiveCoverAnchorSurvivesNativeStatusFlicker), DefensiveCoverAnchorSurvivesNativeStatusFlicker),
            (nameof(ReachedBuildingSlotLatchesWithoutNativeCoverFlag), ReachedBuildingSlotLatchesWithoutNativeCoverFlag),
            (nameof(NativeCoverClearRespectsProtectedOwnership), NativeCoverClearRespectsProtectedOwnership),
            (nameof(PlayerHoldArrivalClaimsStableProtectedPositions), PlayerHoldArrivalClaimsStableProtectedPositions),
            (nameof(StableAnchorsKeepTheirSpatialReservations), StableAnchorsKeepTheirSpatialReservations),
            (nameof(PlayerHoldCoverDoesNotTakeOverOtherExternalOrders), PlayerHoldCoverDoesNotTakeOverOtherExternalOrders),
            (nameof(WalkingInPlaceTriggersAQuietRecoveryHold), WalkingInPlaceTriggersAQuietRecoveryHold),
            (nameof(RealMovementAndPathChangesResetTheStallWatch), RealMovementAndPathChangesResetTheStallWatch),
            (nameof(TransportDismountsBeforeTakingFire), TransportDismountsBeforeTakingFire),
            (nameof(AttackCoverCorridorAllowsFlankingWithinBoundedBacktrack), AttackCoverCorridorAllowsFlankingWithinBoundedBacktrack),
            (nameof(FailedCoverSearchBacksOffProgressively), FailedCoverSearchBacksOffProgressively),
            (nameof(PoseArbiterLatchShapesTransitions), PoseArbiterLatchShapesTransitions),
            (nameof(DisagreeingStationaryOwnersConvergeToOneStance), DisagreeingStationaryOwnersConvergeToOneStance),
            (nameof(ClearanceStandIsGrantedWhileADefensiveHoldIsActive), ClearanceStandIsGrantedWhileADefensiveHoldIsActive),
            (nameof(OwnerHandoffHonorsTheAntiFlickerWindow), OwnerHandoffHonorsTheAntiFlickerWindow),
            (nameof(FireArbiterDefaultsToMayFire), FireArbiterDefaultsToMayFire),
            (nameof(RequiredActionAndHazardOutrankEveryLesserFireBlocker), RequiredActionAndHazardOutrankEveryLesserFireBlocker),
            (nameof(PinnedShockBlocksFireThenExpires), PinnedShockBlocksFireThenExpires),
            (nameof(MovingFireIsBoundedByWeaponRoleAndDistance), MovingFireIsBoundedByWeaponRoleAndDistance),
            (nameof(MovementArbiterSafetyOutranksEveryLesserOwner), MovementArbiterSafetyOutranksEveryLesserOwner),
            (nameof(CommittedCoverMoveSurvivesATransientContact), CommittedCoverMoveSurvivesATransientContact),
            (nameof(LapsedHoldsReturnTheSoldierToNativeMovement), LapsedHoldsReturnTheSoldierToNativeMovement),
            (nameof(HaltSpacingStepsOffTheThreatAxisOnlyWhenStacked), HaltSpacingStepsOffTheThreatAxisOnlyWhenStacked),
            (nameof(AMovingSoldierIsNeverHeldProne), AMovingSoldierIsNeverHeldProne),
            (nameof(SafetyPosesOutrankTheMovementContract), SafetyPosesOutrankTheMovementContract),
            (nameof(AHaltedSoldierKeepsHisProneCoverSlot), AHaltedSoldierKeepsHisProneCoverSlot),
            (nameof(CommandLeasesAreStableAndRejectStaleWork), CommandLeasesAreStableAndRejectStaleWork),
            (nameof(CommandLeaseDebugSnapshotIsOrderedAndPrunesExpiredWork), CommandLeaseDebugSnapshotIsOrderedAndPrunesExpiredWork),
            (nameof(ExternalOwnershipPreemptsAndLatches), ExternalOwnershipPreemptsAndLatches),
            (nameof(TacticalArbitrationUsesOneDeterministicWinnerPerChannel), TacticalArbitrationUsesOneDeterministicWinnerPerChannel),
            (nameof(ProtectedAssignmentOutranksCoverHoldAtEqualAuthority), ProtectedAssignmentOutranksCoverHoldAtEqualAuthority),
            (nameof(ExternalSquadWithoutPlayerHoldCoverEmitsOnlyNativeAndExternal), ExternalSquadWithoutPlayerHoldCoverEmitsOnlyNativeAndExternal),
            (nameof(PlayerHoldCoverFollowsCommittedCoverMove), PlayerHoldCoverFollowsCommittedCoverMove),
            (nameof(MovementSafetyLadderPicksHazardThenSafetyThenSuppression), MovementSafetyLadderPicksHazardThenSafetyThenSuppression),
            (nameof(ProtectedAssignmentSkipsDefensivePositionBranch), ProtectedAssignmentSkipsDefensivePositionBranch),
            (nameof(ContactResponseRequiresPolicyEnabled), ContactResponseRequiresPolicyEnabled),
            (nameof(ReloadSafetyAddsProneAndFireInhibitionAlongsideTheHold), ReloadSafetyAddsProneAndFireInhibitionAlongsideTheHold),
            (nameof(MovementDebugProjectionUsesOnlyExecutorDestination), MovementDebugProjectionUsesOnlyExecutorDestination),
            (nameof(AiDebugAllegianceScopeIsExplicitAndFailClosed), AiDebugAllegianceScopeIsExplicitAndFailClosed),
            (nameof(GameplayMutationIsHostAuthoritative), GameplayMutationIsHostAuthoritative),
            (nameof(DefenderAllocatorStaffsAllViableWeaponsInPriorityOrder), DefenderAllocatorStaffsAllViableWeaponsInPriorityOrder),
            (nameof(DefenderAllocatorProtectsReserveAndCriticalFootStrength), DefenderAllocatorProtectsReserveAndCriticalFootStrength),
            (nameof(DefenderAllocatorHandlesInsufficientCrewsAndInvalidWeapons), DefenderAllocatorHandlesInsufficientCrewsAndInvalidWeapons),
            (nameof(ProtectedWeaponTransitLeaseSurvivesTemporaryInterruption), ProtectedWeaponTransitLeaseSurvivesTemporaryInterruption),
            (nameof(StaticWeaponTransitAcceptsAssignedSeatReservation), StaticWeaponTransitAcceptsAssignedSeatReservation),
            (nameof(LauncherSelectionWaitsForEffectiveRange), LauncherSelectionWaitsForEffectiveRange),
            (nameof(DetonatingProjectilesAreLeftToTheBaseGame), DetonatingProjectilesAreLeftToTheBaseGame),
            (nameof(FortifiedCoverBeatsCloserWeakCover), FortifiedCoverBeatsCloserWeakCover),
            (nameof(FortifiedSlotsGroupWithoutDuplicateReservations), FortifiedSlotsGroupWithoutDuplicateReservations),
            (nameof(DefensiveAnchorsOnlyMoveAfterMaterialDegradation), DefensiveAnchorsOnlyMoveAfterMaterialDegradation),
            (nameof(CoverSamplingKeepsWorkBoundedAndIncludesDepth), CoverSamplingKeepsWorkBoundedAndIncludesDepth),
            (nameof(AuthoredCoverRemainsAFallbackWhenBallisticsCannotClassifyIt), AuthoredCoverRemainsAFallbackWhenBallisticsCannotClassifyIt),
            (nameof(ProtectedFiringLaneBeatsEquallyProtectedBlindSlot), ProtectedFiringLaneBeatsEquallyProtectedBlindSlot),
            (nameof(PostureThreatAxisStabilizesAcrossAlternatingBearings), PostureThreatAxisStabilizesAcrossAlternatingBearings),
            (nameof(CoverDowngradeToProneRequiresPersistence), CoverDowngradeToProneRequiresPersistence),
            (nameof(AuthoredPoseIsKeptOnlyWhenBallisticsCannotClassify), AuthoredPoseIsKeptOnlyWhenBallisticsCannotClassify),
            (nameof(CoverPostureOwnershipSurvivesBriefContactLoss), CoverPostureOwnershipSurvivesBriefContactLoss),
            (nameof(EngagedCoverPoseConvergesInsteadOfLooping), EngagedCoverPoseConvergesInsteadOfLooping),
            (nameof(SuppressionRecoveryKeepsAnAlreadyProneSoldierDownInTheOpen), SuppressionRecoveryKeepsAnAlreadyProneSoldierDownInTheOpen),
            (nameof(SuppressionRecoveryPreventsProneCrouchLoopOnRelease), SuppressionRecoveryPreventsProneCrouchLoopOnRelease),
            (nameof(SuppressionRecoveryDefersToAnOwnedProneCoverEvaluation), SuppressionRecoveryDefersToAnOwnedProneCoverEvaluation),
            (nameof(WrongSideAnchorReleasesForALiveEngagedThreat), WrongSideAnchorReleasesForALiveEngagedThreat),
            (nameof(TargetObservationAccruesOnlyDuringContinuousWatching), TargetObservationAccruesOnlyDuringContinuousWatching),
            (nameof(TankEngagementEntersAndReleasesHoldWithHysteresis), TankEngagementEntersAndReleasesHoldWithHysteresis),
            (nameof(TankEngagementLosFlickerGrantsGraceBeforeReleasingHold), TankEngagementLosFlickerGrantsGraceBeforeReleasingHold),
            (nameof(TankEngagementHoldAndReverseNeverDitherAroundTheirBoundaries), TankEngagementHoldAndReverseNeverDitherAroundTheirBoundaries),
            (nameof(TankEngagementDamagedReverseDoesNotLoopWhenRearIsBlocked), TankEngagementDamagedReverseDoesNotLoopWhenRearIsBlocked),
            (nameof(TankEngagementHullRotationOnlyAllowedInHoldOutsideReverseBand), TankEngagementHullRotationOnlyAllowedInHoldOutsideReverseBand),
            (nameof(TankStallWatchdogRecoversResetsAndGivesUp), TankStallWatchdogRecoversResetsAndGivesUp)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} deterministic scenarios passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void DetonatingProjectilesAreLeftToTheBaseGame()
    {
        // Failure criterion: a rocket or HEAT warhead must never be carried through a prop
        // and re-spawned past the exit face. Shell-type tests missed this because the game's
        // IsHe() covers HE, APHE and Rocket but not HEAT, so the projectile's own behaviour
        // and blast radius are what the classifier must key on.
        True(ProjectilePenetrationEligibilityCore.Detonates(true, 0f),
            "A projectile that explodes on impact was offered to the penetration system.");
        True(ProjectilePenetrationEligibilityCore.Detonates(false, 3.5f),
            "A projectile with a blast radius was offered to the penetration system.");
        False(ProjectilePenetrationEligibilityCore.Detonates(false, 0f),
            "An inert solid round was withheld from the penetration system.");
        False(ProjectilePenetrationEligibilityCore.Detonates(false, float.NaN),
            "Unreadable blast data withheld an ordinary round from the penetration system.");
        False(ProjectilePenetrationEligibilityCore.Detonates(false, -1f),
            "A negative blast radius was treated as an explosive warhead.");
    }

    private static void LauncherSelectionWaitsForEffectiveRange()
    {
        True(LauncherSelectionDecisionCore.ShouldDeferSelection(true, true, 90.01f, 90f),
            "A launcher was selected beyond its maximum engagement distance.");
        False(LauncherSelectionDecisionCore.ShouldDeferSelection(true, true, 90f, 90f),
            "A launcher was withheld on the exact engagement-range boundary.");
        False(LauncherSelectionDecisionCore.ShouldDeferSelection(false, true, 200f, 90f),
            "A high-velocity anti-tank weapon was incorrectly range-gated.");
        False(LauncherSelectionDecisionCore.ShouldDeferSelection(true, false, 200f, 90f),
            "Missing target data overrode the base game's weapon selection.");
        False(LauncherSelectionDecisionCore.ShouldDeferSelection(true, true, float.NaN, 90f),
            "Invalid distance data overrode the base game's weapon selection.");
    }

    private static void CoverSamplingKeepsWorkBoundedAndIncludesDepth()
    {
        var indices = CoverCandidateSamplingCore.SelectIndices(96, 12, 6);
        Equal(12, indices.Length, "Detailed cover work exceeded its fixed budget.");
        SequenceEqual(Enumerable.Range(0, 6), indices.Take(6),
            "The nearest cover candidates were not retained.");
        Equal(95, indices[^1], "The deepest candidate was omitted from broad sampling.");
        Equal(indices.Length, indices.Distinct().Count(),
            "Cover sampling selected a candidate more than once.");
        True(indices.All(index => index >= 0 && index < 96),
            "Cover sampling produced an out-of-range index.");

        SequenceEqual(Enumerable.Range(0, 5),
            CoverCandidateSamplingCore.SelectIndices(5, 12, 6),
            "Small cover inventories should be evaluated in full.");
        Equal(0, CoverCandidateSamplingCore.SelectIndices(96, 0, 6).Length,
            "A zero detail budget should select no cover candidates.");

        var defensive = CoverCandidateSamplingCore.SelectIndices(192, 20, 3);
        Equal(20, defensive.Length,
            "Defensive building/trench sampling exceeded its bounded detail budget.");
        SequenceEqual(Enumerable.Range(0, 3), defensive.Take(3),
            "Defensive sampling did not retain its nearest candidates.");
        Equal(191, defensive[^1],
            "Defensive sampling failed to inspect deep building/trench candidates.");
        True(defensive.Count(index => index >= 96) >= 8,
            "Defensive sampling remained biased toward nearby open-ground cover.");
    }

    private static void AttackCoverCorridorAllowsFlankingWithinBoundedBacktrack()
    {
        var soldier = new MapPoint(0f, 0f);

        // Strict forward progress is always accepted, on- or off-axis.
        True(AttackCoverCorridorCore.Accepts(
                soldier, new MapPoint(10f, 50f), new MapPoint(0f, 100f)),
            "A candidate closer to the waypoint was rejected.");

        // A small lateral step inside the forward corridor that only slightly
        // increases the distance to a nearby waypoint is accepted.
        True(AttackCoverCorridorCore.Accepts(
                soldier, new MapPoint(10f, 15f), new MapPoint(0f, 10f)),
            "A small lateral step inside the forward corridor was rejected.");

        // A large retreat directly away from the waypoint is rejected.
        False(AttackCoverCorridorCore.Accepts(
                soldier, new MapPoint(0f, -20f), new MapPoint(0f, 100f)),
            "A large backtrack away from the waypoint was accepted.");

        // A candidate only 1 m behind but well off the assault axis is rejected by
        // the corridor even though its backtrack is small.
        False(AttackCoverCorridorCore.Accepts(
                soldier, new MapPoint(6f, -1f), new MapPoint(0f, 100f)),
            "An off-axis candidate beyond the forward corridor was accepted.");

        // Non-finite inputs fail safe.
        False(AttackCoverCorridorCore.Accepts(
                soldier, new MapPoint(float.NaN, 5f), new MapPoint(0f, 100f)),
            "A NaN candidate was accepted.");

        // Boundary: an on-axis candidate exactly MaximumBacktrackMeters farther from
        // the waypoint is accepted; a hair beyond is rejected.
        True(AttackCoverCorridorCore.Accepts(
                soldier, new MapPoint(0f, 48f), new MapPoint(0f, 20f)),
            "A candidate exactly on the backtrack boundary was rejected.");
        False(AttackCoverCorridorCore.Accepts(
                soldier, new MapPoint(0f, 48.1f), new MapPoint(0f, 20f)),
            "A candidate just beyond the backtrack boundary was accepted.");

        // A candidate on the soldier's own footprint is accepted regardless of
        // waypoint geometry.
        True(AttackCoverCorridorCore.Accepts(
                soldier, new MapPoint(1f, 0f), new MapPoint(0f, 100f)),
            "A candidate on the soldier's own footprint was rejected.");
    }

    private static void FailedCoverSearchBacksOffProgressively()
    {
        var baseInterval = 12f;

        Near(baseInterval,
            CoverSearchBackoffCore.NextDecisionDelaySeconds(baseInterval, 0),
            0.001f,
            "A first failure did not use the normal decision interval.");
        Near(baseInterval,
            CoverSearchBackoffCore.NextDecisionDelaySeconds(baseInterval, 1),
            0.001f,
            "A single failure did not use the normal decision interval.");
        Near(20f,
            CoverSearchBackoffCore.NextDecisionDelaySeconds(baseInterval, 2),
            0.001f,
            "A second failure did not extend the decision interval.");
        Near(30f,
            CoverSearchBackoffCore.NextDecisionDelaySeconds(baseInterval, 3),
            0.001f,
            "A third failure did not reach the maximum backoff.");
        Near(30f,
            CoverSearchBackoffCore.NextDecisionDelaySeconds(baseInterval, 9),
            0.001f,
            "Repeated failures exceeded the maximum backoff.");

        // Invalid inputs fall back to the base interval.
        Near(baseInterval,
            CoverSearchBackoffCore.NextDecisionDelaySeconds(baseInterval, -4),
            0.001f,
            "A negative failure count altered the backoff.");
        Near(baseInterval,
            CoverSearchBackoffCore.NextDecisionDelaySeconds(float.NaN, 3),
            0.001f,
            "An invalid base interval was not rejected.");
    }

    // Mirrors ContactResponse.CommitArbitratedPose over the pure PoseArbiterCore: applies
    // one arbitrated (owner, stance) proposal to the latched (owner, stance, holdUntil)
    // and returns whether the STANCE changed. A same-stance owner relabel leaves the hold
    // untouched, exactly as the real latch does.
    private static bool StepLatch(
        ref PoseOwner owner,
        ref TacticalStance stance,
        ref float holdUntil,
        PoseOwner proposedOwner,
        TacticalStance proposedStance,
        bool measuredStand,
        float t)
    {
        if (!PoseArbiterCore.ShouldAccept(
                owner, stance, proposedOwner, proposedStance, measuredStand, t, holdUntil))
        {
            return false;
        }

        owner = proposedOwner;
        if (stance == proposedStance)
            return false;

        stance = proposedStance;
        holdUntil = t + PoseArbiterCore.MinimumHoldSeconds;
        return true;
    }

    private static void PoseArbiterLatchShapesTransitions()
    {
        // A more protective (more covered) stance is always safe immediately, whoever
        // proposes it.
        True(PoseArbiterCore.ShouldAccept(
                PoseOwner.SuppressionRecovery, TacticalStance.Crouched,
                PoseOwner.SuppressionRecovery, TacticalStance.Prone, false, 1f, 10f),
            "An immediate lower safety posture was delayed.");

        // Raising to a more exposed stance under the same owner waits out the hold.
        False(PoseArbiterCore.ShouldAccept(
                PoseOwner.SuppressionRecovery, TacticalStance.Prone,
                PoseOwner.SuppressionRecovery, TacticalStance.Crouched, false, 9.9f, 10f),
            "Prone was released before its minimum stable hold expired.");
        True(PoseArbiterCore.ShouldAccept(
                PoseOwner.SuppressionRecovery, TacticalStance.Prone,
                PoseOwner.SuppressionRecovery, TacticalStance.Crouched, false, 10f, 10f),
            "A pose could not rise after its minimum stable hold expired.");

        // An unchanged committed (owner, pose) is never a transition.
        False(PoseArbiterCore.ShouldAccept(
                PoseOwner.TacticalCrouch, TacticalStance.Crouched,
                PoseOwner.TacticalCrouch, TacticalStance.Crouched, false, 5f, 0f),
            "An unchanged (owner, pose) was treated as a transition.");

        // The same committed stance under a new owner relabels immediately (no motion),
        // even inside the anti-flicker window.
        True(PoseArbiterCore.ShouldAccept(
                PoseOwner.TacticalCrouch, TacticalStance.Crouched,
                PoseOwner.SuppressionRecovery, TacticalStance.Crouched, false, 5f, 100f),
            "A same-stance owner relabel was blocked by the anti-flicker hold.");

        // A higher-priority owner takes over at once, even to a more exposed stance.
        True(PoseArbiterCore.ShouldAccept(
                PoseOwner.CoverEvaluation, TacticalStance.Crouched,
                PoseOwner.CoverClearance, TacticalStance.Standing, false, 1f, 100f),
            "A higher-priority owner could not take over before the hold expired.");

        // A lower-priority owner taking over from a released higher one waits out the hold
        // when it raises the soldier to a more exposed stance.
        False(PoseArbiterCore.ShouldAccept(
                PoseOwner.TacticalCrouch, TacticalStance.Crouched,
                PoseOwner.HaltFallback, TacticalStance.Standing, false, 5f, 100f),
            "A lower-priority owner raised the soldier before the anti-flicker window.");
        True(PoseArbiterCore.ShouldAccept(
                PoseOwner.TacticalCrouch, TacticalStance.Crouched,
                PoseOwner.HaltFallback, TacticalStance.Standing, false, 100f, 100f),
            "A lower-priority owner could not take over after the hold expired.");

        // Non-finite timing never accepts.
        False(PoseArbiterCore.ShouldAccept(
                PoseOwner.TacticalCrouch, TacticalStance.Crouched,
                PoseOwner.SuppressionRecovery, TacticalStance.Prone, false, float.NaN, 5f),
            "A non-finite timestamp was accepted.");
    }

    private static void DisagreeingStationaryOwnersConvergeToOneStance()
    {
        // W1: two halt call sites used to force different stationary poses on adjacent
        // frames (Crouch via the StationaryHoldPose fallback, Prone via the defensive-
        // occupation waiting branch), and the asymmetric latch amplified that into a
        // sustained ~3.5s Prone<->Crouch rhythm. The single arbiter now resolves ONE
        // (owner, pose) per frame from the ownership flags: with a contact/tactical
        // crouch owner active and no usable cover, every writer resolves to
        // (TacticalCrouch, Crouched) - the ownerless Prone fallback is never selected
        // because an owner is active. Drive the latch with that deterministic proposal
        // across a long hold and assert it converges once and never oscillates.
        var owner = PoseOwner.None;
        var stance = TacticalStance.Standing;
        var holdUntil = 0f;
        var poseChanges = 0;
        for (var t = 0f; t <= 30f; t += 0.5f)
        {
            if (StepLatch(ref owner, ref stance, ref holdUntil,
                    PoseOwner.TacticalCrouch, TacticalStance.Crouched, measuredStand: false, t))
            {
                poseChanges++;
            }
        }

        Equal(TacticalStance.Crouched, stance,
            "The disagreeing stationary owners did not converge to one stance.");
        Equal(1, poseChanges,
            "The converged stationary pose oscillated instead of settling exactly once.");
    }

    private static void ClearanceStandIsGrantedWhileADefensiveHoldIsActive()
    {
        // W3: a defender crouched on anchored cover (a permanent hold) whose muzzle is
        // obstructed must be able to stand and clear it. The old latch renewed the hold
        // forever whenever a lower-pose owner was active, so the crouch->stand rise was
        // rejected on every frame and the defender stayed pinned behind the parapet. The
        // clearance owner now outranks the cover-evaluation owner and takes over at once,
        // and the cover-evaluation owner may raise its OWN measured stand at once - both
        // grant the stand even though the anchored hold never expires on its own.
        var owner = PoseOwner.CoverEvaluation;
        var stance = TacticalStance.Crouched;
        var holdUntil = 100f; // an anchored hold that never expires

        True(StepLatch(ref owner, ref stance, ref holdUntil,
                PoseOwner.CoverClearance, TacticalStance.Standing, measuredStand: true, t: 1f),
            "The cover-clearance owner could not stand a crouched defender to clear his muzzle.");
        Equal(TacticalStance.Standing, stance, "The clearance stand was not committed.");

        // The cover-evaluation owner re-measuring a standing firing pose gets its own
        // explicitly measured stand even while the hold is unexpired; an UNMEASURED
        // crouch->stand still waits out the hold.
        True(PoseArbiterCore.ShouldAccept(
                PoseOwner.CoverEvaluation, TacticalStance.Crouched,
                PoseOwner.CoverEvaluation, TacticalStance.Standing,
                proposedMeasuredStand: true, now: 1f, holdUntil: 100f),
            "A cover-evaluation owner could not raise its own measured stand before the hold.");
        False(PoseArbiterCore.ShouldAccept(
                PoseOwner.CoverEvaluation, TacticalStance.Crouched,
                PoseOwner.CoverEvaluation, TacticalStance.Standing,
                proposedMeasuredStand: false, now: 1f, holdUntil: 100f),
            "An unmeasured crouch->stand skipped the anti-flicker hold.");
    }

    private static void OwnerHandoffHonorsTheAntiFlickerWindow()
    {
        // A higher-priority owner releasing to a lower one that wants a MORE EXPOSED pose
        // must wait out the anti-flicker window, so a soldier does not pop upright the
        // instant a suppression/cover owner lapses.
        var owner = PoseOwner.TacticalCrouch;
        var stance = TacticalStance.Crouched;
        var holdUntil = 0f;

        // Cover evaluation drops the soldier prone at t=1 (instant; sets the hold to 4.5).
        True(StepLatch(ref owner, ref stance, ref holdUntil,
                PoseOwner.CoverEvaluation, TacticalStance.Prone, false, 1f),
            "A more protective cover-evaluation prone was not adopted immediately.");

        // At t=2 that owner has lapsed and a lower crouch owner wants him back up. Held.
        False(StepLatch(ref owner, ref stance, ref holdUntil,
                PoseOwner.TacticalCrouch, TacticalStance.Crouched, false, 2f),
            "A lower owner raised the soldier out of prone before the anti-flicker window.");
        Equal(TacticalStance.Prone, stance, "The prone stance was dropped early.");

        // After the window the handoff completes.
        True(StepLatch(ref owner, ref stance, ref holdUntil,
                PoseOwner.TacticalCrouch, TacticalStance.Crouched, false, 4.5f),
            "The owner handoff never completed after the anti-flicker window.");
        Equal(TacticalStance.Crouched, stance, "The owner handoff did not settle on the new stance.");
    }

    // Mirrors ContactResponse.ComputeFireDecision over the pure FireArbiterCore: the
    // moving predicate is the LOWEST-priority blocker, so it is only consulted when
    // nothing above already decided the frame.
    private static FireBlocker ResolveFire(
        bool nativeControlled = false,
        bool requiredAction = false,
        bool hazard = false,
        bool pinnedShock = false,
        bool rangeInhibited = false,
        bool movingWithoutMovingFire = false)
        => FireArbiterCore.Resolve(
            nativeControlled, requiredAction, hazard, pinnedShock, rangeInhibited,
            movingWithoutMovingFire);

    private static void FireArbiterDefaultsToMayFire()
    {
        // The default is permission: absent a reason to withhold the trigger the soldier
        // fires. This is the property the deleted FireRestorePending handshake could not
        // guarantee - a missed consume step left a soldier mute forever.
        Equal(FireBlocker.None, ResolveFire(), "A soldier with no blocker was denied fire.");
        True(FireArbiterCore.MayFire(ResolveFire()), "The default decision was not may-fire.");

        // A mounted or externally driven soldier is a passthrough, not a blocked one: the
        // mounted duck owns that channel and this arbiter must not write the flag at all.
        Equal(FireBlocker.NativeControl, ResolveFire(nativeControlled: true),
            "A mounted/native soldier was not passed through.");
        False(FireArbiterCore.MayFire(ResolveFire(nativeControlled: true)),
            "A passthrough decision was treated as permission to fire.");
    }

    private static void RequiredActionAndHazardOutrankEveryLesserFireBlocker()
    {
        // Failure criterion: a soldier must never fire during a reload/bandage. Required
        // action wins over every lesser reason, and over all of them at once.
        Equal(FireBlocker.RequiredAction,
            ResolveFire(requiredAction: true, hazard: true, pinnedShock: true,
                rangeInhibited: true, movingWithoutMovingFire: true),
            "A required action lost the fire channel to a lesser blocker.");

        // Native control still outranks the required action - it is a passthrough.
        Equal(FireBlocker.NativeControl, ResolveFire(nativeControlled: true, requiredAction: true),
            "A mounted soldier was resolved as a foot-soldier required action.");

        // Then the rest of the ladder, each beating everything below it.
        Equal(FireBlocker.Hazard,
            ResolveFire(hazard: true, pinnedShock: true, rangeInhibited: true,
                movingWithoutMovingFire: true),
            "A lethal hazard lost the fire channel to a lesser blocker.");
        Equal(FireBlocker.PinnedShock,
            ResolveFire(pinnedShock: true, rangeInhibited: true, movingWithoutMovingFire: true),
            "The pinned shock lost the fire channel to a lesser blocker.");
        Equal(FireBlocker.Range,
            ResolveFire(rangeInhibited: true, movingWithoutMovingFire: true),
            "The range/armor inhibition lost the fire channel to the moving blocker.");
        Equal(FireBlocker.Moving, ResolveFire(movingWithoutMovingFire: true),
            "A soldier moving without moving-fire permission was allowed to fire.");
    }

    private static void PinnedShockBlocksFireThenExpires()
    {
        // The shock reaction is bounded by PinnedFireBlockedUntil: the soldier is mute
        // while it runs and returns fire from his committed posture once it passes. He is
        // still pinned - only the trigger comes back.
        const float pinnedFireBlockedUntil = 12f;
        for (var now = 10f; now < pinnedFireBlockedUntil; now += 0.5f)
        {
            Equal(FireBlocker.PinnedShock, ResolveFire(pinnedShock: now < pinnedFireBlockedUntil),
                "A pinned soldier fired during his shock reaction.");
        }

        Equal(FireBlocker.None, ResolveFire(pinnedShock: 12f < pinnedFireBlockedUntil),
            "A pinned soldier stayed mute after the bounded shock reaction expired.");
        Equal(FireBlocker.None, ResolveFire(pinnedShock: 30f < pinnedFireBlockedUntil),
            "The pinned shock never released the fire channel.");
    }

    private static void MovingFireIsBoundedByWeaponRoleAndDistance()
    {
        const float smgBand = 20f;
        const float rifleBand = 12f;

        // vision.md: ordinary rifles do not fire while moving. The shipped default band is
        // 0, so a moving rifleman is blocked at every distance and halts to shoot instead.
        False(MovingFireCore.Allows(true, MovingFireWeapon.Rifle, true, 1f, smgBand, 0f),
            "A rifleman fired while moving with the rifle band disabled.");

        // With an opt-in band he may fire inside it and is blocked beyond it.
        True(MovingFireCore.Allows(true, MovingFireWeapon.Rifle, true, rifleBand - 4f, smgBand, rifleBand),
            "A rifleman inside the configured close band could not fire while moving.");
        True(MovingFireCore.Allows(true, MovingFireWeapon.Rifle, true, rifleBand, smgBand, rifleBand),
            "The rifle moving-fire band excluded its own boundary.");
        False(MovingFireCore.Allows(true, MovingFireWeapon.Rifle, true, rifleBand + 0.1f, smgBand, rifleBand),
            "A rifleman fired while moving beyond the configured close band.");

        // SMGs keep their own, wider close-assault band.
        True(MovingFireCore.Allows(true, MovingFireWeapon.SubmachineGun, true, smgBand, smgBand, rifleBand),
            "An SMG could not fire while moving inside its own band.");
        False(MovingFireCore.Allows(true, MovingFireWeapon.SubmachineGun, true, smgBand + 1f, smgBand, rifleBand),
            "An SMG fired while moving beyond its own band.");

        // Failure criterion: machine guns and launchers never fire while moving, at any
        // distance and whatever the rifle band is set to.
        False(MovingFireCore.Allows(true, MovingFireWeapon.MachineGun, true, 1f, smgBand, rifleBand),
            "A machine gunner fired while moving.");
        False(MovingFireCore.Allows(true, MovingFireWeapon.Launcher, true, 1f, smgBand, rifleBand),
            "A launcher operator fired while moving.");

        // No usable target: nothing to permit moving fire against.
        False(MovingFireCore.Allows(true, MovingFireWeapon.SubmachineGun, false, 0f, smgBand, rifleBand),
            "Moving fire was permitted without a visible target.");
        False(MovingFireCore.Allows(true, MovingFireWeapon.Rifle, true, float.NaN, smgBand, rifleBand),
            "A non-finite target distance was treated as inside the moving-fire band.");

        // Disabling the restriction returns the base game's behavior for every role.
        True(MovingFireCore.Allows(false, MovingFireWeapon.MachineGun, false, 999f, smgBand, 0f),
            "Disabling the moving-fire restriction did not restore native behavior.");
    }

    private static MovementOwner ResolveMovement(
        MovementOwner declared = MovementOwner.Free,
        bool safetyHalt = false,
        bool hazardEscape = false,
        bool pinnedHold = false,
        bool haltSpacing = false,
        bool engagementHold = false,
        bool coverHold = false,
        bool committedMove = false)
        => MovementArbiterCore.Resolve(
            declared, safetyHalt, hazardEscape, pinnedHold, haltSpacing,
            engagementHold, coverHold, committedMove);

    private static void MovementArbiterSafetyOutranksEveryLesserOwner()
    {
        // Failure criterion: a soldier must never move while burning, mid-reload, or
        // pinned. Safety wins over every lesser owner, and over all of them at once.
        Equal(MovementOwner.SafetyHalt,
            ResolveMovement(MovementOwner.OrderedMove, safetyHalt: true, hazardEscape: true,
                pinnedHold: true, haltSpacing: true, engagementHold: true,
                coverHold: true, committedMove: true),
            "A safety halt lost locomotion to a lesser owner.");
        False(MovementArbiterCore.Grants(MovementOwner.SafetyHalt),
            "The safety halt was treated as permission to move.");

        // Flame escape is the one owner above the halts that GRANTS movement: a man in
        // the beaten zone of a flamethrower leaves it even while pinned.
        Equal(MovementOwner.HazardEscape,
            ResolveMovement(hazardEscape: true, pinnedHold: true,
                engagementHold: true, coverHold: true, committedMove: true),
            "Flame evasion lost locomotion to a lesser halt.");
        True(MovementArbiterCore.Grants(MovementOwner.HazardEscape),
            "Flame evasion did not grant movement.");

        // Then the rest of the ladder, each beating everything below it. Safety
        // specifically outranks the engagement hold, which is the case that decides
        // whether a pinned or reloading soldier can be walked out of his own halt.
        Equal(MovementOwner.PinnedHold,
            ResolveMovement(pinnedHold: true, haltSpacing: true,
                engagementHold: true, coverHold: true, committedMove: true),
            "Pinning lost locomotion to a lesser owner.");
        Equal(MovementOwner.HaltSpacing,
            ResolveMovement(haltSpacing: true, engagementHold: true, coverHold: true),
            "The halt-spacing step could not step out of the halt it belongs to.");
        Equal(MovementOwner.EngagementHold,
            ResolveMovement(engagementHold: true, coverHold: true, committedMove: true),
            "The engagement hold lost locomotion to a lesser owner.");
        Equal(MovementOwner.CoverHold,
            ResolveMovement(coverHold: true, committedMove: true),
            "The cover hold lost locomotion to a committed move.");

        // A declared owner competes at its own rank rather than bypassing the ladder: a
        // grenade-safety halt with no state of its own still wins, an ordered move does
        // not override a live fighting halt.
        Equal(MovementOwner.SafetyHalt,
            ResolveMovement(MovementOwner.SafetyHalt, engagementHold: true, committedMove: true),
            "A declared safety halt was outranked by a fighting halt.");
        Equal(MovementOwner.EngagementHold,
            ResolveMovement(MovementOwner.OrderedMove, engagementHold: true),
            "A declared ordered move overrode a live engagement hold.");
    }

    private static void CommittedCoverMoveSurvivesATransientContact()
    {
        // A dash to a chosen cover slot outranks the ordered move it replaced, so a
        // transient contact that only re-arms the objective route cannot interrupt it.
        Equal(MovementOwner.CommittedMove,
            ResolveMovement(MovementOwner.OrderedMove, committedMove: true),
            "A committed cover move was downgraded to an ordered move.");
        True(MovementArbiterCore.Grants(MovementOwner.CommittedMove),
            "A committed cover move did not grant locomotion.");

        // Failure criterion: it must not be interrupted every frame by a lower-priority
        // owner. Nothing below it can halt it.
        for (var frame = 0; frame < 8; frame++)
        {
            Equal(MovementOwner.CommittedMove,
                ResolveMovement(MovementOwner.OrderedMove, committedMove: true),
                "A committed cover move was interrupted by a lower-priority owner.");
        }

        // A real halt still wins - the dash is committed, not immune.
        Equal(MovementOwner.EngagementHold,
            ResolveMovement(committedMove: true, engagementHold: true),
            "A close-contact fighting halt could not pause a committed cover move.");
        Equal(MovementOwner.PinnedHold,
            ResolveMovement(committedMove: true, pinnedHold: true),
            "A pinned soldier kept running to cover.");
    }

    private static void LapsedHoldsReturnTheSoldierToNativeMovement()
    {
        // Every hold in the ladder is a bounded timer read as an input (plan 015). Once
        // they all lapse and no site declares anything, the arbiter yields Free: nothing
        // is written and native locomotion owns the soldier again. This is the ladder-gap
        // failure criterion - every branch terminates in a decision.
        const float engagementHoldUntil = 14f;
        const float coverHoldUntil = 18f;
        var sawFree = false;
        for (var now = 10f; now <= 24f; now += 1f)
        {
            var owner = ResolveMovement(
                engagementHold: now < engagementHoldUntil,
                coverHold: now < coverHoldUntil);
            if (now < engagementHoldUntil)
                Equal(MovementOwner.EngagementHold, owner, "A live engagement hold did not own locomotion.");
            else if (now < coverHoldUntil)
                Equal(MovementOwner.CoverHold, owner, "A live cover hold did not own locomotion.");
            else
            {
                Equal(MovementOwner.Free, owner, "A lapsed hold did not return the soldier to native movement.");
                sawFree = true;
            }
        }

        True(sawFree, "The holds never lapsed.");
        False(MovementArbiterCore.Halts(MovementOwner.Free),
            "The native owner was treated as a halt.");
        False(MovementArbiterCore.Grants(MovementOwner.Free),
            "The native owner wrote a movement grant instead of leaving native locomotion alone.");
    }

    // Mirrors the ordering of ContactResponse.ResolvePose across the ranks the pose/movement
    // contract touches (plan 019): the three SAFETY poses, then the movement rank, then
    // whatever fighting pose the cover/suppression evaluation would have produced.
    private static TacticalStance ResolvePoseWithMovementContract(
        MovementOwner committedMovement,
        bool movementHalted,
        TacticalStance fightingStance,
        out PoseOwner owner,
        bool requiredAction = false,
        bool pinnedOrBurning = false,
        bool flameEvading = false,
        bool nativelyMoving = false)
    {
        if (requiredAction && !flameEvading)
        {
            owner = PoseOwner.RequiredAction;
            return TacticalStance.Prone;
        }
        if (pinnedOrBurning && !flameEvading)
        {
            owner = PoseOwner.Suppression;
            return TacticalStance.Prone;
        }
        if (PoseMovementContractCore.MovementOwnsPose(
                committedMovement, movementHalted, nativelyMoving))
        {
            owner = PoseOwner.MovementPose;
            return PoseMovementContractCore.MovementStance;
        }

        owner = PoseOwner.CoverEvaluation;
        return fightingStance;
    }

    private static void AMovingSoldierIsNeverHeldProne()
    {
        // The reported bug: the movement arbiter authorized a bound while the pose arbiter
        // independently held the soldier prone from a cover evaluation, so he crawled, made
        // no progress, and the stall watchdog cycled him. Every owner that GRANTS movement
        // now owns the pose, and the movement pose is not Prone.
        foreach (var granting in new[]
                 {
                     MovementOwner.OrderedMove, MovementOwner.CommittedMove,
                     MovementOwner.HaltSpacing, MovementOwner.HazardEscape
                 })
        {
            True(MovementArbiterCore.Grants(granting),
                "The ladder stopped treating a moving owner as a grant.");
            var stance = ResolvePoseWithMovementContract(
                granting, movementHalted: false, TacticalStance.Prone, out var owner);
            Equal(PoseOwner.MovementPose, owner, "A granted move did not own the pose.");
            False(stance == TacticalStance.Prone, "A soldier was told to bound while prone.");
            Equal(TacticalStance.Crouched, stance, "The movement pose was not the crouch bound.");
        }

        // A native move this mod is not overriding counts too, but only while the soldier is
        // actually moving - Free means nothing was written, not "he is running".
        Equal(TacticalStance.Crouched,
            ResolvePoseWithMovementContract(
                MovementOwner.Free, movementHalted: false, TacticalStance.Prone,
                out var nativeOwner, nativelyMoving: true),
            "A natively moving soldier kept a prone fighting pose.");
        Equal(PoseOwner.MovementPose, nativeOwner,
            "A native move did not hand the pose to the movement contract.");

        // The rank placement itself: the movement pose outranks every FIGHTING pose, which
        // is what stops rank e/f from putting a bounding soldier back on his belly.
        foreach (var fighting in new[]
                 {
                     PoseOwner.TacticalCrouch, PoseOwner.SuppressionRecovery,
                     PoseOwner.CoverEvaluation, PoseOwner.CoverClearance
                 })
        {
            True((int)PoseOwner.MovementPose > (int)fighting,
                "A fighting pose outranked the movement contract.");
        }
    }

    private static void SafetyPosesOutrankTheMovementContract()
    {
        // The invariant that keeps the two ladders consistent: wherever the pose ladder
        // insists on Prone for SAFETY, the movement ladder is already halting.
        True(MovementArbiterCore.Halts(ResolveMovement(safetyHalt: true)),
            "A required-action prone pose was not matched by a movement halt.");
        True(MovementArbiterCore.Halts(ResolveMovement(pinnedHold: true)),
            "A pinned prone pose was not matched by a movement halt.");

        foreach (var safety in new[]
                 {
                     PoseOwner.Suppression, PoseOwner.RequiredAction
                 })
        {
            True((int)safety > (int)PoseOwner.MovementPose,
                "The movement contract outranked a safety pose.");
        }

        // Even with a move declared, each safety owner keeps the soldier down.
        Equal(TacticalStance.Prone,
            ResolvePoseWithMovementContract(
                MovementOwner.OrderedMove, movementHalted: false, TacticalStance.Crouched,
                out var reloadOwner, requiredAction: true),
            "A reloading soldier stood up for a move.");
        Equal(PoseOwner.RequiredAction, reloadOwner, "The required action lost the pose.");
        Equal(TacticalStance.Prone,
            ResolvePoseWithMovementContract(
                MovementOwner.CommittedMove, movementHalted: false, TacticalStance.Crouched,
                out var pinnedOwner, pinnedOrBurning: true),
            "A pinned or burning soldier stood up for a move.");
        Equal(PoseOwner.Suppression, pinnedOwner, "Pinning lost the pose.");

        // Flame escape is the one movement GRANT above the halts, so the safety poses carry
        // the same carve-out the pinned rank always had: a man leaving a beaten zone runs.
        True(MovementArbiterCore.Grants(
                ResolveMovement(hazardEscape: true, pinnedHold: true)),
            "Flame evasion stopped granting movement.");
        Equal(TacticalStance.Crouched,
            ResolvePoseWithMovementContract(
                MovementOwner.HazardEscape, movementHalted: false, TacticalStance.Prone,
                out var hazardOwner, requiredAction: true, pinnedOrBurning: true,
                flameEvading: true),
            "A soldier escaping a flame was held prone while the ladder moved him.");
        Equal(PoseOwner.MovementPose, hazardOwner, "The flame escape did not own the pose.");
    }

    private static void AHaltedSoldierKeepsHisProneCoverSlot()
    {
        // Do not regress the halt case: the movement contract only speaks for a soldier the
        // arbiter is actually MOVING. Every halting owner leaves the fighting pose alone.
        foreach (var halting in new[]
                 {
                     MovementOwner.SafetyHalt, MovementOwner.PinnedHold,
                     MovementOwner.EngagementHold, MovementOwner.CoverHold
                 })
        {
            False(PoseMovementContractCore.MovementOwnsPose(halting, true, false),
                "A halting owner claimed the pose.");
            Equal(TacticalStance.Prone,
                ResolvePoseWithMovementContract(
                    halting, movementHalted: true, TacticalStance.Prone, out var owner),
                "A halted soldier on a prone-protective slot was raised to a crouch.");
            Equal(PoseOwner.CoverEvaluation, owner,
                "The cover evaluation lost a halted soldier's pose.");
        }

        // A stale grant cannot survive an actual halt, and Free without movement is not a
        // move: a stationary soldier the mod is not touching keeps his evaluated pose.
        False(PoseMovementContractCore.MovementOwnsPose(MovementOwner.CommittedMove, true, false),
            "A halted soldier was still treated as moving.");
        False(PoseMovementContractCore.MovementOwnsPose(MovementOwner.Free, false, false),
            "A stationary soldier under native control was treated as moving.");
        Equal(TacticalStance.Prone,
            ResolvePoseWithMovementContract(
                MovementOwner.Free, movementHalted: false, TacticalStance.Prone, out _),
            "A stationary defender popped up out of his prone cover slot.");
    }

    private static void HaltSpacingStepsOffTheThreatAxisOnlyWhenStacked()
    {
        var self = new MapPoint(10f, 10f);
        var threat = new MapPoint(10f, 40f); // due +Z, so the lateral axis is +/-X.

        // Adequately spaced: no step, the soldier simply halts where he stands.
        False(HaltSpacingCore.TryResolveStep(
                self,
                new MapPoint(10f + HaltSpacingCore.MinimumSpacingMeters + 0.5f, 10f),
                threat, true, out _),
            "A soldier with room to halt was dispersed anyway.");

        // Stacked on a halted squadmate: step laterally, ACROSS the threat axis (no
        // component toward the enemy), and to the side that opens the gap.
        True(HaltSpacingCore.TryResolveStep(
                self, new MapPoint(11f, 10f), threat, true, out var step),
            "A soldier stacked on a halted squadmate was not offset.");
        True(step.X < 0f, "The lateral step did not move away from the neighbour.");
        Equal(0f, MathF.Round(step.Z, 4), "The lateral step had a component along the threat axis.");
        Equal(HaltSpacingCore.LateralStepMeters,
            MathF.Round(MathF.Sqrt(step.X * step.X + step.Z * step.Z), 4),
            "The lateral step was not the bounded step length.");

        // Mirror side.
        True(HaltSpacingCore.TryResolveStep(
                self, new MapPoint(9f, 10f), threat, true, out var mirrored),
            "A soldier stacked from the other side was not offset.");
        True(mirrored.X > 0f, "The lateral step chose the side that closes the gap.");

        // No usable threat axis: step straight away from the neighbour instead of
        // refusing to disperse.
        True(HaltSpacingCore.TryResolveStep(
                self, new MapPoint(10f, 11f), default, false, out var noThreat),
            "A stacked soldier with no threat memory was not offset.");
        True(noThreat.Z < 0f, "The no-threat step did not move away from the neighbour.");

        // Degenerate inputs must still terminate in a bounded step, never a loop or NaN.
        True(HaltSpacingCore.TryResolveStep(self, self, self, true, out var degenerate),
            "Co-located soldiers produced no step at all.");
        True(float.IsFinite(degenerate.X) && float.IsFinite(degenerate.Z),
            "The degenerate step was not finite.");
        False(HaltSpacingCore.TryResolveStep(
                new MapPoint(float.NaN, 10f), new MapPoint(10f, 10f), threat, true, out _),
            "A non-finite position produced a dispersion step.");

        // The step is a bounded owner, not a movement mode: it outranks exactly the two
        // fighting halts it steps out of, and every real danger halt still overrides it -
        // that is what makes "halt anyway" the terminating case.
        Equal(MovementOwner.HaltSpacing,
            ResolveMovement(haltSpacing: true, engagementHold: true, coverHold: true),
            "The dispersion step could not step out of a fighting halt.");
        Equal(MovementOwner.SafetyHalt,
            ResolveMovement(haltSpacing: true, safetyHalt: true),
            "A burning or reloading soldier was walked sideways.");
    }

    private static void AuthoredCoverRemainsAFallbackWhenBallisticsCannotClassifyIt()
    {
        True(InfantryCoverDecisionCore.ShouldUseAuthoredFallback(false, true),
            "An authored trench/building slot was discarded when material sampling was inconclusive.");
        False(InfantryCoverDecisionCore.ShouldUseAuthoredFallback(true, true),
            "An unverified native slot displaced a measured protective position.");
        False(InfantryCoverDecisionCore.ShouldUseAuthoredFallback(false, false),
            "A fallback was fabricated without a valid authored cover node.");
    }

    private static void ProtectedFiringLaneBeatsEquallyProtectedBlindSlot()
    {
        var protectedBlind = new CoverScoreInput(
            64f, 0f, true, 0, false, false, 1f, 0.2f,
            PrimaryProtectionFraction: 1f,
            PreferProtectionOverFiringLine: true);
        var protectedWithLane = protectedBlind with
        {
            AssignedPoseCanFire = true,
            StandingCanFire = true
        };
        True(InfantryCoverDecisionCore.Score(
                 CoverSelectionMode.DefensiveOccupation, protectedWithLane) <
             InfantryCoverDecisionCore.Score(
                 CoverSelectionMode.DefensiveOccupation, protectedBlind),
            "An equally protected slot with a firing lane did not beat a blind one.");
    }

    private static void PostureThreatAxisStabilizesAcrossAlternatingBearings()
    {
        var east = new MapPoint(1f, 0f);

        // First acquisition adopts the observed bearing immediately.
        var acquired = ThreatAxisStabilityCore.Update(default, east, 0f);
        True(acquired.AxisChangedMaterially, "First threat bearing was not adopted.");
        Near(1f, acquired.State.Axis.X, 0.001f, "First axis was not the observed bearing.");

        // A small drift (~10 deg) follows smoothly without a material change, so the
        // cached posture evaluation survives.
        var drifted = ThreatAxisStabilityCore.Update(acquired.State, DirectionAt(10f), 0.1f);
        False(drifted.AxisChangedMaterially,
            "A small bearing drift forced a full posture re-evaluation.");

        // A large divergence (~90 deg flank) is a new threat and is adopted at once.
        var flanked = ThreatAxisStabilityCore.Update(acquired.State, DirectionAt(90f), 0.2f);
        True(flanked.AxisChangedMaterially,
            "A genuine new flank threat was not adopted promptly.");

        // A moderate divergence (~45 deg) must persist before it rotates the axis.
        var moderate = DirectionAt(45f);
        var pending = ThreatAxisStabilityCore.Update(acquired.State, moderate, 5f);
        False(pending.AxisChangedMaterially,
            "A moderate bearing change rotated the axis before persisting.");
        var early = ThreatAxisStabilityCore.Update(
            pending.State, moderate,
            5f + ThreatAxisStabilityCore.SustainedRotationSeconds - 0.01f);
        False(early.AxisChangedMaterially,
            "A moderate bearing change was accepted before its persistence window.");
        var sustained = ThreatAxisStabilityCore.Update(
            pending.State, moderate, 5f + ThreatAxisStabilityCore.SustainedRotationSeconds);
        True(sustained.AxisChangedMaterially,
            "A sustained moderate bearing change was not accepted at its exact window.");

        // A non-finite bearing is rejected and leaves the stable axis untouched.
        var invalid = ThreatAxisStabilityCore.Update(
            acquired.State, new MapPoint(float.NaN, 0f), 6f);
        False(invalid.AxisChangedMaterially, "A non-finite bearing rotated the posture axis.");
        Near(1f, invalid.State.Axis.X, 0.001f, "A non-finite bearing corrupted the stored axis.");
    }

    private static void CoverDowngradeToProneRequiresPersistence()
    {
        // Suppression and pinning never reach this gate; they keep their instant
        // reaction upstream. This core only governs cover-re-evaluation downgrades.
        False(CoverPostureDowngradeCore.ShouldAccept(
                TacticalStance.Crouched, TacticalStance.Prone, 1f,
                1f + CoverPostureDowngradeCore.MinimumDowngradeHoldSeconds - 0.01f),
            "A transient crouch->prone flip dropped the defender below the parapet.");
        True(CoverPostureDowngradeCore.ShouldAccept(
                TacticalStance.Crouched, TacticalStance.Prone, 1f,
                1f + CoverPostureDowngradeCore.MinimumDowngradeHoldSeconds),
            "A persistent crouch->prone downgrade was not accepted at its exact window.");

        // Upgrades and unchanged stances are never gated here; the separate upgrade
        // hold path governs those.
        True(CoverPostureDowngradeCore.ShouldAccept(
                TacticalStance.Prone, TacticalStance.Crouched, 100f, 0f),
            "A crouch upgrade was blocked by the prone-downgrade gate.");
        True(CoverPostureDowngradeCore.ShouldAccept(
                TacticalStance.Crouched, TacticalStance.Crouched, 100f, 0f),
            "An unchanged pose was treated as a downgrade.");
        False(CoverPostureDowngradeCore.ShouldAccept(
                TacticalStance.Standing, TacticalStance.Prone, 0f, 0.5f),
            "A standing->prone downgrade skipped its persistence window.");

        // Invalid or inverted timing keeps the safer, still-firing pose.
        False(CoverPostureDowngradeCore.ShouldAccept(
                TacticalStance.Crouched, TacticalStance.Prone, float.NaN, 5f),
            "A non-finite downgrade timestamp was accepted.");
        False(CoverPostureDowngradeCore.ShouldAccept(
                TacticalStance.Crouched, TacticalStance.Prone, 10f, 5f),
            "An inverted downgrade timestamp was accepted.");
    }

    private static void AuthoredPoseIsKeptOnlyWhenBallisticsCannotClassify()
    {
        // On authored cover the ballistics could not classify: keep the authored pose.
        Equal(CoverPostureChoice.Crouched,
            AuthoredPoseFallbackCore.ResolvePose(
                CoverPostureChoice.Prone, false, false, true, CoverPostureChoice.Crouched),
            "An unclassifiable authored trench slot dropped the soldier prone below the parapet.");
        True(AuthoredPoseFallbackCore.ResolveProtective(false, false, true),
            "An accepted authored slot was not treated as protective.");

        // A confidently-measured penetrable barrier keeps the ballistic prone fallback.
        Equal(CoverPostureChoice.Prone,
            AuthoredPoseFallbackCore.ResolvePose(
                CoverPostureChoice.Prone, false, true, true, CoverPostureChoice.Crouched),
            "A measured, penetrable barrier wrongly kept the authored pose.");
        False(AuthoredPoseFallbackCore.ResolveProtective(false, true, true),
            "A measured non-protective barrier was reported protective.");

        // Measured protection is trusted directly whenever it is found.
        Equal(CoverPostureChoice.Standing,
            AuthoredPoseFallbackCore.ResolvePose(
                CoverPostureChoice.Standing, true, false, true, CoverPostureChoice.Crouched),
            "A measured protective posture was overridden by the authored pose.");

        // Off authored cover the ballistic result always stands.
        Equal(CoverPostureChoice.Prone,
            AuthoredPoseFallbackCore.ResolvePose(
                CoverPostureChoice.Prone, false, false, false, CoverPostureChoice.Standing),
            "A non-authored open position was granted an authored standing pose.");
    }

    private static void CoverPostureOwnershipSurvivesBriefContactLoss()
    {
        // The prone<->crouch loop: on a prone-protective wall the cover evaluation owns
        // 'prone' while contact is fresh, but ownership used to lapse with the 3s contact
        // timer, handing the pose to the generic crouch fallback whenever the enemy
        // flicked out of sight. Ownership must not depend on that timer.
        True(CoverPostureOwnershipCore.CoverPoseOwned(
                hasThreatMemory: true, onUsableCover: true, defensiveHold: false),
            "An on-cover soldier lost cover-pose ownership between sightings.");
        True(CoverPostureOwnershipCore.CoverPoseOwned(
                hasThreatMemory: true, onUsableCover: false, defensiveHold: true),
            "A defensive anchor lost cover-pose ownership off native cover.");
        False(CoverPostureOwnershipCore.CoverPoseOwned(
                hasThreatMemory: false, onUsableCover: true, defensiveHold: false),
            "Cover-pose ownership persisted with no threat memory.");
        False(CoverPostureOwnershipCore.CoverPoseOwned(
                hasThreatMemory: true, onUsableCover: false, defensiveHold: false),
            "Cover-pose ownership persisted off cover without a stable anchor.");
    }

    private static void EngagedCoverPoseConvergesInsteadOfLooping()
    {
        // Drive the real pose latch with the fixed ownership rule across a long
        // engagement whose line of sight flickers every few seconds. Because ownership
        // no longer lapses, the proposal is the stable evaluation pose on every update,
        // so the latched stance converges to prone and never rises back out of it -
        // the sustained prone<->crouch cycle is gone.
        var owner = PoseOwner.TacticalCrouch;
        var latched = TacticalStance.Crouched;
        var holdUntil = 0f;
        var reachedProne = false;
        var everRoseAfterProne = false;
        for (var t = 0f; t <= 30f; t += 0.5f)
        {
            var owned = CoverPostureOwnershipCore.CoverPoseOwned(
                hasThreatMemory: true, onUsableCover: true, defensiveHold: false);
            var proposedOwner = owned ? PoseOwner.CoverEvaluation : PoseOwner.TacticalCrouch;
            var proposed = owned ? TacticalStance.Prone : TacticalStance.Crouched;
            if (StepLatch(ref owner, ref latched, ref holdUntil,
                    proposedOwner, proposed, measuredStand: false, t) &&
                reachedProne && proposed != TacticalStance.Prone)
            {
                everRoseAfterProne = true;
            }

            if (latched == TacticalStance.Prone)
                reachedProne = true;
        }

        True(reachedProne, "The engaged cover pose never settled on the protective stance.");
        False(everRoseAfterProne,
            "The engaged cover pose oscillated back out of the protective stance.");
        Equal(TacticalStance.Prone, latched,
            "The engaged cover pose did not converge to a single stance.");
    }

    private static void SuppressionRecoveryKeepsAnAlreadyProneSoldierDownInTheOpen()
    {
        // Root cause: rising prone->crouch in the open while still suppressed produced
        // the reported prone<->crouch loop. Recovery must keep an already-prone soldier
        // down off cover, while still crouching a standing/crouched soldier as before -
        // that reaction and the on-cover fighting crouch must both survive the fix.
        Equal(TacticalStance.Prone,
            SuppressionRecoveryPoseCore.Resolve(
                onUsableCover: false, current: TacticalStance.Prone, coverEvaluationOwnsProne: false),
            "A prone soldier in the open was raised to crouch by the suppression recovery rule.");
        Equal(TacticalStance.Crouched,
            SuppressionRecoveryPoseCore.Resolve(
                onUsableCover: true, current: TacticalStance.Prone, coverEvaluationOwnsProne: false),
            "A prone soldier on usable cover with no owned prone evaluation was not allowed to rise to the fighting crouch.");
        Equal(TacticalStance.Crouched,
            SuppressionRecoveryPoseCore.Resolve(
                onUsableCover: false, current: TacticalStance.Crouched, coverEvaluationOwnsProne: false),
            "The already-crouched suppression reaction was lost off cover.");
        Equal(TacticalStance.Crouched,
            SuppressionRecoveryPoseCore.Resolve(
                onUsableCover: false, current: TacticalStance.Standing, coverEvaluationOwnsProne: false),
            "The standing->crouch suppression reaction was lost off cover.");
        Equal(TacticalStance.Prone,
            SuppressionRecoveryPoseCore.Resolve(
                onUsableCover: true, current: TacticalStance.Crouched, coverEvaluationOwnsProne: true),
            "Suppression recovery fought an owned cover evaluation that already measured the slot as prone-only.");
    }

    private static void SuppressionRecoveryPreventsProneCrouchLoopOnRelease()
    {
        // Reproduces the reported loop end to end through the real pose latch: pin
        // engages (instant prone, held through PinnedMinimumSeconds), then releases
        // while suppression is still in the crouch band. The old flat Crouch proposal
        // forced the soldier up in the open on every release; the recovery-core
        // proposal must keep him prone for the rest of the engagement.
        var owner = PoseOwner.TacticalCrouch;
        var latched = TacticalStance.Crouched;
        var holdUntil = 0f;
        var reachedProne = false;
        var everRoseAfterProne = false;
        for (var t = 0f; t <= 6f; t += 0.5f)
        {
            // The pin is the highest tactical owner; it drops the soldier prone at once.
            StepLatch(ref owner, ref latched, ref holdUntil,
                PoseOwner.Suppression, TacticalStance.Prone, measuredStand: false, t);
            if (latched == TacticalStance.Prone)
                reachedProne = true;
        }

        for (var t = 6.5f; t <= 20f; t += 0.5f)
        {
            // Pin released; the suppression-recovery owner takes over. It keeps an
            // already-prone soldier down in the open, so the proposal stays Prone.
            var proposed = SuppressionRecoveryPoseCore.Resolve(
                onUsableCover: false, latched, coverEvaluationOwnsProne: false);
            if (StepLatch(ref owner, ref latched, ref holdUntil,
                    PoseOwner.SuppressionRecovery, proposed, measuredStand: false, t) &&
                reachedProne && proposed != TacticalStance.Prone)
            {
                everRoseAfterProne = true;
            }
        }

        True(reachedProne, "The pin never latched the soldier prone.");
        False(everRoseAfterProne,
            "Suppression recovery let the pinned soldier rise to crouch in the open on release.");
        Equal(TacticalStance.Prone, latched,
            "The soldier did not stay prone through the suppression-band release window.");
    }

    private static void SuppressionRecoveryDefersToAnOwnedProneCoverEvaluation()
    {
        // Reproduces RC2 end to end, matching the mechanism in the plan: contact has
        // fully lapsed (lowerPoseStillOwned false throughout, as after ContactUntil
        // expires) while cover-posture ownership stays active (known threat memory +
        // usable cover, which - unlike the contact timer - does not lapse) and the
        // suppression crouch band flickers on and off every tick. On the frames the
        // band is active, UpdateSuppressionReaction proposes through
        // SuppressionRecoveryPoseCore; on the other frames MaintainOwnedPose's cover
        // evaluation proposes Prone directly. Before the fix, the suppression
        // proposal was a hard Crouch on usable cover regardless of what the cover
        // evaluation measured, so once the hold expired the latch accepted Crouch,
        // the evaluation's next frame instantly dropped it back to Prone, and it
        // repeated - the observed prone<->crouch loop. The fix makes both proposers
        // agree on Prone so the latch has nothing to oscillate over.
        var owner = PoseOwner.CoverEvaluation;
        var latched = TacticalStance.Prone;
        var holdUntil = 0f;
        var everRoseAfterProne = false;
        var tick = 0;
        for (var t = 0f; t <= 20f; t += 0.5f, tick++)
        {
            var coverOwned = CoverPostureOwnershipCore.CoverPoseOwned(
                hasThreatMemory: true, onUsableCover: true, defensiveHold: false);
            var suppressionBandActive = tick % 2 == 0;
            // On band frames the suppression-recovery owner proposes; on the others the
            // cover-evaluation owner proposes Prone directly. Both now agree on Prone, so
            // the latch has nothing to oscillate over regardless of which owns the frame.
            var proposedOwner = suppressionBandActive
                ? PoseOwner.SuppressionRecovery
                : PoseOwner.CoverEvaluation;
            var proposed = suppressionBandActive
                ? SuppressionRecoveryPoseCore.Resolve(
                    onUsableCover: true, latched, coverEvaluationOwnsProne: coverOwned)
                : TacticalStance.Prone;

            if (StepLatch(ref owner, ref latched, ref holdUntil,
                    proposedOwner, proposed, measuredStand: false, t) &&
                proposed != TacticalStance.Prone)
            {
                everRoseAfterProne = true;
            }
        }

        False(everRoseAfterProne,
            "Suppression recovery fought the owned cover evaluation and reopened the prone<->crouch loop.");
        Equal(TacticalStance.Prone, latched,
            "The latched pose did not stay converged on the protective stance under a flickering suppression band.");
    }

    private static void WrongSideAnchorReleasesForALiveEngagedThreat()
    {
        // Wrong side of cover: a currently-engaged live enemy that the anchored cover
        // does not stop (measured against the anti-flicker stabilized axis) releases the
        // sticky anchor for one relocation to face the real threat.
        True(DefensiveAnchorReevaluationCore.ShouldReleaseForRealThreat(
                hasThreatMemory: true, engagedRecently: true,
                coverEvaluationSucceeded: true, coverProtectsAgainstStableThreat: false),
            "A defender kept an anchor his live enemy could shoot through.");

        // Protective cover is never abandoned.
        False(DefensiveAnchorReevaluationCore.ShouldReleaseForRealThreat(
                true, true, true, true),
            "A protective anchor was abandoned under fire.");

        // No live/recent engagement: the sticky anchor is preserved, so a predicted or
        // stale bearing cannot churn a settled defender.
        False(DefensiveAnchorReevaluationCore.ShouldReleaseForRealThreat(
                true, false, true, false),
            "An anchor churned without a currently-engaged threat.");
        False(DefensiveAnchorReevaluationCore.ShouldReleaseForRealThreat(
                false, true, true, false),
            "An anchor churned without any threat memory.");

        // An inconclusive evaluation (for example a building slot not reporting native
        // cover) never releases the anchor.
        False(DefensiveAnchorReevaluationCore.ShouldReleaseForRealThreat(
                true, true, false, false),
            "An anchor was released on an inconclusive cover evaluation.");
    }

    private static void TargetObservationAccruesOnlyDuringContinuousWatching()
    {
        // A candidate is created at t=0 with zero banked time; 9 further samples
        // every 0.5s (10 samples total) accrue real elapsed time between them.
        var observed = 0f;
        var lastSeenAt = 0f;
        for (var i = 1; i <= 9; i++)
        {
            var now = i * 0.5f;
            observed = TargetConfirmationCore.AccrueObservation(observed, lastSeenAt, now);
            lastSeenAt = now;
        }

        Near(4.5f, observed, 0.001f,
            "Continuous 0.5s sampling did not bank the expected observed time.");

        // The fix's headline case: two glimpses 20 seconds apart must not bank the
        // gap between them as observation.
        Equal(0f, TargetConfirmationCore.AccrueObservation(5f, 0f, 20f),
            "A long gap between glimpses incorrectly banked observed time.");

        // A brief hiccup inside the continuity window credits the sample cap, not
        // the raw gap.
        Near(TargetConfirmationCore.MaxSampleCreditSeconds,
            TargetConfirmationCore.AccrueObservation(0f, 0f, 3f),
            0.001f,
            "A brief hiccup inside the continuity window credited more than the sample cap.");

        // Boundary: exactly the continuity-break threshold still accrues (capped);
        // a hair beyond it resets the streak.
        Near(TargetConfirmationCore.MaxSampleCreditSeconds,
            TargetConfirmationCore.AccrueObservation(
                0f, 0f, TargetConfirmationCore.ContinuityBreakSeconds),
            0.001f,
            "The exact continuity boundary incorrectly reset the observed streak.");
        Equal(0f,
            TargetConfirmationCore.AccrueObservation(
                0f, 0f, TargetConfirmationCore.ContinuityBreakSeconds + 0.01f),
            "A gap just past the continuity boundary failed to reset the observed streak.");
    }

    private static MapPoint DirectionAt(float degrees)
    {
        var radians = degrees * MathF.PI / 180f;
        return new MapPoint(MathF.Cos(radians), MathF.Sin(radians));
    }

    private static void CoverFsmHoldsUsefulCover()
    {
        var decision = InfantryCoverDecisionCore.EvaluateNeed(new CoverNeedInput(
            HasUsableCover: true,
            MayAdvanceFromCover: false,
            CoverCompromised: false,
            UnderDirectFire: true,
            Suppressed: false,
            CloseThreat: false,
            AttackAdvanceBlocked: false,
            NormalDecisionDue: true,
            UrgentDecisionDue: true));

        Equal(InfantryCoverState.Holding, decision.State,
            "A soldier left cover that was still protecting him.");
        False(decision.ShouldSearch, "Useful cover triggered another cover search.");
    }

    private static void CoverPostureRequiresWholeBodyProtection()
    {
        var standing = new CoverPostureInput(1, 4, true);
        var narrowTreeCrouch = new CoverPostureInput(2, 4, true);
        var openProne = new CoverPostureInput(0, 4, true);
        Equal(
            CoverPostureChoice.Prone,
            InfantryCoverDecisionCore.SelectCoverPosture(
                standing, narrowTreeCrouch, openProne),
            "A narrow obstruction made a soldier kneel with most of his body exposed.");
        False(InfantryCoverDecisionCore.HasMeaningfulProtection(narrowTreeCrouch),
            "Two protected body samples were incorrectly treated as genuine cover.");

        var lowWallCrouch = new CoverPostureInput(4, 4, false);
        Equal(
            CoverPostureChoice.Standing,
            InfantryCoverDecisionCore.SelectCoverPosture(
                standing, lowWallCrouch, openProne),
            "A soldier did not rise behind genuinely protective low cover to clear his weapon.");

        var proneOnlyCover = new CoverPostureInput(0, 4, true);
        var exposedCrouch = new CoverPostureInput(1, 4, true);
        var protectedProne = new CoverPostureInput(4, 4, true);
        Equal(
            CoverPostureChoice.Prone,
            InfantryCoverDecisionCore.SelectCoverPosture(
                proneOnlyCover, exposedCrouch, protectedProne),
            "Cover that only protected a prone soldier selected a higher posture.");
    }

    private static void BallisticCoverRatesMaterialAndThickness()
    {
        var budget = BallisticCoverDecisionCore.RepresentativeOrdinaryRoundBudget;
        var foliage = BallisticCoverDecisionCore.RateProtection(
            budget,
            new[] { new BallisticBarrierInput(0.015f, 0.16f, 0.25f) });
        var thinWood = BallisticCoverDecisionCore.RateProtection(
            budget,
            new[] { new BallisticBarrierInput(0.075f, 1f, 0.15f) });
        var thickWood = BallisticCoverDecisionCore.RateProtection(
            budget,
            new[] { new BallisticBarrierInput(0.075f, 1f, 0.45f) });
        var sandbagEarth = BallisticCoverDecisionCore.RateProtection(
            budget,
            new[] { new BallisticBarrierInput(0.12f, 2.8f, 0.28f) });
        var masonry = BallisticCoverDecisionCore.RateProtection(
            budget,
            new[] { new BallisticBarrierInput(0.18f, 6.2f, 0.14f) });

        True(foliage < thinWood && thinWood < thickWood,
            "Material resistance and thickness did not increase cover protection.");
        False(BallisticCoverDecisionCore.IsMeaningfulRay(foliage),
            "Foliage was credited as ballistic cover.");
        False(BallisticCoverDecisionCore.IsMeaningfulRay(thinWood),
            "A thin wooden prop was credited as strong cover.");
        True(BallisticCoverDecisionCore.IsMeaningfulRay(thickWood),
            "Substantial wood was not recognized as useful ballistic cover.");
        True(BallisticCoverDecisionCore.IsMeaningfulRay(sandbagEarth),
            "Sandbag/earth protection was not recognized as strong cover.");
        True(BallisticCoverDecisionCore.IsMeaningfulRay(masonry),
            "Masonry protection was not recognized as strong cover.");
        Equal(1f, BallisticCoverDecisionCore.RateProtection(
                budget,
                new[] { new BallisticBarrierInput(0f, 0f, 0f, IsHardStop: true) }),
            "A bunker, terrain mass, or other hard stop was not fully protective.");
        Equal(1f, BallisticCoverDecisionCore.RateProtection(
                budget,
                new[] { new BallisticBarrierInput(0.1f, 1f, 0f, HasMeasuredExit: false) }),
            "A barrier too thick to find an exit was not fully protective.");
    }

    private static void VisualObstructionAloneIsNotProtectiveCover()
    {
        var visuallyBlockedByWeakMaterial = new CoverPostureInput(4, 4, true, 0.12f);
        False(InfantryCoverDecisionCore.HasMeaningfulProtection(
                visuallyBlockedByWeakMaterial),
            "Four visibility hits through weak material were mistaken for ballistic cover.");

        var protectedBody = new CoverPostureInput(3, 4, false, 0.78f);
        True(InfantryCoverDecisionCore.HasMeaningfulProtection(protectedBody),
            "Three strongly protected body regions did not establish useful cover.");

        var narrowHardObject = new CoverPostureInput(2, 4, true, 1f);
        False(InfantryCoverDecisionCore.HasMeaningfulProtection(narrowHardObject),
            "A hard but narrow obstruction was allowed to leave half the body exposed.");
    }

    private static void ProtectionWeightedScorePrefersSurvivableCover()
    {
        var nearMarginal = new CoverScoreInput(
            4f, 0f, true, 0, true, true, 0f, 0f,
            PrimaryProtectionFraction: 0.61f,
            PreferProtectionOverFiringLine: true);
        var fartherMasonry = nearMarginal with
        {
            DistanceSqr = 400f,
            PrimaryProtectionFraction = 0.96f,
            AssignedPoseCanFire = false,
            StandingCanFire = false
        };

        True(InfantryCoverDecisionCore.Score(
                 CoverSelectionMode.DefensiveOccupation, fartherMasonry) <
             InfantryCoverDecisionCore.Score(
                 CoverSelectionMode.DefensiveOccupation, nearMarginal),
            "A nearby marginal obstruction beat substantially safer masonry cover.");
    }

    private static void CoverScoringSpreadsSoldiersWithoutOverridingProtection()
    {
        var uncrowded = new CoverScoreInput(
            25f, 0f, true, 0, true, true, 0f, 0f,
            PrimaryProtectionFraction: 0.8f,
            NearbyReservationCount: 0);
        var crowded = uncrowded with { NearbyReservationCount = 3 };

        True(InfantryCoverDecisionCore.Score(CoverSelectionMode.Normal, crowded) >
             InfantryCoverDecisionCore.Score(CoverSelectionMode.Normal, uncrowded),
            "An equally protective crowded slot did not score worse than an uncrowded one.");

        var crowdedProtective = uncrowded with
        {
            PrimaryProtectionFraction = 0.95f,
            NearbyReservationCount = 3
        };
        var exposedEmpty = uncrowded with
        {
            PrimaryProtectionFraction = 0f,
            NearbyReservationCount = 0
        };

        True(InfantryCoverDecisionCore.Score(CoverSelectionMode.Normal, crowdedProtective) <
             InfantryCoverDecisionCore.Score(CoverSelectionMode.Normal, exposedEmpty),
            "Crowding pushed a soldier off protective cover toward exposed open ground.");
    }

    private static void DispersionDegradesInsteadOfBlockingNearbyCover()
    {
        // Mirrors InfantryCoverPolicy (ContactResponseState.cs is Unity-side and
        // cannot be compiled here): the small HARD radius every reservation-conflict
        // check passes to AiState.CoverReservedByOther, and the wider SCORING-only
        // radius used for the crowding count.
        const float hardReservationRadius = 1.75f;
        const float dispersionScoringRadius = 5f;

        var reserved = new MapPoint(0f, 0f);
        var twoMetresAway = new MapPoint(2f, 0f);
        var threeMetresAway = new MapPoint(0f, 3f);

        // A trench or building whose slots sit 2-3 m apart must stay usable by the
        // rest of the squad: dispersion may rank a slot lower, never veto it.
        False(
            InfantryCoverDecisionCore.CoverPositionsConflict(
                twoMetresAway, reserved, hardReservationRadius),
            "A cover slot 2 m from a squadmate's reservation was rejected outright.");
        False(
            InfantryCoverDecisionCore.CoverPositionsConflict(
                threeMetresAway, reserved, hardReservationRadius),
            "A cover slot 3 m from a squadmate's reservation was rejected outright.");
        True(
            InfantryCoverDecisionCore.CoverPositionsConflict(
                new MapPoint(0.5f, 0f), reserved, hardReservationRadius),
            "Two soldiers were allowed to claim the same physical cover slot.");

        // Those same neighbours still count for the soft crowding penalty.
        True(
            InfantryCoverDecisionCore.CoverPositionsConflict(
                threeMetresAway, reserved, dispersionScoringRadius),
            "A squadmate 3 m away was not counted as a crowding neighbour.");

        // And the penalty only reorders: a crowded slot next to the soldier still
        // beats empty ground 20 m away.
        var crowdedNearby = new CoverScoreInput(
            4f, 0f, true, 0, true, true, 0f, 0f,
            PrimaryProtectionFraction: 0.8f,
            NearbyReservationCount: 1);
        var emptyFarAway = crowdedNearby with
        {
            DistanceSqr = 400f,
            NearbyReservationCount = 0
        };
        True(
            InfantryCoverDecisionCore.Score(CoverSelectionMode.Normal, crowdedNearby) <
            InfantryCoverDecisionCore.Score(CoverSelectionMode.Normal, emptyFarAway),
            "Crowding sent a soldier on a long move away from equally protective cover.");
    }

    private static void AttackProgressHasMaximumCombatHalt()
    {
        False(InfantryCoverDecisionCore.ShouldForceAttackProgress(
                true, true, 10f, 21.99f, 12f),
            "An attacker resumed before completing the firing halt.");
        True(InfantryCoverDecisionCore.ShouldForceAttackProgress(
                true, true, 10f, 22f, 12f),
            "Continuous enemy fire created an unlimited attack halt.");
        False(InfantryCoverDecisionCore.ShouldForceAttackProgress(
                false, true, 10f, 40f, 12f),
            "A defender inherited forced attack movement.");
        False(InfantryCoverDecisionCore.ShouldForceAttackProgress(
                true, false, 10f, 40f, 12f),
            "An attacker without an objective route was forced to wander.");
    }

    private static void DefensivePositionOwnershipStaysLatchedOutsideTheArrivalArea()
    {
        True(DefensivePositionOwnershipCore.ShouldOwn(
                new DefensivePositionOwnershipInput(false, true, true, false, false)),
            "An eligible defender did not acquire position ownership on arrival.");
        True(DefensivePositionOwnershipCore.ShouldOwn(
                new DefensivePositionOwnershipInput(true, true, false, true, true)),
            "Native displacement or area flicker released an established defensive position.");
        False(DefensivePositionOwnershipCore.ShouldOwn(
                new DefensivePositionOwnershipInput(true, true, true, false, true)),
            "A squad transfer retained the previous defensive position.");
        False(DefensivePositionOwnershipCore.ShouldOwn(
                new DefensivePositionOwnershipInput(true, true, true, true, false)),
            "An objective revision retained a stale defensive position.");
        False(DefensivePositionOwnershipCore.ShouldOwn(
                new DefensivePositionOwnershipInput(true, false, true, true, true)),
            "External or attacker ownership failed to release a defensive position.");
    }

    private static void IdleSoldiersRemainUnderNativeControl()
    {
        var idle = new ContactMovementSensor(
            HasActionableContact: false,
            HasRecentContact: false,
            HasCommittedCoverMove: false,
            HasStableCoverHold: false,
            HasTimedCoverHold: false,
            CanClaimReachedCover: false,
            HasEngagementHold: false);

        False(CombatMovementPolicyCore.NeedsLocalCombatControl(idle),
            "An idle soldier was incorrectly claimed by the contact-response movement executor.");
        False(CombatMovementPolicyCore.NeedsProtectedCoverControl(idle),
            "An idle soldier was incorrectly treated as a protected cover occupant.");

        var contact = idle with { HasActionableContact = true };
        True(CombatMovementPolicyCore.NeedsLocalCombatControl(contact),
            "An actionable contact did not activate local combat movement control.");
        Equal(TacticalAction.Hold, CombatMovementPolicyCore.SelectLocalAction(contact),
            "A new contact made an uncommitted soldier keep jogging instead of fighting.");

        var committedCoverDash = contact with { HasCommittedCoverMove = true };
        Equal(TacticalAction.Move, CombatMovementPolicyCore.SelectLocalAction(committedCoverDash),
            "A committed move to selected cover was incorrectly stopped.");
    }

    private static void ReachedCoverCreatesAStableFightingHalt()
    {
        var reachedAttackCover = new ContactMovementSensor(
            HasActionableContact: false,
            HasRecentContact: false,
            HasCommittedCoverMove: false,
            HasStableCoverHold: false,
            HasTimedCoverHold: true,
            CanClaimReachedCover: false,
            HasEngagementHold: false);
        True(CombatMovementPolicyCore.NeedsLocalCombatControl(reachedAttackCover),
            "An attacker already holding reached cover lost local cover ownership.");
        Equal(TacticalAction.Hold, CombatMovementPolicyCore.SelectLocalAction(reachedAttackCover),
            "Reached attack cover did not resolve to a fighting halt.");

        var fortifiedDefender = reachedAttackCover with
        {
            HasStableCoverHold = true,
            HasTimedCoverHold = false
        };
        True(CombatMovementPolicyCore.NeedsProtectedCoverControl(fortifiedDefender),
            "A defender in fortified cover did not retain protected-position ownership.");
        Equal(TacticalAction.Hold, CombatMovementPolicyCore.SelectLocalAction(fortifiedDefender),
            "A fortified defender was allowed to wander out of cover.");
    }

    private static void ArrivedDefendersStayUnderPositionControl()
    {
        var arrivedDefender = new ContactMovementSensor(
            HasActionableContact: false,
            HasRecentContact: false,
            HasCommittedCoverMove: false,
            HasStableCoverHold: false,
            HasTimedCoverHold: false,
            CanClaimReachedCover: false,
            HasEngagementHold: false,
            NeedsDefensivePositionControl: true);

        True(CombatMovementPolicyCore.NeedsDefensivePositionControl(arrivedDefender),
            "An arrived defender was released to native HoldArea circulation.");
        False(CombatMovementPolicyCore.NeedsLocalCombatControl(arrivedDefender),
            "Defensive position ownership was mistaken for contact-driven maneuver.");
        False(CombatMovementPolicyCore.NeedsDefensivePositionControl(
                arrivedDefender with { NeedsDefensivePositionControl = false }),
            "A reinforcement outside the defensive area was stopped before arrival.");
    }

    // ShouldAuthorizeAttackBound's parameter order below:
    // (hasAttackRoute, coveringFireEstablished, maximumHaltReached,
    //  maximumOnCoverHaltReached, underDirectFire, pinned, onUsableCover,
    //  coverHoldUntil, now)
    private static void AttackBoundsRequireSafetyAndTacticalAuthorization()
    {
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, false, false, false, false, false, true, 20f, 30f),
            "An attacker left cover without covering fire or reaching the maximum halt.");
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, true, true, false, true, false, true, 20f, 30f),
            "Direct fire under cover was authorized by the ordinary (off-cover) halt cap alone.");
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, true, true, true, false, true, true, 20f, 30f),
            "A pinned attacker was forced out of cover.");
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, true, true, true, false, false, true, 31f, 30f),
            "An attacker abandoned useful cover before completing the minimum fighting halt.");
        True(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, true, false, false, false, false, true, 20f, 30f),
            "Covering fire failed to authorize a safe attack bound after the halt.");
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, false, true, false, false, false, true, 20f, 30f),
            "The (shorter) off-cover halt deadline alone authorized a bound while still on usable cover.");
        True(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, false, true, false, false, false, false, 20f, 30f),
            "The maximum safe halt failed to resume an exposed, otherwise stalled attack.");
    }

    private static void OpenFieldAttackBoundsIgnoreDirectFireButNotPinning()
    {
        // The open-field branch must ignore underDirectFire (a soldier lying
        // exposed in a beaten zone with no cover is worse off than bounding),
        // while the on-cover branch keeps an underDirectFire veto that only the
        // longer on-cover halt cap can override (D1, plan 015).
        True(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, false, true, false, true, false, false, 20f, 30f),
            "Direct fire in the open blocked the maximum-halt escape from an exposed stall.");
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, true, true, false, false, true, false, 20f, 30f),
            "A pinned attacker was authorized to bound in the open.");
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, true, false, false, true, false, true, 20f, 30f),
            "On-cover direct fire was authorized by covering fire alone before the on-cover halt cap expired.");
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, false, false, false, false, false, true, 20f, 30f),
            "An on-cover soldier with no covering fire and no halt deadline was authorized to bound.");
    }

    private static void SustainedDirectFireOnCoverEventuallyAuthorizesABound()
    {
        // D1 (plan 015), success criterion (a): the on-cover halt cap must be
        // able to override a live underDirectFire cue, or an enemy firing more
        // often than the cue's lifetime freezes a covered squad forever.
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, false, false, false, true, false, true, 20f, 30f),
            "A covered soldier under sustained direct fire was authorized before the on-cover halt cap expired.");
        True(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, false, false, true, true, false, true, 20f, 30f),
            "A covered soldier under sustained direct fire never bounded once the on-cover halt cap expired.");
    }

    private static void CoveringFireIsAcceptedFromAnyConfirmedEnemyToken()
    {
        // D2 (plan 015): the mover is eligible on his own tracked contact token,
        // and a squadmate firing at a DIFFERENT confirmed enemy still counts as
        // covering fire — the two tokens are never compared against each other.
        var moverTargetToken = new IntPtr(1);
        var squadmateTargetToken = new IntPtr(2);
        True(CombatMovementPolicyCore.MoverQualifiesForAttackAdvance(
                moverTargetToken, moverSquadId: 5, moverAttackContactToken: moverTargetToken),
            "The mover was not eligible for a coordinated attack advance on his own tracked contact.");
        True(CombatMovementPolicyCore.IsCoveringFireEstablished(
                moverSquadId: 5,
                candidateSquadId: 5,
                candidateLastShotTargetToken: squadmateTargetToken,
                candidateShotWasStationary: true,
                candidateRelocating: false,
                candidatePinned: false,
                candidateSuppressionMovementOwned: false,
                candidateLastShotAt: 28f,
                now: 30f,
                freshnessSeconds: 7f),
            "A squadmate's fresh stationary shot at a different confirmed enemy was not accepted as covering fire.");
        False(CombatMovementPolicyCore.IsCoveringFireEstablished(
                moverSquadId: 5,
                candidateSquadId: 5,
                candidateLastShotTargetToken: IntPtr.Zero,
                candidateShotWasStationary: true,
                candidateRelocating: false,
                candidatePinned: false,
                candidateSuppressionMovementOwned: false,
                candidateLastShotAt: 28f,
                now: 30f,
                freshnessSeconds: 7f),
            "A squadmate with no confirmed shot at any enemy was accepted as covering fire.");
    }

    private static void MoverWithNoShotsFiredCanStillAdvanceOnCoveringFire()
    {
        // D3 (plan 015): the mover's own-fire prerequisite is gone entirely — a
        // soldier who has never fired a shot (no HasFiredAtAttackContact-style
        // gate exists any more) must still be able to advance on a squadmate's
        // covering fire alone.
        var targetToken = new IntPtr(7);
        True(CombatMovementPolicyCore.MoverQualifiesForAttackAdvance(
                targetToken, moverSquadId: 3, moverAttackContactToken: targetToken),
            "A mover who has never fired was permanently disqualified from a coordinated attack advance.");
        True(CombatMovementPolicyCore.IsCoveringFireEstablished(
                moverSquadId: 3,
                candidateSquadId: 3,
                candidateLastShotTargetToken: new IntPtr(99),
                candidateShotWasStationary: true,
                candidateRelocating: false,
                candidatePinned: false,
                candidateSuppressionMovementOwned: false,
                candidateLastShotAt: 29f,
                now: 30f,
                freshnessSeconds: 7f),
            "A squadmate's covering fire failed to establish for a mover with no shots of his own.");
    }

    private static void PinnedReleaseGrantsImmunityOnlyOnTimeCapRelease()
    {
        // Time cap forces a release despite suppression staying above the release
        // threshold, and that release alone grants a re-pin immunity window.
        var timeCapRelease = PinnedReleaseCore.EvaluatePinnedRelease(
            pinnedSince: 0f, pinnedUntil: 6f, suppression: 200,
            releaseSuppressionThreshold: 25, maximumPinnedSeconds: 25f, now: 25f);
        True(timeCapRelease.Released,
            "The maximum pinned time cap failed to release a soldier still under heavy suppression.");
        True(timeCapRelease.GrantsImmunity,
            "A time-cap pin release did not grant re-pin immunity.");

        // Still short of the cap and above the release threshold: stays pinned.
        False(PinnedReleaseCore.EvaluatePinnedRelease(
                pinnedSince: 0f, pinnedUntil: 6f, suppression: 200,
                releaseSuppressionThreshold: 25, maximumPinnedSeconds: 25f, now: 24.9f)
            .Released,
            "A pin released before either the time cap or the suppression threshold was reached.");

        // Normal threshold release (suppression low, minimum hold passed) grants no
        // immunity Ã¢â‚¬â€ identical to today's behavior.
        var thresholdRelease = PinnedReleaseCore.EvaluatePinnedRelease(
            pinnedSince: 0f, pinnedUntil: 6f, suppression: 10,
            releaseSuppressionThreshold: 25, maximumPinnedSeconds: 25f, now: 7f);
        True(thresholdRelease.Released,
            "A pin failed to release once its minimum hold passed and suppression fell below the threshold.");
        False(thresholdRelease.GrantsImmunity,
            "A normal threshold release incorrectly granted re-pin immunity.");

        // Minimum hold not yet elapsed: no release even with low suppression.
        False(PinnedReleaseCore.EvaluatePinnedRelease(
                pinnedSince: 0f, pinnedUntil: 6f, suppression: 10,
                releaseSuppressionThreshold: 25, maximumPinnedSeconds: 25f, now: 5.9f)
            .Released,
            "A pin released before its minimum hold elapsed.");

        // Immunity granted by a time-cap release (e.g. until t=35) blocks an
        // immediate re-pin under continuing heavy suppression, but a fresh pin
        // engages normally once the immunity window lapses.
        False(PinnedReleaseCore.ShouldEngagePin(
                suppression: 200, proneSuppressionThreshold: 51, immunityUntil: 35f, now: 25.1f),
            "Re-pin immunity failed to block an immediate re-pin right after a time-cap release.");
        False(PinnedReleaseCore.ShouldEngagePin(
                suppression: 200, proneSuppressionThreshold: 51, immunityUntil: 35f, now: 34.9f),
            "Re-pin immunity lapsed early under continuing heavy suppression.");
        True(PinnedReleaseCore.ShouldEngagePin(
                suppression: 200, proneSuppressionThreshold: 51, immunityUntil: 35f, now: 35f),
            "A new pin failed to engage once suppression continued past the immunity window.");
        False(PinnedReleaseCore.ShouldEngagePin(
                suppression: 40, proneSuppressionThreshold: 51, immunityUntil: 0f, now: 35f),
            "A pin engaged below the suppression threshold with no immunity in effect.");
    }

    private static void DefensiveRelocationsRemainWithinHoldArea()
    {
        var center = new MapPoint(10f, -10f);
        True(DefensivePositioningCore.IsInsideArea(
                new MapPoint(45f, -10f), center, 25f, 10f),
            "A cover position on the defensive tolerance boundary was rejected.");
        False(DefensivePositioningCore.IsInsideArea(
                new MapPoint(45.01f, -10f), center, 25f, 10f),
            "A cover position outside the defensive area was accepted.");
        False(DefensivePositioningCore.IsInsideArea(
                new MapPoint(float.NaN, 0f), center, 25f, 10f),
            "Invalid defensive geometry was accepted.");
    }

    private static void DefendersRequireProtectionBeforeAnchoringCover()
    {
        False(InfantryCoverDecisionCore.ShouldTreatCurrentCoverAsUsable(
                true, true, false),
            "An unprotected native node became sticky merely because it was inside the objective.");
        True(InfantryCoverDecisionCore.ShouldTreatCurrentCoverAsUsable(
                true, false, true),
            "Protective maneuver cover was rejected.");
        False(InfantryCoverDecisionCore.ShouldTreatCurrentCoverAsUsable(
                true, false, false),
            "Non-protective cover outside a defensive area became sticky.");
        False(InfantryCoverDecisionCore.ShouldTreatCurrentCoverAsUsable(
                false, true, true),
            "A destroyed or unsafe native cover slot was treated as usable.");
    }

    private static void DefensiveCoverAnchorSurvivesNativeStatusFlicker()
    {
        True(InfantryCoverDecisionCore.ShouldKeepDefensiveCoverAnchor(
                true, true, false, true),
            "A valid defensive anchor was released when native cover status flickered.");
        False(InfantryCoverDecisionCore.ShouldKeepDefensiveCoverAnchor(
                false, true, false, true),
            "A defensive anchor survived the end of its defend order.");
        False(InfantryCoverDecisionCore.ShouldKeepDefensiveCoverAnchor(
                true, false, false, true),
            "A defensive anchor survived an objective-area change.");
        False(InfantryCoverDecisionCore.ShouldKeepDefensiveCoverAnchor(
                true, true, true, true),
            "Known destroyed or unsafe cover remained anchored.");
        False(InfantryCoverDecisionCore.ShouldKeepDefensiveCoverAnchor(
                true, true, false, false),
            "A soldier remained anchored after leaving the defensive position.");
    }

    private static void ReachedBuildingSlotLatchesWithoutNativeCoverFlag()
    {
        True(InfantryCoverDecisionCore.ShouldClaimReachedDefensiveSlot(
                true, true, true, false, true),
            "A defender did not claim a reached building slot when native cover reporting flickered off.");
        True(InfantryCoverDecisionCore.ShouldClaimReachedDefensiveSlot(
                true, true, true, true, false),
            "A defender did not claim a reserved slot while native cover reporting remained active.");
        False(InfantryCoverDecisionCore.ShouldClaimReachedDefensiveSlot(
                true, true, false, false, true),
            "A defender claimed a building slot before reaching it.");
        False(InfantryCoverDecisionCore.ShouldClaimReachedDefensiveSlot(
                false, true, true, false, true),
            "An attacker incorrectly converted an ended movement into a permanent defensive post.");
    }

    private static void NativeCoverClearRespectsProtectedOwnership()
    {
        True(InfantryCoverDecisionCore.ShouldBlockNativeCoverClear(
                true, false, false, false, false),
            "A native destination clear broke a protected static-weapon assignment.");
        True(InfantryCoverDecisionCore.ShouldBlockNativeCoverClear(
                false, true, true, false, false),
            "A native destination clear broke an active defensive cover move.");
        True(InfantryCoverDecisionCore.ShouldBlockNativeCoverClear(
                false, true, false, true, false),
            "A native destination clear broke a held defensive anchor.");
        True(InfantryCoverDecisionCore.ShouldBlockNativeCoverClear(
                false, false, false, false, true),
            "A native destination clear broke a reached maneuver-cover hold.");
        False(InfantryCoverDecisionCore.ShouldBlockNativeCoverClear(
                false, false, false, false, false),
            "An unowned native destination clear was blocked.");
    }

    private static void PlayerHoldArrivalClaimsStableProtectedPositions()
    {
        True(PlayerHoldPositionCore.OrderChanged(
                false, default, 0f, new MapPoint(10f, 20f), 12f),
            "A newly arrived player-held soldier did not acquire local position ownership.");
        False(PlayerHoldPositionCore.OrderChanged(
                true, new MapPoint(10f, 20f), 12f,
                new MapPoint(10.5f, 20.5f), 12.25f),
            "Minor native order jitter replaced an otherwise stable player hold.");
        True(PlayerHoldPositionCore.OrderChanged(
                true, new MapPoint(10f, 20f), 12f,
                new MapPoint(12f, 20f), 12f),
            "A materially moved player hold retained stale cover ownership.");
        True(PlayerHoldPositionCore.ShouldSeekCover(true, false, false, true),
            "An exposed soldier inside a player hold did not seek cover.");
        False(PlayerHoldPositionCore.ShouldSeekCover(false, false, false, true),
            "A soldier left the native route before reaching the player hold.");
        False(PlayerHoldPositionCore.ShouldSeekCover(true, true, false, true),
            "An active player-hold cover move was replaced before completion.");
        False(PlayerHoldPositionCore.ShouldSeekCover(true, false, true, true),
            "A stable player-hold cover anchor was churned.");
        False(PlayerHoldPositionCore.ShouldSeekCover(true, false, false, false),
            "A player-hold cover search ignored its bounded retry cadence.");
    }

    private static void StableAnchorsKeepTheirSpatialReservations()
    {
        False(InfantryCoverDecisionCore.ShouldReleaseUnoccupiedReservation(
                false, false, true),
            "A latched building or trench slot lost its reservation when native cover status flickered.");
        True(InfantryCoverDecisionCore.ShouldReleaseUnoccupiedReservation(
                false, false, false),
            "An unoccupied, unanchored cover reservation was retained indefinitely.");
        False(InfantryCoverDecisionCore.ShouldReleaseUnoccupiedReservation(
                true, false, false),
            "A cover reservation was released while its soldier was still in transit.");
    }

    private static void AutonomousDefendersSeekCoverEvenWithVisibleContact()
    {
        True(InfantryCoverDecisionCore.ShouldSeekInitialDefensiveCover(
                true, false, false, true, false),
            "An exposed autonomous defender without contact did not seek a fighting position.");
        True(InfantryCoverDecisionCore.ShouldSeekInitialDefensiveCover(
                true, false, false, true, true),
            "A visible enemy incorrectly pinned an exposed autonomous defender in place.");
        False(InfantryCoverDecisionCore.ShouldSeekInitialDefensiveCover(
                false, false, false, true, true),
            "A soldier without defensive-position ownership was taken over.");
        False(InfantryCoverDecisionCore.ShouldSeekInitialDefensiveCover(
                true, true, false, true, true),
            "A stable defensive anchor was churned during contact.");
        False(InfantryCoverDecisionCore.ShouldSeekInitialDefensiveCover(
                true, false, true, true, true),
            "An active defensive cover move was replaced during contact.");
        False(InfantryCoverDecisionCore.ShouldSeekInitialDefensiveCover(
                true, false, false, false, true),
            "A defensive cover search ignored its bounded retry cadence.");
    }

    private static void PlayerHoldCoverDoesNotTakeOverOtherExternalOrders()
    {
        True(ExternalMovementPolicyCore.AllowsPlayerHoldCover(
                true, false, true, true),
            "Autonomous squadmates were not allowed to occupy cover under a valid player hold.");
        False(ExternalMovementPolicyCore.AllowsPlayerHoldCover(
                true, true, true, true),
            "A Lua/script-owned order was replaced by player-hold cover logic.");
        False(ExternalMovementPolicyCore.AllowsPlayerHoldCover(
                true, false, false, true),
            "The player's own soldier was taken over by autonomous cover logic.");
        False(ExternalMovementPolicyCore.AllowsPlayerHoldCover(
                true, false, true, false),
            "A non-hold player command was replaced by autonomous cover logic.");
    }

    private static void WalkingInPlaceTriggersAQuietRecoveryHold()
    {
        Equal(MovementProgressDecision.Observe,
            MovementProgressWatchdogCore.Evaluate(new MovementProgressInput(
                true, false, false, 0.01f,
                MovementProgressWatchdogCore.StallSeconds - 0.01f)),
            "The watchdog halted a soldier before the no-progress window elapsed.");
        Equal(MovementProgressDecision.Halt,
            MovementProgressWatchdogCore.Evaluate(new MovementProgressInput(
                true, false, false, 0.01f,
                MovementProgressWatchdogCore.StallSeconds)),
            "Walking animation without physical displacement did not trigger a halt.");
        Near(4f, MovementProgressWatchdogCore.RecoverySeconds(1), 0.001f,
            "The first stalled destination did not receive a quiet recovery hold.");
        Near(8f, MovementProgressWatchdogCore.RecoverySeconds(2), 0.001f,
            "A repeated stall did not lengthen the quiet recovery hold.");
        Near(12f, MovementProgressWatchdogCore.RecoverySeconds(10), 0.001f,
            "Repeated stalls were not capped at the intended quiet hold.");
    }

    private static void RealMovementAndPathChangesResetTheStallWatch()
    {
        Equal(MovementProgressDecision.Progressed,
            MovementProgressWatchdogCore.Evaluate(new MovementProgressInput(
                true, false, false,
                MovementProgressWatchdogCore.ProgressEpsilonMeters, 20f)),
            "Real horizontal travel was mistaken for a locomotion stall.");
        Equal(MovementProgressDecision.Progressed,
            MovementProgressWatchdogCore.Evaluate(new MovementProgressInput(
                true, false, true, 0f, 20f)),
            "A materially new destination inherited stale no-progress time.");
        Equal(MovementProgressDecision.Observe,
            MovementProgressWatchdogCore.Evaluate(new MovementProgressInput(
                true, true, false, 0f,
                MovementProgressWatchdogCore.PathRequestStallSeconds - 0.01f)),
            "An active native path request did not receive its bounded grace period.");
        Equal(MovementProgressDecision.Halt,
            MovementProgressWatchdogCore.Evaluate(new MovementProgressInput(
                true, true, false, 0f,
                MovementProgressWatchdogCore.PathRequestStallSeconds)),
            "A permanently outstanding path request bypassed stall recovery.");
        Equal(MovementProgressDecision.Reset,
            MovementProgressWatchdogCore.Evaluate(new MovementProgressInput(
                false, false, false, 0f, 20f)),
            "A stationary hold remained under the movement watchdog.");
    }

    private static void TransportDismountsBeforeTakingFire()
    {
        True(TransportDismountDecisionCore.IsImminent(new TransportThreatInput(
                TransportThreatType.Infantry, 160f, 0f, 1f, true)),
            "A visible infantry threat at the contact boundary did not trigger dismounting.");
        False(TransportDismountDecisionCore.IsImminent(new TransportThreatInput(
                TransportThreatType.Infantry, 160.01f, 0f, 1f, true)),
            "A distant infantry sighting made the transport unload prematurely.");
        True(TransportDismountDecisionCore.IsImminent(new TransportThreatInput(
                TransportThreatType.GroundVehicle, 260f, 12f, 0.30f, false)),
            "A fresh, credible armor report did not trigger a pre-contact dismount.");
        False(TransportDismountDecisionCore.IsImminent(new TransportThreatInput(
                TransportThreatType.GroundVehicle, 260f, 12.01f, 0.30f, false)),
            "A stale armor report remained actionable.");
        False(TransportDismountDecisionCore.IsImminent(new TransportThreatInput(
                TransportThreatType.GroundVehicle, 260f, 12f, 0.299f, false)),
            "An unreliable armor report made the transport unload.");

        False(TransportDismountDecisionCore.ReadyToUnload(8f, 1.49f, false),
            "Passengers dismounted from a fast vehicle before it had time to brake.");
        True(TransportDismountDecisionCore.ReadyToUnload(3.5f, 0.1f, false),
            "A safely slowed transport kept passengers exposed inside.");
        True(TransportDismountDecisionCore.ReadyToUnload(8f, 1.5f, false),
            "Braking became an unlimited wait under a known threat.");
        True(TransportDismountDecisionCore.ReadyToUnload(20f, 0f, true),
            "Incoming fire did not force an immediate emergency dismount.");
    }

    private static void CoverFsmSuppressionOverridesUrgency()
    {
        var decision = InfantryCoverDecisionCore.EvaluateNeed(new CoverNeedInput(
            HasUsableCover: false,
            MayAdvanceFromCover: false,
            CoverCompromised: true,
            UnderDirectFire: true,
            Suppressed: true,
            CloseThreat: false,
            AttackAdvanceBlocked: false,
            NormalDecisionDue: true,
            UrgentDecisionDue: true));

        Equal(InfantryCoverState.Holding, decision.State,
            "A suppressed soldier started a new exposed movement.");
        False(decision.ShouldSearch, "Suppression did not inhibit cover churn.");
    }

    private static void CoverFsmUrgencyBypassesDeliberateWait()
    {
        var decision = InfantryCoverDecisionCore.EvaluateNeed(new CoverNeedInput(
            HasUsableCover: false,
            MayAdvanceFromCover: false,
            CoverCompromised: false,
            UnderDirectFire: true,
            Suppressed: false,
            CloseThreat: false,
            AttackAdvanceBlocked: true,
            NormalDecisionDue: false,
            UrgentDecisionDue: true));

        True(decision.ShouldSearch, "Direct fire did not trigger an urgent cover assessment.");
        Equal(CoverSelectionMode.Urgent, decision.SelectionMode,
            "Direct fire used deliberate cover priorities.");
    }

    private static void CoverSelectionRequiresProtectionAndSafeNormalRoute()
    {
        var exposedDestination = new CoverScoreInput(
            25f, 0f, false, 0, true, true, 0f, 0f);
        False(InfantryCoverDecisionCore.IsRouteAcceptable(
                CoverSelectionMode.Urgent, exposedDestination),
            "Urgency accepted a destination that did not protect from the main threat.");

        var dangerousRoute = exposedDestination with
        {
            PrimaryThreatProtected = true,
            ExposedRouteMeters = 8f,
            ExposedRouteFraction = 0.67f
        };
        False(InfantryCoverDecisionCore.IsRouteAcceptable(
                CoverSelectionMode.Normal, dangerousRoute),
            "A normal cover move accepted a mostly exposed route.");
        True(InfantryCoverDecisionCore.IsRouteAcceptable(
                CoverSelectionMode.Urgent, dangerousRoute),
            "Urgency rejected a dash from exposure to genuinely protective cover.");
    }

    private static void DefensiveOccupationAllowsOneMoveFromOpenGround()
    {
        var protectedDestinationAcrossOpenGround = new CoverScoreInput(
            400f, 0f, true, 0, false, false, 18f, 0.9f,
            PrimaryProtectionFraction: 1f,
            PreferProtectionOverFiringLine: true);
        True(InfantryCoverDecisionCore.IsRouteAcceptable(
                CoverSelectionMode.DefensiveOccupation,
                protectedDestinationAcrossOpenGround),
            "An exposed defender was condemned to stay in the open because the route to real cover was exposed.");

        var exposedDestination = protectedDestinationAcrossOpenGround with
        {
            PrimaryThreatProtected = false,
            AssignedPoseCanFire = true,
            StandingCanFire = true
        };
        False(InfantryCoverDecisionCore.IsRouteAcceptable(
                CoverSelectionMode.DefensiveOccupation,
                exposedDestination),
            "A clear firing lane was mistaken for a protected defensive position.");

        // The no-fire penalty now matches an attacker's Normal slot (500), so a defender
        // still refuses to abandon markedly stronger cover for a fire line only when the
        // protection gap is decisive. A modest gap now correctly favors a firing lane.
        var strongCoverWithoutFireLine = protectedDestinationAcrossOpenGround;
        var weakCoverWithFireLine = protectedDestinationAcrossOpenGround with
        {
            DistanceSqr = 100f,
            AssignedPoseCanFire = true,
            StandingCanFire = true,
            PrimaryProtectionFraction = 0.4f
        };
        True(InfantryCoverDecisionCore.Score(
                 CoverSelectionMode.DefensiveOccupation,
                 strongCoverWithoutFireLine) <
             InfantryCoverDecisionCore.Score(
                 CoverSelectionMode.DefensiveOccupation,
                 weakCoverWithFireLine),
            "Defensive occupation valued sight and proximity above survival.");
    }

    private static void DeliberateCoverValuesFiringQualityMoreThanUrgency()
    {
        var closePoorFiringCover = new CoverScoreInput(
            16f, 0f, true, 0, false, false, 2f, 0.33f);
        var fartherFiringCover = new CoverScoreInput(
            100f, 0f, true, 0, true, true, 2f, 0.33f);

        True(InfantryCoverDecisionCore.Score(CoverSelectionMode.Normal, fartherFiringCover) <
             InfantryCoverDecisionCore.Score(CoverSelectionMode.Normal, closePoorFiringCover),
            "Deliberate selection did not value a usable firing lane.");
        True(InfantryCoverDecisionCore.Score(CoverSelectionMode.Urgent, closePoorFiringCover) <
             InfantryCoverDecisionCore.Score(CoverSelectionMode.Urgent, fartherFiringCover),
            "Urgent selection did not prioritize nearby survival cover.");
    }

    private static void DefensiveCoverValuesProtectionOverImmediateFireLine()
    {
        var strongCoverWithoutFireLine = new CoverScoreInput(
            64f, 0f, true, 0, false, false, 1f, 0.2f,
            PrimaryProtectionFraction: 1f,
            PreferProtectionOverFiringLine: true);
        // Protection gap widened (0.75 -> 0.4) because the no-fire penalty now equals an
        // attacker's Normal slot (500); a defender keeps decisively stronger cover but no
        // longer clings to a fully blind slot when a well-protected slot offers a lane.
        var weakerCoverWithFireLine = new CoverScoreInput(
            64f, 0f, true, 0, true, true, 1f, 0.2f,
            PrimaryProtectionFraction: 0.4f,
            PreferProtectionOverFiringLine: true);

        True(InfantryCoverDecisionCore.Score(
                 CoverSelectionMode.Normal, strongCoverWithoutFireLine) <
             InfantryCoverDecisionCore.Score(
                 CoverSelectionMode.Normal, weakerCoverWithFireLine),
            "A defender abandoned stronger cover to gain an immediate firing lane.");
    }

    private static void SpatialCoverReservationsRejectOverlappingSlots()
    {
        var reserved = new MapPoint(10f, -5f);
        True(InfantryCoverDecisionCore.CoverPositionsConflict(
                reserved, new MapPoint(11.75f, -5f), 1.75f),
            "A duplicate cover slot on the spacing boundary was accepted.");
        False(InfantryCoverDecisionCore.CoverPositionsConflict(
                reserved, new MapPoint(11.751f, -5f), 1.75f),
            "A genuinely separate cover slot was rejected.");
        False(InfantryCoverDecisionCore.CoverPositionsConflict(
                reserved, new MapPoint(float.NaN, -5f), 1.75f),
            "Invalid cover coordinates conflicted with a valid reservation.");
    }

    private static TankEngagementInput TankInput(
        bool hasArmoredTarget = true,
        float distance = 150f,
        float timeSinceVisible = 0f,
        float lifeFraction = 1f,
        bool hullFacesThreat = true,
        bool reverseAvailable = true,
        bool rearBlocked = false,
        bool reverseTimerElapsed = true,
        float standoff = 180f,
        float reverseDistance = 100f,
        float damagedThreshold = 0.45f)
        => new(
            hasArmoredTarget, distance, timeSinceVisible, lifeFraction, hullFacesThreat,
            reverseAvailable, rearBlocked, reverseTimerElapsed, standoff, reverseDistance,
            damagedThreshold);

    private static void TankEngagementEntersAndReleasesHoldWithHysteresis()
    {
        Equal(TankEngagementState.Follow,
            TankEngagementDecisionCore.NextState(
                TankEngagementState.Follow, TankInput(distance: 181f)),
            "Follow entered Hold before the standoff distance was reached.");
        Equal(TankEngagementState.Hold,
            TankEngagementDecisionCore.NextState(
                TankEngagementState.Follow, TankInput(distance: 180f)),
            "Follow did not enter Hold on the standoff boundary.");

        // Hold must not release on a distance just past standoff; the exit
        // threshold sits at standoff * 1.15, not at standoff itself.
        Equal(TankEngagementState.Hold,
            TankEngagementDecisionCore.NextState(
                TankEngagementState.Hold, TankInput(distance: 200f)),
            "Hold released before its hysteresis exit distance.");
        Equal(TankEngagementState.Follow,
            TankEngagementDecisionCore.NextState(
                TankEngagementState.Hold, TankInput(distance: 207.01f)),
            "Hold failed to release once the enemy passed the exit distance.");
    }

    private static void TankEngagementLosFlickerGrantsGraceBeforeReleasingHold()
    {
        Equal(TankEngagementState.Hold,
            TankEngagementDecisionCore.NextState(
                TankEngagementState.Hold, TankInput(distance: 150f, timeSinceVisible: 3f)),
            "A one-frame LOS flicker released an active hold before its grace period.");
        Equal(TankEngagementState.Follow,
            TankEngagementDecisionCore.NextState(
                TankEngagementState.Hold, TankInput(distance: 150f, timeSinceVisible: 3.01f)),
            "Hold never released after the target stayed unseen beyond the grace window.");
    }

    private static void TankEngagementHoldAndReverseNeverDitherAroundTheirBoundaries()
    {
        // Sweeping distance +/-5m around the reverse boundary (100m) while already
        // in Reverse must never bounce back to Hold on a single step.
        foreach (var distance in new[] { 95f, 100f, 105f, 95f, 105f })
        {
            Equal(TankEngagementState.Reverse,
                TankEngagementDecisionCore.NextState(
                    TankEngagementState.Reverse,
                    TankInput(distance: distance, reverseTimerElapsed: true)),
                $"Reverse dithered at {distance}m, inside its hysteresis band.");
        }

        // Sweeping distance +/-5m around the standoff boundary (180m) while
        // already in Hold must never bounce to Follow on a single step.
        foreach (var distance in new[] { 175f, 180f, 185f, 175f, 185f })
        {
            Equal(TankEngagementState.Hold,
                TankEngagementDecisionCore.NextState(
                    TankEngagementState.Hold, TankInput(distance: distance)),
                $"Hold dithered at {distance}m, inside its hysteresis band.");
        }
    }

    private static void TankEngagementDamagedReverseDoesNotLoopWhenRearIsBlocked()
    {
        Equal(TankEngagementState.Reverse,
            TankEngagementDecisionCore.NextState(
                TankEngagementState.Hold,
                TankInput(distance: 150f, lifeFraction: 0.4f, rearBlocked: false)),
            "A damaged tank with a clear rear did not begin a tactical reverse.");
        Equal(TankEngagementState.Hold,
            TankEngagementDecisionCore.NextState(
                TankEngagementState.Hold,
                TankInput(distance: 150f, lifeFraction: 0.4f, rearBlocked: true)),
            "A damaged tank reversed into a blocked rear instead of holding to fight.");

        // Once reversing, a blocked rear (or lost reverse capability) must end the
        // retreat immediately rather than loop a blind reverse forever.
        Equal(TankEngagementState.Hold,
            TankEngagementDecisionCore.NextState(
                TankEngagementState.Reverse,
                TankInput(distance: 90f, reverseTimerElapsed: false, rearBlocked: true)),
            "Reverse kept looping after its rear became blocked.");
        Equal(TankEngagementState.Hold,
            TankEngagementDecisionCore.NextState(
                TankEngagementState.Reverse,
                TankInput(distance: 90f, reverseTimerElapsed: false, reverseAvailable: false)),
            "Reverse kept looping after reverse capability was lost.");

        // A target that dies or goes unseen past the grace window must end the
        // reverse and resume pathing, even while still deep inside the reverse
        // band; otherwise the tank backs up from nothing forever.
        Equal(TankEngagementState.Follow,
            TankEngagementDecisionCore.NextState(
                TankEngagementState.Reverse,
                TankInput(distance: 90f, reverseTimerElapsed: false, timeSinceVisible: 3.01f)),
            "Reverse never released after its target stayed unseen beyond the grace window.");
        Equal(TankEngagementState.Follow,
            TankEngagementDecisionCore.NextState(
                TankEngagementState.Reverse,
                TankInput(hasArmoredTarget: false, distance: 90f, reverseTimerElapsed: false)),
            "Reverse outlived a target that no longer exists.");
    }

    private static void TankEngagementHullRotationOnlyAllowedInHoldOutsideReverseBand()
    {
        False(TankEngagementDecisionCore.AllowHullRotation(
                TankEngagementState.Follow, 150f, 100f, false),
            "Hull rotation was allowed outside of Hold.");
        False(TankEngagementDecisionCore.AllowHullRotation(
                TankEngagementState.Reverse, 150f, 100f, false),
            "Hull rotation was allowed while reversing.");
        False(TankEngagementDecisionCore.AllowHullRotation(
                TankEngagementState.Hold, 90f, 100f, false),
            "Hull rotation was allowed inside the close reverse band.");
        False(TankEngagementDecisionCore.AllowHullRotation(
                TankEngagementState.Hold, 150f, 100f, true),
            "Hull rotation was requested while already facing the threat.");
        True(TankEngagementDecisionCore.AllowHullRotation(
                TankEngagementState.Hold, 150f, 100f, false),
            "Hull rotation was withheld at standoff range while side-on to the threat.");
    }

    private static void TankStallWatchdogRecoversResetsAndGivesUp()
    {
        False(TankStallWatchdogCore.IsStalled(3f, 14.99f),
            "The watchdog fired before its 15s window elapsed.");
        True(TankStallWatchdogCore.IsStalled(3f, 15f),
            "The watchdog failed to fire after 15s with under 4m of displacement.");
        False(TankStallWatchdogCore.IsStalled(4f, 20f),
            "The watchdog fired despite 4m of displacement (its own threshold).");

        False(TankStallWatchdogCore.HasNetProgress(9.99f),
            "Net progress was credited below the 10m threshold.");
        True(TankStallWatchdogCore.HasNetProgress(10f),
            "Net progress was not credited at the 10m threshold.");

        False(TankStallWatchdogCore.ShouldGiveUp(2), "The watchdog gave up before 3 failed recoveries.");
        True(TankStallWatchdogCore.ShouldGiveUp(3), "The watchdog did not give up after 3 failed recoveries.");
    }

    private static void CommandLeasesAreStableAndRejectStaleWork()
    {
        var registry = new CommandLeaseRegistryCore();
        var request = LeaseRequest(11, "planner", CommandAuthority.ImmediateCombat, 2);
        True(registry.TryAcquire(request, 10f, out var first), "The initial squad lease was rejected.");
        False(registry.TryAcquire(
                request with { Owner = "contact-loop" }, 10f, out _),
            "An equal-priority loop stole a stable lease.");
        True(registry.TryAcquire(request with { ValidUntil = 30f }, 11f, out var renewed),
            "The existing owner could not renew its lease.");
        True(registry.IsCurrent(first, 11f),
            "An unchanged planner heartbeat created a false command generation.");
        Equal(first.Generation, renewed.Generation,
            "An unchanged planner heartbeat churned the command lease generation.");
        True(registry.IsCurrent(renewed, 11f), "The renewed generation was rejected.");

        True(registry.TryAcquire(request with { ObjectiveRevision = 3 }, 12f, out var revisionThree),
            "A new objective revision could not replace its old lease.");
        False(registry.TryAcquire(request with { ObjectiveRevision = 2 }, 12f, out _),
            "Delayed work from the prior objective replaced a current command.");
        True(registry.IsCurrent(revisionThree, 12f), "The current objective lease was lost.");
    }

    private static void CommandLeaseDebugSnapshotIsOrderedAndPrunesExpiredWork()
    {
        var registry = new CommandLeaseRegistryCore();
        True(registry.TryAcquire(
                LeaseRequest(30, "weapon", CommandAuthority.ImmediateCombat, 1) with
                {
                    Channel = CommandChannel.InfantryAssignment,
                    ValidUntil = 20f
                },
                5f, out _),
            "Infantry-assignment debug lease setup failed.");
        True(registry.TryAcquire(
                LeaseRequest(10, "squad", CommandAuthority.ImmediateCombat, 1) with
                {
                    ValidUntil = 20f
                },
                5f, out _),
            "Squad debug lease setup failed.");
        True(registry.TryAcquire(
                LeaseRequest(5, "expired", CommandAuthority.ImmediateCombat, 1) with
                {
                    Channel = CommandChannel.InfantryAssignment,
                    ValidUntil = 6f
                },
                5f, out _),
            "Expired debug lease setup failed.");

        var snapshot = new List<CommandLease>();
        registry.CopyActive(10f, snapshot);
        Equal(2, snapshot.Count, "The visual snapshot retained an expired command lease.");
        Equal(CommandChannel.SquadOrders, snapshot[0].Key.Channel,
            "The visual snapshot did not use deterministic channel ordering.");
        Equal(CommandChannel.InfantryAssignment, snapshot[1].Key.Channel,
            "The visual snapshot did not retain the active infantry-assignment lease.");
        Equal(2, registry.Count, "Snapshot cleanup did not prune the expired registry entry.");
    }

    private static void ExternalOwnershipPreemptsAndLatches()
    {
        var registry = new CommandLeaseRegistryCore();
        True(registry.TryAcquire(LeaseRequest(20, "planner", CommandAuthority.ImmediateCombat, 1),
            2f, out _), "Planner setup lease failed.");
        True(registry.TryAcquire(LeaseRequest(20, "lua", CommandAuthority.PlayerOrScript, 1),
            2f, out _), "Lua ownership did not preempt planner ownership.");
        False(registry.TryAcquire(LeaseRequest(20, "planner", CommandAuthority.ImmediateCombat, 2),
            3f, out _), "Planner reacquired a channel while Lua ownership was still active.");
        True(registry.Release(CommandChannel.SquadOrders, 20, "lua"),
            "External ownership could not be explicitly ended.");
        True(registry.TryAcquire(LeaseRequest(20, "planner", CommandAuthority.ImmediateCombat, 2),
            3f, out _), "Planner did not reacquire after external ownership ended.");
    }

    private static void TacticalArbitrationUsesOneDeterministicWinnerPerChannel()
    {
        var snapshot = new SoldierTacticalSnapshot(
            7, 1, 4, StrategicPosture.Defend, false, false, true, false,
            true, false, false, new MapPoint(0f, 0f), new MapPoint(10f, 0f));
        var proposals = new[]
        {
            new TacticalProposal(TacticalChannel.Movement, TacticalAction.Move,
                CommandAuthority.ImmediateCombat, ProposalSource.Contact, new MapPoint(2f, 0f), "bound"),
            new TacticalProposal(TacticalChannel.Movement, TacticalAction.Hold,
                CommandAuthority.ProtectedFortification, ProposalSource.ProtectedAssignment, new MapPoint(1f, 0f), "slot"),
            new TacticalProposal(TacticalChannel.Pose, TacticalAction.Crouch,
                CommandAuthority.CriticalSuppression, ProposalSource.Suppression, default, "duck"),
            new TacticalProposal(TacticalChannel.Pose, TacticalAction.Stand,
                CommandAuthority.ImmediateCombat, ProposalSource.Contact, default, "fire")
        };

        var expected = TacticalArbitrationCore.Resolve(snapshot, proposals);
        Equal(2, expected.Winners.Count, "The arbiter did not produce exactly one result per channel.");
        Equal(TacticalAction.Hold, expected.Winners[TacticalChannel.Movement].Action,
            "Immediate contact displaced a protected fortification assignment.");
        Equal(TacticalAction.Crouch, expected.Winners[TacticalChannel.Pose].Action,
            "Critical suppression lost the pose channel.");
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var actual = TacticalArbitrationCore.Resolve(snapshot, Rotate(proposals, iteration));
            Equal(expected.Winners[TacticalChannel.Movement], actual.Winners[TacticalChannel.Movement],
                "Movement arbitration depended on proposal order.");
            Equal(expected.Winners[TacticalChannel.Pose], actual.Winners[TacticalChannel.Pose],
                "Pose arbitration depended on proposal order.");
        }
    }

    private static void ProtectedAssignmentOutranksCoverHoldAtEqualAuthority()
    {
        var snapshot = new SoldierTacticalSnapshot(
            12, 2, 1, StrategicPosture.Defend, false, false, true, false,
            false, false, false, new MapPoint(0f, 0f), new MapPoint(5f, 0f));
        var coverHold = new TacticalProposal(TacticalChannel.Movement, TacticalAction.Hold,
            CommandAuthority.ProtectedFortification, ProposalSource.CoverHold, new MapPoint(0f, 0f), "cover");
        var protectedAssignment = new TacticalProposal(TacticalChannel.Movement, TacticalAction.Move,
            CommandAuthority.ProtectedFortification, ProposalSource.ProtectedAssignment, new MapPoint(1f, 0f),
            "assignment");

        var forward = TacticalArbitrationCore.Resolve(snapshot, new[] { coverHold, protectedAssignment });
        Equal(ProposalSource.ProtectedAssignment, forward.Winners[TacticalChannel.Movement].Source,
            "A protected fortification/weapon assignment lost to cover-hold at equal authority.");

        var reversed = TacticalArbitrationCore.Resolve(snapshot, new[] { protectedAssignment, coverHold });
        Equal(ProposalSource.ProtectedAssignment, reversed.Winners[TacticalChannel.Movement].Source,
            "Submission order changed the protected-assignment vs cover-hold tie-break.");

        var defensivePosition = new TacticalProposal(TacticalChannel.Movement, TacticalAction.Hold,
            CommandAuthority.ProtectedFortification, ProposalSource.DefensivePosition, new MapPoint(2f, 0f),
            "defend");

        var defenseVsCoverForward = TacticalArbitrationCore.Resolve(
            snapshot, new[] { coverHold, defensivePosition });
        Equal(ProposalSource.DefensivePosition, defenseVsCoverForward.Winners[TacticalChannel.Movement].Source,
            "Defensive position control lost to cover-hold at equal authority.");

        var defenseVsCoverReversed = TacticalArbitrationCore.Resolve(
            snapshot, new[] { defensivePosition, coverHold });
        Equal(ProposalSource.DefensivePosition, defenseVsCoverReversed.Winners[TacticalChannel.Movement].Source,
            "Submission order changed the defensive-position vs cover-hold tie-break.");
    }

    private static SoldierTacticalSnapshot ProposalSnapshot(
        bool playerLed = false,
        bool scriptOwned = false,
        bool suppressed = false,
        bool needsReloadSafety = false,
        bool lethalHazard = false,
        MapPoint position = default,
        MapPoint threatPosition = default,
        MapPoint hazardPosition = default,
        ContactMovementSensor contactMovement = default,
        bool autonomous = true,
        bool hasPlayerHoldOrder = false,
        bool hasProtectedAssignment = false,
        bool mounted = false)
    {
        return new SoldierTacticalSnapshot(
            1, 1, 1, StrategicPosture.Attack, playerLed, scriptOwned, true, mounted,
            suppressed, needsReloadSafety, lethalHazard, position, threatPosition,
            hazardPosition, contactMovement, autonomous, hasPlayerHoldOrder,
            hasProtectedAssignment);
    }

    private static void ExternalSquadWithoutPlayerHoldCoverEmitsOnlyNativeAndExternal()
    {
        var snapshot = ProposalSnapshot(playerLed: true, hasPlayerHoldOrder: false);
        var destination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(snapshot, new TacticalPolicyOptions(true), destination);

        Equal(2, destination.Count,
            "An external squad without a player-hold order produced more than Native+External.");
        True(destination.Any(p => p.Channel == TacticalChannel.Movement && p.Source == ProposalSource.Native),
            "The native fallback proposal was missing.");
        var external = destination.Single(p => p.Source == ProposalSource.External);
        Equal(TacticalChannel.Movement, external.Channel, "The external proposal was not on the movement channel.");
        Equal(TacticalAction.Native, external.Action, "The external proposal did not defer to native movement.");
        Equal(CommandAuthority.PlayerOrScript, external.Priority, "The external proposal used the wrong authority.");
        False(destination.Any(p => p.Source is ProposalSource.Hazard or ProposalSource.ActionSafety
                or ProposalSource.Suppression),
            "A hazard/safety/suppression proposal leaked in for an external squad.");
    }

    private static void PlayerHoldCoverFollowsCommittedCoverMove()
    {
        var holdSensor = new ContactMovementSensor(
            HasActionableContact: false, HasRecentContact: false, HasCommittedCoverMove: false,
            HasStableCoverHold: false, HasTimedCoverHold: false, CanClaimReachedCover: false,
            HasEngagementHold: false);
        var position = new MapPoint(5f, 9f);
        var holding = ProposalSnapshot(playerLed: true, hasPlayerHoldOrder: true, autonomous: true,
            position: position, contactMovement: holdSensor);
        var destination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(holding, new TacticalPolicyOptions(true), destination);
        var holdProposal = destination.Single(p => p.Source == ProposalSource.PlayerHold);
        Equal(TacticalAction.Hold, holdProposal.Action,
            "An uncommitted player-hold cover move issued Move instead of Hold.");
        Equal(position, holdProposal.Destination,
            "The player-hold proposal did not target the squad's hold position.");

        var moving = holding with { ContactMovement = holdSensor with { HasCommittedCoverMove = true } };
        destination.Clear();
        ProposalGenerationCore.Collect(moving, new TacticalPolicyOptions(true), destination);
        var moveProposal = destination.Single(p => p.Source == ProposalSource.PlayerHold);
        Equal(TacticalAction.Move, moveProposal.Action,
            "A committed cover move under player hold did not issue Move.");
    }

    private static ProposalSource MovementSafetyLadderWinner(List<TacticalProposal> proposals)
    {
        var ladder = proposals.Where(p => p.Channel == TacticalChannel.Movement &&
            p.Source is ProposalSource.Hazard or ProposalSource.ActionSafety or ProposalSource.Suppression)
            .ToArray();
        Equal(1, ladder.Length, "The movement safety ladder did not emit exactly one proposal.");
        return ladder[0].Source;
    }

    private static void MovementSafetyLadderPicksHazardThenSafetyThenSuppression()
    {
        var options = new TacticalPolicyOptions(true);

        var hazardDestination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(
            ProposalSnapshot(lethalHazard: true, needsReloadSafety: true, suppressed: true),
            options, hazardDestination);
        Equal(ProposalSource.Hazard, MovementSafetyLadderWinner(hazardDestination),
            "Lethal hazard did not win the movement safety ladder over reload safety and suppression.");

        var safetyDestination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(
            ProposalSnapshot(lethalHazard: false, needsReloadSafety: true, suppressed: true),
            options, safetyDestination);
        Equal(ProposalSource.ActionSafety, MovementSafetyLadderWinner(safetyDestination),
            "Reload safety did not win the movement safety ladder over suppression.");

        var suppressionDestination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(
            ProposalSnapshot(lethalHazard: false, needsReloadSafety: false, suppressed: true),
            options, suppressionDestination);
        Equal(ProposalSource.Suppression, MovementSafetyLadderWinner(suppressionDestination),
            "Suppression did not produce a movement hold once hazard and reload safety were absent.");
    }

    private static void ProtectedAssignmentSkipsDefensivePositionBranch()
    {
        var sensor = new ContactMovementSensor(
            HasActionableContact: false, HasRecentContact: false, HasCommittedCoverMove: false,
            HasStableCoverHold: false, HasTimedCoverHold: false, CanClaimReachedCover: false,
            HasEngagementHold: false, NeedsDefensivePositionControl: true);
        var options = new TacticalPolicyOptions(true);

        var protectedDestination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(
            ProposalSnapshot(hasProtectedAssignment: true, contactMovement: sensor),
            options, protectedDestination);
        True(protectedDestination.Any(p => p.Source == ProposalSource.ProtectedAssignment),
            "A protected fortification/weapon assignment did not produce a proposal.");
        False(protectedDestination.Any(p => p.Source == ProposalSource.DefensivePosition),
            "The defensive-position branch ran even though a protected assignment was active.");

        var unprotectedDestination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(
            ProposalSnapshot(hasProtectedAssignment: false, contactMovement: sensor),
            options, unprotectedDestination);
        False(unprotectedDestination.Any(p => p.Source == ProposalSource.ProtectedAssignment),
            "A protected-assignment proposal appeared without a protected assignment.");
        True(unprotectedDestination.Any(p => p.Source == ProposalSource.DefensivePosition),
            "The defensive-position branch did not run once the protected assignment was released.");
    }

    private static void ContactResponseRequiresPolicyEnabled()
    {
        var contactSensor = new ContactMovementSensor(
            HasActionableContact: true, HasRecentContact: false, HasCommittedCoverMove: false,
            HasStableCoverHold: false, HasTimedCoverHold: false, CanClaimReachedCover: false,
            HasEngagementHold: false);
        var threat = new MapPoint(30f, 40f);
        var contactAllowed = ProposalSnapshot(contactMovement: contactSensor, threatPosition: threat);

        var allowedDestination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(contactAllowed, new TacticalPolicyOptions(true), allowedDestination);
        True(allowedDestination.Any(p => p.Source == ProposalSource.Contact),
            "Contact response did not fire when policy was enabled and the sensor " +
            "demanded local control.");

        var policyDisabledDestination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(
            contactAllowed, new TacticalPolicyOptions(false), policyDisabledDestination);
        False(policyDisabledDestination.Any(p => p.Source == ProposalSource.Contact),
            "Contact response fired even though ContactResponseEnabled was false.");

        var contact = allowedDestination.Single(p => p.Source == ProposalSource.Contact);
        Equal(TacticalChannel.Movement, contact.Channel,
            "Contact response did not target the movement channel.");
        Equal(CommandAuthority.ImmediateCombat, contact.Priority,
            "Contact response did not use immediate-combat authority.");
        Equal(threat, contact.Destination,
            "Contact response did not target the sensed threat position.");
    }

    private static void ReloadSafetyAddsProneAndFireInhibitionAlongsideTheHold()
    {
        var snapshot = ProposalSnapshot(needsReloadSafety: true);
        var destination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(snapshot, new TacticalPolicyOptions(true), destination);

        var hold = destination.Single(p =>
            p.Channel == TacticalChannel.Movement && p.Source == ProposalSource.ActionSafety);
        Equal(TacticalAction.Hold, hold.Action, "Reload safety did not hold the movement channel.");

        var pose = destination.Single(p => p.Channel == TacticalChannel.Pose && p.Source == ProposalSource.ActionSafety);
        Equal(TacticalAction.Prone, pose.Action, "Reload safety did not drop to prone on the pose channel.");
        Equal(CommandAuthority.RequiredSafety, pose.Priority,
            "Reload safety pose proposal used the wrong authority.");

        var firePermission = destination.Single(p =>
            p.Channel == TacticalChannel.FirePermission && p.Source == ProposalSource.ActionSafety);
        Equal(TacticalAction.InhibitFire, firePermission.Action, "Reload safety did not inhibit fire.");
        Equal(CommandAuthority.RequiredSafety, firePermission.Priority,
            "Reload safety fire-permission proposal used the wrong authority.");
    }

    private static void MovementDebugProjectionUsesOnlyExecutorDestination()
    {
        var snapshot = new SoldierTacticalSnapshot(
            91, 4, 8, StrategicPosture.Attack, false, false, true, false,
            true, false, false, default, default);
        var proposalContext = new MapPoint(100f, 100f);
        var resolution = TacticalArbitrationCore.Resolve(snapshot, new[]
        {
            new TacticalProposal(
                TacticalChannel.Movement,
                TacticalAction.Move,
                CommandAuthority.ImmediateCombat,
                ProposalSource.Contact,
                proposalContext,
                "advance")
        });

        var withoutExecutor = MovementDebugProjectionCore.Project(resolution, false, default);
        Equal(ProposalSource.Contact, withoutExecutor.Source,
            "The movement debug view lost the arbitration owner when the executor had no route.");
        Equal(TacticalAction.Move, withoutExecutor.Action,
            "The movement debug view lost the winning action.");
        False(withoutExecutor.HasExecutorDestination,
            "A semantic proposal destination was incorrectly presented as an active route.");

        var liveExecutorDestination = new MapPoint(24f, -7f);
        var withExecutor = MovementDebugProjectionCore.Project(
            resolution, true, liveExecutorDestination);
        True(withExecutor.HasExecutorDestination,
            "The live movement executor destination was not exposed to diagnostics.");
        Equal(liveExecutorDestination, withExecutor.ExecutorDestination,
            "The debug route did not use the live movement executor destination.");
        False(withExecutor.ExecutorDestination.Equals(proposalContext),
            "The debug route substituted arbitration context for the live executor destination.");
    }

    private static void AiDebugAllegianceScopeIsExplicitAndFailClosed()
    {
        True(AiDebugAllegianceCore.Includes(
                AiDebugScope.All, false, false, false),
            "The all-sides debug scope unexpectedly required a player faction.");
        False(AiDebugAllegianceCore.Includes(
                AiDebugScope.Allies, false, true, false),
            "Allied scope silently became all-sides when the player faction was missing.");
        False(AiDebugAllegianceCore.Includes(
                AiDebugScope.Enemies, true, false, true),
            "Enemy scope guessed the allegiance of an unknown candidate faction.");
        True(AiDebugAllegianceCore.Includes(
                AiDebugScope.Allies, true, true, false),
            "A game-classified friendly was omitted from allied scope.");
        False(AiDebugAllegianceCore.Includes(
                AiDebugScope.Allies, true, true, true),
            "A game-classified enemy leaked into allied scope.");
        True(AiDebugAllegianceCore.Includes(
                AiDebugScope.Enemies, true, true, true),
            "A game-classified enemy was omitted from enemy scope.");
        False(AiDebugAllegianceCore.Includes(
                AiDebugScope.Enemies, true, true, false),
            "A game-classified friendly leaked into enemy scope.");
    }

    private static void GameplayMutationIsHostAuthoritative()
    {
        True(GroundAuthorityCore.CanMutate(false, false), "Single-player mutation required a host flag.");
        True(GroundAuthorityCore.CanMutate(true, true), "The multiplayer host could not mutate AI.");
        False(GroundAuthorityCore.CanMutate(true, false), "A multiplayer client could mutate AI.");
    }

    private static void DefenderAllocatorStaffsAllViableWeaponsInPriorityOrder()
    {
        var squads = new[]
        {
            new DefenderSquadCandidate(1, 10f, 7, false, false),
            new DefenderSquadCandidate(2, 9f, 7, false, false),
            new DefenderSquadCandidate(3, 8f, 7, true, false)
        };
        var crews = Enumerable.Range(1, 3).SelectMany(squadId => new[]
        {
            new DefenderCrewCandidate(squadId * 100, squadId, true, false, false, false, true),
            new DefenderCrewCandidate(squadId * 100 + 1, squadId, false, false, false, false, true),
            new DefenderCrewCandidate(squadId * 100 + 2, squadId, false, false, false, false, true),
            new DefenderCrewCandidate(squadId * 100 + 3, squadId, false, false, false, false, true)
        }).ToArray();
        var weapons = new[]
        {
            new DefensiveWeaponCandidate(30, true, false, 8f, 1f, 0.2f, 0.9f),
            new DefensiveWeaponCandidate(20, true, true, 57f, 1f, 0.8f, 0.5f),
            new DefensiveWeaponCandidate(10, true, true, 88f, 0.8f, 0.9f, 0.4f)
        };

        var plan = DefenderAllocationCore.Allocate(squads, crews, weapons, true);
        SequenceEqual(new[] { 3 }, plan.ReserveSquadIds,
            "The complete planned reserve was not protected.");
        SequenceEqual(new[] { 10, 20, 30 }, plan.WeaponAssignments.Select(item => item.WeaponId),
            "AP guns were not staffed first by caliber and coverage.");
        Equal(0, plan.UnstaffedWeaponIds.Count, "A viable objective weapon was left unstaffed.");
        Equal(plan.WeaponAssignments.Count,
            plan.WeaponAssignments.Select(item => item.SoldierId).Distinct().Count(),
            "One soldier was assigned to multiple weapons.");
    }

    private static void DefenderAllocatorProtectsReserveAndCriticalFootStrength()
    {
        var squads = new[]
        {
            new DefenderSquadCandidate(1, 10f, 4, false, false),
            new DefenderSquadCandidate(2, 12f, 8, true, false)
        };
        var crews = new[]
        {
            new DefenderCrewCandidate(10, 1, true, false, false, false, true),
            new DefenderCrewCandidate(11, 1, false, true, false, false, true),
            new DefenderCrewCandidate(12, 1, false, false, false, false, true),
            new DefenderCrewCandidate(20, 2, false, false, false, false, true)
        };
        var weapons = new[]
        {
            new DefensiveWeaponCandidate(1, true, false, 8f, 1f, 0f, 1f),
            new DefensiveWeaponCandidate(2, true, false, 8f, 1f, 0f, 0.9f)
        };

        var plan = DefenderAllocationCore.Allocate(squads, crews, weapons, false);
        SequenceEqual(new[] { 2 }, plan.ReserveSquadIds, "The full mobile reserve donated a gunner.");
        Equal(1, plan.WeaponAssignments.Count,
            "A donor squad was reduced below its leader plus two combat-ready soldiers.");
        Equal(12, plan.WeaponAssignments[0].SoldierId,
            "The last medic was consumed before an ordinary rifleman.");
    }

    private static void DefenderAllocatorHandlesInsufficientCrewsAndInvalidWeapons()
    {
        var squads = new[] { new DefenderSquadCandidate(1, 10f, 4, false, false) };
        var crews = new[]
        {
            new DefenderCrewCandidate(1, 1, true, false, false, false, true),
            new DefenderCrewCandidate(2, 1, false, false, false, false, true)
        };
        var weapons = new[]
        {
            new DefensiveWeaponCandidate(1, true, true, 75f, 1f, 1f, 0.5f),
            new DefensiveWeaponCandidate(2, true, false, 12f, 1f, 0f, 1f),
            new DefensiveWeaponCandidate(3, false, true, 100f, 0f, 1f, 1f)
        };

        var plan = DefenderAllocationCore.Allocate(squads, crews, weapons, true);
        Equal(1, plan.WeaponAssignments.Count, "Available crew capacity was not used.");
        SequenceEqual(new[] { 2 }, plan.UnstaffedWeaponIds,
            "Insufficient crews or destroyed/empty weapons were handled incorrectly.");
    }

    private static void ProtectedWeaponTransitLeaseSurvivesTemporaryInterruption()
    {
        var registry = new CommandLeaseRegistryCore();
        var request = new CommandLeaseRequest(
            CommandChannel.InfantryAssignment, 55, "static-weapon", CommandAuthority.ProtectedFortification,
            4, "gunner", new MapPoint(10f, 10f), "weapon=9", float.PositiveInfinity);
        True(registry.TryAcquire(request, 1f, out var assignment),
            "The protected gun transit lease was rejected.");
        var snapshot = new SoldierTacticalSnapshot(
            55, 5, 4, StrategicPosture.Defend, false, false, true, false,
            false, false, true, default, default);
        var resolution = TacticalArbitrationCore.Resolve(snapshot, new[]
        {
            new TacticalProposal(TacticalChannel.Movement, TacticalAction.Move,
                CommandAuthority.ProtectedFortification, ProposalSource.ProtectedAssignment, new MapPoint(10f, 10f), "resume"),
            new TacticalProposal(TacticalChannel.Movement, TacticalAction.Move,
                CommandAuthority.LethalEmergency, ProposalSource.Hazard, new MapPoint(-5f, 0f), "temporary")
        });
        Equal(ProposalSource.Hazard, resolution.Winners[TacticalChannel.Movement].Source,
            "A lethal hazard did not temporarily interrupt weapon transit.");
        True(registry.IsCurrent(assignment, 2f),
            "The protected weapon assignment was destroyed by a temporary movement interruption.");
    }

    private static void StaticWeaponTransitAcceptsAssignedSeatReservation()
    {
        False(StaticWeaponAssignmentCore.SeatPreventsTransit(true, 55, 55),
            "The assigned gunner's native seat reservation was mistaken for an external occupant.");
        True(StaticWeaponAssignmentCore.SeatPreventsTransit(true, 66, 55),
            "A gun occupied by another soldier was treated as available.");
        True(StaticWeaponAssignmentCore.SeatPreventsTransit(false, 0, 55),
            "A weapon with no usable turret seat remained assignable.");
        True(StaticWeaponAssignmentCore.ShouldReassertDestination(false, false, false),
            "A cleared static-weapon route was not restored.");
        False(StaticWeaponAssignmentCore.ShouldReassertDestination(false, true, false),
            "An intact static-weapon route was needlessly rewritten.");
        False(StaticWeaponAssignmentCore.ShouldReassertDestination(false, false, true),
            "A lethal emergency was overridden by static-weapon transit.");
    }

    private static void FortifiedCoverBeatsCloserWeakCover()
    {
        var nearWeak = new FortifiedCoverSlot(
            1, new MapPoint(2f, 0f), 0.25f, 0.20f, 0.80f, 2f, true);
        var fartherTrench = new FortifiedCoverSlot(
            2, new MapPoint(28f, 0f), 1f, 0.90f, 0.15f, 28f, true);
        True(FortifiedPositionCore.Score(fartherTrench, 55f, 4) >
             FortifiedPositionCore.Score(nearWeak, 55f, 1),
            "A nearby weak slot beat a protected trench with a clear firing lane.");
    }

    private static void FortifiedSlotsGroupWithoutDuplicateReservations()
    {
        var slots = new[]
        {
            new FortifiedCoverSlot(1, new MapPoint(0f, 0f), 1f, 0.8f, 0.1f, 10f, true),
            new FortifiedCoverSlot(2, new MapPoint(2f, 0f), 0.9f, 0.9f, 0.1f, 11f, true),
            new FortifiedCoverSlot(3, new MapPoint(30f, 0f), 0.8f, 0.7f, 0.2f, 30f, true),
            new FortifiedCoverSlot(2, new MapPoint(2f, 0f), 0.9f, 0.9f, 0.1f, 11f, true)
        };
        var groups = FortifiedPositionCore.Group(slots, 6f, 55f);
        Equal(2, groups.Count, "Nearby trench/building slots were not grouped geometrically.");
        Equal(3, groups.SelectMany(group => group.SlotIds).Distinct().Count(),
            "A duplicate cover slot survived global grouping.");
        False(InfantryCoverDecisionCore.CoverPositionsConflict(
                slots[0].Position, slots[1].Position,
                FortifiedPositionCore.MinimumSlotSeparationMeters),
            "Distinct 1.75m-separated firing slots were treated as one reservation.");
    }

    private static void DefensiveAnchorsOnlyMoveAfterMaterialDegradation()
    {
        False(FortifiedPositionCore.ShouldReplace(0.60f, 0.90f, 14.99f, false),
            "A valid defensive anchor moved before its firing lane had been irrelevant for 15 seconds.");
        False(FortifiedPositionCore.ShouldReplace(0.60f, 0.74f, 15f, false),
            "A defender moved for an alternative that was less than 25 percent better.");
        True(FortifiedPositionCore.ShouldReplace(0.60f, 0.75f, 15f, false),
            "A materially better reserved position did not replace a degraded anchor.");
        True(FortifiedPositionCore.ShouldReplace(0.60f, 0.20f, 0f, true),
            "A destroyed or unsafe anchor was retained.");
    }

    private static CommandLeaseRequest LeaseRequest(
        int entityId,
        string owner,
        CommandAuthority authority,
        int revision)
        => new(
            CommandChannel.SquadOrders,
            entityId,
            owner,
            authority,
            revision,
            "test",
            new MapPoint(1f, 1f),
            string.Empty,
            20f);

    private static T[] Rotate<T>(IReadOnlyList<T> source, int amount)
    {
        if (source.Count == 0)
            return Array.Empty<T>();
        var offset = amount % source.Count;
        return source.Skip(offset).Concat(source.Take(offset)).ToArray();
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
            throw new InvalidOperationException($"{message} Expected={expected}; Actual={actual}");
    }

    private static void Near(float expected, float actual, float tolerance, string message)
    {
        if (!float.IsFinite(actual) || MathF.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message} Expected={expected}; Actual={actual}");
    }

    private static void Finite(float value, string message)
    {
        if (!float.IsFinite(value))
            throw new InvalidOperationException(message);
    }

    private static float Distance(MapPoint left, MapPoint right)
    {
        var x = left.X - right.X;
        var z = left.Z - right.Z;
        return MathF.Sqrt(x * x + z * z);
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(message);
    }
}
