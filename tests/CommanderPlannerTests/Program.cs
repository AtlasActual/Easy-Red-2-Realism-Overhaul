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
            (nameof(WalkingInPlaceTriggersAQuietRecoveryHold), WalkingInPlaceTriggersAQuietRecoveryHold),
            (nameof(RealMovementAndPathChangesResetTheStallWatch), RealMovementAndPathChangesResetTheStallWatch),
            (nameof(TransportDismountsBeforeTakingFire), TransportDismountsBeforeTakingFire),
            (nameof(PostureFollowsObjectiveOwnership), PostureFollowsObjectiveOwnership),
            (nameof(AttackerAndDefenderPlannerCoresSelectTheirPostures), AttackerAndDefenderPlannerCoresSelectTheirPostures),
            (nameof(CommandLeasesAreStableAndRejectStaleWork), CommandLeasesAreStableAndRejectStaleWork),
            (nameof(DefensiveOrdersIgnorePlannerHeartbeatAndRoleChurn), DefensiveOrdersIgnorePlannerHeartbeatAndRoleChurn),
            (nameof(ExternalOwnershipPreemptsAndLatches), ExternalOwnershipPreemptsAndLatches),
            (nameof(TacticalArbitrationUsesOneDeterministicWinnerPerChannel), TacticalArbitrationUsesOneDeterministicWinnerPerChannel),
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
            (nameof(CoverSamplingKeepsWorkBoundedAndIncludesDepth), CoverSamplingKeepsWorkBoundedAndIncludesDepth)
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

        var strongCoverWithoutFireLine = protectedDestinationAcrossOpenGround;
        var weakCoverWithFireLine = protectedDestinationAcrossOpenGround with
        {
            DistanceSqr = 100f,
            AssignedPoseCanFire = true,
            StandingCanFire = true,
            PrimaryProtectionFraction = 0.75f
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
        var weakerCoverWithFireLine = new CoverScoreInput(
            64f, 0f, true, 0, true, true, 1f, 0.2f,
            PrimaryProtectionFraction: 0.75f,
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
            true, false, false, false, new MapPoint(0f, 0f), new MapPoint(10f, 0f));
        var proposals = new[]
        {
            new TacticalProposal(TacticalChannel.Movement, TacticalAction.Move,
                CommandAuthority.ImmediateCombat, "contact", new MapPoint(2f, 0f), "bound"),
            new TacticalProposal(TacticalChannel.Movement, TacticalAction.Hold,
                CommandAuthority.ProtectedFortification, "fortification", new MapPoint(1f, 0f), "slot"),
            new TacticalProposal(TacticalChannel.Pose, TacticalAction.Crouch,
                CommandAuthority.CriticalSuppression, "suppression", default, "duck"),
            new TacticalProposal(TacticalChannel.Pose, TacticalAction.Stand,
                CommandAuthority.ImmediateCombat, "contact", default, "fire")
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
            false, false, false, true, default, default);
        var resolution = TacticalArbitrationCore.Resolve(snapshot, new[]
        {
            new TacticalProposal(TacticalChannel.Movement, TacticalAction.Move,
                CommandAuthority.ProtectedFortification, "static-weapon", new MapPoint(10f, 10f), "resume"),
            new TacticalProposal(TacticalChannel.Movement, TacticalAction.Move,
                CommandAuthority.LethalEmergency, "hazard", new MapPoint(-5f, 0f), "temporary")
        });
        Equal("hazard", resolution.Winners[TacticalChannel.Movement].Source,
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
        bool smokeReady = false)
    {
        return CommanderPlannerCore.Plan(new CommanderPlanInput(
            new MapPoint(100f, 100f), offensive, smokeRequired, smokeReady, squads, reports, axes));
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

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(message);
    }
}
