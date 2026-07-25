using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static partial class ContactResponse
{
    private const float ContactPersistenceSeconds = 3f;

    private const float PinnedShockSeconds = 2f;

    private const float AttackContactContinuitySeconds = 5f;

    // D2 (plan 015): widened from 3s so squadmates engaging a different visible
    // enemy still count as covering fire instead of only the mover's own target.
    private const float CoveringFireFreshSeconds = 7f;

    private const float UrgentCoverReassessmentSeconds = 4f;

    // D1 (plan 015): the on-cover halt cap is a multiple of the off-cover one so
    // real cover is still strongly preferred, but a soldier pinned in place by
    // sustained direct fire is guaranteed to eventually bound instead of waiting
    // forever for covering fire that may never come.
    private const float OnCoverAttackHaltMultiplier = 2.5f;

    internal const float TacticalCrouchPersistenceSeconds = 1.5f;

    private static int _coverAssignmentExecutorSoldierId;

    // Round-robin decision stagger. The TacticalMove pipeline
    // (SharedTacticalMovePrefix + the MaintainOwnedPose/ApplyFireDecision
    // postfix) runs for every owned soldier EVERY frame; its interop-heavy per-soldier
    // DECISION work (which stationary hold owns the soldier, and the cover/suppression
    // pose RESOLUTION) is spread so each soldier re-decides on one of every
    // DecisionStaggerModulus frames, its cohort keyed by instance id — the same shape
    // as the game's own rolling SoldierAI.SequentialUpdate index. Between decision
    // frames the soldier holds its already-latched decisions via cheap write-through
    // re-assertion, so decision latency is bounded to <= DecisionStaggerModulus - 1
    // frames (~33ms at K=3). SAFETY reactions (fire/flame/pinned/reload/stall/tank-hide)
    // and pure write-through (movement-inhibition flags, moveCharacter/sprint
    // suppression, latched-pose re-assertion, stationary threat facing, moving-fire
    // gate) stay per-frame. Every timer that gates behavior (ContactUntil,
    // EngagementHoldUntil, HoldCoverUntil, TacticalPoseHoldUntil, MovementStallHoldUntil)
    // is Time.time based, so this frame-count stagger cannot desync them.
    internal const int DecisionStaggerModulus = 3;

    internal static bool RunsDecisionThisFrame(int soldierId)
        // == 0 tests divisibility regardless of sign, so a negative Unity instance id
        // still fires exactly once every DecisionStaggerModulus frames. (K=3 is not a
        // power of two, so the plan's `id & (K-1)` cohort formula would collapse to two
        // cohorts and leave one frame in three empty; `+ soldierId` spreads all cohorts
        // evenly across frames — see Review notes.)
        => (Time.frameCount + soldierId) % DecisionStaggerModulus == 0;

    private static int _staggerSkipFrame = -1;
    private static int _staggerSkipsThisFrame;

    // Stutter-probe markers: the frame on which soldiers last took the stagger
    // write-through fast path, and how many did. Diagnostic-only.
    internal static int LastStaggerSkipFrame => _staggerSkipFrame;

    internal static int LastStaggerSkipCount => _staggerSkipsThisFrame;

    private static void CountStaggerSkip()
    {
        if (!Settings.StutterProbeEnabled.Value)
            return;

        var frame = Time.frameCount;
        if (frame != _staggerSkipFrame)
        {
            _staggerSkipFrame = frame;
            _staggerSkipsThisFrame = 0;
        }

        _staggerSkipsThisFrame++;
    }

    /// <summary>
    /// Write-through replay of the last-decided TacticalMove gate, used on the
    /// non-decision frames of the round-robin stagger. It re-asserts the already
    /// established control state without re-selecting the owning hold or re-resolving
    /// the pose. Returns true and sets <paramref name="passThrough"/> (the value the
    /// prefix must return to gate native movement) when the cached decision can be
    /// re-asserted; returns false to fall back to a full decision (no latched pose yet).
    /// The discriminator is the movement arbiter itself (plan 018): the ladder resolves
    /// from the same timers and flags on this cheap path as on a full decision frame, so
    /// the write-through can never disagree with the decision it is replaying.
    /// </summary>
    internal static bool TryWriteThroughTacticalMove(
        SoldierAI ai,
        Soldier soldier,
        float deltaTime,
        ref bool sprint,
        bool updateFireInhibitionOnPass,
        out bool passThrough)
    {
        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);
        var now = Time.time;

        var staggerOwner = ResolveMovementOwner(soldier, state, id, now, MovementOwner.Free);
        if (MovementArbiterCore.Halts(staggerOwner))
        {
            // A stationary hold is in force. Never re-assert an unlatched pose — route
            // an as-yet-undecided soldier to the full decision instead.
            if (!state.HasLatchedTacticalPose)
            {
                passThrough = false;
                return false;
            }

            // Re-assert the stop and the latched pose through the one write site.
            // Stationary threat facing is intentionally NOT re-asserted here: the
            // per-frame prefix never did (the FSM owns it, and its moveLookingTarget flag
            // persists between updates), so replaying it would add rotation the baseline
            // never performed.
            sprint = false;
            ApplyResolvedMovementDecision(
                ai, soldier, state, id, staggerOwner, deltaTime, now,
                state.LatchedTacticalPose, "stagger", resolvePose: false);

            CountStaggerSkip();
            passThrough = false;
            return true;
        }

        // The last decision granted locomotion. Re-assert it through the same single write
        // site (a granting owner releases the brake; Free writes nothing and simply records
        // that this mod is not holding him), then the moving-fire gate and the owned
        // movement crouch exactly as the pass-through tail does — all cheap write-through
        // (no ownership refresh, no hold selection, no cover geometry).
        ApplyResolvedMovementDecision(
            ai, soldier, state, id, staggerOwner, deltaTime, now,
            SoldierPose.Crouch, "stagger", resolvePose: false);
        var suppression = soldier.GetSuppressionValue();
        var activeThreatMovement = HasActiveContact(id, now) ||
                                   IncomingFireAwareness.HasActiveCue(id, now);
        SoldierTacticalSprintPatch.ApplyTacticalMovementPose(
            ai, soldier, now, suppression, activeThreatMovement);
        if (updateFireInhibitionOnPass)
            ApplyFireDecision(ai, soldier, now, authoritative: false);
        ReleaseStationaryThreatFacingForMovement(ai, soldier);
        KnownTargetSuppressiveFire.InterruptForMovement(ai, soldier);

        CountStaggerSkip();
        passThrough = true;
        return true;
    }

    internal static void Update(SoldierAI ai, Soldier soldier)
        => UpdateInternal(ai, soldier, forceDefensivePositionControl: false);

    internal static void UpdateDefensivePosition(SoldierAI ai, Soldier soldier)
        => UpdateInternal(ai, soldier, forceDefensivePositionControl: true);

    private static void UpdateInternal(
        SoldierAI ai,
        Soldier soldier,
        bool forceDefensivePositionControl)
    {
        if (!Settings.ContactResponseEnabled.Value && !forceDefensivePositionControl)
            return;

        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);
        RefreshDefensivePositionOwnership(soldier, state);
        state.ContactResponseActive = true;
        var target = GetActionableTarget(ai, soldier);
        var observedTargetToken = IntPtr.Zero;
        var observedTargetPosition = default(Vector3);
        if (target != null &&
            !TargetAcquisition.TryGetTargetSnapshot(
                target, out observedTargetToken, out observedTargetPosition))
        {
            target = null;
        }
        var now = Time.time;
        state.SquadId = SquadIdentity.GetSquadId(soldier);
        var hasAttackRoute = TryGetAttackWaypoint(soldier, out _) &&
                             HasCommittedDestination(soldier);
        var targetInsideAttackHalt = target != null &&
                                     HorizontalDistanceSqr(
                                         soldier.transform.position,
                                         observedTargetPosition) <=
                                     Settings.ContactEngagementHaltDistance.Value *
                                     Settings.ContactEngagementHaltDistance.Value;
        var underDirectFire = IncomingFireAwareness.TryGetActiveDirectCue(
            id, now, out var directFirePosition);
        var attackUnderPressure = target != null || state.Pinned ||
                                  IncomingFireAwareness.HasActiveCue(id, now);
        var (maximumAttackHaltReached, maximumOnCoverAttackHaltReached) =
            UpdateAttackProgressClock(state, hasAttackRoute, attackUnderPressure, now);
        if (state.LastOutgoingShotWasStationary &&
            (soldier.IsMoving(0.2f) || state.Relocating || state.Pinned ||
             state.SuppressionMovementOwned))
        {
            state.LastOutgoingShotWasStationary = false;
        }

        UpdateDefensiveCoverHold(soldier, state, id, now);

        if (state.CoverClearancePoseOwned && !OwnsCurrentCoverClearancePose(soldier, state))
            ClearCoverClearancePose(state);

        if (state.ReservedCoverId != IntPtr.Zero &&
            InfantryCoverDecisionCore.ShouldReleaseUnoccupiedReservation(
                state.Relocating,
                soldier.IsOnCover(),
                state.DefensiveCoverHold || state.HasDefensiveCoverAnchor))
        {
            state.ReservedCoverId = IntPtr.Zero;
            state.ReservedCoverPosition = default;
            AiState.ReleaseCoverReservation(id);
        }

        if (Settings.DangerReactionsEnabled.Value && soldier.IsOnFire)
        {
            if (state.Relocating)
                FinishRelocation(ai, soldier, state, id, now, false, false, false);
            state.EngagementHoldUntil = 0f;
            soldier.StopFire();
            StopDangerMovement(ai, soldier, SoldierPose.Prone, Time.deltaTime);
            return;
        }

        if (target != null)
        {
            // One halt budget belongs to the continuous contact, not to a particular
            // best-target pointer. Alternating visible enemies must not restart it.
            state.LastThreatPosition = observedTargetPosition;
            state.HasThreatPosition = true;
            state.ContactUntil = now + ContactPersistenceSeconds;
            if (state.AttackContactToken != observedTargetToken ||
                now - state.AttackContactLastSeenAt > AttackContactContinuitySeconds)
            {
                state.AttackContactToken = observedTargetToken;
            }
            state.AttackContactLastSeenAt = now;
        }

        // Active flame is an immediate lethal hazard. Even a pinned soldier will
        // leave the beaten zone of the fire before resuming the suppression hold.
        var flameEvading = Settings.DangerReactionsEnabled.Value &&
                           AiState.IsFlameEvading(id, now);
        if (flameEvading)
        {
            if (state.Relocating)
                FinishRelocation(ai, soldier, state, id, now, false, false, false);
            state.EngagementHoldUntil = 0f;
            ApplyMovementDecision(
                ai, soldier, Time.deltaTime, now, MovementOwner.Free,
                SoldierPose.Crouch, "fsm-flame-evade");
            soldier.StopFire();
            return;
        }

        // Pinning owns ordinary locomotion independently of Contact Response. A
        // selected cover destination is retained, but the soldier first gets down
        // and survives the burst that pinned him.
        if (IsPinned(id))
        {
            if (state.Relocating)
                PauseRelocation(state, id, now, true);
            ApplyPinnedSuppression(ai, soldier, state, now, Time.deltaTime);
            return;
        }

        // A player-issued hold owns the area, not the exact patch of ground selected
        // by native formation logic. Once a squadmate arrives, occupy one protected,
        // reserved fighting position inside that area and then remain there.
        if (TryHonorPlayerLedHoldOrder(
                ai,
                soldier,
                state,
                target,
                observedTargetPosition,
                id,
                now))
        {
            return;
        }

        // Infantry hiding from armor remain at the selected position. This stops
        // locomotion but leaves valid reaction fire against infantry available.
        if (TryHoldTankCover(ai, soldier, now, Time.deltaTime))
            return;

        // A real charge clears ordinary cover and engagement holds, but it does not
        // make a rifleman ignore an enemy inside immediate survival distance.
        var actualCharge = IsActualCharge(soldier);
        if (actualCharge)
        {
            if (state.Relocating)
                FinishRelocation(ai, soldier, state, id, now, false, false, false);
            ReleaseDefensiveCoverHold(state, id);
            state.HoldCoverUntil = 0f;
            state.ManeuverCoverMinimumHoldUntil = 0f;
            state.ManeuverCoverReleaseUntil = 0f;
            state.ManeuverCoverReleasedId = IntPtr.Zero;
            state.ManeuverCoverAnchorId = IntPtr.Zero;
            state.ManeuverCoverAnchorPosition = default;
            state.EngagementHoldUntil = 0f;
            state.ContactCrouchOwned = false;
            ClearCoverClearancePose(state);
            state.MovementInhibitedByContactResponse = false;
        }

        // A close rifle threat overrides an already-started cover run. Automatic
        // close-assault weapons can keep the move because their moving-fire gate
        // remains open inside the configured range.
        var closeThreatRequiresStationaryFire = target != null &&
            !state.DefensivePositionOwned &&
            HorizontalDistanceSqr(soldier.transform.position, observedTargetPosition) <=
            Settings.ContactImmediateFireDistance.Value * Settings.ContactImmediateFireDistance.Value &&
            !HandheldWeaponClassifier.AllowsMovingFire(soldier, ai);
        if (closeThreatRequiresStationaryFire)
        {
            if (state.Relocating)
                PauseRelocation(state, id, now, false);
            RespondWithoutNewCover(
                ai,
                soldier,
                state,
                observedTargetPosition,
                now);
            return;
        }

        if (actualCharge)
        {
            ApplyMovementDecision(
                ai, soldier, Time.deltaTime, now, MovementOwner.OrderedMove,
                SoldierPose.Crouch, "fsm-charge");
            return;
        }

        // The close-contact pause above is no longer a separate coordination flag: the
        // arbiter's own MovementHalted output says a hold stopped this soldier while he
        // was relocating, and granting movement below clears it, so this resume runs
        // exactly once per pause exactly as RelocationPausedByCloseFire did.
        if (state.Relocating && state.MovementHalted)
        {
            state.RelocateLastProgressAt = now;
            state.RelocateUntil = now + InfantryCoverPolicy.MoveProgressWindowSeconds;
            RefreshPath(ai, "Close-contact cover path resume failed");
        }

        // Once a cover move begins, commit to it even if visibility flickers. Releasing
        // this state whenever the target disappeared caused the observed move/stop loop.
        if (state.Relocating)
        {
            var activeDestination = soldier.targetDestination;
            var destinationWasReplaced = activeDestination != null &&
                                         !SameNativeDestination(activeDestination, state.RelocateDestinationPointer);
            var atSelectedCover = (soldier.transform.position - state.RelocateDestinationPosition).sqrMagnitude <= 9f;
            var nativeCoverReported = soldier.IsOnCover();
            var destinationEnded = soldier.DestinationReached ||
                                   !soldier.HasDestinationAssigned;
            var reachedReservedDefensiveSlot =
                InfantryCoverDecisionCore.ShouldClaimReachedDefensiveSlot(
                    OwnsTacticalDefensivePosition(state),
                    state.ReservedCoverId != IntPtr.Zero,
                    atSelectedCover,
                    nativeCoverReported,
                    destinationEnded);
            if (destinationWasReplaced)
            {
                FinishRelocation(ai, soldier, state, id, now, false, false);
            }
            else if (nativeCoverReported && atSelectedCover ||
                     reachedReservedDefensiveSlot)
            {
                FinishRelocation(ai, soldier, state, id, now, true, true);
            }
            else if (nativeCoverReported || destinationEnded)
            {
                FinishRelocation(ai, soldier, state, id, now, false, false);
                BeginMovementStallHold(
                    ai, soldier, state, now, "cover destination ended before arrival");
                return;
            }
            else if (now >= state.RelocateUntil)
            {
                FinishRelocation(ai, soldier, state, id, now, false, false);
                BeginMovementStallHold(
                    ai, soldier, state, now, "cover transit exceeded its progress window");
                return;
            }
            else
            {
                var destinationDistance = soldier.DestinationDistance;
                var physicalTravel = HorizontalDistance(
                    soldier.transform.position, state.RelocateLastProgressPosition);
                if (physicalTravel >= MovementProgressWatchdogCore.ProgressEpsilonMeters)
                {
                    state.RelocateLastDistance = destinationDistance;
                    state.RelocateLastProgressAt = now;
                    state.RelocateLastProgressPosition = soldier.transform.position;
                    state.RelocateUntil = now + InfantryCoverPolicy.MoveProgressWindowSeconds;
                    if (state.ReservedCoverId != IntPtr.Zero)
                        AiState.ReserveCover(
                            state.ReservedCoverId,
                            state.ReservedCoverPosition,
                            id,
                            state.RelocateUntil + 2f);
                }

                var stallSeconds = Mathf.Clamp(
                    InfantryCoverPolicy.MoveProgressWindowSeconds * 0.5f, 1.5f, 3f);
                if (now - state.RelocateLastProgressAt >= stallSeconds && !ai.HasPathRequest)
                {
                    AiState.Trace($"Contact response: soldier {id} cancelled a stalled cover move");
                    FinishRelocation(ai, soldier, state, id, now, false, false);
                    BeginMovementStallHold(
                        ai, soldier, state, now, "no physical progress toward cover");
                    return;
                }
                else
                {
                    ContinueCommittedMovement(ai, soldier, state, now);
                    return;
                }
            }
        }

        // Evaluate this after relocation completion. A soldier can enter cover on the
        // same update that the move is finalized, and that arrival must become a real
        // fighting halt before any objective-progress rule is allowed to move him again.
        var observingReachedCover = UpdateManeuverCoverObservation(
            soldier, state, id, now);

        // Defensive occupation outranks ordinary contact response. A visible enemy
        // must not turn an exposed arrival point into a permanent fighting halt or
        // interrupt the defender's one committed move into protected cover.
        if (TryEstablishInitialDefensivePosition(
                ai,
                soldier,
                state,
                target,
                observedTargetPosition,
                id,
                now))
        {
            return;
        }

        if (target == null)
        {
            state.AttackConditionsWereFavorable = false;
            if (KnownTargetSuppressiveFire.TryGetOwnedAimPoint(id, now, out var suppressionAimPoint))
            {
                // D4 (plan 015): no live Spottable target here, so the hold is
                // bounded to the ordinary decision cadence instead of +inf; a
                // lapsed hold simply re-decides next tick rather than freezing.
                state.EngagementHoldUntil = now + InfantryCoverPolicy.DecisionIntervalSeconds;
                state.ContactCrouchOwned = true;
                StopTacticalMovement(
                    ai,
                    soldier,
                    GetStationaryEngagementPose(soldier, state, suppressionAimPoint),
                    Time.deltaTime,
                    "fsm-suppressive-aim");
                return;
            }

            if (observingReachedCover)
            {
                SetCoverState(state, InfantryCoverState.Holding, id,
                    "observing and fighting from reached cover");
                state.ContactCrouchOwned = true;
                StopTacticalMovement(
                    ai,
                    soldier,
                    state.HasThreatPosition
                        ? GetStationaryEngagementPose(soldier, state, state.LastThreatPosition)
                        : SoldierPose.Crouch,
                    Time.deltaTime,
                    "fsm-observe-cover");
                if (state.HasThreatPosition && now < state.ContactUntil)
                    FaceThreatWhenStationary(ai, soldier, state.LastThreatPosition);
                return;
            }

            var onUsableCoverWithoutVisibleTarget = IsOnUsableCover(soldier);
            var forcedAttackProgressWithoutVisibleTarget = CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                hasAttackRoute,
                coveringFireEstablished: false,
                maximumAttackHaltReached,
                maximumOnCoverAttackHaltReached,
                underDirectFire,
                state.Pinned,
                onUsableCover: onUsableCoverWithoutVisibleTarget,
                state.ManeuverCoverMinimumHoldUntil,
                now);
            if (forcedAttackProgressWithoutVisibleTarget)
            {
                // Rising edge only. ContinueAttackObjectiveMovement below latches
                // AttackProgressForced, so this diagnostic cannot repeat (and cannot
                // allocate its message) on every decision frame the cap stays true.
                if (!state.AttackProgressForced)
                {
                    TraceOnCoverHaltCapBound(
                        id, onUsableCoverWithoutVisibleTarget, coveringFireEstablished: false);
                }
                var suppressedAdvance = Settings.DangerReactionsEnabled.Value &&
                                        soldier.GetSuppressionValue() >= Settings.CrouchSuppression.Value;
                ContinueAttackObjectiveMovement(
                    ai, soldier, state, id, now, suppressedAdvance, true);
                return;
            }

            if (now >= state.ContactUntil)
                ClearCoverClearancePose(state);

            if (state.DefensiveCoverHold)
            {
                StopTacticalMovement(
                    ai,
                    soldier,
                    now < state.ContactUntil
                        ? StationaryHoldPose(soldier)
                        : SoldierPose.Crouch,
                    Time.deltaTime,
                    "fsm-defensive-hold");
                // Hold the position, not an obsolete bearing. After the immediate
                // contact fades, native scanning must be free to find a flanker.
                if (state.HasThreatPosition && now < state.ContactUntil)
                    FaceThreatWhenStationary(ai, soldier, state.LastThreatPosition);
                return;
            }

            var wasHoldingMovement = state.MovementInhibitedByContactResponse ||
                                     state.EngagementHoldUntil > 0f;
            if (state.ContactCrouchOwned && now < state.ContactUntil)
            {
                if (wasHoldingMovement)
                {
                    state.EngagementHoldUntil = state.ContactUntil;
                    StopTacticalMovement(
                        ai,
                        soldier,
                        StationaryHoldPose(soldier),
                        Time.deltaTime,
                        "fsm-contact-hold");
                    if (state.HasThreatPosition)
                        FaceThreatWhenStationary(ai, soldier, state.LastThreatPosition);
                }
                else
                {
                    ApplyMovementDecision(
                        ai, soldier, Time.deltaTime, now, MovementOwner.OrderedMove,
                        SoldierPose.Crouch, "fsm-contact-move");
                    ApplyContactMovementPose(ai, soldier, state, now);
                }
                return;
            }

            state.ContactCrouchOwned = false;
            state.EngagementHoldUntil = 0f;
            var isOnCover = soldier.IsOnCover();
            if (!isOnCover)
                state.HoldCoverUntil = 0f;
            if (!isOnCover || now >= state.HoldCoverUntil)
            {
                // The old "if (!SuppressionMovementOwned)" guard is the PinnedHold rank
                // outranking OrderedMove; the arbiter enforces it now, and the declared
                // OrderedMove releases the contact-halt flag on its way through.
                var owner = ApplyMovementDecision(
                    ai, soldier, Time.deltaTime, now, MovementOwner.OrderedMove,
                    SoldierPose.Crouch, "fsm-contact-release");
                if (MovementArbiterCore.Grants(owner) && wasHoldingMovement &&
                    HasCommittedDestination(soldier))
                {
                    RefreshPath(ai, "Contact path release failed");
                }
            }
            return;
        }

        var targetPosition = state.LastThreatPosition;
        var distance = Vector3.Distance(soldier.transform.position, targetPosition);
        var attackContactInsideHalt = hasAttackRoute && targetInsideAttackHalt;
        var coordinatedAttackAdvance = hasAttackRoute &&
                                       HasFavorableAttackAdvance(
                                           state, id, observedTargetToken, now);
        var onUsableNativeCover = IsOnUsableCover(soldier);
        var authorizedAttackAdvance = CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
            hasAttackRoute,
            coordinatedAttackAdvance,
            maximumAttackHaltReached,
            maximumOnCoverAttackHaltReached,
            underDirectFire,
            state.Pinned,
            onUsableNativeCover,
            state.ManeuverCoverMinimumHoldUntil,
            now);
        // Authorized without coordinated covering fire can only mean the halt cap
        // (off-cover or the longer on-cover one) was the deciding factor.
        var forcedAttackProgress = authorizedAttackAdvance && !coordinatedAttackAdvance;
        var favorableJustEstablished = authorizedAttackAdvance &&
                                       !state.AttackConditionsWereFavorable;
        if (!authorizedAttackAdvance)
            state.AttackConditionsWereFavorable = false;

        // Native cover metadata is not sufficient: an exposed node inside an
        // objective is still exposed. Stable defender anchors were already checked
        // geometrically when claimed and use the separate DefensiveCoverHold path.
        var insideDefensiveArea = onUsableNativeCover && IsInsideDefensiveArea(soldier);
        var protectsFromCurrentThreat = onUsableNativeCover &&
                                        IsCurrentCoverProtective(
                                            soldier, state, targetPosition, now);
        var hasUsableCover = state.DefensiveCoverHold ||
                             InfantryCoverDecisionCore.ShouldTreatCurrentCoverAsUsable(
                                 onUsableNativeCover,
                                 insideDefensiveArea,
                                 protectsFromCurrentThreat);
        if (hasUsableCover)
        {
            if (state.ReservedCoverId != IntPtr.Zero)
                AiState.ReserveCover(
                    state.ReservedCoverId,
                    state.ReservedCoverPosition,
                    id,
                    now + InfantryCoverPolicy.DecisionIntervalSeconds);
            if (!authorizedAttackAdvance)
            {
                SetCoverState(state, InfantryCoverState.Holding, id,
                    "current position remains protective");
                state.ContactCrouchOwned = true;
                StopTacticalMovement(
                    ai,
                    soldier,
                    GetStationaryEngagementPose(soldier, state, targetPosition),
                    Time.deltaTime,
                    "fsm-cover-hold");
                FaceThreatWhenStationary(ai, soldier, targetPosition);
                return;
            }
        }

        // A destroyed or globally unsafe cover position is compromised. It no
        // longer qualifies for a persistent defensive hold.
        // Outside a defensive hold, a native slot is compromised when destroyed,
        // globally unsafe, or unable to protect the soldier from this threat.
        var coverCompromised = soldier.IsOnCover() && !hasUsableCover;
        var suppressed = Settings.DangerReactionsEnabled.Value &&
                         soldier.GetSuppressionValue() >= Settings.CrouchSuppression.Value;
        var coverDecision = InfantryCoverDecisionCore.EvaluateNeed(new CoverNeedInput(
            hasUsableCover,
            authorizedAttackAdvance,
            coverCompromised,
            underDirectFire,
            suppressed && !hasAttackRoute,
            distance <= Settings.ContactImmediateFireDistance.Value,
            attackContactInsideHalt && !authorizedAttackAdvance,
            now >= state.NextRelocationAllowedAt &&
            (now >= state.NextDecisionAt || favorableJustEstablished),
            now >= state.NextUrgentCoverDecisionAt));

        SetCoverState(state, coverDecision.State, id, coverDecision.Reason);
        if (!coverDecision.ShouldSearch)
        {
            // Attackers use the halt to establish fire, but it cannot become an
            // infinite veto. Once coordinated fire or the maximum wait authorizes
            // the next bound, an exposed soldier resumes the objective route.
            if (authorizedAttackAdvance && !hasUsableCover)
            {
                ContinueAttackObjectiveMovement(
                    ai, soldier, state, id, now, suppressed, forcedAttackProgress);
                return;
            }

            RespondWithoutNewCover(
                ai,
                soldier,
                state,
                targetPosition,
                now,
                preferProne: (underDirectFire || suppressed) && !hasUsableCover);
            return;
        }

        state.AttackConditionsWereFavorable = authorizedAttackAdvance;
        // Traced here, where the authorization is actually acted on (the search for
        // the next bound position), not on every decision frame it stays true: this
        // branch is throttled by NextRelocationAllowedAt / NextDecisionAt, so the
        // diagnostic marks the event instead of flooding the trace ring.
        if (authorizedAttackAdvance)
            TraceOnCoverHaltCapBound(id, onUsableNativeCover, coordinatedAttackAdvance);
        state.NextDecisionAt = now + InfantryCoverPolicy.DecisionIntervalSeconds;
        if (coverDecision.SelectionMode == CoverSelectionMode.Urgent)
            state.NextUrgentCoverDecisionAt = now + UrgentCoverReassessmentSeconds;

        var cover = FindCover(
            soldier,
            targetPosition,
            Settings.ContactCoverSearchRadius.Value,
            state,
            now,
            coverDecision.SelectionMode,
            underDirectFire ? directFirePosition : null,
            respectAttackWaypoint: true,
            evaluateFiringQuality: true,
            out _,
            out var searchDeferred);
        if (searchDeferred)
        {
            state.NextDecisionAt = DeferredCoverRetryAt(id, now);
            if (coverDecision.SelectionMode == CoverSelectionMode.Urgent)
                state.NextUrgentCoverDecisionAt = state.NextDecisionAt;
            RespondWithoutNewCover(
                ai,
                soldier,
                state,
                targetPosition,
                now,
                preferProne: underDirectFire || suppressed);
            return;
        }
        if (cover == null)
        {
            SetCoverState(state, InfantryCoverState.WaitingForSafeMove, id,
                "no protective cover with an acceptable route was found");
            // A soldier with no reachable cover backs off its next assessment so it
            // stays prone and fights instead of looping search -> fail -> search.
            state.ConsecutiveCoverSearchFailures++;
            state.NextDecisionAt = now + CoverSearchBackoffCore.NextDecisionDelaySeconds(
                InfantryCoverPolicy.DecisionIntervalSeconds,
                state.ConsecutiveCoverSearchFailures);
            if (coverDecision.SelectionMode == CoverSelectionMode.Urgent)
                state.NextUrgentCoverDecisionAt = state.NextDecisionAt;
            if (authorizedAttackAdvance)
            {
                ContinueAttackObjectiveMovement(
                    ai, soldier, state, id, now, suppressed, forcedAttackProgress);
                return;
            }
            RespondWithoutNewCover(
                ai,
                soldier,
                state,
                targetPosition,
                now,
                preferProne: underDirectFire || suppressed);
            return;
        }

        if (BeginRelocation(ai, soldier, state, cover, id, now))
        {
            SetCoverState(state, InfantryCoverState.Moving, id,
                $"committed to {coverDecision.SelectionMode.ToString().ToLowerInvariant()} cover move");
        }
        else
        {
            SetCoverState(state, InfantryCoverState.WaitingForSafeMove, id,
                "selected cover could not be assigned");
            if (authorizedAttackAdvance)
            {
                ContinueAttackObjectiveMovement(
                    ai, soldier, state, id, now, suppressed, forcedAttackProgress);
                return;
            }
            RespondWithoutNewCover(
                ai,
                soldier,
                state,
                targetPosition,
                now,
                preferProne: underDirectFire || suppressed);
        }
    }

    private static void ResetManeuverCoverHold(ContactResponseState state)
    {
        state.HoldCoverUntil = 0f;
        state.ManeuverCoverMinimumHoldUntil = 0f;
        state.ManeuverCoverReleaseUntil = 0f;
        state.ManeuverCoverReleasedId = IntPtr.Zero;
        state.ManeuverCoverAnchorId = IntPtr.Zero;
        state.ManeuverCoverAnchorPosition = default;
    }

    private static void ResetDefensiveOwnershipState(ContactResponseState state)
    {
        state.DefensiveCoverHold = false;
        state.HasDefensiveCoverAnchor = false;
        state.DefensiveCoverAnchorId = IntPtr.Zero;
        state.DefensiveCoverAnchorPosition = default;
        state.DefensivePositionOwned = false;
        state.DefensivePositionSquadId = 0;
        state.DefensivePositionObjectiveRevision = 0;
        state.DefensivePositionEntryPoint = default;
        state.PlayerHoldPositionOwned = false;
        state.PlayerHoldCenter = default;
        state.PlayerHoldRadius = 0f;
    }

    internal static void Disable(SoldierAI ai, Soldier soldier)
    {
        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);
        ReleaseStationaryThreatFacing(ai, state);
        ResetAttackFireEvidence(state);
        if (!state.ContactResponseActive && !state.MovementInhibitedByContactResponse &&
            !state.Relocating && !state.ContactCrouchOwned && !state.CoverClearancePoseOwned)
            return;

        var now = Time.time;
        var wasControllingMovement = state.MovementInhibitedByContactResponse ||
                                     state.Relocating ||
                                     now < state.EngagementHoldUntil || now < state.HoldCoverUntil ||
                                     soldier.IsOnFire;

        state.Relocating = false;
        state.NextDecisionAt = 0f;
        state.NextUrgentCoverDecisionAt = 0f;
        state.CoverState = InfantryCoverState.Holding;
        state.RelocateUntil = 0f;
        state.RelocateLastDistance = 0f;
        state.RelocateLastProgressAt = 0f;
        state.RelocateLastProgressPosition = default;
        state.RelocateDestinationPointer = IntPtr.Zero;
        state.RelocateDestinationPosition = default;
        state.RelocationPausedBySuppression = false;
        state.NextRelocationAllowedAt = 0f;
        state.ReservedCoverId = IntPtr.Zero;
        state.ReservedCoverPosition = default;
        state.FailedCoverId = IntPtr.Zero;
        state.FailedCoverUntil = 0f;
        state.ConsecutiveCoverSearchFailures = 0;
        state.ConsecutiveTankCoverFailures = 0;
        state.LastTankCoverFailureAt = 0f;
        state.EngagementHoldUntil = 0f;
        state.ContactUntil = 0f;
        state.ContactCrouchOwned = false;
        ClearCoverClearancePose(state);
        ResetCoverPostureEvaluation(state);
        ResetTacticalPoseLatch(state);
        ResetManeuverCoverHold(state);
        ResetDefensiveOwnershipState(state);
        state.LastThreatPosition = default;
        state.HasThreatPosition = false;
        AiState.ReleaseCoverReservation(id);

        state.MovementInhibitedByContactResponse = false;
        if (wasControllingMovement)
        {
            // The suppression/hazard exclusions this used to spell out are simply the
            // PinnedHold, SafetyHalt and HazardEscape ranks outranking OrderedMove.
            var owner = ApplyMovementDecision(
                ai, soldier, Time.deltaTime, now, MovementOwner.OrderedMove,
                SoldierPose.Crouch, "contact-disable");
            if (owner == MovementOwner.OrderedMove && HasCommittedDestination(soldier))
                RefreshPath(ai, "Contact path release after disabling failed");
        }

        // No fire restore handshake is needed: the arbiter recomputes the gate from the
        // surviving owners on the director's next tail call.
        state.ContactResponseActive = false;
    }

    /// <summary>
    /// Releases only the locomotion state owned by contact response when the
    /// director grants movement to a player/script order or a protected
    /// fortification assignment. Suppression and fire-safety ownership remain
    /// intact and can continue to react locally.
    /// </summary>
    internal static void YieldMovementToHigherAuthority(
        SoldierAI ai,
        Soldier soldier,
        bool releaseDefensiveAnchor)
    {
        var soldierId = soldier.GetInstanceID();
        var state = AiState.GetContactState(soldierId);
        var wasControllingMovement = state.Relocating ||
                                     state.MovementInhibitedByContactResponse ||
                                     state.EngagementHoldUntil > 0f;
        if (state.PlayerHoldPositionOwned &&
            !HasActivePlayerHoldPositionControl(soldier, state))
        {
            ReleasePlayerHoldPositionOwnership(state, soldierId);
        }

        ReleaseStationaryThreatFacing(ai, state);
        if (state.Relocating)
            FinishRelocation(ai, soldier, state, soldierId, Time.time,
                keepOccupiedCover: false, completedMove: false, markFailedCover: false);
        if (releaseDefensiveAnchor)
        {
            ReleaseDefensivePositionOwnership(state, soldierId);
            ReleasePlayerHoldPositionOwnership(state, soldierId);
            ReleaseDefensiveCoverHold(state, soldierId);
            ResetManeuverCoverHold(state);
        }
        else if (!soldier.IsOnCover())
        {
            ResetManeuverCoverHold(state);
        }

        state.ContactResponseActive = false;
        state.MovementInhibitedByContactResponse = false;
        state.ContactCrouchOwned = false;
        state.EngagementHoldUntil = 0f;
        // A player/script order outranks this mod's fighting-position hold, and the
        // movement ladder has no external-authority rank to express that with - so the
        // hold is released here, where the decision was already made. The maneuver ANCHOR
        // is deliberately kept (above) so the soldier can return to the slot afterwards.
        state.HoldCoverUntil = 0f;
        ClearCoverClearancePose(state);
        if (wasControllingMovement)
        {
            var owner = ApplyMovementDecision(
                ai, soldier, Time.deltaTime, Time.time, MovementOwner.OrderedMove,
                SoldierPose.Crouch, "yield-authority");
            if (owner == MovementOwner.OrderedMove && HasCommittedDestination(soldier))
                RefreshPath(ai, "Higher-authority movement path resume failed");
        }
    }

    internal static void SuspendForVehicle(SoldierAI ai, Soldier soldier)
    {
        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);
        ReleaseStationaryThreatFacing(ai, state);
        if (state.FireInhibitedByRange || state.FireInhibitedByArmoredTarget)
            ai.targetInWeaponRange = true;
        state.FireInhibitedByMovement = false;
        state.FireInhibitedByRange = false;
        state.FireInhibitedByArmoredTarget = false;
        state.SuppressionFireInhibited = false;
        state.SuppressionMovementOwned = false;
        state.SuppressionPoseOwned = false;
        state.SuppressionCrouchUntil = 0f;
        state.Pinned = false;
        state.PinnedUntil = 0f;
        state.PinnedFireBlockedUntil = 0f;
        state.PinnedSince = 0f;
        state.PinnedImmunityUntil = 0f;
        ResetCoverPostureEvaluation(state);
        ResetTacticalPoseLatch(state);

        state.Relocating = false;
        state.NextUrgentCoverDecisionAt = 0f;
        state.CoverState = InfantryCoverState.Holding;
        state.RelocationPausedBySuppression = false;
        state.RelocateDestinationPointer = IntPtr.Zero;
        state.RelocateDestinationPosition = default;
        state.ReservedCoverId = IntPtr.Zero;
        state.ReservedCoverPosition = default;
        state.ConsecutiveCoverSearchFailures = 0;
        state.ConsecutiveTankCoverFailures = 0;
        state.LastTankCoverFailureAt = 0f;
        state.ContactResponseActive = false;
        state.MovementInhibitedByContactResponse = false;
        state.ContactCrouchOwned = false;
        state.ContactUntil = 0f;
        state.EngagementHoldUntil = 0f;
        ResetManeuverCoverHold(state);
        ResetDefensiveOwnershipState(state);
        ResetAttackFireEvidence(state);
        state.LastFireBlocker = FireBlocker.NativeControl;
        AiState.ReleaseCoverReservation(id);
    }

    internal static bool IsRelocating(int soldierId)
        => Settings.ContactResponseEnabled.Value &&
           AiState.ContactStates.TryGetValue(soldierId, out var state) && state.Relocating;

    internal static void RecordActualShot(Soldier shooter, Vector3 fireDirection, float now)
    {
        if (!Settings.ContactResponseEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() || shooter == null ||
            !shooter.IsAlive || !AiOwnership.IsAutonomous(shooter) || shooter.IsOnVehicle())
        {
            return;
        }

        try
        {
            var ai = shooter.aiController;
            if (ai == null)
                return;

            IntPtr targetToken;
            Vector3 targetPosition;
            var soldierId = shooter.GetInstanceID();
            var target = GetActionableTarget(ai, shooter);
            if (!TargetAcquisition.TryGetTargetSnapshot(
                    target, out targetToken, out targetPosition) &&
                !KnownTargetSuppressiveFire.TryGetOwnedTarget(
                    soldierId, out targetToken, out targetPosition))
            {
                return;
            }

            var shotDirection = fireDirection;
            if (shotDirection.sqrMagnitude < 0.001f)
                return;
            shotDirection.Normalize();
            var towardTarget = targetPosition - shooter.LookPosition();
            if (towardTarget.sqrMagnitude < 0.01f ||
                Vector3.Angle(shotDirection, towardTarget) > 15f)
            {
                return;
            }

            var state = AiState.GetContactState(soldierId);
            state.SquadId = SquadIdentity.GetSquadId(shooter);
            state.LastOutgoingShotTargetToken = targetToken;
            state.LastOutgoingShotAt = now;
            state.LastOutgoingShotWasStationary =
                !shooter.IsMoving(0.2f) && !state.Relocating;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Outgoing-fire evidence failed: {ex.Message}");
        }
    }

    internal static void ResetBattleAttackEvidence()
    {
        foreach (var state in AiState.ContactStates.Values)
            ResetAttackFireEvidence(state);
    }

    private static bool HasFavorableAttackAdvance(
        ContactResponseState state,
        int soldierId,
        IntPtr targetToken,
        float now)
    {
        // D3 (plan 015): the mover no longer has to have fired himself — a
        // soldier whose cover slot has no firing lane was otherwise permanently
        // ineligible to advance.
        if (!CombatMovementPolicyCore.MoverQualifiesForAttackAdvance(
                targetToken, state.SquadId, state.AttackContactToken))
        {
            return false;
        }

        // Only reached when this soldier is an attacker on a live contact; the
        // O(all ContactStates) covering-fire scan below is timed separately so
        // the probe can tell it apart from the ballistic geometry cost.
        var __t = ModTimeProbe.Begin();
        try
        {
            foreach (var pair in AiState.ContactStates)
            {
                if (pair.Key == soldierId)
                    continue;

                var covering = pair.Value;
                // D2 (plan 015): any squadmate's fresh stationary shot at a
                // confirmed enemy counts, not just one at this mover's own token.
                if (CombatMovementPolicyCore.IsCoveringFireEstablished(
                        state.SquadId, covering.SquadId, covering.LastOutgoingShotTargetToken,
                        covering.LastOutgoingShotWasStationary, covering.Relocating,
                        covering.Pinned, covering.SuppressionMovementOwned,
                        covering.LastOutgoingShotAt, now, CoveringFireFreshSeconds))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            ModTimeProbe.EndSub(ModSubSite.SquadScan, __t);
        }
    }

    /// <summary>
    /// Maintains the attack-halt clock and reports both the ordinary off-cover
    /// deadline and the longer on-cover deadline (D1, plan 015) against the same
    /// clock, so <see cref="ShouldAuthorizeAttackBound"/> can guarantee liveness
    /// on cover without shortening the off-cover cap.
    /// </summary>
    private static (bool MaximumHaltReached, bool MaximumOnCoverHaltReached) UpdateAttackProgressClock(
        ContactResponseState state,
        bool hasAttackRoute,
        bool underPressure,
        float now)
    {
        if (!hasAttackRoute)
        {
            state.AttackHaltStartedAt = 0f;
            state.AttackProgressForced = false;
            return (false, false);
        }

        if (underPressure)
        {
            if (state.AttackHaltStartedAt <= 0f)
                state.AttackHaltStartedAt = Mathf.Max(now, 0.0001f);
        }
        else if (now >= state.ContactUntil)
        {
            state.AttackHaltStartedAt = 0f;
            state.AttackProgressForced = false;
            return (false, false);
        }

        var maximumHaltReached = InfantryCoverDecisionCore.ShouldForceAttackProgress(
            hasAttackOrder: true,
            hasDestination: true,
            state.AttackHaltStartedAt,
            now,
            Settings.MaximumAttackCombatHaltSeconds.Value);
        var maximumOnCoverHaltReached = InfantryCoverDecisionCore.ShouldForceAttackProgress(
            hasAttackOrder: true,
            hasDestination: true,
            state.AttackHaltStartedAt,
            now,
            Settings.MaximumAttackCombatHaltSeconds.Value * OnCoverAttackHaltMultiplier);
        return (maximumHaltReached, maximumOnCoverHaltReached);
    }

    private static void TraceOnCoverHaltCapBound(
        int soldierId, bool onUsableCover, bool coveringFireEstablished)
    {
        if (onUsableCover && !coveringFireEstablished)
        {
            AiState.Trace(
                $"Attack bound: soldier {soldierId} left cover on the extended halt cap without covering fire");
        }
    }

    /// <summary>
    /// D4 (plan 015): a bounded stand-in for +inf. A live contact owns the hold
    /// until it lapses on its own persistence timer; otherwise the hold is capped
    /// to the ordinary decision cadence so a soldier always re-decides instead of
    /// freezing.
    /// </summary>
    private static float BoundedEngagementHold(bool hasLiveContact, ContactResponseState state, float now)
        => hasLiveContact ? state.ContactUntil : now + InfantryCoverPolicy.DecisionIntervalSeconds;

    private static void ResetAttackFireEvidence(ContactResponseState state)
    {
        state.SquadId = 0;
        state.AttackContactToken = IntPtr.Zero;
        state.AttackContactLastSeenAt = 0f;
        state.AttackConditionsWereFavorable = false;
        state.AttackHaltStartedAt = 0f;
        state.AttackProgressForced = false;
        state.LastOutgoingShotTargetToken = IntPtr.Zero;
        state.LastOutgoingShotAt = 0f;
        state.LastOutgoingShotWasStationary = false;
    }

    internal static bool IsPinned(int soldierId)
        => Settings.DangerReactionsEnabled.Value &&
           AiState.ContactStates.TryGetValue(soldierId, out var state) && state.Pinned;

    internal static bool ShouldHoldCover(int soldierId, float now)
        => Settings.ContactResponseEnabled.Value &&
           AiState.ContactStates.TryGetValue(soldierId, out var state) && now < state.HoldCoverUntil;

    internal static bool MayWriteCoverAssignment(Soldier soldier)
    {
        var soldierId = soldier.GetInstanceID();
        return _coverAssignmentExecutorSoldierId == soldierId ||
               !ShouldControlTacticalPosition(soldier);
    }

    internal static void ExecuteOwnedCoverWrite(Soldier soldier, Action nativeWrite)
    {
        var previousCoverExecutor = _coverAssignmentExecutorSoldierId;
        try
        {
            _coverAssignmentExecutorSoldierId = soldier.GetInstanceID();
            nativeWrite();
        }
        finally
        {
            _coverAssignmentExecutorSoldierId = previousCoverExecutor;
        }
    }

    internal static bool ShouldHoldEngagement(int soldierId, float now)
        => Settings.ContactResponseEnabled.Value &&
           AiState.ContactStates.TryGetValue(soldierId, out var state) && now < state.EngagementHoldUntil;

    internal static bool HasActiveContact(int soldierId, float now)
        => Settings.ContactResponseEnabled.Value &&
           AiState.ContactStates.TryGetValue(soldierId, out var state) && now < state.ContactUntil;

    internal static ContactMovementSensor SenseMovement(
        SoldierAI ai,
        Soldier soldier,
        float now)
    {
        var soldierId = soldier.GetInstanceID();
        var state = AiState.GetContactState(soldierId);
        var defensivePositionControl =
            RefreshDefensivePositionOwnership(soldier, state);
        var playerHoldPositionControl =
            HasActivePlayerHoldPositionControl(soldier, state);
        if (!Settings.ContactResponseEnabled.Value && !defensivePositionControl &&
            !playerHoldPositionControl)
            return default;

        var onUsableCover = IsOnUsableCover(soldier);
        var stableCover = state.DefensiveCoverHold || state.HasDefensiveCoverAnchor;
        var currentCoverId = CurrentCoverId(soldier);
        var canClaimReachedCover = !stableCover && !IsActualCharge(soldier) &&
                                   onUsableCover && currentCoverId != IntPtr.Zero &&
                                   state.ManeuverCoverAnchorId != currentCoverId &&
                                   (IsAttackingSquadSoldier(soldier) ||
                                    TryGetAttackWaypoint(soldier, out _));

        return new ContactMovementSensor(
            GetActionableTarget(ai, soldier) != null,
            now < state.ContactUntil || IncomingFireAwareness.HasActiveCue(soldierId, now),
            state.Relocating,
            stableCover,
            onUsableCover && now < state.HoldCoverUntil,
            canClaimReachedCover,
            now < state.EngagementHoldUntil,
            defensivePositionControl || playerHoldPositionControl);
    }

    internal static bool IsActualCharge(Soldier soldier)
    {
        try
        {
            // A native charge flag cannot cancel a host-authoritative defend lease
            // after the squad has reached its defended area.
            if (ShouldControlTacticalPosition(soldier))
                return false;
            return soldier.joinedSquad?.InChargeTime() == true;
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
    }

    internal static bool TryBeginTankHide(
        SoldierAI ai,
        Soldier soldier,
        Vector3 tankPosition,
        float now)
    {
        if (!Settings.ContactResponseEnabled.Value ||
            TryGetPlayerHoldOrder(soldier, out _, out _))
        {
            return false;
        }

        var soldierId = soldier.GetInstanceID();
        var state = AiState.GetContactState(soldierId);
        if (state.Relocating)
        {
            // Finish the movement already chosen by the FSM. Replacing a live
            // destination every armor scan recreates the back-and-forth failure.
            return true;
        }

        if (now < state.NextUrgentCoverDecisionAt)
            return false;

        state.NextUrgentCoverDecisionAt = now + UrgentCoverReassessmentSeconds;
        var searchRadius = Mathf.Max(
            Settings.ContactCoverSearchRadius.Value,
            Settings.TankEscapeDistance.Value);
        var cover = FindCover(
            soldier,
            tankPosition,
            searchRadius,
            state,
            now,
            CoverSelectionMode.Urgent,
            null,
            respectAttackWaypoint: false,
            evaluateFiringQuality: false,
            out _,
            out var searchDeferred);
        if (searchDeferred)
        {
            state.NextUrgentCoverDecisionAt = DeferredCoverRetryAt(soldierId, now);
            return false;
        }
        if (cover == null)
        {
            RecordTankCoverSearchFailure(state, now);
            SetCoverState(state, InfantryCoverState.WaitingForSafeMove, soldierId,
                "no tank-masked cover is reachable");
            return false;
        }

        if (!BeginRelocation(ai, soldier, state, cover, soldierId, now))
        {
            RecordTankCoverSearchFailure(state, now);
            SetCoverState(state, InfantryCoverState.WaitingForSafeMove, soldierId,
                "tank-masked cover could not be assigned");
            return false;
        }

        // A reachable cover route is the "new candidate cover" that re-arms the wait.
        state.ConsecutiveTankCoverFailures = 0;
        state.LastTankCoverFailureAt = 0f;
        AiState.TankCoverHideUntil.Remove(soldierId);
        SetCoverState(state, InfantryCoverState.Moving, soldierId,
            "committed to tank-masked cover");
        return true;
    }

    internal static bool TryHoldTankCover(
        SoldierAI ai,
        Soldier soldier,
        float now,
        float deltaTime)
    {
        var id = soldier.GetInstanceID();
        if (!AiState.IsHidingFromTank(id, now))
            return false;

        var pose = IsOnUsableCover(soldier)
            ? StationaryHoldPose(soldier)
            : SoldierPose.Prone;
        StopDangerMovement(ai, soldier, pose, deltaTime);
        return true;
    }

    private static void RecordTankCoverSearchFailure(ContactResponseState state, float now)
    {
        state.ConsecutiveTankCoverFailures = TankCoverWaitCore.RecordFailure(
            state.ConsecutiveTankCoverFailures, state.LastTankCoverFailureAt, now);
        state.LastTankCoverFailureAt = now;
    }

    /// <summary>
    /// After the urgent tank-cover search has conclusively failed, a soldier with no
    /// reachable tank-masked cover stops being movement-inhibited by tank fear and
    /// resumes his orders instead of lying prone in the open forever. Returns true when
    /// the wait has been abandoned (caller must not re-halt); the streak self-re-arms
    /// through <see cref="TankCoverWaitCore"/> when a search finds cover or the tank
    /// threat materially changes.
    /// </summary>
    internal static bool TryResumeFromUnreachableTankCover(
        SoldierAI ai,
        Soldier soldier,
        float now)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        if (!TankCoverWaitCore.ShouldResumeOrders(state.ConsecutiveTankCoverFailures))
            return false;

        // Restore locomotion once and re-request the path native movement was halted
        // from. Idempotent across the 0.75s tank scans: once moveCharacter is back true
        // the soldier is already executing his order, so no repeated path refresh.
        var wasHalted = !ai.moveCharacter;
        var owner = ApplyMovementDecision(
            ai, soldier, Time.deltaTime, now, MovementOwner.OrderedMove,
            SoldierPose.Crouch, "tank-nocover-resume");
        if (wasHalted && owner == MovementOwner.OrderedMove &&
            HasCommittedDestination(soldier))
        {
            RefreshPath(ai, "Tank-fear no-cover resume path refresh failed");
        }

        return true;
    }

    internal static void UpdateSuppressionReaction(
        SoldierAI ai,
        Soldier soldier,
        float now,
        float deltaTime)
    {
        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);
        if (!Settings.DangerReactionsEnabled.Value)
        {
            // Flame evasion is owned by this setting. Drop its short-lived marker
            // immediately so contact movement and fire are not held for the old
            // destination's remaining lifetime after a live toggle-off.
            AiState.FlameEvasionUntil.Remove(id);
            DisableSuppressionReaction(ai, soldier, state, id, now);
            return;
        }

        var suppression = soldier.GetSuppressionValue();
        UpdatePinnedState(state, suppression, now);
        if (state.Pinned)
        {
            state.SuppressionPoseOwned = true;
            state.SuppressionCrouchUntil = now + TacticalCrouchPersistenceSeconds;
            if (state.Relocating)
                PauseRelocation(state, id, now, true);

            // Immediate fire is more dangerous than remaining in a textbook pinned
            // posture. Keep the pin latched, but yield movement until clear of flame.
            if (AiState.IsFlameEvading(id, now))
            {
                state.SuppressionMovementOwned = false;
                return;
            }

            ApplyPinnedSuppression(ai, soldier, state, now, deltaTime);
            return;
        }

        if (state.SuppressionMovementOwned || state.SuppressionFireInhibited ||
            state.RelocationPausedBySuppression)
        {
            ReleasePinnedSuppression(ai, soldier, state, id, now);
        }

        if (suppression >= Settings.CrouchSuppression.Value)
        {
            state.SuppressionPoseOwned = true;
            state.SuppressionCrouchUntil = now + TacticalCrouchPersistenceSeconds;
            ApplyArbitratedPose(
                ai, soldier, now, resolveDecisionTail: true, SuppressionRecoveryPose(soldier), "suppr-band");
            return;
        }

        if (!state.SuppressionPoseOwned)
            return;

        if (now < state.SuppressionCrouchUntil)
        {
            ApplyArbitratedPose(
                ai, soldier, now, resolveDecisionTail: true, SuppressionRecoveryPose(soldier), "suppr-window");
            return;
        }

        state.SuppressionPoseOwned = false;
        state.SuppressionCrouchUntil = 0f;
    }

    internal static bool ApplyPinnedSuppression(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        float now,
        float deltaTime)
    {
        var soldierId = soldier.GetInstanceID();
        if (AiState.IsFlameEvading(soldierId, now))
        {
            state.SuppressionMovementOwned = false;
            return false;
        }

        state.SuppressionMovementOwned = true;
        state.SuppressionPoseOwned = true;

        // The first reaction to a hard burst is shock: get down and stop exposing
        // yourself. Once that brief reaction passes, the soldier remains stationary
        // but is allowed to aim and return fire instead of becoming inert forever.
        // The arbiter reads PinnedFireBlockedUntil directly and owns the flag; this only
        // maintains the descriptive state other systems read.
        state.SuppressionFireInhibited = now < state.PinnedFireBlockedUntil;
        if (state.SuppressionFireInhibited)
            soldier.StopFire();

        ApplyMovementDecision(
            ai, soldier, deltaTime, now, MovementOwner.Free, SuppressionPose(soldier), "pinned");
        if (state.HasThreatPosition)
            FaceThreatWhenStationary(ai, soldier, state.LastThreatPosition);
        return true;
    }

    private static void DisableSuppressionReaction(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now)
    {
        var ownedMovement = state.SuppressionMovementOwned;
        var pausedRelocation = state.RelocationPausedBySuppression;

        state.Pinned = false;
        state.PinnedUntil = 0f;
        state.PinnedFireBlockedUntil = 0f;
        state.PinnedSince = 0f;
        state.PinnedImmunityUntil = 0f;
        state.SuppressionMovementOwned = false;
        state.SuppressionPoseOwned = false;
        state.SuppressionCrouchUntil = 0f;
        state.SuppressionFireInhibited = false;
        state.RelocationPausedBySuppression = false;

        if (pausedRelocation && state.Relocating)
            ResumePausedRelocation(ai, soldier, state, soldierId, now, "Suppression-disabled cover path resume failed");

        if (ownedMovement)
        {
            ApplyMovementDecision(
                ai, soldier, Time.deltaTime, now, MovementOwner.OrderedMove,
                SoldierPose.Crouch, "suppression-disabled");
        }
    }

    private static void ReleasePinnedSuppression(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now)
    {
        var ownedMovement = state.SuppressionMovementOwned;
        state.SuppressionMovementOwned = false;
        state.SuppressionFireInhibited = false;
        state.PinnedFireBlockedUntil = 0f;

        if (state.RelocationPausedBySuppression)
        {
            state.RelocationPausedBySuppression = false;
            if (state.Relocating)
                ResumePausedRelocation(ai, soldier, state, soldierId, now, "Suppression cover path resume failed");
        }

        if (ownedMovement)
        {
            ApplyMovementDecision(
                ai, soldier, Time.deltaTime, now, MovementOwner.OrderedMove,
                SoldierPose.Crouch, "suppression-release");
        }
    }

    private static void PauseRelocation(
        ContactResponseState state,
        int soldierId,
        float now,
        bool bySuppression)
    {
        if (!state.Relocating)
            return;

        if (bySuppression)
            state.RelocationPausedBySuppression = true;
        state.RelocateLastProgressAt = now;
        state.RelocateUntil = now + InfantryCoverPolicy.MoveProgressWindowSeconds;
        if (state.ReservedCoverId != IntPtr.Zero)
            AiState.ReserveCover(
                state.ReservedCoverId,
                state.RelocateDestinationPosition,
                soldierId,
                state.RelocateUntil + 2f);
    }

    private static void ResumePausedRelocation(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now,
        string warning)
    {
        state.RelocateLastProgressAt = now;
        state.RelocateUntil = now + InfantryCoverPolicy.MoveProgressWindowSeconds;
        if (state.ReservedCoverId != IntPtr.Zero)
            AiState.ReserveCover(
                state.ReservedCoverId,
                state.RelocateDestinationPosition,
                soldierId,
                state.RelocateUntil + 2f);
        // Only re-path a soldier nothing is currently holding. This replaces the
        // RelocationPausedByCloseFire read: "a close-contact halt still owns him" is one
        // case of the arbiter reporting a halting owner, and asking the ladder is both
        // cheaper to reason about and impossible to leave stale.
        if (!MovementArbiterCore.Halts(
                ResolveMovementOwner(soldier, state, soldierId, now, MovementOwner.Free)))
        {
            RefreshPath(ai, warning);
        }
    }

    private static bool TryGetAttackWaypoint(Soldier soldier, out Vector3 waypoint)
    {
        waypoint = default;
        try
        {
            var squad = soldier.joinedSquad;
            if (squad == null || squad.order != Order.attackFromSide)
                return false;

            waypoint = squad.moveOrderPosition;
            return !float.IsNaN(waypoint.x) && !float.IsInfinity(waypoint.x) &&
                   !float.IsNaN(waypoint.y) && !float.IsInfinity(waypoint.y) &&
                   !float.IsNaN(waypoint.z) && !float.IsInfinity(waypoint.z);
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
    }

    private static Spottable? GetActionableTarget(SoldierAI ai, Soldier soldier)
    {
        if (Settings.PerceptionEnabled.Value)
        {
            var soldierId = soldier.GetInstanceID();
            if (!AiState.TargetMemory.TryGetValue(soldierId, out var memory) ||
                !memory.HasConfirmedTarget)
            {
                return null;
            }

            var visible = TargetAcquisition.GetUsableAiTarget(ai);
            if (TargetAcquisition.MatchesTarget(visible, memory.TargetToken))
                return visible;

            var current = TargetAcquisition.GetUsableSoldierTarget(soldier);
            if (!TargetAcquisition.MatchesTarget(current, memory.TargetToken))
                return null;

            // SoldierAI fires through visibleTarget, so mirror the matching native
            // target when the staggered update exposed it only on Soldier.
            ai.visibleTarget = current;
            return current;
        }

        var target = TargetAcquisition.GetUsableAiTarget(ai) ??
                     TargetAcquisition.GetUsableSoldierTarget(soldier);
        if (!TargetAcquisition.TryGetTargetSnapshot(target, out var targetToken, out _))
            return null;

        if (!TargetAcquisition.MatchesTarget(ai.visibleTarget, targetToken))
            ai.visibleTarget = target;
        return target;
    }

    private static void RefreshPath(SoldierAI ai, string warning)
    {
        try
        {
            ai.UpdatePath();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"{warning}: {ex.Message}");
        }
    }

    /// <summary>
    /// THE per-soldier fire arbiter (plan 017). Computes the single reason the trigger is
    /// withheld this frame, in strict priority order, from existing predicates - the one
    /// place the fire channel is decided. It replaces the old many-writers /
    /// <c>FireRestorePending</c> restore handshake, whose failure mode was a soldier left
    /// permanently mute because no site re-ran the consume step after a transition.
    /// </summary>
    internal static FireBlocker ComputeFireDecision(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        float now)
    {
        var id = soldier.GetInstanceID();

        // a. Not ours to write: a mounted gunner's fire is owned by the separate mounted
        // suppression channel, and a dead or externally driven soldier keeps native fire.
        if (!soldier.IsAlive || !AiOwnership.IsAutonomous(soldier) || soldier.IsOnVehicle())
            return FireBlocker.NativeControl;

        var dangerReactions = Settings.DangerReactionsEnabled.Value;
        var blocker = FireArbiterCore.Resolve(
            false,
            // b. A required action owns the soldier until it completes.
            state.ExposedReloadProneOwned,
            // c. Lethal hazard.
            dangerReactions && (soldier.IsOnFire || AiState.IsFlameEvading(id, now)),
            // d. Bounded shock reaction to being pinned.
            IsPinned(id) && now < state.PinnedFireBlockedUntil,
            // e. Out of engagement range, or small arms against armor.
            state.FireInhibitedByRange || state.FireInhibitedByArmoredTarget,
            // f. Evaluated below: it is the LOWEST-priority blocker and the only
            // interop-expensive one, so anything above already decided the frame.
            false);
        if (blocker != FireBlocker.None)
            return blocker;

        return IsMovingWithoutMovingFire(ai, soldier, state)
            ? FireBlocker.Moving
            : FireBlocker.None;
    }

    private static bool IsMovingWithoutMovingFire(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state)
    {
        // A contact-owned hard stop is already cancelling locomotion this frame. Residual
        // velocity must not withhold the trigger from a soldier who has halted to engage.
        if (state.MovementInhibitedByContactResponse && !state.SuppressionMovementOwned)
            return false;

        // Player-led squad members keep moveCharacter set while following their leader even
        // when they have halted to engage, so use actual locomotion.
        return soldier.IsMoving() && !HandheldWeaponClassifier.AllowsMovingFire(soldier, ai);
    }

    /// <summary>
    /// THE single write site for <c>allowFireAtEnemy</c> on the foot-soldier path. Resolves
    /// the arbiter and applies it; no other site writes the flag for a soldier on foot.
    /// Because the decision is recomputed rather than latched, a soldier whose blocker
    /// clears regains permission on the very next frame with no handshake to miss.
    /// <paramref name="authoritative"/> marks the director's per-soldier tick, which
    /// re-asserts permission at the same cadence the deleted grant sites used; the
    /// per-frame movement passes only act on a change so they never overwrite the native
    /// flag on frames where this mod has nothing to say.
    /// </summary>
    internal static void ApplyFireDecision(
        SoldierAI ai,
        Soldier soldier,
        float now,
        bool authoritative)
    {
        if (ai == null || soldier == null || !MultiplayerAuthority.CanMutateGameplay())
            return;

        var state = AiState.GetContactState(soldier.GetInstanceID());
        var blocker = ComputeFireDecision(ai, soldier, state, now);
        var changed = blocker != state.LastFireBlocker;
        TraceFireDecision(soldier, state, blocker, changed, now);
        state.FireInhibitedByMovement = blocker == FireBlocker.Moving;
        if (blocker == FireBlocker.NativeControl)
            return;

        if (FireArbiterCore.MayFire(blocker))
        {
            // This is a permission flag, not a request to shoot. Releasing it without a
            // current target is necessary so a later native acquisition is not blocked by
            // a stale false value owned by this mod.
            if (authoritative || changed)
                ai.allowFireAtEnemy = true;
            return;
        }

        ai.allowFireAtEnemy = false;

        // The soldier keeps tracking the threat while the arbiter withholds the trigger
        // (plan 017 item 5) - that is what lets him shoot the instant he halts instead of
        // re-acquiring. Only a lethal hazard or the pinned shock breaks his aim.
        if (blocker is FireBlocker.Hazard or FireBlocker.PinnedShock)
            ai.aimingEnemy = false;
        if (blocker == FireBlocker.Range)
            ai.targetInWeaponRange = false;
        soldier.StopFire();
    }

    private static void TraceFireDecision(
        Soldier soldier,
        ContactResponseState state,
        FireBlocker blocker,
        bool changed,
        float now)
    {
        if (!changed)
            return;

        var previous = state.LastFireBlocker;
        state.LastFireBlocker = blocker;
        if (!Settings.VerboseLogging.Value)
            return;

        AiState.Trace(
            $"Fire decision: soldier {soldier.GetInstanceID()} " +
            $"{FireBlockerTag(previous)}->{FireBlockerTag(blocker)} " +
            $"moving={(soldier.IsMoving() ? 1 : 0)} " +
            $"relocating={(state.Relocating ? 1 : 0)} " +
            $"pinnedShockRemain={Mathf.Max(0f, state.PinnedFireBlockedUntil - now):0.0}s");
    }

    private static string FireBlockerTag(FireBlocker blocker)
        => blocker switch
        {
            FireBlocker.RequiredAction => "required-action",
            FireBlocker.Hazard => "hazard",
            FireBlocker.PinnedShock => "pinned-shock",
            FireBlocker.Range => "range",
            FireBlocker.Moving => "moving",
            FireBlocker.NativeControl => "native",
            _ => "may-fire"
        };

    private static void UpdatePinnedState(ContactResponseState state, int suppression, float now)
    {
        if (!Settings.DangerReactionsEnabled.Value)
        {
            state.Pinned = false;
            state.PinnedUntil = 0f;
            state.PinnedFireBlockedUntil = 0f;
            state.PinnedSince = 0f;
            state.PinnedImmunityUntil = 0f;
            return;
        }

        // While already pinned, a bounded time cap can force a release even under
        // suppression still above the normal release threshold; check it before
        // the ordinary high-suppression re-affirmation below so the cap actually
        // fires instead of being pre-empted every frame by the suppression branch.
        if (state.Pinned)
        {
            var release = PinnedReleaseCore.EvaluatePinnedRelease(
                state.PinnedSince,
                state.PinnedUntil,
                suppression,
                Settings.ProneReleaseSuppression.Value,
                Settings.MaximumPinnedSeconds.Value,
                now);
            if (release.Released)
            {
                state.Pinned = false;
                state.PinnedFireBlockedUntil = 0f;
                state.PinnedImmunityUntil = release.GrantsImmunity
                    ? now + Settings.PinnedImmunitySeconds.Value
                    : 0f;
                return;
            }

            if (suppression >= Settings.ProneSuppression.Value)
                state.PinnedUntil = Mathf.Max(state.PinnedUntil, now + Settings.PinnedMinimumSeconds.Value);
            return;
        }

        // A time-cap release grants a short re-pin immunity window: the same
        // incoming fire that forced the release must not instantly re-pin the
        // soldier before it can act on it.
        if (PinnedReleaseCore.ShouldEngagePin(
                suppression, Settings.ProneSuppression.Value, state.PinnedImmunityUntil, now))
        {
            state.PinnedFireBlockedUntil = now + PinnedShockSeconds;
            state.Pinned = true;
            state.PinnedSince = now;
            state.PinnedUntil = Mathf.Max(state.PinnedUntil, now + Settings.PinnedMinimumSeconds.Value);
        }
    }

    // Halt helpers are OWNER DECLARATIONS now, not independent writers (plan 018) - the
    // same treatment plan 014 gave them on the pose channel. They record the ownership the
    // caller is claiming and hand the frame to the single movement arbiter, which decides
    // whether that claim actually wins and performs the one moveCharacter/StopMove write.
    // The caller's <paramref name="fallbackPose"/> is still only the pose arbiter's
    // ownerless fallback.
    internal static MovementOwner StopTacticalMovement(
        SoldierAI ai,
        Soldier soldier,
        SoldierPose fallbackPose,
        float deltaTime,
        string proposalSource = "stop-tactical")
    {
        AiState.GetContactState(soldier.GetInstanceID()).MovementInhibitedByContactResponse = true;
        return ApplyMovementDecision(
            ai, soldier, deltaTime, Time.time, MovementOwner.Free, fallbackPose, proposalSource);
    }

    /// <param name="declared">The owner this halt claims when it has no state of its own
    /// to resolve from (a grenade-safety or required-action halt). Halts whose ownership
    /// IS state - tank hide, the stall watchdog, burning - leave it Free and let the
    /// arbiter read their timers, so their rank is not silently promoted.</param>
    internal static MovementOwner StopDangerMovement(
        SoldierAI ai,
        Soldier soldier,
        SoldierPose fallbackPose,
        float deltaTime,
        string proposalSource = "stop-danger",
        MovementOwner declared = MovementOwner.Free)
        => ApplyMovementDecision(
            ai, soldier, deltaTime, Time.time, declared, fallbackPose, proposalSource);

    // Low-level soldier command executor. Tactical feature modules request these
    // mutations through GroundAiDirector so movement, pose, aim, and fire policy
    // cannot independently fight over the same native state. Fire PERMISSION is not
    // among them any more: only ApplyFireDecision writes allowFireAtEnemy.
    internal static void ExecuteStopFire(Soldier soldier)
        => soldier.StopFire();

    internal static void ExecuteAim(Soldier soldier, bool aiming)
        => soldier.SetAiming(aiming);

    internal static void ExecuteHazardEscape(
        SoldierAI ai,
        Soldier soldier,
        Vector3 escape)
    {
        ai.MoveDirectlyToward(escape, 1.5f);
        // The movement decision is committed FIRST (plan 019): the pose ladder now reads the
        // committed movement owner, so resolving the pose before the escape was granted
        // would arbitrate this frame against the PREVIOUS decision - the one stale case that
        // could still put a running man on his belly.
        ApplyMovementDecision(
            ai, soldier, Time.deltaTime, Time.time, MovementOwner.HazardEscape,
            SoldierPose.Crouch, "hazard-escape");
        SetTacticalPose(ai, soldier, SoldierPose.Crouch, "hazard-escape");
    }
}
