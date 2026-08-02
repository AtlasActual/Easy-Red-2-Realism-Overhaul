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
    private static int _coverAssignmentExecutorSquadId;

    // Decision scheduling. The TacticalMove pipeline (SharedTacticalMovePrefix + the
    // MaintainOwnedPose/ApplyFireDecision postfix) runs for every owned soldier EVERY
    // frame, but its interop-heavy per-soldier DECISION work — which stationary hold owns
    // the soldier, and the cover/suppression pose RESOLUTION — is scheduled by two rules
    // working together:
    //
    //   1. A wall-clock floor, so a soldier re-decides at most 30 times a second no
    //      matter the framerate. This bounds TOTAL decision work per second. It replaced
    //      a pure frame modulus, which tied the cadence to the framerate and had a
    //      210 FPS run re-deciding every 14ms — 3.5x a 60 FPS run's work for decisions
    //      no player can see change that often.
    //   2. A per-frame allowance, so the soldiers whose floors happen to expire together
    //      do not all re-decide on the same frame. The floor alone left that clustering
    //      free to happen, and it is the measured spike shape: no single slow call, every
    //      site elevated at once.
    //
    // Between decision frames a soldier holds its already-latched decisions through cheap
    // write-through re-assertion. SAFETY reactions (fire/flame/pinned/reload/stall)
    // and the locomotion gate itself (moveCharacter/sprint) stay per-frame for
    // every soldier and are never budgeted. Every timer that gates behavior (ContactUntil,
    // EngagementHoldUntil, HoldCoverUntil, TacticalPoseHoldUntil, MovementStallHoldUntil)
    // is Time.time based, so deferring a decision by a frame cannot desync them.
    private const float MinimumDecisionIntervalSeconds = 1f / 30f;

    // 60Hz floor for the write-through maintenance tail: at or below 60 FPS it runs every
    // frame exactly as before, so only high-framerate runs change.
    private const float WriteThroughMaintenanceSeconds = 1f / 60f;

    // The 30Hz floor already caps TOTAL decisions per second, so the problem this budget
    // solves is not volume but CLUSTERING: nothing stopped thirty soldiers' floors from
    // expiring on the same frame, and that is the measured spike shape exactly — no
    // single slow call, every site elevated at once. The budget spreads that wave over
    // consecutive frames.
    //
    // It is expressed per SECOND, not per frame, and converted using the frame's own
    // duration. A fixed per-frame count would silently throttle low-framerate machines
    // (8 per frame at 60 FPS starves a 60-soldier battle) while a 5ms frame and a 17ms
    // frame can plainly afford different amounts of work. 1800/s is what a 60-soldier
    // battle already consumes at the 30Hz floor, so this re-shapes the existing work
    // rather than removing any of it.
    private const float TargetDecisionsPerSecond = 1800f;
    private const int MinimumDecisionsPerFrame = 4;
    private const int MaximumDecisionsPerFrame = 48;

    // Safety net only, in WALL-CLOCK time. A frame count here defeats the budget it is
    // meant to backstop: 12 frames is 68ms at 175 FPS, barely longer than the 33ms
    // service interval, so in a battle large enough for demand to exceed the allowance
    // nearly every deferred soldier aged past it within a frame or two and was admitted
    // anyway — measured as decided=23 against a budget of 10, with deferred reading 0
    // because forced admissions never take the denial path. At 0.2s the budget does the
    // scheduling and this only catches genuine starvation, which is its job.
    //
    // The consequence is deliberate: when a battle is too large to service every soldier
    // at 30Hz within the allowance, the per-soldier rate degrades gracefully (about 19Hz
    // at 90 soldiers) instead of the frame cost growing without limit. Bounded frame cost
    // is the point.
    private const float MaxStarvationSeconds = 0.2f;

    // Ceiling on how many overdue soldiers may bypass the budget in one frame. The
    // starvation guard exists so no individual soldier is starved; it must not become a
    // route for the whole battle to bypass the allowance at once.
    private const int MaxStarvationOverridesPerFrame = 3;

    private static int _serviceFrame = -1;
    private static float _lastServiceFrameAt = -1f;
    private static float _smoothedFrameSeconds = 1f / 60f;
    private static int _frameDecisionBudget = MinimumDecisionsPerFrame;
    private static int _servicedThisFrame;
    private static int _deniedThisFrame;
    private static int _starvationOverridesThisFrame;

    // Diagnostic only: lets the probe show whether the allowance is actually binding,
    // rather than being set so high it never does anything.
    internal static int LastServicedCount => _servicedThisFrame;

    internal static int LastDeniedCount => _deniedThisFrame;

    internal static bool RunsDecisionThisFrame(int soldierId)
    {
        var frame = Time.frameCount;
        var state = AiState.GetContactState(soldierId);
        // One verdict per soldier per frame, replayed to every caller: resolving it
        // independently would let the prefix decide and the postfix skip, splitting a
        // decision across two different cadences.
        if (state.DecisionVerdictFrame == frame)
            return state.DecisionVerdict;

        if (frame != _serviceFrame)
        {
            // Measured here rather than read from Time.deltaTime, which reports the fixed
            // step when this is reached from a physics-driven path and would size the
            // budget against the wrong clock. frameCount only advances on rendered frames,
            // so the gap between resets is the real frame duration.
            var realtime = Time.realtimeSinceStartup;
            var frameSeconds = _lastServiceFrameAt >= 0f
                ? Mathf.Clamp(realtime - _lastServiceFrameAt, 0.001f, 0.1f)
                : 1f / 60f;
            _lastServiceFrameAt = realtime;

            // Sized from a SMOOTHED frame time, never from the frame that just happened.
            // Using the last frame directly is a feedback loop: a hitch — from any cause —
            // hands the next frame a larger allowance, which makes that frame longer,
            // which raises the allowance again. Measured as decided=27 on a 143ms frame
            // whose allowance should have been 10. Each sample is capped at twice the
            // current average so a spike cannot drag the average up behind it.
            _smoothedFrameSeconds = Mathf.Lerp(
                _smoothedFrameSeconds,
                Mathf.Min(frameSeconds, _smoothedFrameSeconds * 2f),
                0.05f);

            _frameDecisionBudget = Mathf.Clamp(
                Mathf.CeilToInt(TargetDecisionsPerSecond * _smoothedFrameSeconds),
                MinimumDecisionsPerFrame,
                MaximumDecisionsPerFrame);

            _serviceFrame = frame;
            _servicedThisFrame = 0;
            _deniedThisFrame = 0;
            _starvationOverridesThisFrame = 0;
        }

        var runs = true;
        var now = Time.time;

        // Wall-clock floor first: a soldier that re-decided a moment ago does not need a
        // slot at all, and releasing it early leaves room for one that does.
        if (now - state.LastDecisionAt < MinimumDecisionIntervalSeconds)
        {
            runs = false;
        }
        else if (_servicedThisFrame >= _frameDecisionBudget)
        {
            // Over budget. A soldier long overdue may still override, but only a few per
            // frame: whenever a frame runs long, Time.time jumps by that whole duration
            // and EVERY soldier crosses the starvation threshold at once, so an uncapped
            // override let the entire battle bypass the budget on precisely the frame
            // that could least afford it. Measured as decided=19/deferred=0 on the two
            // worst frames of a run whose healthy frames read decided=8-11/deferred=18-24.
            // That turned an external hitch into a mod-work storm that deepened it.
            var overdue = state.LastServicedAt <= 0f ||
                          now - state.LastServicedAt >= MaxStarvationSeconds;
            if (overdue && _starvationOverridesThisFrame < MaxStarvationOverridesPerFrame)
            {
                _starvationOverridesThisFrame++;
            }
            else
            {
                runs = false;
                _deniedThisFrame++;
            }
        }

        if (runs)
        {
            state.LastDecisionAt = now;
            state.LastServicedAt = now;
            _servicedThisFrame++;
        }

        state.DecisionVerdictFrame = frame;
        state.DecisionVerdict = runs;
        return runs;
    }

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
        int id,
        float now,
        float deltaTime,
        ref bool sprint,
        bool updateFireInhibitionOnPass,
        out bool passThrough)
    {
        var state = AiState.GetContactState(id);

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
                "stagger", resolvePose: false);

            CountStaggerSkip();
            passThrough = false;
            return true;
        }

        // The last decision granted locomotion. Re-assert it through the same single write
        // site (a granting owner releases the brake; Free writes nothing and simply records
        // that this mod is not holding him), then the moving-fire gate and the owned
        // movement pose exactly as the pass-through tail does — all cheap write-through
        // (no ownership refresh, no hold selection, no cover geometry).
        ApplyResolvedMovementDecision(
            ai, soldier, state, id, staggerOwner, deltaTime, now,
            "stagger", resolvePose: false);

        // The locomotion gate above is the only part that must track the frame, because
        // native movement reads it every frame. The maintenance tail below re-asserts
        // state the game already holds — pose latch, fire permission, body facing — and
        // re-asserting it 200 times a second instead of 60 buys nothing observable while
        // multiplying this mod's interop volume by the framerate. Spike frames now show
        // no slow individual call (max 0.7ms) and 140ms of uniformly slowed calls, i.e.
        // the remaining cost IS the call count, so that is what this cuts.
        if (now < state.NextWriteThroughMaintenanceAt)
        {
            CountStaggerSkip();
            passThrough = true;
            return true;
        }

        state.NextWriteThroughMaintenanceAt = now + WriteThroughMaintenanceSeconds;

        SoldierTacticalSprintPatch.ApplyTacticalMovementPose(ai, soldier, id, now);
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

    internal static void ReactToNewCloseTarget(
        SoldierAI ai,
        Soldier soldier,
        IntPtr targetToken,
        Vector3 targetPosition,
        float now)
    {
        if (!Settings.ContactResponseEnabled.Value ||
            HorizontalDistanceSqr(soldier.transform.position, targetPosition) >
            AiBehaviorTuning.ImmediateFireDistance *
            AiBehaviorTuning.ImmediateFireDistance)
        {
            return;
        }

        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);
        var wasActuallyMoving = soldier.IsMoving(0.2f);
        var contactWasContinuous = ContactDivePolicyCore.ContactWasContinuous(
            state.AttackContactToken != IntPtr.Zero,
            state.AttackContactLastSeenAt,
            now,
            AttackContactContinuitySeconds);
        state.ContactResponseActive = true;
        state.LastThreatPosition = targetPosition;
        state.HasThreatPosition = true;
        state.ContactUntil = Mathf.Max(
            state.ContactUntil, now + ContactPersistenceSeconds);
        state.AttackContactToken = targetToken;
        state.AttackContactLastSeenAt = now;

        // The ordinary FSM preserves these higher-priority survival actions. A
        // close acquisition may prime contact memory during them, but must not
        // steal their movement/fire channel.
        var dangerReactions = Settings.DangerReactionsEnabled.Value;
        if (state.ExposedReloadSafetyOwned ||
            dangerReactions &&
            (soldier.IsOnFire || AiState.IsFlameEvading(id, now) || IsPinned(id)))
        {
            return;
        }

        TryStartContactDive(
            state,
            wasActuallyMoving,
            hasObservedTarget: true,
            contactWasContinuous: contactWasContinuous,
            now: now);
        if (state.Relocating)
            PauseRelocation(ai, soldier, state, id, now, false);
        RespondWithoutNewCover(ai, soldier, state, targetPosition, now);
        ApplyFireDecision(ai, soldier, now, authoritative: true);
    }

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
        var wasActuallyMovingWhenObserved = target != null && soldier.IsMoving(0.2f);
        var contactWasContinuous = target != null &&
                                   ContactDivePolicyCore.ContactWasContinuous(
                                       state.AttackContactToken != IntPtr.Zero,
                                       state.AttackContactLastSeenAt,
                                       now,
                                       AttackContactContinuitySeconds);
        state.SquadId = SquadIdentity.GetSquadId(soldier);
        var hasAttackRoute = TryGetAttackWaypoint(soldier, out _) &&
                             HasCommittedDestination(soldier);
        // The liveness fact, deliberately WIDER than hasAttackRoute (plan 028): the halt
        // caps below must bound every soldier his squad is walking away from, not only
        // the ones on an attackFromSide order. hasAttackRoute stays in use for the
        // cover-SELECTION inputs, which must not widen with it.
        var hasMovementOrder = HasLiveMovementOrder(soldier);
        var targetInsideAttackHalt = target != null &&
                                     HorizontalDistanceSqr(
                                         soldier.transform.position,
                                         observedTargetPosition) <=
                                     AiBehaviorTuning.EngagementHaltDistance *
                                     AiBehaviorTuning.EngagementHaltDistance;
        var underDirectFire = IncomingFireAwareness.TryGetActiveDirectCue(
            id, now, out var directFirePosition);
        var attackUnderPressure = target != null || state.Pinned ||
                                  IncomingFireAwareness.HasActiveCue(id, now);
        state.AttackFiringCommitUntil = CombatMovementPolicyCore.EnsureInitialAttackFiringCommit(
            state.AttackFiringCommitUntil,
            state.AttackObjectiveBoundActive,
            hasMovementOrder,
            attackUnderPressure,
            now,
            AiBehaviorTuning.AttackFiringHoldSeconds);
        UpdateAttackObjectiveBoundCycle(
            state, hasMovementOrder, attackUnderPressure, now);
        var (maximumAttackHaltReached, maximumOnCoverAttackHaltReached) =
            UpdateAttackProgressClock(state, hasMovementOrder, attackUnderPressure, now);
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
                InfantryCoverDecisionCore.HasStableReservationAnchor(
                    state.DefensiveCoverHold,
                    state.HasDefensiveCoverAnchor,
                    state.ManeuverCoverAnchorId != IntPtr.Zero)))
        {
            state.ReservedCoverId = IntPtr.Zero;
            state.ReservedCoverPosition = default;
            state.OccupiedCoverClaimId = IntPtr.Zero;
            state.OccupiedCoverClaimRefreshAt = 0f;
            AiState.ReleaseCoverReservation(id);
        }

        if (Settings.DangerReactionsEnabled.Value && soldier.IsOnFire)
        {
            if (state.Relocating)
                FinishRelocation(ai, soldier, state, id, now, false, false, false);
            state.EngagementHoldUntil = 0f;
            ExecuteStopFire(soldier);
            StopDangerMovement(ai, soldier, Time.deltaTime);
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
                "fsm-flame-evade");
            ExecuteStopFire(soldier);
            return;
        }

        if (target != null)
        {
            TryStartContactDive(
                state,
                wasActuallyMovingWhenObserved,
                hasObservedTarget: true,
                contactWasContinuous: contactWasContinuous,
                now: now);
        }

        // The dive is a complete combat action, not a one-frame pose request. It owns
        // the halt through the same configurable firing commitment used after an attack
        // bound, even if the target pointer briefly flickers or the commander reassesses.
        if (IsContactDiveActive(state, now) && state.HasThreatPosition)
        {
            if (state.Relocating)
                PauseRelocation(ai, soldier, state, id, now, false);
            state.EngagementHoldUntil = Mathf.Max(
                state.EngagementHoldUntil, state.ContactDiveProneUntil);
            state.NextRelocationAllowedAt = Mathf.Max(
                state.NextRelocationAllowedAt, state.ContactDiveProneUntil);
            RespondWithoutNewCover(
                ai, soldier, state, state.LastThreatPosition, now);
            return;
        }

        // Pinning owns ordinary locomotion independently of Contact Response. A
        // selected cover destination is retained, but the soldier first halts and
        // survives the burst that pinned him. Pinning itself does not create Prone.
        if (IsPinned(id))
        {
            if (state.Relocating)
                PauseRelocation(ai, soldier, state, id, now, true);
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

        // A close rifle threat overrides an already-started cover run, including
        // a defender's committed move into its assigned position. Automatic
        // close-assault weapons can keep the move because their moving-fire gate
        // remains open inside the configured range.
        var closeThreatRequiresStationaryFire = target != null &&
            HorizontalDistanceSqr(soldier.transform.position, observedTargetPosition) <=
            AiBehaviorTuning.ImmediateFireDistance * AiBehaviorTuning.ImmediateFireDistance &&
            !HandheldWeaponClassifier.AllowsMovingFire(soldier, ai);
        if (closeThreatRequiresStationaryFire)
        {
            if (state.Relocating)
                PauseRelocation(ai, soldier, state, id, now, false);
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
                "fsm-charge");
            return;
        }

        // The close-contact pause above is no longer a separate coordination flag: the
        // arbiter's own MovementHalted output says a hold stopped this soldier while he
        // was relocating, and granting movement below clears it, so this resume runs
        // exactly once per pause exactly as RelocationPausedByCloseFire did.
        if (state.Relocating && state.MovementHalted)
        {
            state.RelocateLastProgressAt = now;
            state.RelocateLastDestinationProgressAt = now;
            state.RelocateUntil = now + InfantryCoverPolicy.MoveProgressWindowSeconds;
            if (!TryRenewRelocationCoverReservation(
                    ai,
                    soldier,
                    state,
                    id,
                    now,
                    state.RelocateUntil + 2f))
            {
                return;
            }
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
                    state.RelocateLastProgressAt = now;
                    state.RelocateLastProgressPosition = soldier.transform.position;
                    state.RelocateUntil = now + InfantryCoverPolicy.MoveProgressWindowSeconds;
                    if (!TryRenewRelocationCoverReservation(
                            ai,
                            soldier,
                            state,
                            id,
                            now,
                            state.RelocateUntil + 2f))
                    {
                        return;
                    }
                }

                var routeDecision = CoverRouteRecoveryCore.Evaluate(
                    destinationDistance,
                    state.RelocateLastDistance,
                    now - state.RelocateLastDestinationProgressAt,
                    state.RelocatePathRetryUsed,
                    ai.HasPathRequest);
                if (routeDecision == CoverRouteRecoveryDecision.DestinationProgressed)
                {
                    state.RelocateLastDistance = destinationDistance;
                    state.RelocateLastDestinationProgressAt = now;
                }
                else if (routeDecision == CoverRouteRecoveryDecision.RefreshPath)
                {
                    if (!RetryRelocationPath(
                        ai,
                        soldier,
                        state,
                        id,
                        now,
                        destinationDistance,
                        "Cover route entrance retry failed"))
                    {
                        return;
                    }
                    ContinueCommittedMovement(ai, soldier, state, now);
                    return;
                }
                else if (routeDecision == CoverRouteRecoveryDecision.Abandon)
                {
                    AiState.Trace(
                        $"Contact response: soldier {id} rejected a cover route " +
                        "that never approached its destination");
                    FinishRelocation(ai, soldier, state, id, now, false, false);
                    BeginMovementStallHold(
                        ai,
                        soldier,
                        state,
                        now,
                        "cover route could not find a usable entrance");
                    return;
                }

                var stallSeconds = Mathf.Clamp(
                    InfantryCoverPolicy.MoveProgressWindowSeconds * 0.5f, 1.5f, 3f);
                if (now - state.RelocateLastProgressAt >= stallSeconds && !ai.HasPathRequest)
                {
                    if (!state.RelocatePathRetryUsed)
                    {
                        if (!RetryRelocationPath(
                            ai,
                            soldier,
                            state,
                            id,
                            now,
                            destinationDistance,
                            "Stalled cover route retry failed"))
                        {
                            return;
                        }
                        ContinueCommittedMovement(ai, soldier, state, now);
                        return;
                    }

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
                state.EngagementHoldUntil = Mathf.Max(
                    now + InfantryCoverPolicy.DecisionIntervalSeconds,
                    state.AttackFiringCommitUntil);
                state.ContactCrouchOwned = true;
                StopTacticalMovement(
                    ai,
                    soldier,
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
                    Time.deltaTime,
                    "fsm-observe-cover");
                if (state.HasThreatPosition && now < state.ContactUntil)
                    FaceThreatWhenStationary(ai, soldier, state.LastThreatPosition);
                return;
            }

            var onUsableCoverWithoutVisibleTarget = IsOnUsableCover(soldier);
            var forcedAttackProgressWithoutVisibleTarget = CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                hasMovementOrder,
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
                                        soldier.GetSuppressionValue() >= AiBehaviorTuning.CrouchSuppressionThreshold;
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
                    Time.deltaTime,
                    "fsm-defensive-hold");
                // Hold the position, not an obsolete bearing. After the immediate
                // contact fades, native scanning must be free to find a flanker.
                if (state.HasThreatPosition && now < state.ContactUntil)
                    FaceThreatWhenStationary(ai, soldier, state.LastThreatPosition);
                return;
            }

            var firingPhaseActive = CombatMovementPolicyCore.AttackFiringPhaseActive(
                state.AttackFiringCommitUntil, now);
            var wasHoldingMovement = state.MovementInhibitedByContactResponse ||
                                     state.EngagementHoldUntil > 0f;
            if (state.ContactCrouchOwned &&
                (now < state.ContactUntil || firingPhaseActive))
            {
                if (wasHoldingMovement)
                {
                    state.EngagementHoldUntil = Mathf.Max(
                        state.ContactUntil,
                        state.AttackFiringCommitUntil);
                    StopTacticalMovement(
                        ai,
                        soldier,
                        Time.deltaTime,
                        "fsm-contact-hold");
                    if (state.HasThreatPosition)
                        FaceThreatWhenStationary(ai, soldier, state.LastThreatPosition);
                }
                else
                {
                    ApplyMovementDecision(
                        ai, soldier, Time.deltaTime, now, MovementOwner.OrderedMove,
                        "fsm-contact-move");
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
                    "fsm-contact-release");
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
        // Widened with the cap (plan 028): "a squadmate is firing at this contact right
        // now" is the same fact whatever the squad's order code, and it only ever
        // authorizes an EARLIER bound than the halt cap would.
        var coordinatedAttackAdvance = hasMovementOrder &&
                                       CombatMovementPolicyCore.CoveringFireMayAuthorizeBound(
                                           state.AttackFiringCommitUntil, now) &&
                                       HasFavorableAttackAdvance(
                                           state, id, observedTargetToken, now);
        var onUsableNativeCover = IsOnUsableCover(soldier);

        // Native cover metadata is not sufficient: an exposed node inside an
        // objective is still exposed. Stable defender anchors were already checked
        // geometrically when claimed and use the separate DefensiveCoverHold path.
        var currentCoverEvaluation = default(CurrentCoverEvaluation);
        var hasCurrentCoverEvaluation = onUsableNativeCover &&
                                        TryGetCurrentCoverEvaluation(
                                            soldier,
                                            state,
                                            targetPosition,
                                            now,
                                            out currentCoverEvaluation,
                                            mayDeferFirstEval: false);
        var insideDefensiveArea = onUsableNativeCover && IsInsideDefensiveArea(soldier);
        var protectsFromCurrentThreat = hasCurrentCoverEvaluation &&
                                        currentCoverEvaluation.IsProtective;
        var hasUsableCover = state.DefensiveCoverHold ||
                             InfantryCoverDecisionCore.ShouldTreatCurrentCoverAsUsable(
                                 onUsableNativeCover,
                                 insideDefensiveArea,
                                 protectsFromCurrentThreat);
        var authorizedAttackAdvance = CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
            hasMovementOrder,
            coordinatedAttackAdvance,
            maximumAttackHaltReached,
            maximumOnCoverAttackHaltReached,
            underDirectFire,
            state.Pinned,
            hasUsableCover,
            state.ManeuverCoverMinimumHoldUntil,
            now);
        var forcedAttackProgress = authorizedAttackAdvance &&
                                   (hasUsableCover
                                       ? maximumOnCoverAttackHaltReached
                                       : maximumAttackHaltReached);
        // A coordinated-fire authorization may already be latched when the hard halt
        // deadline arrives. Treat that deadline edge as a fresh decision so a rejected
        // early bound cannot delay the liveness guarantee until the next long cadence.
        var favorableJustEstablished =
            (authorizedAttackAdvance && !state.AttackConditionsWereFavorable) ||
            (forcedAttackProgress && !state.AttackProgressForced);
        if (!authorizedAttackAdvance)
            state.AttackConditionsWereFavorable = false;
        if (hasUsableCover)
        {
            if (state.ReservedCoverId != IntPtr.Zero &&
                !TryKeepExclusiveCoverReservation(
                    soldier,
                    state,
                    id,
                    state.ReservedCoverId,
                    state.ReservedCoverPosition,
                    now,
                    now + InfantryCoverPolicy.CoverReservationLeaseSeconds))
            {
                return;
            }
            if (!authorizedAttackAdvance)
            {
                SetCoverState(state, InfantryCoverState.Holding, id,
                    "current position remains protective");
                state.ContactCrouchOwned = true;
                StopTacticalMovement(
                    ai,
                    soldier,
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
                         soldier.GetSuppressionValue() >= AiBehaviorTuning.CrouchSuppressionThreshold;
        var coverDecision = InfantryCoverDecisionCore.EvaluateNeed(new CoverNeedInput(
            hasUsableCover,
            authorizedAttackAdvance,
            coverCompromised,
            underDirectFire,
            suppressed && !hasAttackRoute,
            distance <= AiBehaviorTuning.ImmediateFireDistance,
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
                now);
            return;
        }

        state.AttackConditionsWereFavorable = authorizedAttackAdvance;
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
            out var searchDeferred,
            out var selectedCoverQuality);
        if (searchDeferred)
        {
            state.NextDecisionAt = DeferredCoverRetryAt(id, now);
            if (coverDecision.SelectionMode == CoverSelectionMode.Urgent)
                state.NextUrgentCoverDecisionAt = state.NextDecisionAt;
            if (InfantryCoverDecisionCore.ShouldKeepMovingDuringDeferredCoverSearch(
                    searchDeferred,
                    hasUsableCover,
                    distance <= AiBehaviorTuning.ImmediateFireDistance,
                    hasMovementOrder,
                    HasCommittedDestination(soldier),
                    CombatMovementPolicyCore.AttackFiringPhaseActive(
                        state.AttackFiringCommitUntil,
                        now)))
            {
                // A full cover inventory is globally budgeted. Preserve the existing
                // route for this fraction of a second instead of creating a visible
                // stop/start in exposed ground before the retry selects nearby cover.
                // A committed firing phase is the exception: its minimum stop is absolute.
                ContinueAttackObjectiveMovement(
                    ai, soldier, state, id, now, suppressed, forcedAttackProgress);
                return;
            }
            RespondWithoutNewCover(
                ai,
                soldier,
                state,
                targetPosition,
                now);
            return;
        }
        if (cover == null)
        {
            SetCoverState(state, InfantryCoverState.WaitingForSafeMove, id,
                "no protective cover with an acceptable route was found");
            // A soldier with no reachable cover backs off its next assessment so it
            // stays in the current fighting halt instead of looping search -> fail -> search.
            state.ConsecutiveCoverSearchFailures++;
            state.NextDecisionAt = now + CoverSearchBackoffCore.NextDecisionDelaySeconds(
                InfantryCoverPolicy.DecisionIntervalSeconds,
                state.ConsecutiveCoverSearchFailures);
            if (coverDecision.SelectionMode == CoverSelectionMode.Urgent)
                state.NextUrgentCoverDecisionAt = state.NextDecisionAt;
            if (authorizedAttackAdvance && (!hasUsableCover || forcedAttackProgress))
            {
                TraceOnCoverHaltCapBound(
                    id, hasUsableCover, coordinatedAttackAdvance);
                ContinueAttackObjectiveMovement(
                    ai, soldier, state, id, now, suppressed, forcedAttackProgress);
                return;
            }
            RespondWithoutNewCover(
                ai,
                soldier,
                state,
                targetPosition,
                now);
            return;
        }

        if (hasUsableCover &&
            hasCurrentCoverEvaluation &&
            !InfantryCoverDecisionCore.ShouldLeaveCurrentCoverForAttackBound(
                currentCoverEvaluation.Quality,
                selectedCoverQuality,
                forcedAttackProgress))
        {
            SetCoverState(state, InfantryCoverState.Holding, id,
                "selected bound would abandon a better protective firing position");
            state.ContactCrouchOwned = true;
            StopTacticalMovement(
                ai,
                soldier,
                Time.deltaTime,
                "fsm-better-current-cover");
            FaceThreatWhenStationary(ai, soldier, targetPosition);
            return;
        }

        // Preserve the deadline authorization through the selected cover route. The
        // movement/pose contract keeps this forced advance crouched while under contact.
        state.AttackProgressForced = forcedAttackProgress;
        if (BeginRelocation(ai, soldier, state, cover, id, now))
        {
            if (authorizedAttackAdvance)
                TraceOnCoverHaltCapBound(
                    id, hasUsableCover, coordinatedAttackAdvance);
            SetCoverState(state, InfantryCoverState.Moving, id,
                $"committed to {coverDecision.SelectionMode.ToString().ToLowerInvariant()} cover move");
        }
        else
        {
            // A failed route assignment did not actually consume the forced advance.
            // ContinueAttackObjectiveMovement below will re-latch it when the hard
            // deadline requires the objective route to resume.
            state.AttackProgressForced = false;
            SetCoverState(state, InfantryCoverState.WaitingForSafeMove, id,
                "selected cover could not be assigned");
            if (authorizedAttackAdvance && (!hasUsableCover || forcedAttackProgress))
            {
                TraceOnCoverHaltCapBound(
                    id, hasUsableCover, coordinatedAttackAdvance);
                ContinueAttackObjectiveMovement(
                    ai, soldier, state, id, now, suppressed, forcedAttackProgress);
                return;
            }
            RespondWithoutNewCover(
                ai,
                soldier,
                state,
                targetPosition,
                now);
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
            !state.Relocating && !state.ContactCrouchOwned && !state.CoverClearancePoseOwned &&
            !state.HasHaltSpacingTarget && state.HaltSpacingMoveUntil <= 0f)
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
        state.RelocateLastDestinationProgressAt = 0f;
        state.RelocatePathRetryUsed = false;
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
        state.EngagementHoldUntil = 0f;
        state.HaltSpacingMoveUntil = 0f;
        state.HaltSpacingAttemptedThisEpisode = false;
        state.HaltSpacingAttemptPosition = default;
        state.HasHaltSpacingTarget = false;
        state.HaltSpacingTarget = default;
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
                "contact-disable");
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
                "yield-authority");
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
        state.RelocateUntil = 0f;
        state.RelocateLastDistance = 0f;
        state.RelocateLastProgressAt = 0f;
        state.RelocateLastDestinationProgressAt = 0f;
        state.RelocatePathRetryUsed = false;
        state.RelocateLastProgressPosition = default;
        state.NextUrgentCoverDecisionAt = 0f;
        state.CoverState = InfantryCoverState.Holding;
        state.RelocationPausedBySuppression = false;
        state.RelocateDestinationPointer = IntPtr.Zero;
        state.RelocateDestinationPosition = default;
        state.ReservedCoverId = IntPtr.Zero;
        state.ReservedCoverPosition = default;
        state.ConsecutiveCoverSearchFailures = 0;
        state.ContactResponseActive = false;
        state.MovementInhibitedByContactResponse = false;
        state.ContactCrouchOwned = false;
        state.ContactUntil = 0f;
        state.EngagementHoldUntil = 0f;
        state.HaltSpacingMoveUntil = 0f;
        state.HaltSpacingAttemptedThisEpisode = false;
        state.HaltSpacingAttemptPosition = default;
        state.HasHaltSpacingTarget = false;
        state.HaltSpacingTarget = default;
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
            RecordSquadShooter(state.SquadId, soldierId);
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

        // Squad ids are reissued between battles, so a surviving ring would answer the
        // covering-fire question with the previous battle's shooters.
        SquadFireRings.Clear();
    }

    // Well under the CoveringFireFreshSeconds window the scan itself tests, so a cached
    // answer can never outlive the freshness it is reporting on.
    private const float CoveringFireCacheSeconds = 0.25f;

    /// <summary>
    /// The most recent distinct shooters in a squad, newest-biased. The covering-fire
    /// question is "has ANY squadmate laid down fresh stationary fire", and answering it
    /// by walking every tracked soldier is O(n) per attacker — O(n^2) across a battle,
    /// which is the one cost in this mod that grows faster than the battle does. Easy
    /// Red 2 fields hundreds of soldiers, so that scan is the thing that breaks first.
    ///
    /// A ring of this size covers an entire ordinary squad, making the answer identical
    /// for them. For a larger squad it consults the most recent shooters, which — since
    /// the predicate demands a shot fresher than CoveringFireFreshSeconds — is exactly
    /// the set that could satisfy it.
    /// </summary>
    private const int RecentShootersPerSquad = 8;

    private sealed class SquadFireRing
    {
        internal readonly int[] SoldierIds = new int[RecentShootersPerSquad];
        internal int Count;
        internal int Cursor;
    }

    private static readonly Dictionary<int, SquadFireRing> SquadFireRings = new();

    private static void RecordSquadShooter(int squadId, int soldierId)
    {
        if (squadId == 0)
            return;

        if (!SquadFireRings.TryGetValue(squadId, out var ring))
        {
            ring = new SquadFireRing();
            SquadFireRings[squadId] = ring;
        }

        // Already tracked: the ring holds WHO to consult, and their shot recency is read
        // live at query time, so re-adding would only crowd out other shooters.
        for (var i = 0; i < ring.Count; i++)
        {
            if (ring.SoldierIds[i] == soldierId)
                return;
        }

        ring.SoldierIds[ring.Cursor] = soldierId;
        ring.Cursor = (ring.Cursor + 1) % RecentShootersPerSquad;
        if (ring.Count < RecentShootersPerSquad)
            ring.Count++;
    }

    private static bool CacheCoveringFire(ContactResponseState state, float now, bool established)
    {
        state.CoveringFireCheckedUntil = now + CoveringFireCacheSeconds;
        state.CoveringFireEstablished = established;
        return established;
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

        // "Is somebody in my squad covering me right now" is a squad-level fact that
        // changes on the scale of a burst, not of a frame, and answering it walks every
        // contact state — so at 200 FPS with 60 attackers this scan alone is O(n^2) per
        // frame for an answer that cannot meaningfully differ between two of them. The
        // freshness window it tests against is measured in seconds, so a short cache is
        // invisible to the decision and bounds the scan to a few times a second.
        if (now < state.CoveringFireCheckedUntil)
            return state.CoveringFireEstablished;

        // Answered from the squad's own recent-shooter ring rather than by walking every
        // tracked soldier. Easy Red 2 fields battles of several hundred, and a scan over
        // all contact states per attacker is O(n^2) across the battle — the one cost here
        // that gets worse faster than the battle grows.
        var __t = ModTimeProbe.Begin();
        try
        {
            if (!SquadFireRings.TryGetValue(state.SquadId, out var ring))
                return CacheCoveringFire(state, now, false);

            for (var i = 0; i < ring.Count; i++)
            {
                var candidateId = ring.SoldierIds[i];
                if (candidateId == soldierId)
                    continue;

                if (!AiState.ContactStates.TryGetValue(candidateId, out var covering))
                    continue;

                // D2 (plan 015): any squadmate's fresh stationary shot at a
                // confirmed enemy counts, not just one at this mover's own token.
                // Live state is still read per candidate, so a squadmate who has since
                // been pinned or started relocating stops counting exactly as before.
                if (CombatMovementPolicyCore.IsCoveringFireEstablished(
                        state.SquadId, covering.SquadId, covering.LastOutgoingShotTargetToken,
                        covering.LastOutgoingShotWasStationary, covering.Relocating,
                        covering.Pinned, covering.SuppressionMovementOwned,
                        covering.LastOutgoingShotAt, now, CoveringFireFreshSeconds))
                {
                    return CacheCoveringFire(state, now, true);
                }
            }

            return CacheCoveringFire(state, now, false);
        }
        finally
        {
            ModTimeProbe.EndSub(ModSubSite.SquadScan, __t);
        }
    }

    /// <summary>
    /// Maintains the combat-halt clock and reports both the ordinary off-cover
    /// deadline and the longer on-cover deadline (D1, plan 015) against the same
    /// clock, so <see cref="ShouldAuthorizeAttackBound"/> can guarantee liveness
    /// on cover without shortening the off-cover cap.
    ///
    /// Plan 028: <paramref name="hasMovementOrder"/> used to be the attack-waypoint
    /// flag, which made this clock — the ONLY escape from a fighting halt — exist for
    /// <c>attackFromSide</c> squads and nobody else. A soldier under the ordinary
    /// <c>follow</c> move order therefore held his cover for as long as he could see an
    /// enemy, because every sighting pushed <c>ContactUntil</c> forward, while his squad
    /// walked off without him.
    /// </summary>
    private static (bool MaximumHaltReached, bool MaximumOnCoverHaltReached) UpdateAttackProgressClock(
        ContactResponseState state,
        bool hasMovementOrder,
        bool underPressure,
        float now)
    {
        if (!hasMovementOrder)
        {
            state.AttackHaltStartedAt = 0f;
            state.AttackProgressForced = false;
            CancelAttackMovementBound(state);
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
            CancelAttackMovementBound(state);
            return (false, false);
        }

        var maximumHaltReached = InfantryCoverDecisionCore.ShouldForceAttackProgress(
            hasAttackOrder: true,
            hasDestination: true,
            state.AttackHaltStartedAt,
            now,
            AiBehaviorTuning.MaximumAttackCombatHaltSeconds);
        var maximumOnCoverHaltReached = InfantryCoverDecisionCore.ShouldForceAttackProgress(
            hasAttackOrder: true,
            hasDestination: true,
            state.AttackHaltStartedAt,
            now,
            AiBehaviorTuning.MaximumAttackCombatHaltSeconds * OnCoverAttackHaltMultiplier);
        return (maximumHaltReached, maximumOnCoverHaltReached);
    }

    private static void UpdateAttackObjectiveBoundCycle(
        ContactResponseState state,
        bool hasMovementOrder,
        bool underPressure,
        float now)
    {
        if (!state.AttackObjectiveBoundActive)
            return;

        if (!hasMovementOrder || !underPressure)
        {
            CancelAttackMovementBound(state);
            return;
        }

        if (!CombatMovementPolicyCore.AttackBoundComplete(
                state.AttackObjectiveBoundActive,
                state.AttackObjectiveBoundStartedAt,
                now))
        {
            return;
        }

        state.AttackObjectiveBoundActive = false;
        state.AttackObjectiveBoundStartedAt = 0f;
        state.AttackProgressForced = false;
        state.AttackHaltStartedAt = Mathf.Max(now, 0.0001f);
        state.AttackFiringCommitUntil = now +
                                        AiBehaviorTuning.AttackFiringHoldSeconds;
        state.EngagementHoldUntil = Mathf.Max(
            state.EngagementHoldUntil, state.AttackFiringCommitUntil);
        state.NextRelocationAllowedAt = Mathf.Max(
            state.NextRelocationAllowedAt, state.AttackFiringCommitUntil);
        state.ContactCrouchOwned = true;
    }

    // A transient target/order loss may cancel the moving half of the cycle, but a
    // firing phase that already began remains committed until its absolute deadline.
    private static void CancelAttackMovementBound(ContactResponseState state)
    {
        state.AttackObjectiveBoundActive = false;
        state.AttackObjectiveBoundStartedAt = 0f;
    }

    private static bool IsContactDiveActive(ContactResponseState state, float now)
        => float.IsFinite(now) && now < state.ContactDiveProneUntil;

    private static bool TryStartContactDive(
        ContactResponseState state,
        bool wasActuallyMoving,
        bool hasObservedTarget,
        bool contactWasContinuous,
        float now)
    {
        if (!ContactDivePolicyCore.ShouldStart(
                wasActuallyMoving,
                hasObservedTarget,
                contactWasContinuous,
                state.ContactDiveProneUntil,
                now))
        {
            return false;
        }

        var diveUntil = ContactDivePolicyCore.ResolveFiringDeadline(
            now, AiBehaviorTuning.AttackFiringHoldSeconds);
        state.ContactDiveProneUntil = diveUntil;
        state.AttackFiringCommitUntil = Mathf.Max(
            state.AttackFiringCommitUntil, diveUntil);
        state.EngagementHoldUntil = Mathf.Max(state.EngagementHoldUntil, diveUntil);
        state.NextRelocationAllowedAt = Mathf.Max(state.NextRelocationAllowedAt, diveUntil);
        state.ContactCrouchOwned = true;
        state.AttackProgressForced = false;
        CancelAttackMovementBound(state);
        return true;
    }

    private static void ResetAttackObjectiveBound(ContactResponseState state)
    {
        CancelAttackMovementBound(state);
        state.AttackFiringCommitUntil = 0f;
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
        state.ContactDiveProneUntil = 0f;
        state.AttackConditionsWereFavorable = false;
        state.AttackHaltStartedAt = 0f;
        state.AttackProgressForced = false;
        ResetAttackObjectiveBound(state);
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
        return OwnsCoverAssignmentWrite(soldier) ||
               !ShouldControlTacticalPosition(soldier);
    }

    internal static bool OwnsCoverAssignmentWrite(Soldier soldier)
    {
        if (_coverAssignmentExecutorSoldierId == soldier.GetInstanceID())
            return true;

        var squad = soldier.joinedSquad;
        return squad != null &&
               _coverAssignmentExecutorSquadId != 0 &&
               _coverAssignmentExecutorSquadId == SquadIdentity.GetSquadId(squad);
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

    internal static void ExecuteOwnedSquadCoverWrites(Squad squad, Action nativeWrite)
    {
        var previousSquadExecutor = _coverAssignmentExecutorSquadId;
        try
        {
            _coverAssignmentExecutorSquadId = SquadIdentity.GetSquadId(squad);
            nativeWrite();
        }
        finally
        {
            _coverAssignmentExecutorSquadId = previousSquadExecutor;
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

    /// <summary>
    /// <paramref name="id"/> is the caller's already-resolved soldier instance id. Both
    /// call sites hold it, and re-reading it here cost a native GetInstanceID for every AI
    /// soldier every frame — this runs ahead of the round-robin stagger, so it is paid by
    /// the whole roster rather than by the soldiers actually deciding this frame.
    /// </summary>
    internal static void UpdateSuppressionReaction(
        SoldierAI ai,
        Soldier soldier,
        int id,
        float now,
        float deltaTime)
    {
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
                PauseRelocation(ai, soldier, state, id, now, true);

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

        var crouchBandOwned = AiBehaviorTuningCore.ShouldOwnCrouchedFightingPose(
            state.SuppressionPoseOwned,
            suppression,
            AiBehaviorTuning.CrouchSuppressionThreshold,
            AiBehaviorTuning.CrouchSuppressionReleaseThreshold,
            now >= state.SuppressionCrouchUntil);
        if (crouchBandOwned)
        {
            state.SuppressionPoseOwned = true;
            if (suppression >= AiBehaviorTuning.CrouchSuppressionThreshold)
            {
                state.SuppressionCrouchUntil = Mathf.Max(
                    state.SuppressionCrouchUntil,
                    now + TacticalCrouchPersistenceSeconds);
            }
            ApplyArbitratedPose(
                ai, soldier, now, resolveDecisionTail: true, SuppressionRecoveryPose(soldier), "suppr-band");
            return;
        }

        if (!state.SuppressionPoseOwned)
            return;

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
            ExecuteStopFire(soldier);

        ApplyMovementDecision(
            ai, soldier, deltaTime, now, MovementOwner.Free, "pinned");
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

        var coverMoveStillOwned = true;
        if (pausedRelocation && state.Relocating)
        {
            coverMoveStillOwned = ResumePausedRelocation(
                ai,
                soldier,
                state,
                soldierId,
                now,
                "Suppression-disabled cover path resume failed");
        }

        if (ownedMovement && coverMoveStillOwned)
        {
            ApplyMovementDecision(
                ai, soldier, Time.deltaTime, now, MovementOwner.OrderedMove,
                "suppression-disabled");
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

        var coverMoveStillOwned = true;
        if (state.RelocationPausedBySuppression)
        {
            state.RelocationPausedBySuppression = false;
            if (state.Relocating)
            {
                coverMoveStillOwned = ResumePausedRelocation(
                    ai,
                    soldier,
                    state,
                    soldierId,
                    now,
                    "Suppression cover path resume failed");
            }
        }

        if (ownedMovement && coverMoveStillOwned)
        {
            ApplyMovementDecision(
                ai, soldier, Time.deltaTime, now, MovementOwner.OrderedMove,
                "suppression-release");
        }
    }

    private static void PauseRelocation(
        SoldierAI ai,
        Soldier soldier,
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
        state.RelocateLastDestinationProgressAt = now;
        state.RelocateUntil = now + InfantryCoverPolicy.MoveProgressWindowSeconds;
        TryRenewRelocationCoverReservation(
            ai,
            soldier,
            state,
            soldierId,
            now,
            state.RelocateUntil + 2f);
    }

    private static bool ResumePausedRelocation(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now,
        string warning)
    {
        state.RelocateLastProgressAt = now;
        state.RelocateLastDestinationProgressAt = now;
        state.RelocateUntil = now + InfantryCoverPolicy.MoveProgressWindowSeconds;
        if (!TryRenewRelocationCoverReservation(
                ai,
                soldier,
                state,
                soldierId,
                now,
                state.RelocateUntil + 2f))
        {
            return false;
        }
        // Only re-path a soldier nothing is currently holding. This replaces the
        // RelocationPausedByCloseFire read: "a close-contact halt still owns him" is one
        // case of the arbiter reporting a halting owner, and asking the ladder is both
        // cheaper to reason about and impossible to leave stale.
        if (!MovementArbiterCore.Halts(
                ResolveMovementOwner(soldier, state, soldierId, now, MovementOwner.Free)))
        {
            RefreshPath(ai, warning);
        }
        return true;
    }

    private static bool RetryRelocationPath(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now,
        float destinationDistance,
        string warning)
    {
        state.RelocatePathRetryUsed = true;
        state.RelocateLastDistance = destinationDistance;
        state.RelocateLastProgressAt = now;
        state.RelocateLastDestinationProgressAt = now;
        state.RelocateLastProgressPosition = soldier.transform.position;
        state.RelocateUntil = now + InfantryCoverPolicy.MoveProgressWindowSeconds;
        if (!TryRenewRelocationCoverReservation(
                ai,
                soldier,
                state,
                soldierId,
                now,
                state.RelocateUntil + 2f))
        {
            return false;
        }

        AiState.Trace(
            $"Contact response: soldier {soldierId} refreshed a cover route " +
            "that was not making destination progress");
        RefreshPath(ai, warning);
        return true;
    }

    /// <summary>
    /// "This soldier's squad is going somewhere" (plan 028) - the ORDER half of the
    /// liveness fact, deliberately wider than <see cref="TryGetAttackWaypoint"/>, which
    /// only answers true on <c>Order.attackFromSide</c>. The ordinary <c>follow</c> move
    /// order is exactly the case where a soldier is expected to keep up with his squad,
    /// and it used to be treated like a defender's standing order.
    ///
    /// A <c>defend</c> order is stationary only after the soldier reaches its assigned
    /// area. Before arrival it is a reinforcement route, so contact may interrupt it
    /// briefly but must remain bounded by the same liveness rule.
    /// </summary>
    private static bool HasMovingSquadOrder(Soldier soldier)
    {
        try
        {
            var squad = soldier.joinedSquad;
            if (squad == null)
                return false;

            var isDefendOrder = squad.order == Order.defend;
            var isInsideDefendArea = isDefendOrder && IsInsideDefensiveArea(soldier);
            return SquadOrderMovementCore.ShouldTreatAsMoving(
                isDefendOrder,
                isInsideDefendArea);
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

    /// <summary>
    /// "His squad is going somewhere AND he is not there yet" - what the halt caps are
    /// bounded by, mirroring the old <c>hasAttackRoute</c> exactly one order code wider.
    /// The committed-destination term is what keeps the cap honest: a squad that has
    /// halted leaves its members with their destination reached, so nobody is pulled out
    /// of cover while nobody is being left behind.
    /// </summary>
    private static bool HasLiveMovementOrder(Soldier soldier)
        => HasMovingSquadOrder(soldier) && HasCommittedDestination(soldier);

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
                !memory.HasConfirmedTarget ||
                memory.RequiresTargetReacquisition)
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
            state.ExposedReloadSafetyOwned,
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
        {
            ReleaseStopFire(soldier);
            return;
        }

        if (FireArbiterCore.MayFire(blocker))
        {
            // This is a permission flag, not a request to shoot. Releasing it without a
            // current target is necessary so a later native acquisition is not blocked by
            // a stale false value owned by this mod.
            ReleaseStopFire(soldier);
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
        ExecuteStopFire(soldier);
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
                AiBehaviorTuning.ProneReleaseSuppressionThreshold,
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

            if (suppression >= AiBehaviorTuning.ProneSuppressionThreshold)
                state.PinnedUntil = Mathf.Max(state.PinnedUntil, now + Settings.PinnedMinimumSeconds.Value);
            return;
        }

        // A time-cap release grants a short re-pin immunity window: the same
        // incoming fire that forced the release must not instantly re-pin the
        // soldier before it can act on it.
        if (PinnedReleaseCore.ShouldEngagePin(
                suppression, AiBehaviorTuning.ProneSuppressionThreshold, state.PinnedImmunityUntil, now))
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
    // Plan 020 D1 removed the caller's fallbackPose: a halting site declares an OWNER, it
    // does not get to name a pose. The pose is the arbiter's job, and letting two sites
    // name different ones is what kept defenders flipping prone/crouch forever.
    internal static MovementOwner StopTacticalMovement(
        SoldierAI ai,
        Soldier soldier,
        float deltaTime,
        string proposalSource = "stop-tactical")
    {
        AiState.GetContactState(soldier.GetInstanceID()).MovementInhibitedByContactResponse = true;
        return ApplyMovementDecision(
            ai, soldier, deltaTime, Time.time, MovementOwner.Free, proposalSource);
    }

    /// <param name="declared">The owner this halt claims when it has no state of its own
    /// to resolve from (a grenade-safety or required-action halt). Halts whose ownership
    /// IS state - the stall watchdog, burning - leave it Free and let the
    /// arbiter read their timers, so their rank is not silently promoted.</param>
    internal static MovementOwner StopDangerMovement(
        SoldierAI ai,
        Soldier soldier,
        float deltaTime,
        string proposalSource = "stop-danger",
        MovementOwner declared = MovementOwner.Free)
        => ApplyMovementDecision(
            ai, soldier, deltaTime, Time.time, declared, proposalSource);

    // Low-level soldier command executor. Tactical feature modules request these
    // mutations through GroundAiDirector so movement, pose, aim, and fire policy
    // cannot independently fight over the same native state. Fire PERMISSION is not
    // among them any more: only ApplyFireDecision writes allowFireAtEnemy.
    internal static void ExecuteStopFire(Soldier soldier)
    {
        // Soldier.StopFire is not idempotent on the network: every invocation sends a
        // reliable SyncStopFire RPC. The native trigger state cannot deduplicate calls
        // because Soldier.LateUpdate restores it during an ongoing mod fire hold.
        var state = AiState.GetContactState(soldier.GetInstanceID());
        if (!FireArbiterCore.ShouldIssueStopFire(state.StopFireIssued))
            return;

        soldier.StopFire();
        state.StopFireIssued = true;
    }

    internal static void ReleaseStopFire(Soldier soldier)
    {
        if (soldier == null)
            return;

        AiState.GetContactState(soldier.GetInstanceID()).StopFireIssued = false;
    }

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
            "hazard-escape");
        SetTacticalPose(ai, soldier, SoldierPose.Crouch, "hazard-escape");
    }
}
