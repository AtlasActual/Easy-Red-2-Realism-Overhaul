using ER2RealismOverhaul;

internal static class AiPriorityScenarios
{
    internal static void SurvivalOverridesCompetingTasks()
    {
        // Every combination competes with an assignment, a defensive position,
        // contact, and a cover hold. Reverse proposal order to check determinism.
        for (var flags = 0; flags < 16; flags++)
        {
            var hazard = (flags & 1) != 0;
            var action = (flags & 2) != 0;
            var pinned = (flags & 4) != 0;
            var boarding = (flags & 8) != 0;
            var snapshot = Snapshot() with
            {
                LethalHazard = hazard, NeedsReloadSafety = action,
                Pinned = pinned, HasVehicleBoardingOrder = boarding
            };
            var expected = hazard ? ProposalSource.Hazard :
                action ? ProposalSource.ActionSafety :
                pinned ? ProposalSource.Suppression :
                boarding ? ProposalSource.VehicleBoarding : ProposalSource.ProtectedAssignment;
            foreach (var enabled in new[] { false, true })
            {
                var proposals = new List<TacticalProposal>();
                ProposalGenerationCore.Collect(snapshot, new TacticalPolicyOptions(enabled), proposals);
                CheckWinner(snapshot, proposals, expected);
                proposals.Reverse();
                CheckWinner(snapshot, proposals, expected);
            }
        }
    }

    internal static void EscapeSurvivesSafetyCallbacks()
    {
        foreach (var declared in Enum.GetValues<MovementOwner>())
        {
            var owner = MovementArbiterCore.Resolve(declared,
                safetyHalt: true, hazardEscape: true, pinnedHold: true,
                haltSpacing: true, engagementHold: true, coverHold: true,
                committedMove: true);
            Require(owner == (declared == MovementOwner.BurningHold
                    ? MovementOwner.BurningHold : MovementOwner.HazardEscape),
                $"Callback {declared} defeated lethal-hazard escape.");
            owner = MovementArbiterCore.Resolve(declared,
                safetyHalt: true, hazardEscape: true, pinnedHold: true,
                haltSpacing: true, engagementHold: true, coverHold: true,
                committedMove: true, burningHold: true);
            Require(owner == MovementOwner.BurningHold && MovementArbiterCore.Halts(owner),
                "A burning soldier lost the native stationary reaction.");
        }
        Require(MovementArbiterCore.Resolve(MovementOwner.Free, false, false, false,
                false, false, false, true) == MovementOwner.CommittedMove,
            "Resolved danger did not return control to the cover move.");
    }

    internal static void CoverMoveInterruptionDependsOnDistance()
    {
        const float threshold = 20f;
        foreach (var distance in new[] { 0f, 5f, 19.9f, 20f, 20.1f, 75f, 150f })
        {
            var immediate = ContactDivePolicyCore.IsImmediateThreat(distance * distance, threshold);
            Require(immediate == (distance <= threshold), "Immediate threat boundary changed.");
            Require(ContactDivePolicyCore.ShouldStart(true, true, false, 0f, 100f,
                    hasCommittedCoverMove: true, immediateThreat: immediate) == immediate,
                $"Cover move interruption was incorrect at {distance} metres.");
        }
        foreach (var invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
            Require(!ContactDivePolicyCore.IsImmediateThreat(invalid, threshold),
                "Invalid distance created a close threat.");
        Require(!ContactDivePolicyCore.ShouldStart(true, false, false, 0f, 100f,
                hasCommittedCoverMove: true, immediateThreat: true),
            "An unseen enemy interrupted the cover move.");

        var deadline = ContactDivePolicyCore.ResolveFiringDeadline(100f, 8f);
        Require(ContactDivePolicyCore.ShouldStart(true, true, true, 0f, 100f,
                hasCommittedCoverMove: true, immediateThreat: true,
                immediateThreatResponseUsed: false),
            "A previously distant contact could not escalate when it became close.");
        Require(!ContactDivePolicyCore.ShouldStart(true, true, true, deadline, 101f,
                hasCommittedCoverMove: true, immediateThreat: true,
                immediateThreatResponseUsed: false),
            "Proximity escalation restarted an already-active firing response.");
        Require(!ContactDivePolicyCore.ShouldStart(true, true, true, deadline, deadline + 1f,
                hasCommittedCoverMove: true, immediateThreat: true,
                immediateThreatResponseUsed: true),
            "Continuous close contact restarted the minimum reaction instead of reassessing.");
        Require(!InfantryCoverDecisionCore.ShouldHaltForCloseThreat(true, false,
                CombatMovementPolicyCore.AttackFiringPhaseActive(deadline, deadline + 1f)),
            "An expired minimum reaction prevented protection reassessment.");
    }

    internal static void LiveCloseThreatOutlastsFiringDeadline()
    {
        var deadline = ContactDivePolicyCore.ResolveFiringDeadline(100f, 8f);
        // Both covering fire and the longest halt cap are available. Neither is
        // evidence that this soldier's personally observed close enemy is gone.
        foreach (var now in new[] { deadline, deadline + 30f, deadline + 120f })
        foreach (var protectedPosition in new[] { false, true })
        {
            var advance = CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                true, true, true, true, false, false, protectedPosition, 0f, now,
                immediateThreat: true);
            Require(!advance, "Elapsed time authorized advancing past a live close enemy.");
            var input = new CoverNeedInput(
                HasUsableCover: protectedPosition, MayAdvanceFromCover: advance,
                CoverCompromised: false, UnderDirectFire: false, Suppressed: false,
                CloseThreat: true, AttackAdvanceBlocked: !advance,
                NormalDecisionDue: true, UrgentDecisionDue: true,
                FiringPhaseActive: CombatMovementPolicyCore.AttackFiringPhaseActive(deadline, now));
            var decision = InfantryCoverDecisionCore.EvaluateNeed(input);
            Require(decision.ShouldSearch == !protectedPosition,
                "Continuing engagement failed to retain protection or seek it from exposure.");
            if (protectedPosition)
                Require(decision.State == InfantryCoverState.Holding,
                    "The firing deadline released a protective fighting position.");

            // Failed/deferred searches retain a fighting halt and can retry;
            // they do not convert the expired timer into an objective advance.
            Require(!InfantryCoverDecisionCore.EvaluateNeed(
                    input with { UrgentDecisionDue = false }).ShouldSearch,
                "Persistent close contact bypassed cover-search backoff.");
            Require(!InfantryCoverDecisionCore.ShouldKeepMovingDuringDeferredCoverSearch(
                    true, protectedPosition, true, true, true),
                "A deferred search sent the soldier forward past a close threat.");

            Require(CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                    true, true, true, true, false, false, protectedPosition, 0f, now,
                    immediateThreat: false),
                "The close-threat veto survived after actionable proximity cleared.");
        }
    }

    internal static void SurvivalDoesNotArmAContactDive()
    {
        foreach (var committed in new[] { false, true })
        foreach (var immediate in new[] { false, true })
            Require(!ContactDivePolicyCore.ShouldStart(true, true, false, 0f, 100f,
                    committed, survivalActionActive: true, immediateThreat: immediate),
                "A pin or required action armed a competing firing halt.");
        Require(ContactDivePolicyCore.ShouldStart(true, true, false, 0f, 100f,
                hasCommittedCoverMove: false, survivalActionActive: false),
            "An uncommitted moving soldier lost the ordinary contact response.");
    }

    internal static void PinnedBoardingResumesAfterRecovery()
    {
        var snapshot = Snapshot() with { Pinned = true, HasVehicleBoardingOrder = true };
        var proposals = new List<TacticalProposal>();
        ProposalGenerationCore.Collect(snapshot, new TacticalPolicyOptions(true), proposals);
        CheckWinner(snapshot, proposals, ProposalSource.Suppression);
        snapshot = snapshot with { Pinned = false };
        ProposalGenerationCore.Collect(snapshot, new TacticalPolicyOptions(true), proposals);
        CheckWinner(snapshot, proposals, ProposalSource.VehicleBoarding);
        Require((int)CommandAuthority.CriticalSuppression > (int)CommandAuthority.VehicleBoarding,
            "The authority table disagrees with the survival decision order.");

        // Player and mission ownership remain an outer boundary, even with all
        // autonomous choices available. No autonomous lease seizes their orders.
        foreach (var scripted in new[] { false, true })
        {
            var external = snapshot with
            {
                PlayerLed = !scripted, ScriptOwned = scripted,
                LethalHazard = true, Pinned = true, NeedsReloadSafety = true
            };
            ProposalGenerationCore.Collect(external, new TacticalPolicyOptions(true), proposals);
            CheckWinner(external, proposals, ProposalSource.External);
        }
    }

    private static SoldierTacticalSnapshot Snapshot()
        => new(1, 2, 0, StrategicPosture.Defend, false, false, true, false,
            false, false, false, default, new MapPoint(10f, 0f),
            ContactMovement: new ContactMovementSensor(true, true, true, true, true, true, true, true),
            Autonomous: true, HasProtectedAssignment: true);

    private static void CheckWinner(SoldierTacticalSnapshot snapshot,
        List<TacticalProposal> proposals, ProposalSource expected)
    {
        var resolution = TacticalArbitrationCore.Resolve(snapshot, proposals);
        Require(resolution.Winners[TacticalChannel.Movement].Source == expected,
            $"Expected {expected}, got {resolution.Winners[TacticalChannel.Movement].Source}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
