using System.Globalization;
using ER2RealismOverhaul;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            (nameof(EmptyInputIsSafe), EmptyInputIsSafe),
            (nameof(UnsafeSquadsAreFilteredAndRolesAreAllocated), UnsafeSquadsAreFilteredAndRolesAreAllocated),
            (nameof(RoleCountsMeetSmallForceRules), RoleCountsMeetSmallForceRules),
            (nameof(PlanningIsDeterministicAcrossInputOrder), PlanningIsDeterministicAcrossInputOrder),
            (nameof(AxisScoringAndSeparationAreDeterministic), AxisScoringAndSeparationAreDeterministic),
            (nameof(FlankHoldsWhenNoSeparatedAxisExists), FlankHoldsWhenNoSeparatedAxisExists),
            (nameof(AttackGateHonorsRatioAndSuppressionBoundaries), AttackGateHonorsRatioAndSuppressionBoundaries),
            (nameof(AttackerAggressivenessAdjustsAttackGate), AttackerAggressivenessAdjustsAttackGate),
            (nameof(SmokeBlocksManeuverUntilReady), SmokeBlocksManeuverUntilReady),
            (nameof(FireMissionRetargetsAroundFriendlyConcentrations), FireMissionRetargetsAroundFriendlyConcentrations),
            (nameof(ReportsAreFreshFilteredAndDeduplicated), ReportsAreFreshFilteredAndDeduplicated),
            (nameof(SquadEligibilityHonorsExactBoundaries), SquadEligibilityHonorsExactBoundaries),
            (nameof(BrokenSquadIsRemovedOnReplan), BrokenSquadIsRemovedOnReplan),
            (nameof(InvalidNumericDataFailsSafe), InvalidNumericDataFailsSafe),
            (nameof(AntiTankTaskSelectsNearestCapableSquad), AntiTankTaskSelectsNearestCapableSquad),
            (nameof(AntiTankTaskDefaultsToAmbushAndBoundsHunting), AntiTankTaskDefaultsToAmbushAndBoundsHunting),
            (nameof(AntiTankTaskRejectsUnsafeInputs), AntiTankTaskRejectsUnsafeInputs),
            (nameof(AntiTankTaskIsDeterministicAndAvoidsMultiTankHunts), AntiTankTaskIsDeterministicAndAvoidsMultiTankHunts),
            (nameof(AircraftBombReleaseRequiresLiveHostileNearImpact), AircraftBombReleaseRequiresLiveHostileNearImpact),
            (nameof(ArtilleryCrewSelectionRotatesAcrossEligibleGuns), ArtilleryCrewSelectionRotatesAcrossEligibleGuns),
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
            (nameof(AttackProgressHasMaximumCombatHalt), AttackProgressHasMaximumCombatHalt),
            (nameof(IdleSoldiersRemainUnderCommanderOrNativeControl), IdleSoldiersRemainUnderCommanderOrNativeControl),
            (nameof(ArrivedDefendersStayUnderPositionControl), ArrivedDefendersStayUnderPositionControl),
            (nameof(AutonomousDefendersSeekCoverEvenWithVisibleContact), AutonomousDefendersSeekCoverEvenWithVisibleContact),
            (nameof(DefensivePositionOwnershipStaysLatchedOutsideTheArrivalArea), DefensivePositionOwnershipStaysLatchedOutsideTheArrivalArea),
            (nameof(ReachedCoverCreatesAStableFightingHalt), ReachedCoverCreatesAStableFightingHalt),
            (nameof(AttackBoundsRequireSafetyAndTacticalAuthorization), AttackBoundsRequireSafetyAndTacticalAuthorization),
            (nameof(DefensiveReinforcementsReachObjectiveBeforeCommanderOwnership), DefensiveReinforcementsReachObjectiveBeforeCommanderOwnership),
            (nameof(DefensiveFortificationsRemainAssigned), DefensiveFortificationsRemainAssigned),
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
            (nameof(PostureFollowsObjectiveOwnership), PostureFollowsObjectiveOwnership),
            (nameof(ContestedOwnershipRetainsOneDefendingSide), ContestedOwnershipRetainsOneDefendingSide),
            (nameof(AttackerAndDefenderPlannerCoresSelectTheirPostures), AttackerAndDefenderPlannerCoresSelectTheirPostures),
            (nameof(CommanderOrdersChangePhaseInsteadOfChurningDestinations), CommanderOrdersChangePhaseInsteadOfChurningDestinations),
            (nameof(ObjectiveFormationDistributesSquadsAwayFromTheCenter), ObjectiveFormationDistributesSquadsAwayFromTheCenter),
            (nameof(CommanderCoverChoicesPreserveSquadCohesion), CommanderCoverChoicesPreserveSquadCohesion),
            (nameof(AttackCoverCorridorAllowsFlankingWithinBoundedBacktrack), AttackCoverCorridorAllowsFlankingWithinBoundedBacktrack),
            (nameof(CoverClusterSnapsAnchorToDenseProtectiveCover), CoverClusterSnapsAnchorToDenseProtectiveCover),
            (nameof(CoverDensityLeashGrowsOnlyForDenseClusters), CoverDensityLeashGrowsOnlyForDenseClusters),
            (nameof(FailedCoverSearchBacksOffProgressively), FailedCoverSearchBacksOffProgressively),
            (nameof(TacticalPoseLatchRejectsProneCrouchChurn), TacticalPoseLatchRejectsProneCrouchChurn),
            (nameof(CommandLeasesAreStableAndRejectStaleWork), CommandLeasesAreStableAndRejectStaleWork),
            (nameof(CommandLeaseDebugSnapshotIsOrderedAndPrunesExpiredWork), CommandLeaseDebugSnapshotIsOrderedAndPrunesExpiredWork),
            (nameof(DefensiveOrdersIgnorePlannerHeartbeatAndRoleChurn), DefensiveOrdersIgnorePlannerHeartbeatAndRoleChurn),
            (nameof(ExternalOwnershipPreemptsAndLatches), ExternalOwnershipPreemptsAndLatches),
            (nameof(TacticalArbitrationUsesOneDeterministicWinnerPerChannel), TacticalArbitrationUsesOneDeterministicWinnerPerChannel),
            (nameof(ProtectedAssignmentOutranksCoverHoldAtEqualAuthority), ProtectedAssignmentOutranksCoverHoldAtEqualAuthority),
            (nameof(ExternalSquadWithoutPlayerHoldCoverEmitsOnlyNativeAndExternal), ExternalSquadWithoutPlayerHoldCoverEmitsOnlyNativeAndExternal),
            (nameof(PlayerHoldCoverFollowsCommittedCoverMove), PlayerHoldCoverFollowsCommittedCoverMove),
            (nameof(MovementSafetyLadderPicksHazardThenSafetyThenSuppression), MovementSafetyLadderPicksHazardThenSafetyThenSuppression),
            (nameof(ProtectedAssignmentSkipsDefensivePositionBranch), ProtectedAssignmentSkipsDefensivePositionBranch),
            (nameof(ContactRequiresPolicyEnabledAndNoTankThreatOtherwiseTankFearWins), ContactRequiresPolicyEnabledAndNoTankThreatOtherwiseTankFearWins),
            (nameof(MountedCrewNeverReceivesATankFearProposal), MountedCrewNeverReceivesATankFearProposal),
            (nameof(ArmorRoleAllocationIsStickyAndOnlyRebuildsOnDiscreteTriggers), ArmorRoleAllocationIsStickyAndOnlyRebuildsOnDiscreteTriggers),
            (nameof(CommanderIntentProposesMoveToTheAcceptedDestination), CommanderIntentProposesMoveToTheAcceptedDestination),
            (nameof(ReloadSafetyAddsProneAndFireInhibitionAlongsideTheHold), ReloadSafetyAddsProneAndFireInhibitionAlongsideTheHold),
            (nameof(MovementDebugProjectionUsesOnlyExecutorDestination), MovementDebugProjectionUsesOnlyExecutorDestination),
            (nameof(AiDebugAllegianceScopeIsExplicitAndFailClosed), AiDebugAllegianceScopeIsExplicitAndFailClosed),
            (nameof(SupportRequestsAreDeduplicatedByObjectiveRevision), SupportRequestsAreDeduplicatedByObjectiveRevision),
            (nameof(GameplayMutationIsHostAuthoritative), GameplayMutationIsHostAuthoritative),
            (nameof(DefenderAllocatorStaffsAllViableWeaponsInPriorityOrder), DefenderAllocatorStaffsAllViableWeaponsInPriorityOrder),
            (nameof(DefenderAllocatorProtectsReserveAndCriticalFootStrength), DefenderAllocatorProtectsReserveAndCriticalFootStrength),
            (nameof(DefenderAllocatorHandlesInsufficientCrewsAndInvalidWeapons), DefenderAllocatorHandlesInsufficientCrewsAndInvalidWeapons),
            (nameof(ProtectedWeaponTransitLeaseSurvivesTemporaryInterruption), ProtectedWeaponTransitLeaseSurvivesTemporaryInterruption),
            (nameof(StaticWeaponTransitAcceptsAssignedSeatReservation), StaticWeaponTransitAcceptsAssignedSeatReservation),
            (nameof(LauncherSelectionWaitsForEffectiveRange), LauncherSelectionWaitsForEffectiveRange),
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
            (nameof(ContactReportReplacementPrefersFresherInformation), ContactReportReplacementPrefersFresherInformation),
            (nameof(ContactConfidenceDecaysLinearlyToZero), ContactConfidenceDecaysLinearlyToZero),
            (nameof(AttackersLeaveACapturedObjectiveForAFarUnsecuredOne), AttackersLeaveACapturedObjectiveForAFarUnsecuredOne),
            (nameof(AttackersWithEveryObjectiveSecuredDefendTheBestOne), AttackersWithEveryObjectiveSecuredDefendTheBestOne),
            (nameof(AttackerStickinessKeepsTheCurrentUnsecuredObjective), AttackerStickinessKeepsTheCurrentUnsecuredObjective),
            (nameof(AttackerStickinessReleasesOnceItsObjectiveIsSecured), AttackerStickinessReleasesOnceItsObjectiveIsSecured),
            (nameof(DefenderObjectiveSelectionIsUnchangedByThePenalty), DefenderObjectiveSelectionIsUnchangedByThePenalty),
            (nameof(ObjectiveSelectionTiesAreBrokenByLowerId), ObjectiveSelectionTiesAreBrokenByLowerId),
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

    private static void AircraftBombReleaseRequiresLiveHostileNearImpact()
    {
        var valid = new AircraftBombReleaseInput(true, true, true, true, 65f, 65f);
        True(AircraftBombDecisionCore.TargetSupportsRelease(valid),
            "A live hostile target on the release boundary was rejected.");

        False(AircraftBombDecisionCore.TargetSupportsRelease(valid with { TargetAlive = false }),
            "A dead target authorized a bomb release.");
        False(AircraftBombDecisionCore.TargetSupportsRelease(valid with { TargetHostile = false }),
            "A non-hostile target authorized a bomb release.");
        False(AircraftBombDecisionCore.TargetSupportsRelease(valid with { HasUsableGroundTarget = false }),
            "A missing ground target authorized a bomb release.");
        False(AircraftBombDecisionCore.TargetSupportsRelease(valid with { HasValidPredictedImpact = false }),
            "A missing impact prediction authorized a bomb release.");
        False(AircraftBombDecisionCore.TargetSupportsRelease(valid with { HorizontalMissMeters = 65.01f }),
            "An empty impact point beyond the target tolerance authorized a bomb release.");
        False(AircraftBombDecisionCore.TargetSupportsRelease(valid with { HorizontalMissMeters = float.NaN }),
            "An invalid impact solution authorized a bomb release.");
    }

    private static void ArtilleryCrewSelectionRotatesAcrossEligibleGuns()
    {
        var crews = new[] { 11, 25, 48 };
        Equal(0, ArtilleryCrewSelectionCore.SelectNextCandidateIndex(crews, 0),
            "The first artillery request did not select the first eligible crew.");
        Equal(1, ArtilleryCrewSelectionCore.SelectNextCandidateIndex(crews, 11),
            "The next artillery request did not rotate away from the previous crew.");
        Equal(2, ArtilleryCrewSelectionCore.SelectNextCandidateIndex(crews, 25),
            "Artillery rotation skipped an eligible crew.");
        Equal(0, ArtilleryCrewSelectionCore.SelectNextCandidateIndex(crews, 48),
            "Artillery rotation did not wrap to the first crew.");
        Equal(-1, ArtilleryCrewSelectionCore.SelectNextCandidateIndex(Array.Empty<int>(), 0),
            "An empty artillery roster selected a crew.");
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

    private static void CommanderCoverChoicesPreserveSquadCohesion()
    {
        var smallArea = SquadCohesionCore.StationaryAreaRadius(20f, 1);
        var crowdedArea = SquadCohesionCore.StationaryAreaRadius(80f, 9);
        True(smallArea >= 14f && smallArea <= 24f,
            "A commander stationary area escaped the compact fighting-area limits.");
        True(crowdedArea >= 14f && crowdedArea <= 24f,
            "Squad count produced an invalid stationary fighting area.");
        True(SquadCohesionCore.AllowsCover(
                new MapPoint(29.9f, 0f), new MapPoint(0f, 0f)),
            "A valid cover slot inside the squad cohesion leash was rejected.");
        False(SquadCohesionCore.AllowsCover(
                new MapPoint(30.1f, 0f), new MapPoint(0f, 0f)),
            "A distant cover slot was allowed to scatter a squad.");
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

    private static void CoverClusterSnapsAnchorToDenseProtectiveCover()
    {
        var anchor = new MapPoint(0f, 0f);

        // A dense five-candidate building beats a single closer roadside node.
        var building = new[]
        {
            new CoverClusterCandidate(new MapPoint(20f, 0f), 1f),
            new CoverClusterCandidate(new MapPoint(22f, 1f), 1f),
            new CoverClusterCandidate(new MapPoint(21f, -2f), 1f),
            new CoverClusterCandidate(new MapPoint(23f, 2f), 1f),
            new CoverClusterCandidate(new MapPoint(19f, 3f), 1f),
            new CoverClusterCandidate(new MapPoint(4f, 0f), 1f)
        };
        True(CoverClusterCore.TrySelect(building, anchor, 60f, out var center, out var count),
            "A dense protective cluster was not selected.");
        True(count >= CoverClusterCore.MinimumClusterCandidates,
            "The selected cluster did not meet the minimum density.");
        True(Distance(center, new MapPoint(21f, 0.8f)) < 6f,
            "The cluster center did not settle on the dense building.");

        // Empty input selects nothing.
        False(CoverClusterCore.TrySelect(
                Array.Empty<CoverClusterCandidate>(), anchor, 60f, out _, out _),
            "An empty candidate list produced a cluster.");

        // A lone candidate cannot form a cluster.
        False(CoverClusterCore.TrySelect(
                new[] { new CoverClusterCandidate(new MapPoint(10f, 0f), 1f) },
                anchor, 60f, out _, out _),
            "A single candidate was treated as a cluster.");

        // A dense cluster beyond the maximum shift is rejected.
        var farBuilding = new[]
        {
            new CoverClusterCandidate(new MapPoint(200f, 0f), 1f),
            new CoverClusterCandidate(new MapPoint(202f, 1f), 1f),
            new CoverClusterCandidate(new MapPoint(201f, -2f), 1f),
            new CoverClusterCandidate(new MapPoint(203f, 2f), 1f)
        };
        False(CoverClusterCore.TrySelect(farBuilding, anchor, 60f, out _, out _),
            "A cluster beyond the maximum shift was accepted.");

        // NaN candidates are ignored and cannot seed a cluster.
        var withNan = new[]
        {
            new CoverClusterCandidate(new MapPoint(float.NaN, 0f), 1f),
            new CoverClusterCandidate(new MapPoint(30f, 0f), 1f),
            new CoverClusterCandidate(new MapPoint(float.PositiveInfinity, 1f), 1f)
        };
        False(CoverClusterCore.TrySelect(withNan, anchor, 60f, out _, out _),
            "Invalid candidates seeded a cluster.");

        // Deterministic tie-break: two equal-weight clusters resolve to the lowest
        // X then Z center.
        var symmetric = new[]
        {
            new CoverClusterCandidate(new MapPoint(-30f, 0f), 1f),
            new CoverClusterCandidate(new MapPoint(-31f, 1f), 1f),
            new CoverClusterCandidate(new MapPoint(-29f, -1f), 1f),
            new CoverClusterCandidate(new MapPoint(30f, 0f), 1f),
            new CoverClusterCandidate(new MapPoint(31f, 1f), 1f),
            new CoverClusterCandidate(new MapPoint(29f, -1f), 1f)
        };
        True(CoverClusterCore.TrySelect(symmetric, anchor, 60f, out var tieCenter, out _),
            "A symmetric layout failed to select any cluster.");
        True(tieCenter.X < 0f,
            "The symmetric tie-break did not prefer the lowest-X cluster center.");
    }

    private static void CoverDensityLeashGrowsOnlyForDenseClusters()
    {
        var baseRadius = 24f;

        // Below the density threshold the leash keeps the compact base radius.
        Near(baseRadius,
            SquadCohesionCore.ClusterCohesionRadius(baseRadius, 2),
            0.001f,
            "A sparse area grew the cohesion leash.");

        // A dense cluster is allowed to grow up to the expanded ceiling.
        Near(SquadCohesionCore.MaximumClusterCohesionRadiusMeters,
            SquadCohesionCore.ClusterCohesionRadius(baseRadius, 12),
            0.001f,
            "A dense cluster did not expand the cohesion leash to the ceiling.");

        // The minimum floor is preserved for tiny base radii.
        True(SquadCohesionCore.ClusterCohesionRadius(5f, 12) >= 14f,
            "The cluster cohesion radius dropped below the minimum leash.");

        // Boundary: exactly the minimum cluster size does not grow the leash beyond
        // the base; one more candidate begins to grow it.
        Near(baseRadius,
            SquadCohesionCore.ClusterCohesionRadius(
                baseRadius, CoverClusterCore.MinimumClusterCandidates - 1),
            0.001f,
            "A sub-threshold cluster grew the leash.");
        True(SquadCohesionCore.ClusterCohesionRadius(
                 baseRadius, CoverClusterCore.MinimumClusterCandidates) > baseRadius,
            "A threshold cluster did not begin to grow the leash.");

        // Invalid inputs fall back to the base radius.
        Near(baseRadius,
            SquadCohesionCore.ClusterCohesionRadius(baseRadius, -3),
            0.001f,
            "A negative cluster count altered the leash.");
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

    private static void TacticalPoseLatchRejectsProneCrouchChurn()
    {
        True(TacticalPoseStabilityCore.ShouldAccept(
                TacticalStance.Crouched, TacticalStance.Prone, 1f, 10f, false),
            "An immediate lower safety posture was delayed.");
        False(TacticalPoseStabilityCore.ShouldAccept(
                TacticalStance.Prone, TacticalStance.Crouched, 9.9f, 10f, false),
            "Prone was released before its minimum stable hold expired.");
        True(TacticalPoseStabilityCore.ShouldAccept(
                TacticalStance.Prone, TacticalStance.Crouched, 10f, 10f, false),
            "A released prone owner could not transition after the hold expired.");
        False(TacticalPoseStabilityCore.ShouldAccept(
                TacticalStance.Prone, TacticalStance.Crouched, 20f, 10f, true),
            "A lower pose was discarded while suppression or protective cover still owned it.");
        Equal(23.5f, TacticalPoseStabilityCore.RenewHoldUntil(20f, 10f, true),
            "Active lower-pose ownership did not renew the stability interval.");
        Equal(23.5f, TacticalPoseStabilityCore.RenewHoldUntil(21f, 23.5f, false),
            "A released pose owner continued renewing its stability interval.");
        False(TacticalPoseStabilityCore.ShouldAccept(
                TacticalStance.Prone, TacticalStance.Crouched, 23.49f, 23.5f, false),
            "A one-frame owner release bypassed the renewable stability interval.");
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
        var latched = TacticalStance.Crouched;
        var holdUntil = 0f;
        var reachedProne = false;
        var everRoseAfterProne = false;
        for (var t = 0f; t <= 30f; t += 0.5f)
        {
            var owned = CoverPostureOwnershipCore.CoverPoseOwned(
                hasThreatMemory: true, onUsableCover: true, defensiveHold: false);
            var proposed = owned ? TacticalStance.Prone : TacticalStance.Crouched;
            holdUntil = TacticalPoseStabilityCore.RenewHoldUntil(t, holdUntil, owned);
            if (TacticalPoseStabilityCore.ShouldAccept(latched, proposed, t, holdUntil, owned))
            {
                if (reachedProne && proposed != TacticalStance.Prone)
                    everRoseAfterProne = true;
                latched = proposed;
                holdUntil = t + TacticalPoseStabilityCore.MinimumHoldSeconds;
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
        var latched = TacticalStance.Crouched;
        var holdUntil = 0f;
        var reachedProne = false;
        var everRoseAfterProne = false;
        for (var t = 0f; t <= 6f; t += 0.5f)
        {
            const bool pinned = true;
            const TacticalStance proposed = TacticalStance.Prone;
            holdUntil = TacticalPoseStabilityCore.RenewHoldUntil(t, holdUntil, pinned);
            if (TacticalPoseStabilityCore.ShouldAccept(latched, proposed, t, holdUntil, pinned))
            {
                latched = proposed;
                holdUntil = t + TacticalPoseStabilityCore.MinimumHoldSeconds;
            }

            if (latched == TacticalStance.Prone)
                reachedProne = true;
        }

        for (var t = 6.5f; t <= 20f; t += 0.5f)
        {
            const bool pinned = false;
            var proposed = SuppressionRecoveryPoseCore.Resolve(
                onUsableCover: false, latched, coverEvaluationOwnsProne: false);
            holdUntil = TacticalPoseStabilityCore.RenewHoldUntil(t, holdUntil, pinned);
            if (TacticalPoseStabilityCore.ShouldAccept(latched, proposed, t, holdUntil, pinned))
            {
                if (reachedProne && proposed != TacticalStance.Prone)
                    everRoseAfterProne = true;
                latched = proposed;
                holdUntil = t + TacticalPoseStabilityCore.MinimumHoldSeconds;
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
        var latched = TacticalStance.Prone;
        var holdUntil = 0f;
        var everRoseAfterProne = false;
        var tick = 0;
        for (var t = 0f; t <= 20f; t += 0.5f, tick++)
        {
            const bool lowerPoseStillOwned = false; // ContactUntil has already lapsed.
            var coverOwned = CoverPostureOwnershipCore.CoverPoseOwned(
                hasThreatMemory: true, onUsableCover: true, defensiveHold: false);
            var suppressionBandActive = tick % 2 == 0;
            var proposed = suppressionBandActive
                ? SuppressionRecoveryPoseCore.Resolve(
                    onUsableCover: true, latched, coverEvaluationOwnsProne: coverOwned)
                : TacticalStance.Prone;

            holdUntil = TacticalPoseStabilityCore.RenewHoldUntil(t, holdUntil, lowerPoseStillOwned);
            if (TacticalPoseStabilityCore.ShouldAccept(
                    latched, proposed, t, holdUntil, lowerPoseStillOwned))
            {
                if (proposed != TacticalStance.Prone)
                    everRoseAfterProne = true;
                latched = proposed;
                holdUntil = t + TacticalPoseStabilityCore.MinimumHoldSeconds;
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

    private static void ContactReportReplacementPrefersFresherInformation()
    {
        // A stale, more direct report must not replace a fresher relayed report.
        False(ContactReportStoreCore.ShouldReplace(
                100f, ContactDeliveryKind.Radio, 90f, ContactDeliveryKind.Direct),
            "A stale direct report replaced a fresher radio report.");

        // A fresher report replaces an older one regardless of delivery kind, in
        // both directions.
        True(ContactReportStoreCore.ShouldReplace(
                90f, ContactDeliveryKind.Direct, 100f, ContactDeliveryKind.Radio),
            "A fresher radio report did not replace an older direct sighting.");
        True(ContactReportStoreCore.ShouldReplace(
                90f, ContactDeliveryKind.Radio, 100f, ContactDeliveryKind.Direct),
            "A fresher direct sighting did not replace an older radio report.");

        // Exact same ObservedAt: the more direct kind wins.
        True(ContactReportStoreCore.ShouldReplace(
                100f, ContactDeliveryKind.Radio, 100f, ContactDeliveryKind.Direct),
            "A tied timestamp did not prefer the more direct delivery kind.");
        False(ContactReportStoreCore.ShouldReplace(
                100f, ContactDeliveryKind.Direct, 100f, ContactDeliveryKind.Radio),
            "A tied timestamp let a less direct report replace a more direct one.");

        // Same timestamp and same kind must not churn.
        False(ContactReportStoreCore.ShouldReplace(
                100f, ContactDeliveryKind.Voice, 100f, ContactDeliveryKind.Voice),
            "An identical report replaced itself.");
    }

    private static void ContactConfidenceDecaysLinearlyToZero()
    {
        Near(1f, ContactReportStoreCore.DecayedConfidence(1f, 0f, 30f), 0.001f,
            "Fresh confidence did not start at the initial value.");
        Near(0.5f, ContactReportStoreCore.DecayedConfidence(1f, 15f, 30f), 0.001f,
            "Confidence at the halfway point did not decay linearly.");
        Near(0f, ContactReportStoreCore.DecayedConfidence(1f, 30f, 30f), 0.001f,
            "Confidence at the exact lifetime boundary was not fully decayed.");
        Near(0f, ContactReportStoreCore.DecayedConfidence(1f, 45f, 30f), 0.001f,
            "Confidence beyond its lifetime went negative instead of clamping to zero.");

        // A non-positive lifetime must not divide by zero.
        Finite(ContactReportStoreCore.DecayedConfidence(1f, 5f, 0f),
            "A non-positive lifetime produced a non-finite confidence value.");
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

    private static void IdleSoldiersRemainUnderCommanderOrNativeControl()
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

    private static void AttackBoundsRequireSafetyAndTacticalAuthorization()
    {
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, false, false, false, false, true, 20f, 30f),
            "An attacker left cover without covering fire or reaching the maximum halt.");
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, true, true, true, false, true, 20f, 30f),
            "Direct incoming fire authorized an exposed attack bound.");
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, true, true, false, true, true, 20f, 30f),
            "A pinned attacker was forced out of cover.");
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, true, true, false, false, true, 31f, 30f),
            "An attacker abandoned useful cover before completing the fighting halt.");
        True(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, true, false, false, false, true, 20f, 30f),
            "Covering fire failed to authorize a safe attack bound after the halt.");
        False(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, false, true, false, false, true, 20f, 30f),
            "A maximum-halt deadline pulled an attacker out of useful cover without covering fire.");
        True(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, false, true, false, false, false, 20f, 30f),
            "The maximum safe halt failed to resume an exposed, otherwise stalled attack.");
    }

    private static void DefensiveReinforcementsReachObjectiveBeforeCommanderOwnership()
    {
        var objective = new MapPoint(0f, 0f);
        False(DefensivePositioningCore.ShouldAssumeCommand(
                false, false, false, new MapPoint(135.01f, 0f), objective, 100f, 35f),
            "A fresh defensive reinforcement was claimed before reaching the objective area.");
        True(DefensivePositioningCore.ShouldAssumeCommand(
                false, false, false, new MapPoint(135f, 0f), objective, 100f, 35f),
            "A defensive reinforcement was not claimed at the arrival boundary.");
        True(DefensivePositioningCore.ShouldAssumeCommand(
                false, true, false, new MapPoint(500f, 0f), objective, 100f, 35f),
            "A previously owned defender was released during a temporary displacement.");
        True(DefensivePositioningCore.ShouldAssumeCommand(
                false, false, true, new MapPoint(500f, 0f), objective, 100f, 35f),
            "A defender already occupying a fortification was not preserved.");
        True(DefensivePositioningCore.ShouldAssumeCommand(
                true, false, false, new MapPoint(500f, 0f), objective, 100f, 35f),
            "The defensive arrival gate leaked into an offensive operation.");
    }

    private static void DefensiveFortificationsRemainAssigned()
    {
        True(DefensivePositioningCore.ShouldPreserveFortification(true, true, false),
            "An intact cover position under a defend order was overwritten.");
        True(DefensivePositioningCore.ShouldPreserveFortification(false, false, true),
            "An occupied static weapon was overwritten by a squad-area order.");
        False(DefensivePositioningCore.ShouldPreserveFortification(false, true, false),
            "Maneuver cover without a defend order became permanently sticky.");
        False(DefensivePositioningCore.ShouldPreserveFortification(true, false, false),
            "A defender without a reached fortification skipped its area order.");
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

    private static void EmptyInputIsSafe()
    {
        var plan = CommanderPlannerCore.Plan(new CommanderPlanInput(
            new MapPoint(0f, 0f), true, false, false,
            Array.Empty<CommanderSquadSnapshot>(),
            Array.Empty<CommanderReportSnapshot>(),
            Array.Empty<CommanderAxisCandidate>()));

        Equal(0, plan.Directives.Count, "Empty input emitted directives.");
        False(plan.AttackAuthorized, "Empty input authorized an attack.");
        Equal(null, plan.MainAxisId, "Empty input selected a main axis.");
        Finite(plan.Metrics.StrengthRatio, "Empty input produced a non-finite ratio.");

        var nullPlan = CommanderPlannerCore.Plan(null);
        Equal(0, nullPlan.Directives.Count, "Null input emitted directives.");
    }

    private static void UnsafeSquadsAreFilteredAndRolesAreAllocated()
    {
        var squads = StandardSquads(5).Concat(new[]
        {
            Squad(6, eligible: false),
            Squad(7, player: true),
            Squad(8, scriptLocked: true),
            Squad(9, strength: 4.9f, peak: 10f),
            Squad(10, suppression: 0.601f),
            Squad(11, position: new MapPoint(float.NaN, 0f))
        }).ToArray();

        var plan = Plan(squads, Reports(10f), StandardAxes());
        SequenceEqual(new[] { 1, 2, 3, 4, 5 }, plan.Directives.Select(directive => directive.SquadId),
            "Unsafe squads leaked into the plan.");
        Equal(1, CountRole(plan, CommanderRole.Reserve), "Five squads must retain one reserve.");
        Equal(1, CountRole(plan, CommanderRole.SupportByFire), "Five squads must assign one SBF squad.");
        Equal(1, CountRole(plan, CommanderRole.Flank), "Five squads must assign one flank squad.");
        Equal(2, CountRole(plan, CommanderRole.Assault), "Remaining squads must be assault squads.");
    }

    private static void RoleCountsMeetSmallForceRules()
    {
        for (var count = 1; count <= 20; count++)
        {
            var plan = Plan(StandardSquads(count), Reports(1f), StandardAxes());
            var expectedReserve = count >= 4 ? (int)Math.Ceiling(count * 0.20f) : 0;
            var expectedSupport = count >= 3 ? 1 : 0;
            var expectedFlank = count >= 2 ? 1 : 0;
            var expectedAssault = count - expectedReserve - expectedSupport - expectedFlank;

            Equal(expectedReserve, CountRole(plan, CommanderRole.Reserve), $"Reserve count failed for n={count}.");
            Equal(expectedSupport, CountRole(plan, CommanderRole.SupportByFire), $"SBF count failed for n={count}.");
            Equal(expectedFlank, CountRole(plan, CommanderRole.Flank), $"Flank count failed for n={count}.");
            Equal(expectedAssault, CountRole(plan, CommanderRole.Assault), $"Assault count failed for n={count}.");
        }
    }

    private static void PlanningIsDeterministicAcrossInputOrder()
    {
        var squads = StandardSquads(8);
        var reports = new[]
        {
            Report(7, 7f, age: 2f, confidence: 0.8f),
            Report(3, 4f, age: 1f, confidence: 0.9f),
            Report(7, 99f, age: 5f, confidence: 1f)
        };
        var axes = new[]
        {
            Axis(9, 90f, 0.8f),
            Axis(2, 0f, 0.9f),
            Axis(5, 30f, 0.85f)
        };
        var expected = Canonical(Plan(squads, reports, axes));

        for (var iteration = 0; iteration < 100; iteration++)
        {
            var rotatedSquads = Rotate(squads, iteration);
            var rotatedReports = Rotate(reports, iteration);
            var rotatedAxes = Rotate(axes, iteration);
            var actual = Canonical(Plan(rotatedSquads, rotatedReports, rotatedAxes));
            Equal(expected, actual, $"Plan changed with input order on iteration {iteration}.");
        }
    }

    private static void AxisScoringAndSeparationAreDeterministic()
    {
        var axes = new[]
        {
            Axis(20, 0f, terrain: 0.95f),
            Axis(10, 30f, terrain: 0.94f),
            Axis(30, 60f, terrain: 0.80f),
            Axis(40, 120f, terrain: 0.80f),
            Axis(5, 180f, terrain: 1f, congestion: 0.50f)
        };
        var plan = Plan(StandardSquads(5), Reports(10f), axes);

        Equal(20, plan.MainAxisId, "Highest usable axis score was not selected.");
        Equal(30, plan.FlankAxisId, "Flank did not use the best axis separated by at least 45 degrees.");

        var tied = Plan(StandardSquads(2), Reports(1f), new[]
        {
            Axis(8, 0f, 0.8f),
            Axis(3, 90f, 0.8f)
        });
        Equal(3, tied.MainAxisId, "Axis score ties must use the lower stable ID.");
        Equal(8, tied.FlankAxisId, "The other separated axis must become the flank.");
    }

    private static void FlankHoldsWhenNoSeparatedAxisExists()
    {
        var plan = Plan(StandardSquads(2), Reports(1f), new[]
        {
            Axis(1, 0f, 0.9f),
            Axis(2, 30f, 0.8f)
        });

        True(plan.AttackAuthorized, "A viable main-axis attack should remain authorized.");
        Equal(null, plan.FlankAxisId, "An axis under 45 degrees was incorrectly selected as a flank.");
        var flank = plan.Directives.Single(directive => directive.Role == CommanderRole.Flank);
        Equal(CommanderAction.Hold, flank.Action, "Flank squad attacked without a separated flank axis.");
        Equal(null, flank.AxisId, "Flank squad received the congested main axis.");
    }

    private static void AttackGateHonorsRatioAndSuppressionBoundaries()
    {
        var allowed = Plan(new[]
        {
            Squad(1, strength: 5f, peak: 5f, suppression: 0.34f),
            Squad(2, strength: 4f, peak: 4f, suppression: 0.34f),
            Squad(3, strength: 4f, peak: 4f, suppression: 0.34f)
        }, Reports(10f), StandardAxes());
        Near(1.30f, allowed.Metrics.StrengthRatio, 0.0001f, "Boundary strength ratio was incorrect.");
        True(allowed.AttackAuthorized, "Ratio 1.30 and suppression 0.34 must authorize attack.");

        var weak = Plan(new[]
        {
            Squad(1, strength: 4.9f, peak: 4.9f, suppression: 0.34f),
            Squad(2, strength: 4f, peak: 4f, suppression: 0.34f),
            Squad(3, strength: 4f, peak: 4f, suppression: 0.34f)
        }, Reports(10f), StandardAxes());
        Near(1.29f, weak.Metrics.StrengthRatio, 0.0001f, "Weak strength ratio was incorrect.");
        False(weak.AttackAuthorized, "Ratio 1.29 must block attack.");

        var suppressed = Plan(new[]
        {
            Squad(1, strength: 5f, peak: 5f, suppression: 0.36f),
            Squad(2, strength: 4f, peak: 4f, suppression: 0.36f),
            Squad(3, strength: 4f, peak: 4f, suppression: 0.36f)
        }, Reports(10f), StandardAxes());
        False(suppressed.AttackAuthorized, "Average suppression 0.36 must block attack.");
        True(suppressed.Directives.All(directive => directive.Action == CommanderAction.Hold),
            "Suppression-blocked plan emitted a maneuver action.");

        var defensive = Plan(StandardSquads(3), Reports(1f), StandardAxes(), offensive: false);
        False(defensive.AttackAuthorized, "A non-offensive operation authorized attack.");
    }

    private static void AttackerAggressivenessAdjustsAttackGate()
    {
        var squads = Enumerable.Range(1, 10)
            .Select(id => Squad(id, strength: 4f, peak: 4f, suppression: 0.40f))
            .ToArray();

        var cautious = Plan(squads, Reports(30f), StandardAxes(), aggressiveness: 0.5f);
        False(cautious.AttackAuthorized,
            "A cautious attack posture authorized a marginally supported assault.");

        var aggressive = Plan(squads, Reports(30f), StandardAxes(), aggressiveness: 1.5f);
        True(aggressive.AttackAuthorized,
            "An aggressive attack posture did not release a marginally supported assault.");
        True(aggressive.Metrics.ReserveSquadCount < cautious.Metrics.ReserveSquadCount,
            "An aggressive attack posture did not commit more squads.");
    }

    private static void SmokeBlocksManeuverUntilReady()
    {
        var blocked = Plan(StandardSquads(3), Reports(1f), StandardAxes(), smokeRequired: true, smokeReady: false);
        False(blocked.AttackAuthorized, "Attack began before required smoke was ready.");
        True(blocked.Metrics.SmokeBlocked, "Plan did not identify smoke as the blocking condition.");
        True(blocked.Directives
                .Where(directive => directive.Role is CommanderRole.Assault or CommanderRole.Flank)
                .All(directive => directive.Action == CommanderAction.Prepare),
            "Maneuver squads did not prepare while waiting for smoke.");
        Equal(CommanderAction.Hold,
            blocked.Directives.Single(directive => directive.Role == CommanderRole.SupportByFire).Action,
            "SBF squad should hold its support position while smoke is pending.");

        var ready = Plan(StandardSquads(3), Reports(1f), StandardAxes(), smokeRequired: true, smokeReady: true);
        True(ready.AttackAuthorized, "Ready smoke did not release the assault.");
        True(ready.Directives
                .Where(directive => directive.Role is CommanderRole.Assault or CommanderRole.Flank)
                .All(directive => directive.Action == CommanderAction.Attack),
            "Ready smoke did not release every axis-backed maneuver squad.");
    }

    private static void FireMissionRetargetsAroundFriendlyConcentrations()
    {
        var target = new MapPoint(0f, 0f);
        var concentratedFriendlies = new[]
        {
            new MapPoint(0f, 0f),
            new MapPoint(3f, 0f),
            new MapPoint(-3f, 0f),
            new MapPoint(0f, 3f),
            new MapPoint(0f, -3f)
        };

        var shifted = CommanderPlannerCore.SelectFireMissionAim(
            target, concentratedFriendlies, 55f, 80f);
        True(shifted != null, "A concentrated friendly group prevented every bounded retarget.");
        Equal(5, shifted!.Value.FriendliesAtReportedTarget,
            "The fire mission did not measure the crowded original impact area.");
        Near(65f, shifted.Value.ShiftMeters, 0.001f,
            "The fire mission moved farther than the nearest safe ring required.");
        True(concentratedFriendlies.All(point =>
        {
            var dx = point.X - shifted.Value.Aim.X;
            var dz = point.Z - shifted.Value.Aim.Z;
            return dx * dx + dz * dz > 55f * 55f;
        }), "The relocated fire mission still contained a friendly in its clearance area.");

        var reordered = CommanderPlannerCore.SelectFireMissionAim(
            target, concentratedFriendlies.Reverse().ToArray(), 55f, 80f);
        Equal(shifted.Value.Aim, reordered!.Value.Aim,
            "Friendly enumeration order changed the selected fire-mission aim point.");

        var encircled = new List<MapPoint> { target };
        for (var direction = 0; direction < 16; direction++)
        {
            var angle = direction * (2f * MathF.PI / 16f);
            encircled.Add(new MapPoint(MathF.Cos(angle) * 45f, MathF.Sin(angle) * 45f));
        }

        True(CommanderPlannerCore.SelectFireMissionAim(target, encircled, 55f, 80f) == null,
            "A fully encircled contact produced an unsafe artillery solution.");
    }

    private static void ReportsAreFreshFilteredAndDeduplicated()
    {
        var reports = new[]
        {
            Report(1, 10f, age: 1f, confidence: 0.75f),
            Report(1, 99f, age: 2f, confidence: 1f),
            Report(2, 100f, age: 20.001f, confidence: 1f),
            Report(3, 100f, age: 1f, confidence: 0.249f),
            Report(4, 100f, age: -0.01f, confidence: 1f)
        };
        var plan = Plan(StandardSquads(3), reports, StandardAxes());

        Equal(1, plan.Metrics.FreshReportCount, "Reports were not filtered and deduplicated by target ID.");
        Near(10f, plan.Metrics.EnemyEstimatedPower, 0.0001f, "Newest valid report was not selected.");

        var noReports = Plan(StandardSquads(3), Array.Empty<CommanderReportSnapshot>(), StandardAxes());
        Near(0f, noReports.Metrics.EnemyEstimatedPower, 0.0001f, "No-report plan invented enemy power.");
        Finite(noReports.Metrics.StrengthRatio, "No-report plan produced an infinite ratio.");
    }

    private static void SquadEligibilityHonorsExactBoundaries()
    {
        var squads = new[]
        {
            Squad(1, strength: 5f, peak: 10f, suppression: 0.60f),
            Squad(2, strength: 4.999f, peak: 10f, suppression: 0f),
            Squad(3, strength: 5f, peak: 10f, suppression: 0.601f),
            Squad(4, strength: 5f, peak: 0f, suppression: 0f),
            Squad(5, strength: float.NaN, peak: 10f, suppression: 0f),
            Squad(6, strength: 10f, peak: 10f, suppression: 0f)
        };
        var plan = Plan(squads, Reports(1f), StandardAxes());
        SequenceEqual(new[] { 1, 6 }, plan.Directives.Select(directive => directive.SquadId),
            "Squad strength/suppression boundary filtering failed.");
    }

    private static void BrokenSquadIsRemovedOnReplan()
    {
        var initialSquads = StandardSquads(5);
        var initial = Plan(initialSquads, Reports(1f), StandardAxes());
        var brokenId = initial.Directives.First(directive => directive.Role == CommanderRole.Assault).SquadId;
        var replannedSquads = initialSquads
            .Select(squad => squad.Id == brokenId
                ? squad with { EffectiveStrength = squad.PeakStrength * 0.49f }
                : squad)
            .ToArray();
        var replanned = Plan(replannedSquads, Reports(1f), StandardAxes());

        False(replanned.Directives.Any(directive => directive.SquadId == brokenId),
            "Broken squad retained a commander directive.");
        Equal(4, replanned.Directives.Count, "Replan did not retain all other operational squads.");
        Equal(1, CountRole(replanned, CommanderRole.Reserve), "Replan lost the required reserve.");
        Equal(1, CountRole(replanned, CommanderRole.Assault), "Replan did not preserve an assault element.");
    }

    private static void InvalidNumericDataFailsSafe()
    {
        var invalidObjective = CommanderPlannerCore.Plan(new CommanderPlanInput(
            new MapPoint(float.NaN, 0f), true, false, false,
            StandardSquads(3), Reports(1f), StandardAxes()));
        Equal(0, invalidObjective.Directives.Count, "Invalid objective did not produce an empty safe plan.");

        var invalidAxes = Plan(StandardSquads(3), Reports(1f), new[]
        {
            Axis(1, float.NaN, 1f),
            Axis(2, 90f, float.PositiveInfinity)
        });
        Equal(null, invalidAxes.MainAxisId, "Invalid axis was selected.");
        False(invalidAxes.AttackAuthorized, "Attack was authorized without a usable axis.");
        True(invalidAxes.Directives.All(directive => directive.Action == CommanderAction.Hold),
            "Invalid axes emitted a maneuver action.");
        Finite(invalidAxes.Metrics.StrengthRatio, "Invalid-axis plan produced non-finite metrics.");
    }

    private static void AntiTankTaskSelectsNearestCapableSquad()
    {
        var squads = new[]
        {
            Squad(1, position: new MapPoint(0f, 0f)),
            Squad(2, position: new MapPoint(95f, 0f)),
            Squad(3, position: new MapPoint(110f, 0f))
        };
        var reports = new[]
        {
            Report(40, 8f, type: CommanderContactType.GroundVehicle,
                position: new MapPoint(125f, 0f))
        };

        var task = CommanderPlannerCore.SelectAntiTankTask(squads, new[] { 1, 3 }, reports);

        True(task.HasValue, "A fresh tank report did not produce an AT task.");
        Equal(3, task!.Value.SquadId, "The nearest AT-capable squad was not selected.");
        Equal(40, task.Value.TargetId, "The AT task selected the wrong tank report.");
        Equal(CommanderAntiTankAction.Ambush, task.Value.Action,
            "A tank already inside the minimum hunt distance should be ambushed, not chased.");
    }

    private static void AntiTankTaskDefaultsToAmbushAndBoundsHunting()
    {
        var squads = new[] { Squad(1, position: new MapPoint(0f, 0f)) };
        var ids = new[] { 1 };

        var oldTask = CommanderPlannerCore.SelectAntiTankTask(squads, ids, new[]
        {
            Report(10, 8f, age: 6.001f, confidence: 1f,
                type: CommanderContactType.GroundVehicle, position: new MapPoint(100f, 0f))
        });
        Equal(CommanderAntiTankAction.Ambush, oldTask!.Value.Action,
            "An aging report incorrectly authorized a hunt.");

        var boundaryHunt = CommanderPlannerCore.SelectAntiTankTask(squads, ids, new[]
        {
            Report(10, 8f, age: 6f, confidence: 0.70f,
                type: CommanderContactType.GroundVehicle, position: new MapPoint(130f, 0f))
        });
        Equal(CommanderAntiTankAction.Hunt, boundaryHunt!.Value.Action,
            "The exact fresh/confident hunt boundary was rejected.");

        var outsideHunt = CommanderPlannerCore.SelectAntiTankTask(squads, ids, new[]
        {
            Report(10, 8f, age: 1f, confidence: 1f,
                type: CommanderContactType.GroundVehicle, position: new MapPoint(130.01f, 0f))
        });
        Equal(CommanderAntiTankAction.Ambush, outsideHunt!.Value.Action,
            "A squad was allowed to hunt beyond the short pursuit radius.");
    }

    private static void AntiTankTaskRejectsUnsafeInputs()
    {
        var healthy = new[] { Squad(1, position: new MapPoint(0f, 0f)) };
        var tank = Report(10, 8f, type: CommanderContactType.GroundVehicle,
            position: new MapPoint(100f, 0f));

        Equal(null, CommanderPlannerCore.SelectAntiTankTask(healthy, Array.Empty<int>(), new[] { tank }),
            "A non-AT squad received an AT task.");
        Equal(null, CommanderPlannerCore.SelectAntiTankTask(healthy, new[] { 1 }, new[]
        {
            tank with { Type = CommanderContactType.Infantry }
        }), "An infantry report produced an AT task.");
        Equal(null, CommanderPlannerCore.SelectAntiTankTask(healthy, new[] { 1 }, new[]
        {
            tank with { AgeSeconds = 12.001f }
        }), "A stale tank report produced an AT task.");
        Equal(null, CommanderPlannerCore.SelectAntiTankTask(healthy, new[] { 1 }, new[]
        {
            tank with { Confidence = 0.349f }
        }), "A low-confidence tank report produced an AT task.");
        Equal(null, CommanderPlannerCore.SelectAntiTankTask(
            new[] { Squad(1, strength: 4.9f, peak: 10f) }, new[] { 1 }, new[] { tank }),
            "A depleted AT squad received a tank task.");
        Equal(null, CommanderPlannerCore.SelectAntiTankTask(
            new[] { Squad(1, suppression: 0.601f) }, new[] { 1 }, new[] { tank }),
            "An over-suppressed AT squad received a tank task.");
        Equal(null, CommanderPlannerCore.SelectAntiTankTask(healthy, new[] { 1 }, new[]
        {
            tank with { Position = new MapPoint(300.01f, 0f) }
        }), "An AT squad was sent on a map-wide response.");
    }

    private static void AntiTankTaskIsDeterministicAndAvoidsMultiTankHunts()
    {
        var squads = new[]
        {
            Squad(2, position: new MapPoint(0f, 0f)),
            Squad(1, position: new MapPoint(0f, 0f))
        };
        var reports = new[]
        {
            Report(20, 8f, type: CommanderContactType.GroundVehicle,
                position: new MapPoint(100f, 0f)),
            Report(10, 8f, type: CommanderContactType.GroundVehicle,
                position: new MapPoint(100f, 0f))
        };
        var expected = CommanderPlannerCore.SelectAntiTankTask(squads, new[] { 2, 1 }, reports);

        True(expected.HasValue, "Equal AT candidates did not produce a task.");
        Equal(1, expected!.Value.SquadId, "Equal squad candidates did not use the lower stable ID.");
        Equal(10, expected.Value.TargetId, "Equal tank reports did not use the lower stable ID.");
        Equal(CommanderAntiTankAction.Ambush, expected.Value.Action,
            "A nearby second tank should prevent a hunt.");

        for (var iteration = 0; iteration < 20; iteration++)
        {
            var actual = CommanderPlannerCore.SelectAntiTankTask(
                Rotate(squads, iteration),
                Rotate(new[] { 2, 1 }, iteration),
                Rotate(reports, iteration));
            Equal(expected, actual, $"AT task changed with input order on iteration {iteration}.");
        }
    }

    private static void PostureFollowsObjectiveOwnership()
    {
        Equal(StrategicPosture.Attack, StrategicPostureCore.FromObjectiveOwnership(false),
            "A faction attacking an enemy-held objective was put in defend posture.");
        Equal(StrategicPosture.Defend, StrategicPostureCore.FromObjectiveOwnership(true),
            "A faction holding the objective was not put in defend posture.");
    }

    private static void ContestedOwnershipRetainsOneDefendingSide()
    {
        Equal(StrategicPosture.Defend,
            StrategicPostureCore.Resolve(false, false, StrategicPosture.Defend,
                StrategicPosture.Attack),
            "A contested objective converted its established defender into an attacker.");
        Equal(StrategicPosture.Attack,
            StrategicPostureCore.Resolve(false, false, StrategicPosture.Attack,
                StrategicPosture.Defend),
            "A contested objective cancelled an established assault.");
        Equal(StrategicPosture.Defend,
            StrategicPostureCore.Resolve(false, false, null, StrategicPosture.Defend),
            "An initially ambiguous defender side did not use defensive doctrine.");
        Equal(StrategicPosture.Attack,
            StrategicPostureCore.Resolve(true, false, StrategicPosture.Defend,
                StrategicPosture.Defend),
            "Known enemy ownership failed to transition a defender into attack posture.");
    }

    private static void AttackerAndDefenderPlannerCoresSelectTheirPostures()
    {
        var input = new CommanderPlanInput(
            new MapPoint(100f, 100f),
            false,
            false,
            true,
            StandardSquads(5),
            Reports(5f),
            StandardAxes());
        var attack = AttackerPlannerCore.Plan(input);
        var defend = DefenderPlannerCore.Plan(input with { OffensiveOperation = true });

        True(attack.AttackAuthorized, "The attacker planner did not evaluate the assault gate.");
        False(defend.AttackAuthorized, "The defender planner opened an assault gate.");
        True(defend.Directives.All(directive => directive.Action == CommanderAction.Hold),
            "The defender planner emitted a maneuver action.");
        Equal(null, defend.MainAxisId, "The defender planner retained an attacker main axis.");
        Equal(null, defend.FlankAxisId, "The defender planner retained an attacker flank axis.");
        var positions = input.Squads!.ToDictionary(squad => squad.Id, squad => squad.Position);
        True(defend.Directives.All(directive =>
                directive.Destination == positions[directive.SquadId]),
            "The defender planner sent squads to the exact objective center.");
    }

    private static void AttackersLeaveACapturedObjectiveForAFarUnsecuredOne()
    {
        var candidates = new[]
        {
            new ObjectiveCandidate(1, 5f, true),
            new ObjectiveCandidate(2, 500f, false)
        };
        Equal(2, ObjectiveSelectionCore.Select(candidates, true, 1),
            "An attacker standing on a captured objective did not push on to the far uncaptured one.");
    }

    private static void AttackersWithEveryObjectiveSecuredDefendTheBestOne()
    {
        var candidates = new[]
        {
            new ObjectiveCandidate(1, 50f, true),
            new ObjectiveCandidate(2, 10f, true)
        };
        Equal(2, ObjectiveSelectionCore.Select(candidates, true, 1),
            "An attacker with every objective already captured did not fall back to defending the best one.");
    }

    private static void AttackerStickinessKeepsTheCurrentUnsecuredObjective()
    {
        var candidates = new[]
        {
            new ObjectiveCandidate(1, 50f, false),
            new ObjectiveCandidate(2, 400f, false)
        };
        Equal(2, ObjectiveSelectionCore.Select(candidates, true, 2),
            "A nearer unsecured objective pulled the attacker off its current uncaptured objective.");
    }

    private static void AttackerStickinessReleasesOnceItsObjectiveIsSecured()
    {
        var candidates = new[]
        {
            new ObjectiveCandidate(2, 400f, true),
            new ObjectiveCandidate(3, 50f, false)
        };
        Equal(3, ObjectiveSelectionCore.Select(candidates, true, 2),
            "The attacker did not move on once its current objective became friendly-secured.");
    }

    private static void DefenderObjectiveSelectionIsUnchangedByThePenalty()
    {
        // Effective score = distance + (140 if friendly-secured). A secured objective
        // only keeps winning while the unsecured alternative is more than ~140m farther
        // away than it is; this replicates the original inline scoring untouched.
        var unsecuredFarEnough = new[]
        {
            new ObjectiveCandidate(1, 10f, true),
            new ObjectiveCandidate(2, 200f, false)
        };
        Equal(1, ObjectiveSelectionCore.Select(unsecuredFarEnough, false, null),
            "A defender abandoned a secured objective for an unsecured one more than 140m farther away.");

        var unsecuredWithinGap = new[]
        {
            new ObjectiveCandidate(1, 10f, true),
            new ObjectiveCandidate(2, 100f, false)
        };
        Equal(2, ObjectiveSelectionCore.Select(unsecuredWithinGap, false, null),
            "A defender clung to a secured objective even though the unsecured alternative was within the 140m penalty gap.");
    }

    private static void ObjectiveSelectionTiesAreBrokenByLowerId()
    {
        var candidates = new[]
        {
            new ObjectiveCandidate(5, 20f, false),
            new ObjectiveCandidate(2, 20f, false)
        };
        Equal(2, ObjectiveSelectionCore.Select(candidates, false, null),
            "An objective-selection tie was not broken in favor of the lower Id.");
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

    private static void CommanderOrdersChangePhaseInsteadOfChurningDestinations()
    {
        var stationary = new StableCommanderOrder(
            7, CommanderRole.SupportByFire, CommanderAction.Hold);
        False(CommanderOrderStabilityCore.ShouldReplace(
                stationary,
                new StableCommanderOrder(
                    7, CommanderRole.SupportByFire, CommanderAction.Prepare)),
            "A stationary planner heartbeat replaced an accepted support position.");
        True(CommanderOrderStabilityCore.ShouldReplace(
                stationary,
                new StableCommanderOrder(
                    7, CommanderRole.SupportByFire, CommanderAction.Attack)),
            "A real hold-to-attack phase transition failed to replace the old order.");
        True(CommanderOrderStabilityCore.ShouldReplace(
                stationary,
                new StableCommanderOrder(
                    8, CommanderRole.SupportByFire, CommanderAction.Hold)),
            "An objective transition retained the previous objective's order.");
        True(CommanderOrderStabilityCore.ShouldReplace(
                stationary,
                new StableCommanderOrder(
                    7, CommanderRole.Reserve, CommanderAction.Hold)),
            "A deliberate role transition failed to replace the old order.");
        False(CommanderOrderStabilityCore.ShouldReplace(
                stationary,
                new StableCommanderOrder(
                    7, CommanderRole.Reserve, CommanderAction.Hold),
                ignoreRole: true),
            "A defensive role rebalance churned an otherwise unchanged hold order.");
    }

    private static void ObjectiveFormationDistributesSquadsAwayFromTheCenter()
    {
        var center = new MapPoint(100f, 200f);
        var defensive = Enumerable.Range(0, 6)
            .Select(slot => ObjectiveFormationCore.DefensiveSector(center, 40f, slot))
            .ToArray();
        var entries = Enumerable.Range(0, 6)
            .Select(slot => ObjectiveFormationCore.AttackEntry(center, 40f, slot))
            .ToArray();

        Equal(defensive.Length, defensive.Distinct().Count(),
            "Two defensive squads received the same objective-sector destination.");
        Equal(entries.Length, entries.Distinct().Count(),
            "Two attacking squads received the same objective-entry destination.");
        True(defensive.All(point => point != center),
            "A defender was ordered directly to the objective center.");
        True(entries.All(point => point != center),
            "An attacker was ordered directly to the objective center.");
        True(defensive.All(point => Distance(point, center) <= 28.01f),
            "A defensive sector escaped the defended objective area.");
        True(entries.All(point => Distance(point, center) <= 20.01f),
            "An attack entry escaped its bounded objective approach.");
    }

    private static void CommandLeasesAreStableAndRejectStaleWork()
    {
        var registry = new CommandLeaseRegistryCore();
        var request = LeaseRequest(11, "commander", CommandAuthority.CommanderIntent, 2);
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
                LeaseRequest(30, "vehicle", CommandAuthority.CommanderIntent, 1) with
                {
                    Channel = CommandChannel.VehicleOrders,
                    ValidUntil = 20f
                },
                5f, out _),
            "Vehicle debug lease setup failed.");
        True(registry.TryAcquire(
                LeaseRequest(10, "squad", CommandAuthority.CommanderIntent, 1) with
                {
                    ValidUntil = 20f
                },
                5f, out _),
            "Squad debug lease setup failed.");
        True(registry.TryAcquire(
                LeaseRequest(5, "expired", CommandAuthority.CommanderIntent, 1) with
                {
                    Channel = CommandChannel.AircraftOrders,
                    ValidUntil = 6f
                },
                5f, out _),
            "Expired debug lease setup failed.");

        var snapshot = new List<CommandLease>();
        registry.CopyActive(10f, snapshot);
        Equal(2, snapshot.Count, "The visual snapshot retained an expired command lease.");
        Equal(CommandChannel.SquadOrders, snapshot[0].Key.Channel,
            "The visual snapshot did not use deterministic channel ordering.");
        Equal(CommandChannel.VehicleOrders, snapshot[1].Key.Channel,
            "The visual snapshot did not retain the active vehicle lease.");
        Equal(2, registry.Count, "Snapshot cleanup did not prune the expired registry entry.");
    }

    private static void DefensiveOrdersIgnorePlannerHeartbeatAndRoleChurn()
    {
        var existing = new StableDefensiveOrder(7, new MapPoint(100f, 100f), 42f);
        False(DefensiveOrderStabilityCore.ShouldReplace(
                existing,
                new StableDefensiveOrder(7, new MapPoint(100f, 100f), 42f)),
            "An identical defensive planning heartbeat rewrote the squad order.");
        False(DefensiveOrderStabilityCore.ShouldReplace(
                existing,
                new StableDefensiveOrder(7, new MapPoint(107f, 100f), 45f)),
            "Small planner drift rewrote a settled defensive squad order.");
        True(DefensiveOrderStabilityCore.ShouldReplace(
                existing,
                new StableDefensiveOrder(8, new MapPoint(100f, 100f), 42f)),
            "An objective change failed to replace the old defensive order.");
        True(DefensiveOrderStabilityCore.ShouldReplace(
                existing,
                new StableDefensiveOrder(7, new MapPoint(111f, 100f), 42f)),
            "A material defensive-area change failed to replace the old order.");
    }

    private static void ExternalOwnershipPreemptsAndLatches()
    {
        var registry = new CommandLeaseRegistryCore();
        True(registry.TryAcquire(LeaseRequest(20, "commander", CommandAuthority.CommanderIntent, 1),
            2f, out _), "Commander setup lease failed.");
        True(registry.TryAcquire(LeaseRequest(20, "lua", CommandAuthority.PlayerOrScript, 1),
            2f, out _), "Lua ownership did not preempt commander ownership.");
        False(registry.TryAcquire(LeaseRequest(20, "commander", CommandAuthority.CommanderIntent, 2),
            3f, out _), "Commander reacquired a channel while Lua ownership was still active.");
        True(registry.Release(CommandChannel.SquadOrders, 20, "lua"),
            "External ownership could not be explicitly ended.");
        True(registry.TryAcquire(LeaseRequest(20, "commander", CommandAuthority.CommanderIntent, 2),
            3f, out _), "Commander did not reacquire after external ownership ended.");
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
        bool tankThreat = false,
        bool hasCommanderIntent = false,
        MapPoint commanderIntentDestination = default,
        bool mounted = false)
    {
        return new SoldierTacticalSnapshot(
            1, 1, 1, StrategicPosture.Attack, playerLed, scriptOwned, true, mounted,
            suppressed, needsReloadSafety, lethalHazard, position, threatPosition,
            hazardPosition, contactMovement, autonomous, hasPlayerHoldOrder,
            hasProtectedAssignment, tankThreat, hasCommanderIntent, commanderIntentDestination);
    }

    private static void ExternalSquadWithoutPlayerHoldCoverEmitsOnlyNativeAndExternal()
    {
        var snapshot = ProposalSnapshot(playerLed: true, hasPlayerHoldOrder: false);
        var destination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(snapshot, new TacticalPolicyOptions(true, true), destination);

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
        ProposalGenerationCore.Collect(holding, new TacticalPolicyOptions(true, true), destination);
        var holdProposal = destination.Single(p => p.Source == ProposalSource.PlayerHold);
        Equal(TacticalAction.Hold, holdProposal.Action,
            "An uncommitted player-hold cover move issued Move instead of Hold.");
        Equal(position, holdProposal.Destination,
            "The player-hold proposal did not target the squad's hold position.");

        var moving = holding with { ContactMovement = holdSensor with { HasCommittedCoverMove = true } };
        destination.Clear();
        ProposalGenerationCore.Collect(moving, new TacticalPolicyOptions(true, true), destination);
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
        var options = new TacticalPolicyOptions(true, true);

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
        var options = new TacticalPolicyOptions(true, true);

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

    private static void ContactRequiresPolicyEnabledAndNoTankThreatOtherwiseTankFearWins()
    {
        var contactSensor = new ContactMovementSensor(
            HasActionableContact: true, HasRecentContact: false, HasCommittedCoverMove: false,
            HasStableCoverHold: false, HasTimedCoverHold: false, CanClaimReachedCover: false,
            HasEngagementHold: false);
        var threat = new MapPoint(30f, 40f);
        var contactAllowed = ProposalSnapshot(contactMovement: contactSensor, threatPosition: threat);

        var allowedDestination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(contactAllowed, new TacticalPolicyOptions(true, true), allowedDestination);
        True(allowedDestination.Any(p => p.Source == ProposalSource.Contact),
            "Contact response did not fire when policy was enabled, there was no tank threat, " +
            "and the sensor demanded local control.");

        var policyDisabledDestination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(
            contactAllowed, new TacticalPolicyOptions(false, true), policyDisabledDestination);
        False(policyDisabledDestination.Any(p => p.Source == ProposalSource.Contact),
            "Contact response fired even though ContactResponseEnabled was false.");

        var tankThreatened = contactAllowed with { TankThreat = true };
        var tankDestination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(tankThreatened, new TacticalPolicyOptions(true, true), tankDestination);
        False(tankDestination.Any(p => p.Source == ProposalSource.Contact),
            "Contact response fired even though a tank threat was present.");
        var tankFear = tankDestination.Single(p => p.Source == ProposalSource.TankFear);
        Equal(TacticalChannel.Movement, tankFear.Channel, "Tank fear did not target the movement channel.");
        Equal(TacticalAction.Move, tankFear.Action, "Tank fear did not command a move.");
        Equal(CommandAuthority.ImmediateCombat, tankFear.Priority,
            "Tank fear did not use immediate-combat authority.");
        Equal(threat, tankFear.Destination, "Tank fear did not target the sensed threat position.");
    }

    private static void MountedCrewNeverReceivesATankFearProposal()
    {
        var snapshot = ProposalSnapshot(tankThreat: true, mounted: true);
        var destination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(snapshot, new TacticalPolicyOptions(true, true), destination);
        False(destination.Any(p => p.Source == ProposalSource.TankFear),
            "A mounted crewman received a tank-fear proposal that would pull him off his own vehicle.");

        var dismounted = snapshot with { Mounted = false };
        var dismountedDestination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(dismounted, new TacticalPolicyOptions(true, true), dismountedDestination);
        True(dismountedDestination.Any(p => p.Source == ProposalSource.TankFear),
            "A dismounted soldier under tank threat did not receive a tank-fear proposal.");
    }

    private static void ArmorRoleAllocationIsStickyAndOnlyRebuildsOnDiscreteTriggers()
    {
        // Three tanks: the 20% top-up must reserve exactly the weakest one.
        var threeTanks = new[]
        {
            new ArmorTankState(1, 0.9f, 0.10f, 8f),
            new ArmorTankState(2, 0.9f, 0.10f, 6f),
            new ArmorTankState(3, 0.9f, 0.10f, 4f)
        };
        var built = ArmorRoleAllocationCore.Allocate(
            threeTanks, ArmorRoleAllocationState.Empty, mainAxisUsable: true, flankAxisUsable: true);
        Equal(3, built.Roles.Count, "The initial allocation did not assign a role to every tank.");
        Equal(1, built.Roles.Count(pair => pair.Value == ArmorRoleAssignment.Reserve),
            "A force of three tanks did not reserve exactly its 20% top-up.");
        Equal(ArmorRoleAssignment.Reserve, built.Roles[3],
            "The reserve top-up did not pick the weakest tank by EffectivePower.");

        // A suppression wobble that crosses a rounded 0.1 boundary but stays clear
        // of the entry/exit thresholds must not touch any role.
        var wobbled = new[]
        {
            threeTanks[0] with { Suppression = 0.14f },
            threeTanks[1] with { Suppression = 0.16f },
            threeTanks[2] with { Suppression = 0.11f }
        };
        var afterWobble = ArmorRoleAllocationCore.Allocate(
            wobbled, built, mainAxisUsable: true, flankAxisUsable: true);
        foreach (var pair in built.Roles)
        {
            Equal(pair.Value, afterWobble.Roles[pair.Key],
                $"Tank {pair.Key} changed role from a suppression wobble that crossed no threshold.");
        }

        // Committed roles must not reorder just because the power ordering flips,
        // as long as no tank's reserve eligibility, the tank set, axis usability,
        // or the top-up count changed.
        var powerFlipped = new[]
        {
            wobbled[0] with { EffectivePower = 1f },
            wobbled[1] with { EffectivePower = 2f },
            wobbled[2] with { EffectivePower = 20f }
        };
        var afterPowerFlip = ArmorRoleAllocationCore.Allocate(
            powerFlipped, afterWobble, mainAxisUsable: true, flankAxisUsable: true);
        foreach (var pair in afterWobble.Roles)
        {
            Equal(pair.Value, afterPowerFlip.Roles[pair.Key],
                $"Tank {pair.Key} changed role from an EffectivePower reordering alone.");
        }

        // Damage/suppression hysteresis, isolated from the top-up mechanic with a
        // two-tank force (below the >=3 top-up threshold) so EffectivePower never
        // drives a role change here.
        var twoTanks = new[]
        {
            new ArmorTankState(10, 0.9f, 0.10f, 8f),
            new ArmorTankState(11, 0.9f, 0.10f, 6f)
        };
        var twoBuilt = ArmorRoleAllocationCore.Allocate(
            twoTanks, ArmorRoleAllocationState.Empty, mainAxisUsable: true, flankAxisUsable: true);
        False(twoBuilt.Roles.Values.Contains(ArmorRoleAssignment.Reserve),
            "A two-tank force reserved a tank below the >=3 top-up threshold.");

        var damaged = new[] { twoTanks[0] with { HullFraction = 0.30f }, twoTanks[1] };
        var afterDamage = ArmorRoleAllocationCore.Allocate(
            damaged, twoBuilt, mainAxisUsable: true, flankAxisUsable: true);
        Equal(ArmorRoleAssignment.Reserve, afterDamage.Roles[10],
            "A tank below the hull-fraction reserve entry threshold was not sent to Reserve.");

        // Recovering just past the entry threshold, but short of the tighter exit
        // threshold, must not bring the tank back to a committed role.
        var partiallyRecovered = new[] { damaged[0] with { HullFraction = 0.50f }, damaged[1] };
        var afterPartialRecovery = ArmorRoleAllocationCore.Allocate(
            partiallyRecovered, afterDamage, mainAxisUsable: true, flankAxisUsable: true);
        Equal(ArmorRoleAssignment.Reserve, afterPartialRecovery.Roles[10],
            "A damage-reserved tank returned to a committed role before crossing the recovery threshold.");

        var fullyRecovered = new[]
        {
            partiallyRecovered[0] with { HullFraction = 0.60f, Suppression = 0.10f },
            partiallyRecovered[1]
        };
        var afterFullRecovery = ArmorRoleAllocationCore.Allocate(
            fullyRecovered, afterPartialRecovery, mainAxisUsable: true, flankAxisUsable: true);
        True(afterFullRecovery.Roles[10] != ArmorRoleAssignment.Reserve,
            "A tank that cleared both recovery thresholds never left Reserve.");
    }

    private static void CommanderIntentProposesMoveToTheAcceptedDestination()
    {
        var intent = new MapPoint(77f, -12f);
        var snapshot = ProposalSnapshot(hasCommanderIntent: true, commanderIntentDestination: intent);
        var destination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(snapshot, new TacticalPolicyOptions(true, true), destination);
        var commander = destination.Single(p => p.Source == ProposalSource.Commander);
        Equal(TacticalChannel.Movement, commander.Channel, "Commander intent did not target the movement channel.");
        Equal(TacticalAction.Move, commander.Action, "Commander intent did not command a move.");
        Equal(CommandAuthority.CommanderIntent, commander.Priority, "Commander intent used the wrong authority.");
        Equal(intent, commander.Destination, "Commander intent did not carry the accepted squad destination.");

        var withoutIntent = snapshot with { HasCommanderIntent = false };
        var withoutDestination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(withoutIntent, new TacticalPolicyOptions(true, true), withoutDestination);
        False(withoutDestination.Any(p => p.Source == ProposalSource.Commander),
            "A commander proposal appeared without an accepted commander intent.");
    }

    private static void ReloadSafetyAddsProneAndFireInhibitionAlongsideTheHold()
    {
        var snapshot = ProposalSnapshot(needsReloadSafety: true);
        var destination = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(snapshot, new TacticalPolicyOptions(true, true), destination);

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
                CommandAuthority.CommanderIntent,
                ProposalSource.Commander,
                proposalContext,
                "advance")
        });

        var withoutExecutor = MovementDebugProjectionCore.Project(resolution, false, default);
        Equal(ProposalSource.Commander, withoutExecutor.Source,
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

    private static void SupportRequestsAreDeduplicatedByObjectiveRevision()
    {
        var broker = new SupportRequestBrokerCore();
        True(broker.TryAccept(1, 2, 99), "The first support request was rejected.");
        False(broker.TryAccept(1, 2, 99), "A duplicate support request was accepted.");
        True(broker.TryAccept(2, 2, 99), "A new objective revision inherited stale deduplication.");
        broker.AdvanceRevision(2);
        False(broker.TryAccept(2, 2, 99), "Revision cleanup lost the current deduplication key.");
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

    private static CommanderPlan Plan(
        IReadOnlyList<CommanderSquadSnapshot> squads,
        IReadOnlyList<CommanderReportSnapshot> reports,
        IReadOnlyList<CommanderAxisCandidate> axes,
        bool offensive = true,
        bool smokeRequired = false,
        bool smokeReady = false,
        float aggressiveness = 1f)
    {
        return CommanderPlannerCore.Plan(new CommanderPlanInput(
            new MapPoint(100f, 100f), offensive, smokeRequired, smokeReady, squads, reports, axes,
            aggressiveness));
    }

    private static CommanderSquadSnapshot[] StandardSquads(int count)
    {
        return Enumerable.Range(1, count)
            .Select(id => Squad(id, position: new MapPoint(id * 5f, id * 2f)))
            .ToArray();
    }

    private static CommanderSquadSnapshot Squad(
        int id,
        float strength = 10f,
        float peak = 10f,
        float suppression = 0.10f,
        bool eligible = true,
        bool player = false,
        bool scriptLocked = false,
        MapPoint? position = null)
    {
        return new CommanderSquadSnapshot(
            id,
            position ?? new MapPoint(id, id),
            strength,
            peak,
            suppression,
            eligible,
            player,
            scriptLocked);
    }

    private static CommanderReportSnapshot[] Reports(float power)
        => new[] { Report(1, power) };

    private static CommanderReportSnapshot Report(
        int targetId,
        float power,
        float age = 1f,
        float confidence = 1f,
        CommanderContactType type = CommanderContactType.Infantry,
        MapPoint? position = null)
    {
        return new CommanderReportSnapshot(
            targetId,
            position ?? new MapPoint(100f + targetId, 100f),
            type,
            age,
            confidence,
            power);
    }

    private static CommanderAxisCandidate[] StandardAxes()
        => new[] { Axis(1, 0f, 0.9f), Axis(2, 90f, 0.8f) };

    private static CommanderAxisCandidate Axis(
        int id,
        float bearing,
        float terrain,
        float congestion = 0f,
        float exposure = 0f)
    {
        return new CommanderAxisCandidate(
            id,
            new MapPoint(id * 10f, id * 3f),
            bearing,
            terrain,
            congestion,
            exposure);
    }

    private static T[] Rotate<T>(IReadOnlyList<T> source, int amount)
    {
        if (source.Count == 0)
            return Array.Empty<T>();
        var offset = amount % source.Count;
        return source.Skip(offset).Concat(source.Take(offset)).ToArray();
    }

    private static int CountRole(CommanderPlan plan, CommanderRole role)
        => plan.Directives.Count(directive => directive.Role == role);

    private static string Canonical(CommanderPlan plan)
    {
        var culture = CultureInfo.InvariantCulture;
        var metrics = plan.Metrics;
        return string.Join("|",
            plan.MainAxisId?.ToString(culture) ?? "-",
            plan.FlankAxisId?.ToString(culture) ?? "-",
            plan.AttackAuthorized,
            metrics.EligibleSquadCount,
            metrics.ReserveSquadCount,
            metrics.FreshReportCount,
            metrics.TotalEffectiveStrength.ToString("R", culture),
            metrics.CommittedEffectiveStrength.ToString("R", culture),
            metrics.EnemyEstimatedPower.ToString("R", culture),
            metrics.StrengthRatio.ToString("R", culture),
            metrics.AverageSuppression.ToString("R", culture),
            metrics.SmokeBlocked,
            string.Join(";", plan.Directives.Select(directive =>
                $"{directive.SquadId},{directive.Role},{directive.Action},{directive.AxisId?.ToString(culture) ?? "-"}," +
                $"{directive.Destination.X.ToString("R", culture)},{directive.Destination.Z.ToString("R", culture)}")));
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
