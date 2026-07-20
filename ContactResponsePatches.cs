using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class ContactResponseState
{
    internal bool Relocating;
    internal float NextDecisionAt;
    internal float RelocateUntil;
    internal float RelocateLastDistance;
    internal float RelocateLastProgressAt;
    internal Vector3 RelocateLastProgressPosition;
    internal IntPtr RelocateDestinationPointer;
    internal Vector3 RelocateDestinationPosition;
    internal bool RelocationPausedBySuppression;
    internal bool RelocationPausedByCloseFire;
    internal float NextRelocationAllowedAt;
    internal IntPtr ReservedCoverId;
    internal Vector3 ReservedCoverPosition;
    internal IntPtr FailedCoverId;
    internal float FailedCoverUntil;
    internal bool FireInhibitedByMovement;
    internal bool FireInhibitedByRange;
    internal bool FireInhibitedByArmoredTarget;
    internal bool FireRestorePending;
    internal bool ContactResponseActive;
    internal bool MovementInhibitedByContactResponse;
    internal bool SuppressionMovementOwned;
    internal bool SuppressionPoseOwned;
    internal bool SuppressionFireInhibited;
    internal float SuppressionCrouchUntil;
    internal bool ContactCrouchOwned;
    internal bool CoverClearancePoseOwned;
    internal IntPtr CoverClearanceCoverId;
    internal bool StationaryThreatFacingOwned;
    internal bool ExposedReloadProneOwned;
    internal float TacticalCrouchUntil;
    internal float EngagementHoldUntil;
    internal float ContactUntil;
    internal bool Pinned;
    internal float PinnedUntil;
    internal float PinnedFireBlockedUntil;
    internal float HoldCoverUntil;
    internal float ManeuverCoverMinimumHoldUntil;
    internal float ManeuverCoverReleaseUntil;
    internal IntPtr ManeuverCoverReleasedId;
    internal IntPtr ManeuverCoverAnchorId;
    internal Vector3 ManeuverCoverAnchorPosition;
    internal bool DefensiveCoverHold;
    internal bool HasDefensiveCoverAnchor;
    internal IntPtr DefensiveCoverAnchorId;
    internal Vector3 DefensiveCoverAnchorPosition;
    internal bool DefensivePositionOwned;
    internal int DefensivePositionSquadId;
    internal int DefensivePositionObjectiveRevision;
    internal Vector3 DefensivePositionEntryPoint;
    internal Vector3 LastThreatPosition;
    internal bool HasThreatPosition;
    internal int SquadId;
    internal IntPtr AttackContactToken;
    internal float AttackContactLastSeenAt;
    internal bool HasFiredAtAttackContact;
    internal bool AttackConditionsWereFavorable;
    internal float AttackHaltStartedAt;
    internal bool AttackProgressForced;
    internal IntPtr LastOutgoingShotTargetToken;
    internal float LastOutgoingShotAt;
    internal bool LastOutgoingShotWasStationary;
    internal IntPtr EvaluatedCoverPostureId;
    internal SoldierPose EvaluatedCoverPosture;
    internal bool EvaluatedCoverIsProtective;
    internal Vector3 EvaluatedCoverThreatDirection;
    internal float EvaluatedCoverPostureUntil;
    internal InfantryCoverState CoverState;
    internal float NextUrgentCoverDecisionAt;
    internal bool MovementWatchActive;
    internal Vector3 MovementWatchPosition;
    internal Vector3 MovementWatchDestination;
    internal float MovementWatchLastProgressAt;
    internal float MovementStallHoldUntil;
    internal int MovementStallFailures;
    internal bool HasMovementStallDestination;
    internal Vector3 MovementStallDestination;
    internal string MovementWatchSource = string.Empty;
}

internal static class InfantryCoverPolicy
{
    // These mechanics deliberately form one policy rather than a collection of
    // user-facing tuning knobs. Keeping them together prevents incompatible timer,
    // reservation, and scoring combinations from reintroducing cover churn.
    // Dense towns can contain dozens of weak roadside nodes before the first
    // trench or building slot in CoverManager's distance-ordered list. Scan a
    // broad inventory cheaply, then run the allocating ballistic/body/route model
    // only on a representative shortlist. The old 96 x full-detail loop could
    // issue several thousand physics queries from one sequential AI update.
    internal const int RawCandidateLimit = 96;
    internal const int ConstrainedRawCandidateLimit = 192;
    internal const int DetailedCandidateLimit = 12;
    internal const int NearestDetailedCandidateCount = 6;
    internal const float OccupancyRadiusMeters = 1.75f;
    internal const float DecisionIntervalSeconds = 12f;
    internal const float MoveProgressWindowSeconds = 6f;
    internal const float RelocationCooldownSeconds = 15f;
    internal const float MinimumManeuverCoverHoldSeconds = 18f;
    internal const float StandingCoverPenalty = 225f;
    internal const float DefensiveAnchorLeashMeters = 4f;
}

internal static class CoverOccupancy
{
    [ThreadStatic]
    private static Collider[]? _nearbyColliders;

    internal static bool IsOccupiedByOther(Vector3 coverPosition, Soldier soldier)
    {
        var radius = InfantryCoverPolicy.OccupancyRadiusMeters;
        var nearby = _nearbyColliders ??= new Collider[24];
        var count = Physics.OverlapSphereNonAlloc(
            coverPosition,
            radius,
            nearby,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        for (var index = 0; index < count; index++)
        {
            var collider = nearby[index];
            if (collider == null)
                continue;

            var other = collider.GetComponentInParent<Soldier>();
            if (other == null || other.GetInstanceID() == soldier.GetInstanceID() ||
                !other.IsAlive || other.IsOnVehicle() || !other.gameObject.activeInHierarchy)
                continue;

            if ((other.transform.position - coverPosition).sqrMagnitude <= radius * radius)
                return true;
        }

        return false;
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.CoverPosition))]
internal static class ExclusiveCoverAssignmentPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Soldier __instance, AiDestination cover)
    {
        if (!MultiplayerAuthority.CanMutateGameplay() ||
            !AiOwnership.IsAutonomous(__instance))
            return true;

        // Native AI also uses CoverPosition(null) as a generic destination clear.
        // That clear must not tear down a protected gun route or a defender's
        // selected building/trench post between director updates.
        if (cover == null)
            return !ContactResponse.ShouldBlockNativeCoverClear(__instance);

        // Vehicles inherit AiDestination and the native boarding path calls
        // Soldier.CoverPosition(vehicle). Cover-only ownership rules must not
        // reject that call or squad vehicle orders, including static AT staffing,
        // silently succeed without sending a soldier to the requested seat.
        if (cover.IsVehicle())
            return true;

        // Inside a director-owned defensive area, only the selected movement
        // executor may change a cover destination. This closes the remaining native
        // CoverPosition writer that used to circulate defenders between updates.
        if (!ContactResponse.MayWriteCoverAssignment(__instance))
            return false;

        // Once a defender has reached useful cover inside the assigned area, that
        // cover is his position. Native cover refreshes must not send him to another
        // slot until the order moves or the position becomes unusable.
        if (ContactResponse.ShouldKeepReachedCover(__instance))
            return false;

        try
        {
            var soldierId = __instance.GetInstanceID();
            var coverId = cover.Pointer;
            if (!TryGetUsableCoverPosition(cover, out var coverPosition))
                return false;

            if (AiState.CoverReservedByOther(
                    coverId,
                    coverPosition,
                    soldierId,
                    Time.time,
                    InfantryCoverPolicy.OccupancyRadiusMeters))
            {
                AiState.Trace($"Cover occupancy: blocked soldier {soldierId} from an occupied position");
                return false;
            }

            // A player-issued hold order defines the area in which this squad may
            // improve its position. Native or mod cover selection must not silently
            // replace that command with a cover slot outside the ordered area.
            if (!ContactResponse.CoverRespectsPlayerHoldOrder(__instance, coverPosition))
            {
                AiState.Trace(
                    $"Player hold: blocked soldier {soldierId} from cover outside the ordered area");
                return false;
            }

            if (CoverOccupancy.IsOccupiedByOther(coverPosition, __instance))
            {
                AiState.Trace($"Cover occupancy: blocked soldier {soldierId} from an occupied position");
                return false;
            }

            // Native AI assignments do not pass through Contact Response's move
            // state. Reserve their physical destination here as well so two native
            // cover objects at the same trench slot cannot attract two soldiers.
            var reservationSeconds = InfantryCoverPolicy.DecisionIntervalSeconds;
            AiState.ReserveCover(
                coverId, coverPosition, soldierId, Time.time + reservationSeconds);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Cover occupancy check failed: {ex.Message}");
            return false;
        }

        return true;
    }

    internal static bool TryGetUsableCoverPosition(AiDestination cover, out Vector3 position)
    {
        position = default;

        try
        {
            if (cover.IsCoverDestroyed())
                return false;

            var combatCover = cover.TryCast<CombatCover>();
            if (!ReferenceEquals(combatCover, null) &&
                (combatCover == null || combatCover.gameObject == null || combatCover.transform == null))
            {
                return false;
            }

            position = cover.GetCoverPosition();
            return true;
        }
        catch (NullReferenceException)
        {
            // The game can briefly retain a CombatCover after its backing
            // object has been torn down. Reject it instead of entering the
            // native CoverPosition path with an invalid destination.
            return false;
        }
        catch (Il2CppException)
        {
            // Exceptions raised by native IL2CPP methods are surfaced through
            // Il2CppException, including the observed CombatCover null access.
            return false;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
    }
}

[HarmonyPatch(typeof(CombatCover), nameof(CombatCover.IsCoverAvailable))]
internal static class BrokenCombatCoverAvailabilityPatch
{
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, ref bool __result)
    {
        if (__exception is NullReferenceException or Il2CppException or ObjectCollectedException)
        {
            // CoverManager evaluates availability from inside its iterator. One
            // torn-down CombatCover otherwise aborts the entire search (and the
            // soldier tactical update) before any healthy trench/building slot can
            // be considered. A broken cover object is simply unavailable.
            __result = false;
            return null;
        }

        return __exception;
    }
}

internal static class ContactResponse
{
    private readonly record struct CoarseCoverCandidate(
        AiDestination Destination,
        Vector3 Position,
        float DistanceSqr);

    private static int _coverSearchFrame = -1;
    private static int _coverSearchesThisFrame;

    [ThreadStatic]
    private static RaycastHit[]? _fireLaneHits;

    private static bool TryBeginDetailedCoverSearch()
    {
        var frame = Time.frameCount;
        if (frame != _coverSearchFrame)
        {
            _coverSearchFrame = frame;
            _coverSearchesThisFrame = 0;
        }

        if (_coverSearchesThisFrame >= 1)
            return false;

        _coverSearchesThisFrame++;
        return true;
    }

    private static float DeferredCoverRetryAt(int soldierId, float now)
        => now + 0.05f + (soldierId & 3) * 0.025f;
    private const float ContactPersistenceSeconds = 3f;
    private const float PinnedShockSeconds = 2f;
    private const float CoverMuzzleClearanceDistance = 2f;
    private const float AttackContactContinuitySeconds = 5f;
    private const float EstablishedFireFreshSeconds = 10f;
    private const float CoveringFireFreshSeconds = 3f;
    private const float DefensiveAreaToleranceMeters = 10f;
    private const float PlayerHoldAreaToleranceMeters = 3f;
    private const float CrouchedCoverBodyHeight = 0.75f;
    private const float StandingCoverBodyHeight = 1.15f;
    private const float ProneCoverMuzzleHeight = 0.42f;
    private const float CrouchedCoverMuzzleHeight = 1.12f;
    private const float StandingCoverMuzzleHeight = 1.58f;
    private const float CoverShoulderHalfWidth = 0.28f;
    private const float CoverPostureCacheSeconds = 1.25f;
    private const float CoverPostureDirectionDotTolerance = 0.97f;
    private const float MovingBodyHeight = 0.9f;
    private const float ThreatEndpointTolerance = 1.25f;
    private const float UrgentCoverReassessmentSeconds = 4f;
    private const int MaximumCoverThreats = 4;
    internal const float TacticalCrouchPersistenceSeconds = 1.5f;
    private static int _coverAssignmentExecutorSoldierId;

    private readonly record struct CoverGeometryEvaluation(
        CoverPostureChoice Choice,
        CoverPostureInput Standing,
        CoverPostureInput Crouched,
        CoverPostureInput Prone)
    {
        internal CoverPostureInput Selected =>
            InfantryCoverDecisionCore.SelectCoverPostureInput(
                Choice, Standing, Crouched, Prone);

        internal bool IsProtective =>
            InfantryCoverDecisionCore.HasMeaningfulProtection(Selected) ||
            (Choice == CoverPostureChoice.Standing &&
             InfantryCoverDecisionCore.HasMeaningfulProtection(Crouched));
    }

    private readonly record struct CurrentCoverEvaluation(
        bool IsProtective,
        SoldierPose Pose);

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
        state.SquadId = ContactKnowledge.GetSquadId(soldier);
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
        var maximumAttackHaltReached = UpdateAttackProgressClock(
            state, hasAttackRoute, attackUnderPressure, now);
        if (state.LastOutgoingShotWasStationary &&
            (soldier.IsMoving(0.2f) || state.Relocating || state.Pinned ||
             state.SuppressionMovementOwned))
        {
            state.LastOutgoingShotWasStationary = false;
        }

        UpdateDefensiveCoverHold(soldier, state, id, now);

        if (state.CoverClearancePoseOwned && !OwnsCurrentCoverClearancePose(soldier, state))
            ClearCoverClearancePose(state);

        if (!state.Relocating && state.ReservedCoverId != IntPtr.Zero && !soldier.IsOnCover())
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
            state.FireRestorePending = true;
            ai.allowFireAtEnemy = false;
            ai.aimingEnemy = false;
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
                state.HasFiredAtAttackContact =
                    state.LastOutgoingShotTargetToken == observedTargetToken &&
                    now - state.LastOutgoingShotAt <= EstablishedFireFreshSeconds;
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
            state.FireRestorePending = true;
            ai.allowFireAtEnemy = false;
            ai.aimingEnemy = false;
            ai.moveCharacter = true;
            state.MovementInhibitedByContactResponse = false;
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

        // A player-issued hold order owns the destination. Survival reactions still
        // apply, but Contact Response may neither stop a squad member short of the
        // ordered area nor choose a different cover destination after arrival.
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
            HorizontalDistanceSqr(soldier.transform.position, observedTargetPosition) <=
            Settings.ContactImmediateFireDistance.Value * Settings.ContactImmediateFireDistance.Value &&
            !HandheldWeaponClassifier.AllowsMovingFire(soldier, ai);
        if (closeThreatRequiresStationaryFire)
        {
            if (state.Relocating)
            {
                state.RelocationPausedByCloseFire = true;
                PauseRelocation(state, id, now, false);
            }
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
            ai.moveCharacter = true;
            return;
        }

        if (state.Relocating && state.RelocationPausedByCloseFire)
        {
            state.RelocationPausedByCloseFire = false;
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
                    state.DefensivePositionOwned,
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

        if (target == null)
        {
            state.AttackConditionsWereFavorable = false;
            if (KnownTargetSuppressiveFire.TryGetOwnedAimPoint(id, now, out var suppressionAimPoint))
            {
                state.EngagementHoldUntil = float.PositiveInfinity;
                state.ContactCrouchOwned = true;
                StopTacticalMovement(
                    ai,
                    soldier,
                    GetStationaryEngagementPose(soldier, state, suppressionAimPoint),
                    Time.deltaTime);
                return;
            }

            // A defender arriving inside the objective receives one deliberate
            // fighting-position assignment. Until a usable slot is found, he holds
            // where he is instead of circulating through the squad's broad native
            // HoldArea. Reassessment can fill a newly vacated slot, but an intact
            // reached position is never traded merely because another looks better.
            if (TryEstablishInitialDefensivePosition(
                    ai, soldier, state, id, now))
            {
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
                    Time.deltaTime);
                if (state.HasThreatPosition && now < state.ContactUntil)
                    FaceThreatWhenStationary(ai, soldier, state.LastThreatPosition);
                return;
            }

            var forcedAttackProgressWithoutVisibleTarget = CombatMovementPolicyCore.ShouldAuthorizeAttackBound(
                hasAttackRoute,
                coveringFireEstablished: false,
                maximumAttackHaltReached,
                underDirectFire,
                state.Pinned,
                onUsableCover: IsOnUsableCover(soldier),
                state.ManeuverCoverMinimumHoldUntil,
                now);
            if (forcedAttackProgressWithoutVisibleTarget)
            {
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
                    Time.deltaTime);
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
                        Time.deltaTime);
                    if (state.HasThreatPosition)
                        FaceThreatWhenStationary(ai, soldier, state.LastThreatPosition);
                }
                else
                {
                    ai.moveCharacter = true;
                    state.MovementInhibitedByContactResponse = false;
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
                if (!state.SuppressionMovementOwned)
                    ai.moveCharacter = true;
                state.MovementInhibitedByContactResponse = false;
                if (wasHoldingMovement && HasCommittedDestination(soldier))
                    RefreshPath(ai, "Contact path release failed");
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
            underDirectFire,
            state.Pinned,
            onUsableNativeCover,
            state.ManeuverCoverMinimumHoldUntil,
            now);
        var forcedAttackProgress = authorizedAttackAdvance &&
                                   maximumAttackHaltReached &&
                                   !coordinatedAttackAdvance;
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
                    now + 2f);
            if (!authorizedAttackAdvance)
            {
                SetCoverState(state, InfantryCoverState.Holding, id,
                    "current position remains protective");
                state.ContactCrouchOwned = true;
                StopTacticalMovement(
                    ai,
                    soldier,
                    GetStationaryEngagementPose(soldier, state, targetPosition),
                    Time.deltaTime);
                FaceThreatWhenStationary(ai, soldier, targetPosition);
                if (target != null)
                    GrantFirePermissionIfReady(ai, soldier);
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
        state.RelocationPausedByCloseFire = false;
        state.NextRelocationAllowedAt = 0f;
        state.ReservedCoverId = IntPtr.Zero;
        state.ReservedCoverPosition = default;
        state.FailedCoverId = IntPtr.Zero;
        state.FailedCoverUntil = 0f;
        state.EngagementHoldUntil = 0f;
        state.ContactUntil = 0f;
        state.ContactCrouchOwned = false;
        ClearCoverClearancePose(state);
        ResetCoverPostureEvaluation(state);
        state.HoldCoverUntil = 0f;
        state.ManeuverCoverMinimumHoldUntil = 0f;
        state.ManeuverCoverReleaseUntil = 0f;
        state.ManeuverCoverReleasedId = IntPtr.Zero;
        state.ManeuverCoverAnchorId = IntPtr.Zero;
        state.ManeuverCoverAnchorPosition = default;
        state.DefensiveCoverHold = false;
        state.HasDefensiveCoverAnchor = false;
        state.DefensiveCoverAnchorId = IntPtr.Zero;
        state.DefensiveCoverAnchorPosition = default;
        state.DefensivePositionOwned = false;
        state.DefensivePositionSquadId = 0;
        state.DefensivePositionObjectiveRevision = 0;
        state.DefensivePositionEntryPoint = default;
        state.LastThreatPosition = default;
        state.HasThreatPosition = false;
        AiState.ReleaseCoverReservation(id);

        var hazardOwnsMovement = Settings.DangerReactionsEnabled.Value &&
                                 (soldier.IsOnFire || AiState.IsFlameEvading(id, now));
        if (wasControllingMovement && !state.SuppressionMovementOwned && !hazardOwnsMovement)
        {
            ai.moveCharacter = true;
            state.MovementInhibitedByContactResponse = false;
            if (HasCommittedDestination(soldier))
                RefreshPath(ai, "Contact path release after disabling failed");
        }
        else
        {
            state.MovementInhibitedByContactResponse = false;
        }

        // Contact response can own a persistent false fire gate during relocation.
        // Suppression and hazard owners remain independent when this setting changes.
        state.ContactResponseActive = false;
        state.FireRestorePending = true;
        RestoreFireAfterOwnedInhibition(ai, soldier);
    }

    /// <summary>
    /// Releases only the locomotion state owned by contact response when the
    /// director grants movement to a player/script order, a commander order, or a
    /// protected fortification assignment. Suppression and fire-safety ownership
    /// remain intact and can continue to react locally.
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

        ReleaseStationaryThreatFacing(ai, state);
        if (state.Relocating)
            FinishRelocation(ai, soldier, state, soldierId, Time.time,
                keepOccupiedCover: false, completedMove: false, markFailedCover: false);
        if (releaseDefensiveAnchor)
        {
            ReleaseDefensivePositionOwnership(state, soldierId);
            ReleaseDefensiveCoverHold(state, soldierId);
            state.HoldCoverUntil = 0f;
            state.ManeuverCoverMinimumHoldUntil = 0f;
            state.ManeuverCoverReleaseUntil = 0f;
            state.ManeuverCoverReleasedId = IntPtr.Zero;
            state.ManeuverCoverAnchorId = IntPtr.Zero;
            state.ManeuverCoverAnchorPosition = default;
        }
        else if (!soldier.IsOnCover())
        {
            state.HoldCoverUntil = 0f;
            state.ManeuverCoverMinimumHoldUntil = 0f;
            state.ManeuverCoverReleaseUntil = 0f;
            state.ManeuverCoverReleasedId = IntPtr.Zero;
            state.ManeuverCoverAnchorId = IntPtr.Zero;
            state.ManeuverCoverAnchorPosition = default;
        }

        state.ContactResponseActive = false;
        state.MovementInhibitedByContactResponse = false;
        state.ContactCrouchOwned = false;
        state.EngagementHoldUntil = 0f;
        ClearCoverClearancePose(state);
        if (wasControllingMovement && !state.SuppressionMovementOwned &&
            !(Settings.DangerReactionsEnabled.Value &&
              (soldier.IsOnFire || AiState.IsFlameEvading(soldierId, Time.time))))
        {
            ai.moveCharacter = true;
            if (HasCommittedDestination(soldier))
                RefreshPath(ai, "Higher-authority movement path resume failed");
        }
    }

    internal static void SuspendForVehicle(SoldierAI ai, Soldier soldier)
    {
        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);
        ReleaseStationaryThreatFacing(ai, state);
        var releasedInfantryFireGate = state.FireInhibitedByMovement || state.FireInhibitedByRange ||
                                         state.FireInhibitedByArmoredTarget ||
                                         state.SuppressionFireInhibited;
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
        ResetCoverPostureEvaluation(state);

        state.Relocating = false;
        state.NextUrgentCoverDecisionAt = 0f;
        state.CoverState = InfantryCoverState.Holding;
        state.RelocationPausedBySuppression = false;
        state.RelocationPausedByCloseFire = false;
        state.RelocateDestinationPointer = IntPtr.Zero;
        state.RelocateDestinationPosition = default;
        state.ReservedCoverId = IntPtr.Zero;
        state.ReservedCoverPosition = default;
        state.ContactResponseActive = false;
        state.MovementInhibitedByContactResponse = false;
        state.ContactCrouchOwned = false;
        state.ContactUntil = 0f;
        state.EngagementHoldUntil = 0f;
        state.HoldCoverUntil = 0f;
        state.ManeuverCoverMinimumHoldUntil = 0f;
        state.ManeuverCoverReleaseUntil = 0f;
        state.ManeuverCoverReleasedId = IntPtr.Zero;
        state.ManeuverCoverAnchorId = IntPtr.Zero;
        state.ManeuverCoverAnchorPosition = default;
        state.DefensiveCoverHold = false;
        state.HasDefensiveCoverAnchor = false;
        state.DefensiveCoverAnchorId = IntPtr.Zero;
        state.DefensiveCoverAnchorPosition = default;
        state.DefensivePositionOwned = false;
        state.DefensivePositionSquadId = 0;
        state.DefensivePositionObjectiveRevision = 0;
        state.DefensivePositionEntryPoint = default;
        ResetAttackFireEvidence(state);
        AiState.ReleaseCoverReservation(id);

        if (releasedInfantryFireGate)
            state.FireRestorePending = true;
        RestoreFireAfterOwnedInhibition(ai, soldier);
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
            state.SquadId = ContactKnowledge.GetSquadId(shooter);
            state.LastOutgoingShotTargetToken = targetToken;
            state.LastOutgoingShotAt = now;
            state.LastOutgoingShotWasStationary =
                !shooter.IsMoving(0.2f) && !state.Relocating;
            if (state.AttackContactToken == targetToken &&
                now - state.AttackContactLastSeenAt <= AttackContactContinuitySeconds)
            {
                state.HasFiredAtAttackContact = true;
            }
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
        if (targetToken == IntPtr.Zero || state.SquadId == 0 ||
            state.AttackContactToken != targetToken ||
            !state.HasFiredAtAttackContact ||
            state.LastOutgoingShotTargetToken != targetToken ||
            now - state.LastOutgoingShotAt > EstablishedFireFreshSeconds)
        {
            return false;
        }

        foreach (var pair in AiState.ContactStates)
        {
            if (pair.Key == soldierId)
                continue;

            var covering = pair.Value;
            if (covering.SquadId != state.SquadId ||
                covering.LastOutgoingShotTargetToken != targetToken ||
                now - covering.LastOutgoingShotAt > CoveringFireFreshSeconds ||
                !covering.LastOutgoingShotWasStationary || covering.Relocating ||
                covering.Pinned || covering.SuppressionMovementOwned)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool UpdateAttackProgressClock(
        ContactResponseState state,
        bool hasAttackRoute,
        bool underPressure,
        float now)
    {
        if (!hasAttackRoute)
        {
            state.AttackHaltStartedAt = 0f;
            state.AttackProgressForced = false;
            return false;
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
            return false;
        }

        return InfantryCoverDecisionCore.ShouldForceAttackProgress(
            hasAttackOrder: true,
            hasDestination: true,
            state.AttackHaltStartedAt,
            now,
            Settings.MaximumAttackCombatHaltSeconds.Value);
    }

    private static void ResetAttackFireEvidence(ContactResponseState state)
    {
        state.SquadId = 0;
        state.AttackContactToken = IntPtr.Zero;
        state.AttackContactLastSeenAt = 0f;
        state.HasFiredAtAttackContact = false;
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
               !ShouldControlDefensivePosition(soldier);
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
        if (!Settings.ContactResponseEnabled.Value && !defensivePositionControl)
            return default;

        var onUsableCover = IsOnUsableCover(soldier);
        var stableCover = state.DefensiveCoverHold || state.HasDefensiveCoverAnchor;
        var currentCoverId = CurrentCoverId(soldier);
        var canClaimReachedCover = !stableCover && !IsActualCharge(soldier) &&
                                   onUsableCover && currentCoverId != IntPtr.Zero &&
                                   state.ManeuverCoverAnchorId != currentCoverId &&
                                   (IsCommanderAttacker(soldier) ||
                                    TryGetAttackWaypoint(soldier, out _));

        return new ContactMovementSensor(
            GetActionableTarget(ai, soldier) != null,
            now < state.ContactUntil || IncomingFireAwareness.HasActiveCue(soldierId, now),
            state.Relocating,
            stableCover,
            onUsableCover && now < state.HoldCoverUntil,
            canClaimReachedCover,
            now < state.EngagementHoldUntil,
            defensivePositionControl);
    }

    private static bool TryEstablishInitialDefensivePosition(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now)
    {
        if (!ShouldControlDefensivePosition(soldier))
            return false;

        UpdateDefensiveCoverHold(soldier, state, soldierId, now);
        if (state.DefensiveCoverHold || state.HasDefensiveCoverAnchor)
        {
            StopTacticalMovement(
                ai, soldier, SoldierPose.Crouch, Time.deltaTime);
            return true;
        }

        if (state.Relocating)
            return true;

        if (now >= state.NextDecisionAt &&
            TryGetDefensiveArea(soldier, out var center, out var radius))
        {
            state.NextDecisionAt = now + InfantryCoverPolicy.DecisionIntervalSeconds;
            var threatPosition = GetDefensiveApproachPoint(
                soldier, state, center, radius, now);
            var cover = FindCover(
                soldier,
                threatPosition,
                Mathf.Max(55f, radius),
                state,
                now,
                CoverSelectionMode.DefensiveOccupation,
                null,
                respectAttackWaypoint: false,
                evaluateFiringQuality: true,
                out _,
                out var searchDeferred);
            if (searchDeferred)
            {
                state.NextDecisionAt = DeferredCoverRetryAt(soldierId, now);
                StopTacticalMovement(ai, soldier, SoldierPose.Crouch, Time.deltaTime);
                return true;
            }
            if (cover != null &&
                BeginRelocation(ai, soldier, state, cover, soldierId, now))
            {
                SetCoverState(state, InfantryCoverState.Moving, soldierId,
                    "taking initial defensive fighting position");
                return true;
            }
        }

        // No usable authored slot is currently available. Holding the arrival
        // point is preferable to letting native HoldArea logic repeatedly send the
        // defender across open ground. A slow retry can occupy a later vacancy.
        SetCoverState(state, InfantryCoverState.Holding, soldierId,
            "holding arrival point while no defensive cover slot is available");
        state.EngagementHoldUntil = float.PositiveInfinity;
        StopTacticalMovement(ai, soldier, SoldierPose.Prone, Time.deltaTime);
        return true;
    }

    private static Vector3 GetDefensiveApproachPoint(
        Soldier soldier,
        ContactResponseState state,
        Vector3 center,
        float radius,
        float now)
    {
        if (state.HasThreatPosition && IsFinite(state.LastThreatPosition) &&
            HorizontalDistanceSqr(soldier.transform.position, state.LastThreatPosition) >= 4f)
        {
            return state.LastThreatPosition;
        }

        var reportedThreats = new List<Vector3>(1);
        ContactKnowledge.AppendRecentGroundThreatPositions(
            soldier, now, reportedThreats, 1);
        if (reportedThreats.Count > 0 && IsFinite(reportedThreats[0]) &&
            HorizontalDistanceSqr(soldier.transform.position, reportedThreats[0]) >= 4f)
        {
            return reportedThreats[0];
        }

        var outward = soldier.transform.position - center;
        outward.y = 0f;
        if (outward.sqrMagnitude < 16f)
        {
            // Soldiers near the objective center share one stable squad-facing
            // approach axis. Per-soldier random axes made one squad scatter across
            // unrelated faces of the position and select incoherent cover.
            var squadId = ContactKnowledge.GetSquadId(soldier);
            var seed = unchecked((uint)(squadId * 397));
            var angle = (seed % 360u) * Mathf.Deg2Rad;
            outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }
        else
        {
            outward.Normalize();
        }

        var eyeLevel = soldier.GetCenterOfUnit().y;
        var distance = Mathf.Max(80f, radius + 60f);
        var approach = center + outward * distance;
        approach.y = eyeLevel;
        return approach;
    }

    private static bool ShouldControlDefensivePosition(Soldier soldier)
        => RefreshDefensivePositionOwnership(
            soldier, AiState.GetContactState(soldier.GetInstanceID()));

    private static bool RefreshDefensivePositionOwnership(
        Soldier soldier,
        ContactResponseState state)
    {
        try
        {
            var squad = soldier.joinedSquad;
            var squadId = squad == null ? 0 : ContactKnowledge.GetSquadId(squad);
            var revision = GroundAiDirector.CurrentObjectiveRevision(soldier.faction);
            var eligible = squad != null &&
                           AiOwnership.IsAutonomous(soldier) &&
                           soldier.IsAlive &&
                           !soldier.IsOnVehicle() &&
                           GroundAiDirector.OwnsSquad(squad) &&
                           GroundAiDirector.IsDefendingSquad(squad) &&
                           !GroundAiDirector.HasProtectedInfantryAssignment(soldier) &&
                           revision > 0;
            var insideArea = !state.DefensivePositionOwned && eligible &&
                             IsInsideDefensiveArea(soldier);
            var shouldOwn = DefensivePositionOwnershipCore.ShouldOwn(
                new DefensivePositionOwnershipInput(
                    state.DefensivePositionOwned,
                    eligible,
                    insideArea,
                    state.DefensivePositionSquadId == squadId,
                    state.DefensivePositionObjectiveRevision == revision));
            if (!shouldOwn)
            {
                if (state.DefensivePositionOwned)
                    ReleaseDefensivePositionOwnership(state, soldier.GetInstanceID());
                return false;
            }

            if (!state.DefensivePositionOwned)
            {
                state.DefensivePositionOwned = true;
                state.DefensivePositionSquadId = squadId;
                state.DefensivePositionObjectiveRevision = revision;
                state.DefensivePositionEntryPoint = soldier.transform.position;
                state.EngagementHoldUntil = float.PositiveInfinity;
                AiState.Trace(
                    $"Defensive position: soldier {soldier.GetInstanceID()} acquired stationary ownership");
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool ShouldHoldDefensivePosition(Soldier soldier, float now)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        return RefreshDefensivePositionOwnership(soldier, state) &&
               !state.Relocating &&
               !soldier.IsOnFire &&
               !AiState.IsFlameEvading(soldier.GetInstanceID(), now);
    }

    internal static void ResetDefensivePositionOwnership(int soldierId)
    {
        if (AiState.ContactStates.TryGetValue(soldierId, out var state))
            ReleaseDefensivePositionOwnership(state, soldierId);
    }

    private static void ReleaseDefensivePositionOwnership(
        ContactResponseState state,
        int soldierId)
    {
        state.DefensivePositionOwned = false;
        state.DefensivePositionSquadId = 0;
        state.DefensivePositionObjectiveRevision = 0;
        state.DefensivePositionEntryPoint = default;
        if (float.IsPositiveInfinity(state.EngagementHoldUntil))
            state.EngagementHoldUntil = 0f;
        ReleaseDefensiveCoverHold(state, soldierId);
    }

    internal static bool IsActualCharge(Soldier soldier)
    {
        try
        {
            // A native charge flag cannot cancel a host-authoritative defend lease
            // after the squad has reached its defended area.
            if (ShouldControlDefensivePosition(soldier))
                return false;
            return soldier.joinedSquad?.InChargeTime() == true;
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr CurrentCoverId(Soldier soldier)
    {
        try
        {
            var cover = soldier.targetDestination;
            return cover == null || cover.WasCollected || !soldier.IsOnCover()
                ? IntPtr.Zero
                : cover.Pointer;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    internal static bool TryGetPlayerHoldOrder(
        Soldier soldier,
        out Vector3 center,
        out float radius)
    {
        center = default;
        radius = 0f;
        try
        {
            var squad = soldier.joinedSquad;
            if (squad == null || squad.order != Order.defend || !IsPlayerLedSquad(squad))
                return false;

            center = squad.moveOrderPosition;
            radius = squad.moveOrderRadius;
            if (!IsFinite(center) || float.IsNaN(radius) || float.IsInfinity(radius))
                return false;

            radius = Mathf.Max(0f, radius);
            return true;
        }
        catch
        {
            center = default;
            radius = 0f;
            return false;
        }
    }

    internal static bool IsInsidePlayerHoldOrder(
        Vector3 position,
        Vector3 center,
        float radius)
    {
        radius = Mathf.Max(0f, radius) + PlayerHoldAreaToleranceMeters;
        return HorizontalDistanceSqr(position, center) <= radius * radius;
    }

    internal static bool CoverRespectsPlayerHoldOrder(
        Soldier soldier,
        Vector3 coverPosition)
        => !TryGetPlayerHoldOrder(soldier, out var center, out var radius) ||
           IsInsidePlayerHoldOrder(coverPosition, center, radius);

    private static bool TryHonorPlayerLedHoldOrder(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        Spottable? target,
        Vector3 targetPosition,
        int soldierId,
        float now)
    {
        if (!TryGetPlayerHoldOrder(soldier, out var center, out var radius))
            return false;

        var insideOrderedArea = IsInsidePlayerHoldOrder(
            soldier.transform.position, center, radius);
        var wasControllingMovement = state.Relocating ||
                                     state.MovementInhibitedByContactResponse ||
                                     state.EngagementHoldUntil > 0f ||
                                     state.HoldCoverUntil > 0f;
        if (state.Relocating)
            FinishRelocation(ai, soldier, state, soldierId, now, false, false, false);

        if (!insideOrderedArea)
        {
            // Do not let an old armor/contact hold stop the native squad route. The
            // game's formation logic remains responsible for each member's exact
            // destination within the player's ordered area.
            AiState.TankCoverHideUntil.Remove(soldierId);
            ReleaseDefensiveCoverHold(state, soldierId);
            state.HoldCoverUntil = 0f;
            state.ManeuverCoverMinimumHoldUntil = 0f;
            state.ManeuverCoverReleaseUntil = 0f;
            state.ManeuverCoverReleasedId = IntPtr.Zero;
            state.ManeuverCoverAnchorId = IntPtr.Zero;
            state.ManeuverCoverAnchorPosition = default;
            state.EngagementHoldUntil = 0f;
            state.ContactCrouchOwned = target != null;
            ClearCoverClearancePose(state);
            state.MovementInhibitedByContactResponse = false;
            state.FireRestorePending = true;
            ai.moveCharacter = true;
            if (target != null)
                ApplyContactMovementPose(ai, soldier, state, now);
            if (wasControllingMovement && HasCommittedDestination(soldier))
                RefreshPath(ai, "Player hold route restoration failed");
            return true;
        }

        if (target == null)
            return false;

        // Once inside, engage from the ordered position rather than replacing the
        // player's hold with an autonomous cover move. If armor is already forcing
        // concealment, minimize exposure without leaving the area.
        SetCoverState(state, InfantryCoverState.Holding, soldierId,
            "player hold order owns the position");
        state.EngagementHoldUntil = float.PositiveInfinity;
        state.ContactCrouchOwned = true;
        var pose = AiState.IsHidingFromTank(soldierId, now)
            ? (IsOnUsableCover(soldier) ? StationaryHoldPose(soldier) : SoldierPose.Prone)
            : GetStationaryEngagementPose(soldier, state, targetPosition);
        StopTacticalMovement(ai, soldier, pose, Time.deltaTime);
        FaceThreatWhenStationary(ai, soldier, targetPosition);
        GrantFirePermissionIfReady(ai, soldier);
        return true;
    }

    private static bool IsPlayerLedSquad(Squad squad)
    {
        try
        {
            if (squad.IsPlayerInSquad())
                return true;

            for (var index = 0; index < squad.CountMembers; index++)
            {
                var member = squad.GetMember(index);
                var sync = member?.GetComponent<SyncSoldier>();
                if (sync != null && sync.IsControlledByAPlayer())
                    return true;
            }

            return false;
        }
        catch (ObjectCollectedException)
        {
            // A disappearing squad must never cause us to overwrite what may be a
            // player command during the teardown frame.
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFinite(Vector3 value)
        => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
           !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
           !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private static void UpdateDefensiveCoverHold(
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now)
    {
        var center = default(Vector3);
        var radius = 0f;
        var defendOrderActive = !IsActualCharge(soldier) &&
                                TryGetDefensiveArea(soldier, out center, out radius);
        if (state.HasDefensiveCoverAnchor)
        {
            var anchorInsideArea = defendOrderActive &&
                                   DefensivePositioningCore.IsInsideArea(
                                       new MapPoint(
                                           state.DefensiveCoverAnchorPosition.x,
                                           state.DefensiveCoverAnchorPosition.z),
                                       new MapPoint(center.x, center.z),
                                       radius);
            var withinAnchorLeash = HorizontalDistanceSqr(
                                        soldier.transform.position,
                                        state.DefensiveCoverAnchorPosition) <=
                                    InfantryCoverPolicy.DefensiveAnchorLeashMeters *
                                    InfantryCoverPolicy.DefensiveAnchorLeashMeters;
            var coverKnownCompromised =
                IsDefensiveAnchorKnownCompromised(soldier, state);
            if (InfantryCoverDecisionCore.ShouldKeepDefensiveCoverAnchor(
                    defendOrderActive,
                    anchorInsideArea,
                    coverKnownCompromised,
                    withinAnchorLeash))
            {
                state.DefensiveCoverHold = true;
                state.HoldCoverUntil = float.PositiveInfinity;
                state.ReservedCoverId = state.DefensiveCoverAnchorId;
                state.ReservedCoverPosition = state.DefensiveCoverAnchorPosition;
                AiState.ReserveCover(
                    state.DefensiveCoverAnchorId,
                    state.DefensiveCoverAnchorPosition,
                    soldierId,
                    now + 2f);
                return;
            }

            ReleaseDefensiveCoverHold(state, soldierId);
        }

        if (!defendOrderActive || !IsOnUsableCover(soldier) ||
            !IsInsideDefensiveArea(soldier))
        {
            return;
        }

        // Native cover status alone is not a reason to anchor a defender. The
        // position must first prove that it puts real geometry between the soldier
        // and a current/recent approach axis. Once accepted, the stable anchor is
        // deliberately not churned merely because the observed angle changes.
        var threatPosition = GetDefensiveApproachPoint(
            soldier, state, center, radius, now);
        if (!TryCaptureDefensiveCoverAnchor(soldier, state, threatPosition, now))
            return;

        state.DefensiveCoverHold = true;
        state.HoldCoverUntil = float.PositiveInfinity;
        AiState.ReserveCover(
            state.DefensiveCoverAnchorId,
            state.DefensiveCoverAnchorPosition,
            soldierId,
            now + 2f);
    }

    private static bool TryCaptureDefensiveCoverAnchor(
        Soldier soldier,
        ContactResponseState state,
        Vector3 threatPosition,
        float now)
    {
        try
        {
            var cover = soldier.targetDestination;
            if (cover == null || cover.WasCollected || cover.Pointer == IntPtr.Zero ||
                !ExclusiveCoverAssignmentPatch.TryGetUsableCoverPosition(
                    cover, out var coverPosition) ||
                !IsCurrentCoverProtective(soldier, state, threatPosition, now))
            {
                return false;
            }

            state.HasDefensiveCoverAnchor = true;
            state.DefensiveCoverAnchorId = cover.Pointer;
            state.DefensiveCoverAnchorPosition = coverPosition;
            state.ReservedCoverId = cover.Pointer;
            state.ReservedCoverPosition = coverPosition;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCaptureReservedDefensiveCoverAnchor(
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now)
    {
        if (!state.DefensivePositionOwned ||
            state.ReservedCoverId == IntPtr.Zero ||
            !IsFinite(state.ReservedCoverPosition) ||
            !IsInsideDefensiveArea(soldier))
        {
            return false;
        }

        try
        {
            // The slot was already accepted by the defensive occupation search,
            // including its ballistic protection. Reject only positive evidence
            // that the same native node was destroyed or became unsafe. Building
            // nodes do not always report IsOnCover after arrival, so that native
            // flag is deliberately not required here.
            var cover = soldier.targetDestination;
            if (cover != null && !cover.WasCollected &&
                cover.Pointer == state.ReservedCoverId &&
                (cover.IsCoverDestroyed() || cover.IsUnsafeCover()))
            {
                return false;
            }
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
        catch
        {
            // A transient wrapper lookup must not discard a previously vetted
            // building or trench slot at the instant the soldier reaches it.
        }

        state.HasDefensiveCoverAnchor = true;
        state.DefensiveCoverAnchorId = state.ReservedCoverId;
        state.DefensiveCoverAnchorPosition = state.ReservedCoverPosition;
        state.DefensiveCoverHold = true;
        state.HoldCoverUntil = float.PositiveInfinity;
        AiState.ReserveCover(
            state.DefensiveCoverAnchorId,
            state.DefensiveCoverAnchorPosition,
            soldierId,
            now + 2f);
        AiState.Trace(
            $"Defensive cover: soldier {soldierId} latched reached reserved position");
        return true;
    }

    private static bool UpdateManeuverCoverObservation(
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now)
    {
        // A director-authorized bound gets a short grace window in which leaving
        // the old cover cannot immediately be mistaken for arriving there again.
        if (now < state.ManeuverCoverReleaseUntil &&
            CurrentCoverId(soldier) == state.ManeuverCoverReleasedId)
        {
            return false;
        }
        if (now >= state.ManeuverCoverReleaseUntil)
            state.ManeuverCoverReleasedId = IntPtr.Zero;

        if (!IsOnUsableCover(soldier))
        {
            if (!state.Relocating)
            {
                state.HoldCoverUntil = 0f;
                state.ManeuverCoverMinimumHoldUntil = 0f;
                state.ManeuverCoverAnchorId = IntPtr.Zero;
                state.ManeuverCoverAnchorPosition = default;
            }
            return false;
        }

        if (state.DefensiveCoverHold)
            return false;

        try
        {
            var cover = soldier.targetDestination;
            if (cover == null || cover.WasCollected || cover.Pointer == IntPtr.Zero ||
                !ExclusiveCoverAssignmentPatch.TryGetUsableCoverPosition(
                    cover, out var coverPosition))
            {
                return false;
            }

            if (state.ManeuverCoverAnchorId != cover.Pointer)
            {
                state.ManeuverCoverAnchorId = cover.Pointer;
                state.ManeuverCoverAnchorPosition = coverPosition;
                state.ManeuverCoverMinimumHoldUntil = now +
                    InfantryCoverPolicy.MinimumManeuverCoverHoldSeconds;
                state.HoldCoverUntil = IsCommanderAttacker(soldier)
                    ? float.PositiveInfinity
                    : state.ManeuverCoverMinimumHoldUntil;
                state.ManeuverCoverReleaseUntil = 0f;
                state.ManeuverCoverReleasedId = IntPtr.Zero;
                AiState.Trace(
                    IsCommanderAttacker(soldier)
                        ? $"Cover hold: attacker {soldierId} reached a fighting position and will wait for covering fire"
                        : $"Cover hold: soldier {soldierId} reached a fighting position for " +
                          $"{InfantryCoverPolicy.MinimumManeuverCoverHoldSeconds:0}s");
            }

            return now < state.HoldCoverUntil;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDefensiveAnchorKnownCompromised(
        Soldier soldier,
        ContactResponseState state)
    {
        try
        {
            var current = soldier.targetDestination;
            if (current == null)
                return false;
            if (current.WasCollected)
                return true;
            if (current.Pointer != state.DefensiveCoverAnchorId)
                return false;

            return current.IsCoverDestroyed() || current.IsUnsafeCover();
        }
        catch (ObjectCollectedException)
        {
            return true;
        }
        catch
        {
            // A transient native-wrapper failure is not enough evidence to abandon
            // a fortification. The next successful update can still invalidate it.
            return false;
        }
    }

    private static bool IsInsideDefensiveArea(Soldier soldier)
    {
        if (!TryGetDefensiveArea(soldier, out var center, out var radius))
            return false;

        return DefensivePositioningCore.IsInsideArea(
            new MapPoint(soldier.transform.position.x, soldier.transform.position.z),
            new MapPoint(center.x, center.z),
            radius);
    }

    private static bool TryGetDefensiveArea(
        Soldier soldier,
        out Vector3 center,
        out float radius)
    {
        center = default;
        radius = 0f;
        try
        {
            var squad = soldier.joinedSquad;
            if (squad == null)
                return false;

            // A commander lease is the authoritative source. The native order
            // fields are mutable game state and may be rewritten between director
            // updates; allowing that to revoke this area was the main movement-churn
            // hole for defenders.
            if (GroundAiDirector.TryGetCommanderDefensiveArea(
                    squad, out center, out radius))
            {
                return IsFinite(center) && !float.IsNaN(radius) &&
                       !float.IsInfinity(radius) && radius >= 0f;
            }

            if (squad.order != Order.defend)
                return false;

            center = squad.moveOrderPosition;
            radius = squad.moveOrderRadius;
            if (float.IsNaN(center.x) || float.IsInfinity(center.x) ||
                float.IsNaN(center.y) || float.IsInfinity(center.y) ||
                float.IsNaN(center.z) || float.IsInfinity(center.z) ||
                float.IsNaN(radius) || float.IsInfinity(radius))
            {
                return false;
            }

            var tolerance = IsPlayerLedSquad(squad)
                ? PlayerHoldAreaToleranceMeters
                : GroundAiDirector.OwnsSquad(squad)
                    ? 0f
                    : DefensiveAreaToleranceMeters;
            radius = Mathf.Max(0f, radius) + tolerance;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ReleaseDefensiveCoverHold(
        ContactResponseState state,
        int soldierId)
    {
        if (!state.DefensiveCoverHold && !state.HasDefensiveCoverAnchor)
            return;

        state.DefensiveCoverHold = false;
        state.HasDefensiveCoverAnchor = false;
        state.DefensiveCoverAnchorId = IntPtr.Zero;
        state.DefensiveCoverAnchorPosition = default;
        if (float.IsPositiveInfinity(state.HoldCoverUntil))
            state.HoldCoverUntil = 0f;
        state.ReservedCoverId = IntPtr.Zero;
        state.ReservedCoverPosition = default;
        AiState.ReleaseCoverReservation(soldierId);
    }

    internal static bool ShouldKeepReachedCover(Soldier soldier)
    {
        // Calls made by the director's sole cover executor are intentional tactical
        // transitions and must pass through the native CoverPosition method.
        if (_coverAssignmentExecutorSoldierId == soldier.GetInstanceID())
            return false;

        if (!Settings.ContactResponseEnabled.Value)
            return false;

        var soldierId = soldier.GetInstanceID();
        var state = AiState.GetContactState(soldierId);
        var now = Time.time;
        UpdateDefensiveCoverHold(soldier, state, soldierId, now);
        if (state.DefensiveCoverHold)
            return true;

        return UpdateManeuverCoverObservation(soldier, state, soldierId, now);
    }

    internal static bool ShouldBlockNativeCoverClear(Soldier soldier)
    {
        var soldierId = soldier.GetInstanceID();
        if (_coverAssignmentExecutorSoldierId == soldierId)
            return false;

        var protectedAssignment =
            GroundAiDirector.HasProtectedInfantryAssignment(soldier);
        var state = AiState.GetContactState(soldierId);
        var defensivePositionOwned =
            RefreshDefensivePositionOwnership(soldier, state);
        var reachedCoverHold = false;
        if (!state.Relocating && !state.DefensiveCoverHold &&
            !state.HasDefensiveCoverAnchor)
        {
            reachedCoverHold = UpdateManeuverCoverObservation(
                soldier, state, soldierId, Time.time);
        }

        return InfantryCoverDecisionCore.ShouldBlockNativeCoverClear(
            protectedAssignment,
            defensivePositionOwned,
            state.Relocating,
            state.DefensiveCoverHold || state.HasDefensiveCoverAnchor,
            reachedCoverHold);
    }

    internal static bool IsOnUsableCover(Soldier soldier)
    {
        if (!soldier.IsOnCover())
            return false;

        try
        {
            var cover = soldier.targetDestination;
            if (cover == null || cover.WasCollected || cover.Pointer == IntPtr.Zero ||
                cover.IsCoverDestroyed() || cover.IsUnsafeCover())
            {
                return false;
            }

            // IsCoverAvailable also rejects an occupied slot, including the
            // soldier's own reached cover, so it must not be used as an occupancy-
            // time facing test here. Threat direction is enforced when selecting it.
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCurrentCoverProtective(
        Soldier soldier,
        ContactResponseState state,
        Vector3 threatPosition,
        float now)
        => TryGetCurrentCoverEvaluation(
               soldier, state, threatPosition, now, out var evaluation) &&
           evaluation.IsProtective;

    private static bool TryGetCurrentCoverEvaluation(
        Soldier soldier,
        ContactResponseState state,
        Vector3 threatPosition,
        float now,
        out CurrentCoverEvaluation evaluation)
    {
        evaluation = default;
        if (!IsOnUsableCover(soldier))
            return false;

        try
        {
            var cover = soldier.targetDestination;
            if (cover == null || cover.WasCollected || cover.Pointer == IntPtr.Zero)
                return false;

            var threatDirection = threatPosition - soldier.transform.position;
            threatDirection.y = 0f;
            if (threatDirection.sqrMagnitude < 0.01f)
                return false;
            threatDirection.Normalize();

            var cacheMatches = state.EvaluatedCoverPostureId == cover.Pointer &&
                               now < state.EvaluatedCoverPostureUntil &&
                               state.EvaluatedCoverThreatDirection.sqrMagnitude > 0.5f &&
                               Vector3.Dot(
                                   state.EvaluatedCoverThreatDirection,
                                   threatDirection) >= CoverPostureDirectionDotTolerance;
            if (cacheMatches)
            {
                evaluation = new CurrentCoverEvaluation(
                    state.EvaluatedCoverIsProtective,
                    state.EvaluatedCoverPosture);
                return true;
            }

            var geometry = EvaluateCoverGeometry(
                soldier.transform.position, threatPosition, true);
            var pose = ToSoldierPose(geometry.Choice);
            state.EvaluatedCoverPostureId = cover.Pointer;
            state.EvaluatedCoverPosture = pose;
            state.EvaluatedCoverIsProtective = geometry.IsProtective;
            state.EvaluatedCoverThreatDirection = threatDirection;
            state.EvaluatedCoverPostureUntil = now + CoverPostureCacheSeconds;
            evaluation = new CurrentCoverEvaluation(geometry.IsProtective, pose);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ResetCoverPostureEvaluation(ContactResponseState state)
    {
        state.EvaluatedCoverPostureId = IntPtr.Zero;
        state.EvaluatedCoverPosture = SoldierPose.Crouch;
        state.EvaluatedCoverIsProtective = false;
        state.EvaluatedCoverThreatDirection = default;
        state.EvaluatedCoverPostureUntil = 0f;
    }

    internal static bool IsWeaponFiring(Soldier soldier)
    {
        try
        {
            var gun = soldier.GetHeldGun();
            return gun != null && gun.IsFiring;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryPreventBlockedCoverShot(
        Soldier soldier,
        Vector3 origin,
        Vector3 fireDirection)
    {
        if (!Settings.ContactResponseEnabled.Value || !IsOnUsableCover(soldier) ||
            !HasNearMuzzleObstruction(soldier, origin, fireDirection))
        {
            return false;
        }

        var soldierId = soldier.GetInstanceID();
        var state = AiState.GetContactState(soldierId);
        if (IsPinned(soldierId))
            return true;

        if (soldier.Pose == SoldierPose.Crouch)
        {
            ClaimCoverClearancePose(soldier, state);
            soldier.SetPose(SoldierPose.Idle);
        }

        // Withhold the obstructed round. Once the standing animation clears a low
        // parapet, the following trigger attempt proceeds normally. If even the
        // standing muzzle remains blocked, continuing to shoot the wall is never a
        // useful fallback.
        return true;
    }

    internal static SoldierPose StationaryHoldPose(Soldier soldier)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        if (OwnsCurrentCoverClearancePose(soldier, state))
            return SoldierPose.Idle;

        if (state.HasThreatPosition &&
            TryGetCurrentCoverEvaluation(
                soldier,
                state,
                state.LastThreatPosition,
                Time.time,
                out var evaluation))
        {
            return evaluation.Pose;
        }

        return SoldierPose.Crouch;
    }

    internal static SoldierPose SuppressionPose(Soldier soldier)
    {
        if (!IsOnUsableCover(soldier))
            return SoldierPose.Prone;

        var state = AiState.GetContactState(soldier.GetInstanceID());
        if (state.HasThreatPosition &&
            TryGetCurrentCoverEvaluation(
                soldier,
                state,
                state.LastThreatPosition,
                Time.time,
                out var evaluation))
        {
            // Pinning never owns a standing firing exposure. Keep the protection
            // decision, but drop behind low cover if its firing pose was standing.
            return evaluation.Pose == SoldierPose.Idle
                ? SoldierPose.Crouch
                : evaluation.Pose;
        }

        return SoldierPose.Crouch;
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
            SetCoverState(state, InfantryCoverState.WaitingForSafeMove, soldierId,
                "no tank-masked cover is reachable");
            return false;
        }

        if (!BeginRelocation(ai, soldier, state, cover, soldierId, now))
        {
            SetCoverState(state, InfantryCoverState.WaitingForSafeMove, soldierId,
                "tank-masked cover could not be assigned");
            return false;
        }

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
            EnsureTacticalPose(ai, soldier, SoldierPose.Crouch);
            return;
        }

        if (!state.SuppressionPoseOwned)
            return;

        if (now < state.SuppressionCrouchUntil)
        {
            EnsureTacticalPose(ai, soldier, SoldierPose.Crouch);
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
        ai.moveCharacter = false;
        soldier.isSprinting = false;

        // The first reaction to a hard burst is shock: get down and stop exposing
        // yourself. Once that brief reaction passes, the soldier remains stationary
        // but is allowed to aim and return fire instead of becoming inert forever.
        if (now < state.PinnedFireBlockedUntil)
        {
            state.SuppressionFireInhibited = true;
            state.FireRestorePending = true;
            ai.allowFireAtEnemy = false;
            ai.aimingEnemy = false;
            soldier.StopFire();
        }
        else if (state.SuppressionFireInhibited)
        {
            state.SuppressionFireInhibited = false;
            state.FireRestorePending = true;
            RestoreFireAfterOwnedInhibition(ai, soldier);
        }

        var pose = SuppressionPose(soldier);
        EnsureTacticalPose(ai, soldier, pose);
        soldier.StopMove(pose, deltaTime);
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
        var ownedFire = state.SuppressionFireInhibited;
        var pausedRelocation = state.RelocationPausedBySuppression;

        state.Pinned = false;
        state.PinnedUntil = 0f;
        state.PinnedFireBlockedUntil = 0f;
        state.SuppressionMovementOwned = false;
        state.SuppressionPoseOwned = false;
        state.SuppressionCrouchUntil = 0f;
        state.SuppressionFireInhibited = false;
        state.RelocationPausedBySuppression = false;

        if (pausedRelocation && state.Relocating)
            ResumePausedRelocation(ai, state, soldierId, now, "Suppression-disabled cover path resume failed");

        if (ownedMovement && !ContactOrHazardOwnsMovement(soldier, state, soldierId, now))
            ai.moveCharacter = true;
        if (ownedFire)
        {
            state.FireRestorePending = true;
            RestoreFireAfterOwnedInhibition(ai, soldier);
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
        var ownedFire = state.SuppressionFireInhibited;
        state.SuppressionMovementOwned = false;
        state.SuppressionFireInhibited = false;
        state.PinnedFireBlockedUntil = 0f;

        if (state.RelocationPausedBySuppression)
        {
            state.RelocationPausedBySuppression = false;
            if (state.Relocating)
                ResumePausedRelocation(ai, state, soldierId, now, "Suppression cover path resume failed");
        }

        if (ownedMovement && !ContactOrHazardOwnsMovement(soldier, state, soldierId, now))
            ai.moveCharacter = true;
        if (ownedFire)
        {
            state.FireRestorePending = true;
            RestoreFireAfterOwnedInhibition(ai, soldier);
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
        if (!state.RelocationPausedByCloseFire)
            RefreshPath(ai, warning);
    }

    private static bool ContactOrHazardOwnsMovement(
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now)
        => (Settings.DangerReactionsEnabled.Value &&
            (soldier.IsOnFire || AiState.IsFlameEvading(soldierId, now))) ||
           (Settings.ContactResponseEnabled.Value &&
            (state.MovementInhibitedByContactResponse || now < state.EngagementHoldUntil ||
             (soldier.IsOnCover() && now < state.HoldCoverUntil)));

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
        catch
        {
            return false;
        }
    }

    private static float HorizontalDistanceSqr(Vector3 from, Vector3 to)
    {
        var delta = to - from;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }

    private static void SetCoverState(
        ContactResponseState state,
        InfantryCoverState nextState,
        int soldierId,
        string reason)
    {
        if (state.CoverState == nextState)
            return;

        state.CoverState = nextState;
        AiState.Trace(
            $"Cover FSM: soldier {soldierId} -> {nextState} ({reason})");
    }

    private static AiDestination? FindCover(
        Soldier soldier,
        Vector3 targetPosition,
        float searchRadius,
        ContactResponseState state,
        float now,
        CoverSelectionMode selectionMode,
        Vector3? directFirePosition,
        bool respectAttackWaypoint,
        bool evaluateFiringQuality,
        out float bestFortifiedScore,
        out bool searchDeferred)
    {
        bestFortifiedScore = float.NegativeInfinity;
        searchDeferred = !TryBeginDetailedCoverSearch();
        if (searchDeferred)
            return null;

        if (state.FailedCoverId != IntPtr.Zero && now >= state.FailedCoverUntil)
            state.FailedCoverId = IntPtr.Zero;

        var position = soldier.transform.position;
        var attackWaypoint = default(Vector3);
        var enforceAttackProgress = respectAttackWaypoint &&
                                    TryGetAttackWaypoint(soldier, out attackWaypoint);
        var enforceDefensiveArea = TryGetDefensiveArea(
            soldier, out var defensiveCenter, out var defensiveRadius);
        if (enforceDefensiveArea)
        {
            searchRadius = Mathf.Max(
                searchRadius,
                55f,
                defensiveRadius);
        }
        var currentWaypointDistanceSqr = enforceAttackProgress
            ? HorizontalDistanceSqr(position, attackWaypoint)
            : 0f;
        var towardThreat = targetPosition - position;
        towardThreat.y = 0f;
        if (towardThreat.sqrMagnitude < 0.01f)
            return null;
        towardThreat.Normalize();

        var knownThreats = new List<Vector3>(MaximumCoverThreats) { targetPosition };
        if (directFirePosition.HasValue)
            AppendDistinctThreat(knownThreats, directFirePosition.Value);
        ContactKnowledge.AppendRecentGroundThreatPositions(
            soldier, now, knownThreats, MaximumCoverThreats);

        var candidates = CoverManager.GetCovers(
            position,
            searchRadius,
            soldier.faction,
            towardThreat,
            true);
        if (candidates == null)
            return null;

        var constrainedSearch = enforceAttackProgress || enforceDefensiveArea;
        var rawCandidateLimit = constrainedSearch
            ? InfantryCoverPolicy.ConstrainedRawCandidateLimit
            : InfantryCoverPolicy.RawCandidateLimit;
        var coarseCandidates = new List<CoarseCoverCandidate>(
            InfantryCoverPolicy.RawCandidateLimit);
        var rawExamined = 0;
        foreach (var rawCandidate in candidates)
        {
            // Do only native validity, reservation, and order-bound checks here.
            // Occupancy overlap queries and ballistic geometry are intentionally
            // delayed until after the bounded shortlist is selected.
            if (++rawExamined > rawCandidateLimit)
                break;

            try
            {
                var candidate = rawCandidate.TryCast<AiDestination>();
                if (candidate == null || candidate.WasCollected || candidate.Pointer == IntPtr.Zero ||
                    candidate.IsVehicle() || candidate.IsCoverDestroyed() || candidate.IsUnsafeCover())
                {
                    continue;
                }

                var candidateId = candidate.Pointer;
                if (state.FailedCoverId == candidateId && now < state.FailedCoverUntil)
                    continue;
                if (!candidate.IsCoverAvailable(towardThreat, soldier.faction))
                    continue;

                if (!ExclusiveCoverAssignmentPatch.TryGetUsableCoverPosition(candidate, out var coverPosition))
                    continue;
                if (AiState.CoverReservedByOther(
                        candidateId,
                        coverPosition,
                        soldier.GetInstanceID(),
                        now,
                        InfantryCoverPolicy.OccupancyRadiusMeters))
                {
                    continue;
                }
                if (enforceDefensiveArea &&
                    !DefensivePositioningCore.IsInsideArea(
                        new MapPoint(coverPosition.x, coverPosition.z),
                        new MapPoint(defensiveCenter.x, defensiveCenter.z),
                        defensiveRadius))
                {
                    continue;
                }
                var distanceSqr = (coverPosition - position).sqrMagnitude;
                if (distanceSqr < 1f)
                    continue;
                if (enforceAttackProgress &&
                    HorizontalDistanceSqr(coverPosition, attackWaypoint) >= currentWaypointDistanceSqr)
                {
                    continue;
                }

                coarseCandidates.Add(new CoarseCoverCandidate(
                    candidate, coverPosition, distanceSqr));
            }
            catch (NullReferenceException)
            {
                // Native cover lists can briefly contain torn-down objects.
            }
            catch (Il2CppException)
            {
            }
            catch (ObjectCollectedException)
            {
            }
        }

        AiDestination? best = null;
        var bestScore = float.MaxValue;
        var detailedIndices = CoverCandidateSamplingCore.SelectIndices(
            coarseCandidates.Count,
            InfantryCoverPolicy.DetailedCandidateLimit,
            InfantryCoverPolicy.NearestDetailedCandidateCount);
        foreach (var detailedIndex in detailedIndices)
        {
            try
            {
                var coarse = coarseCandidates[detailedIndex];
                var candidate = coarse.Destination;
                var coverPosition = coarse.Position;
                var distanceSqr = coarse.DistanceSqr;
                var candidateId = candidate.Pointer;
                if (candidate.WasCollected || candidateId == IntPtr.Zero ||
                    CoverOccupancy.IsOccupiedByOther(coverPosition, soldier))
                {
                    continue;
                }

                var geometry = EvaluateCoverGeometry(
                    coverPosition, targetPosition, evaluateFiringQuality);
                var selectedPosture = geometry.Selected;
                var usesStandingPose = geometry.Choice == CoverPostureChoice.Standing;
                var posePenalty = usesStandingPose
                    ? InfantryCoverPolicy.StandingCoverPenalty
                    : 0f;
                var primaryProtected = geometry.IsProtective;
                var unprotectedSecondaryThreats = 0;
                for (var threatIndex = 1; threatIndex < knownThreats.Count; threatIndex++)
                {
                    var secondaryProtection = EvaluateCoverProtection(
                        coverPosition, knownThreats[threatIndex], geometry.Choice);
                    if (!InfantryCoverDecisionCore.HasMeaningfulProtection(
                            secondaryProtection))
                    {
                        unprotectedSecondaryThreats++;
                    }
                }

                var assignedPoseCanFire = selectedPosture.CanFire;
                var standingCanFire = !evaluateFiringQuality ||
                                      geometry.Standing.CanFire &&
                                      (InfantryCoverDecisionCore.HasMeaningfulProtection(
                                           geometry.Standing) ||
                                       InfantryCoverDecisionCore.HasMeaningfulProtection(
                                           geometry.Crouched));
                var exposedRouteFraction = MeasureExposedRouteFraction(
                    position, coverPosition, knownThreats);
                var protectionPosture = geometry.Choice == CoverPostureChoice.Standing &&
                                        InfantryCoverDecisionCore.HasMeaningfulProtection(
                                            geometry.Crouched)
                    ? geometry.Crouched
                    : selectedPosture;
                var primaryProtectionFraction =
                    InfantryCoverDecisionCore.ProtectionFraction(protectionPosture);
                var scoreInput = new CoverScoreInput(
                    distanceSqr,
                    posePenalty,
                    primaryProtected,
                    unprotectedSecondaryThreats,
                    assignedPoseCanFire,
                    standingCanFire,
                    Mathf.Sqrt(distanceSqr) * exposedRouteFraction,
                    exposedRouteFraction,
                    primaryProtectionFraction,
                    PreferProtectionOverFiringLine: enforceDefensiveArea);
                if (!InfantryCoverDecisionCore.IsRouteAcceptable(selectionMode, scoreInput))
                    continue;

                var score = InfantryCoverDecisionCore.Score(selectionMode, scoreInput);
                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                    var firingLaneQuality = assignedPoseCanFire
                        ? 1f
                        : standingCanFire ? 0.7f : 0f;
                    bestFortifiedScore = FortifiedPositionCore.Score(
                        new FortifiedCoverSlot(
                            candidateId.GetHashCode(),
                            new MapPoint(coverPosition.x, coverPosition.z),
                            primaryProtectionFraction,
                            firingLaneQuality,
                            exposedRouteFraction,
                            Mathf.Sqrt(distanceSqr),
                            true),
                        searchRadius);
                }

            }
            catch (NullReferenceException)
            {
                // Native cover lists can briefly contain torn-down objects.
            }
            catch (Il2CppException)
            {
            }
            catch (ObjectCollectedException)
            {
            }
        }

        return best;
    }

    private static void AppendDistinctThreat(List<Vector3> threats, Vector3 position)
    {
        if (threats.Count >= MaximumCoverThreats)
            return;

        for (var i = 0; i < threats.Count; i++)
        {
            if ((threats[i] - position).sqrMagnitude <= 16f)
                return;
        }

        threats.Add(position);
    }

    private static CoverGeometryEvaluation EvaluateCoverGeometry(
        Vector3 coverPosition,
        Vector3 threatPosition,
        bool evaluateFiringQuality)
    {
        var standing = EvaluateCoverProtection(
            coverPosition, threatPosition, CoverPostureChoice.Standing) with
        {
            CanFire = !evaluateFiringQuality || HasClearFireLane(
                coverPosition + Vector3.up * StandingCoverMuzzleHeight,
                threatPosition)
        };
        var crouched = EvaluateCoverProtection(
            coverPosition, threatPosition, CoverPostureChoice.Crouched) with
        {
            CanFire = !evaluateFiringQuality || HasClearFireLane(
                coverPosition + Vector3.up * CrouchedCoverMuzzleHeight,
                threatPosition)
        };
        var prone = EvaluateCoverProtection(
            coverPosition, threatPosition, CoverPostureChoice.Prone) with
        {
            CanFire = !evaluateFiringQuality || HasClearFireLane(
                coverPosition + Vector3.up * ProneCoverMuzzleHeight,
                threatPosition)
        };
        var choice = InfantryCoverDecisionCore.SelectCoverPosture(
            standing, crouched, prone);
        return new CoverGeometryEvaluation(choice, standing, crouched, prone);
    }

    private static CoverPostureInput EvaluateCoverProtection(
        Vector3 coverPosition,
        Vector3 threatPosition,
        CoverPostureChoice posture)
    {
        float torsoHeight;
        float headHeight;
        float shoulderHeight;
        float shoulderHalfWidth;
        switch (posture)
        {
            case CoverPostureChoice.Standing:
                torsoHeight = StandingCoverBodyHeight;
                headHeight = 1.65f;
                shoulderHeight = 1.38f;
                shoulderHalfWidth = CoverShoulderHalfWidth;
                break;
            case CoverPostureChoice.Crouched:
                torsoHeight = CrouchedCoverBodyHeight;
                headHeight = 1.15f;
                shoulderHeight = 0.96f;
                shoulderHalfWidth = CoverShoulderHalfWidth;
                break;
            default:
                torsoHeight = 0.24f;
                headHeight = 0.42f;
                shoulderHeight = 0.32f;
                shoulderHalfWidth = CoverShoulderHalfWidth * 0.9f;
                break;
        }

        var threatDirection = threatPosition - coverPosition;
        threatDirection.y = 0f;
        if (threatDirection.sqrMagnitude < 0.01f)
            return new CoverPostureInput(0, 4, false);
        threatDirection.Normalize();
        var lateral = Vector3.Cross(Vector3.up, threatDirection).normalized *
                      shoulderHalfWidth;

        var torsoProtection = ProtectionFromThreat(
            coverPosition + Vector3.up * torsoHeight, threatPosition);
        var headProtection = ProtectionFromThreat(
            coverPosition + Vector3.up * headHeight, threatPosition);
        var leftShoulderProtection = ProtectionFromThreat(
            coverPosition + Vector3.up * shoulderHeight + lateral,
            threatPosition);
        var rightShoulderProtection = ProtectionFromThreat(
            coverPosition + Vector3.up * shoulderHeight - lateral,
            threatPosition);
        var protectedSamples = 0;
        if (BallisticCoverDecisionCore.IsMeaningfulRay(torsoProtection))
            protectedSamples++;
        if (BallisticCoverDecisionCore.IsMeaningfulRay(headProtection))
            protectedSamples++;
        if (BallisticCoverDecisionCore.IsMeaningfulRay(leftShoulderProtection))
            protectedSamples++;
        if (BallisticCoverDecisionCore.IsMeaningfulRay(rightShoulderProtection))
            protectedSamples++;

        var ballisticProtection =
            (torsoProtection + headProtection +
             leftShoulderProtection + rightShoulderProtection) * 0.25f;
        return new CoverPostureInput(
            protectedSamples, 4, false, ballisticProtection);
    }

    private static SoldierPose ToSoldierPose(CoverPostureChoice choice)
        => choice switch
        {
            CoverPostureChoice.Standing => SoldierPose.Idle,
            CoverPostureChoice.Crouched => SoldierPose.Crouch,
            _ => SoldierPose.Prone
        };

    private static float MeasureExposedRouteFraction(
        Vector3 start,
        Vector3 destination,
        IReadOnlyList<Vector3> threats)
    {
        const int sampleCount = 3;
        var exposedSamples = 0;
        for (var sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
        {
            var fraction = sampleIndex / (sampleCount + 1f);
            var sample = Vector3.Lerp(start, destination, fraction) +
                         Vector3.up * MovingBodyHeight;
            var exposed = false;
            for (var threatIndex = 0; threatIndex < threats.Count; threatIndex++)
            {
                if (!BallisticCoverDecisionCore.IsMeaningfulRay(
                        ProtectionFromThreat(sample, threats[threatIndex])))
                {
                    exposed = true;
                    break;
                }
            }

            if (exposed)
                exposedSamples++;
        }

        return exposedSamples / (float)sampleCount;
    }

    private static float ProtectionFromThreat(
        Vector3 bodyPosition,
        Vector3 threatPosition)
    {
        try
        {
            return BulletPenetration.EvaluateOrdinaryCoverLine(
                bodyPosition,
                threatPosition).ProtectionFraction;
        }
        catch
        {
            // Unknown geometry is not credited as protection.
            return 0f;
        }
    }

    private static bool HasClearFireLane(Vector3 muzzlePosition, Vector3 targetPosition)
    {
        try
        {
            return !HasWorldObstruction(muzzlePosition, targetPosition);
        }
        catch
        {
            // Do not deliberately choose a firing pose whose lane could not be
            // evaluated. The destination can still win in urgent survival mode.
            return false;
        }
    }

    private static bool HasWorldObstruction(Vector3 origin, Vector3 target)
    {
        var ray = target - origin;
        var distance = ray.magnitude;
        var castDistance = distance - ThreatEndpointTolerance;
        if (castDistance <= 0.1f)
            return false;

        var hits = _fireLaneHits ??= new RaycastHit[32];
        var hitCount = Physics.RaycastNonAlloc(
            origin,
            ray / distance,
            hits,
            castDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        for (var i = 0; i < hitCount; i++)
        {
            var collider = hits[i].collider;
            if (collider == null || collider.GetComponentInParent<Soldier>() != null)
                continue;

            return true;
        }

        return false;
    }

    private static bool BeginRelocation(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        AiDestination cover,
        int soldierId,
        float now)
    {
        IntPtr coverId;
        try
        {
            if (cover.WasCollected || (coverId = cover.Pointer) == IntPtr.Zero)
                return false;
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

        if ((state.FailedCoverId == coverId && now < state.FailedCoverUntil) ||
            !ExclusiveCoverAssignmentPatch.TryGetUsableCoverPosition(cover, out var coverPosition) ||
            AiState.CoverReservedByOther(
                 coverId,
                 coverPosition,
                 soldierId,
                 now,
                 InfantryCoverPolicy.OccupancyRadiusMeters) ||
            CoverOccupancy.IsOccupiedByOther(coverPosition, soldier))
        {
            return false;
        }

        if (TryGetDefensiveArea(soldier, out var defensiveCenter, out var defensiveRadius) &&
            !DefensivePositioningCore.IsInsideArea(
                new MapPoint(coverPosition.x, coverPosition.z),
                new MapPoint(defensiveCenter.x, defensiveCenter.z),
                defensiveRadius))
        {
            return false;
        }

        var relocateUntil = now + InfantryCoverPolicy.MoveProgressWindowSeconds;
        AiState.ReserveCover(
            coverId, coverPosition, soldierId, relocateUntil + 2f);
        var previousCoverExecutor = _coverAssignmentExecutorSoldierId;
        try
        {
            _coverAssignmentExecutorSoldierId = soldierId;
            ReleaseManeuverCoverForAuthorizedMove(state, now);
            soldier.CoverPosition(cover);
            if (!soldier.HasDestinationAssigned ||
                !SameNativeDestination(soldier.targetDestination, cover))
            {
                AiState.ReleaseCoverReservation(soldierId);
                MarkFailedCover(state, coverId, now);
                AiState.Trace($"Contact response: soldier {soldierId} rejected a cover assignment");
                return false;
            }

            ai.UpdatePath();
        }
        catch (Exception ex)
        {
            AiState.ReleaseCoverReservation(soldierId);
            MarkFailedCover(state, coverId, now);
            Plugin.LogSource.LogWarning($"Contact cover assignment failed: {ex.Message}");
            return false;
        }
        finally
        {
            _coverAssignmentExecutorSoldierId = previousCoverExecutor;
        }

        if (!soldier.HasDestinationAssigned || soldier.DestinationReached)
        {
            AiState.ReleaseCoverReservation(soldierId);
            MarkFailedCover(state, coverId, now);
            AiState.Trace($"Contact response: soldier {soldierId} rejected an unassigned cover move");
            return false;
        }

        state.Relocating = true;
        state.LastOutgoingShotWasStationary = false;
        state.RelocationPausedBySuppression = false;
        state.RelocationPausedByCloseFire = false;
        state.EngagementHoldUntil = 0f;
        state.RelocateUntil = relocateUntil;
        state.RelocateLastDistance = soldier.DestinationDistance;
        state.RelocateLastProgressAt = now;
        state.RelocateLastProgressPosition = soldier.transform.position;
        state.RelocateDestinationPointer = coverId;
        state.RelocateDestinationPosition = coverPosition;
        state.ReservedCoverId = coverId;
        state.ReservedCoverPosition = coverPosition;
        ContinueCommittedMovement(ai, soldier, state, now);

        AiState.Trace($"Contact response: soldier {soldierId} relocating {soldier.DestinationDistance:0.0}m to cover");
        return true;
    }

    private static void MarkFailedCover(ContactResponseState state, IntPtr coverId, float now)
    {
        if (coverId == IntPtr.Zero)
            return;

        state.FailedCoverId = coverId;
        state.FailedCoverUntil = now + InfantryCoverPolicy.RelocationCooldownSeconds;
    }

    private static bool SameNativeDestination(AiDestination? actual, AiDestination expected)
    {
        if (actual == null)
            return false;

        try
        {
            return !actual.WasCollected &&
                   !expected.WasCollected &&
                   actual.Pointer != IntPtr.Zero &&
                   actual.Pointer == expected.Pointer;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
    }

    private static bool SameNativeDestination(AiDestination? actual, IntPtr expectedPointer)
    {
        if (actual == null || expectedPointer == IntPtr.Zero)
            return false;

        try
        {
            return !actual.WasCollected && actual.Pointer == expectedPointer;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
    }

    private static void ContinueCommittedMovement(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        float now)
    {
        if (state.Pinned || state.SuppressionMovementOwned)
        {
            PauseRelocation(state, soldier.GetInstanceID(), now, true);
            ApplyPinnedSuppression(ai, soldier, state, now, Time.deltaTime);
            return;
        }

        state.EngagementHoldUntil = 0f;
        state.ContactCrouchOwned = true;
        var canFireWhileMoving = HandheldWeaponClassifier.AllowsMovingFire(soldier, ai);
        if (!canFireWhileMoving)
        {
            state.FireRestorePending = true;
            ai.allowFireAtEnemy = false;
            ai.aimingEnemy = false;
            soldier.StopFire();
        }
        ai.moveCharacter = true;
        state.MovementInhibitedByContactResponse = false;
        EnsureTacticalPose(ai, soldier, SoldierPose.Crouch);
        if (canFireWhileMoving && GetActionableTarget(ai, soldier) != null)
            GrantFirePermissionIfReady(ai, soldier);
    }

    private static void ContinueAttackObjectiveMovement(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now,
        bool suppressed,
        bool forcedByDeadline)
    {
        if (!HasCommittedDestination(soldier))
            return;

        var wasHoldingMovement = state.SuppressionMovementOwned ||
                                 state.MovementInhibitedByContactResponse ||
                                 now < state.EngagementHoldUntil ||
                                 now < state.HoldCoverUntil;
        var newlyForced = forcedByDeadline && !state.AttackProgressForced;
        state.AttackProgressForced = forcedByDeadline;
        ReleaseManeuverCoverForAuthorizedMove(state, now);
        state.EngagementHoldUntil = 0f;
        state.SuppressionMovementOwned = false;
        state.RelocationPausedBySuppression = false;
        state.MovementInhibitedByContactResponse = false;
        state.ContactCrouchOwned = true;
        state.SuppressionPoseOwned = suppressed;
        ai.moveCharacter = true;
        soldier.isSprinting = false;

        if (state.SuppressionFireInhibited && now >= state.PinnedFireBlockedUntil)
        {
            state.SuppressionFireInhibited = false;
            state.FireRestorePending = true;
            RestoreFireAfterOwnedInhibition(ai, soldier);
        }

        EnsureTacticalPose(
            ai,
            soldier,
            state.Pinned ||
            (Settings.DangerReactionsEnabled.Value &&
             soldier.GetSuppressionValue() >= Settings.ProneSuppression.Value)
                ? SoldierPose.Prone
                : SoldierPose.Crouch);

        if (wasHoldingMovement)
            RefreshPath(ai, "Attack objective path resume failed");
        if (newlyForced)
        {
            AiState.Trace(
                $"Attack progress: soldier {soldierId} resumed toward the objective after the maximum combat halt");
        }
    }

    private static bool IsCommanderAttacker(Soldier soldier)
    {
        try
        {
            var squad = soldier.joinedSquad;
            return squad != null && GroundAiDirector.OwnsSquad(squad) &&
                   GroundAiDirector.IsAttackingFaction(soldier.faction);
        }
        catch
        {
            return false;
        }
    }

    private static void ReleaseManeuverCoverForAuthorizedMove(
        ContactResponseState state,
        float now)
    {
        if (state.DefensiveCoverHold)
            return;

        state.ManeuverCoverReleasedId = state.ManeuverCoverAnchorId;
        state.ManeuverCoverReleaseUntil = now + 2f;
        state.ManeuverCoverMinimumHoldUntil = 0f;
        state.ManeuverCoverAnchorId = IntPtr.Zero;
        state.ManeuverCoverAnchorPosition = default;
        state.HoldCoverUntil = 0f;
    }

    private static void FinishRelocation(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now,
        bool keepOccupiedCover,
        bool completedMove,
        bool markFailedCover = true)
    {
        if (!state.Relocating)
            return;

        state.FireRestorePending = true;
        if (!completedMove && markFailedCover)
            MarkFailedCover(state, state.ReservedCoverId, now);
        else if (completedMove && state.FailedCoverId == state.ReservedCoverId)
            state.FailedCoverId = IntPtr.Zero;

        state.Relocating = false;
        SetCoverState(
            state,
            completedMove ? InfantryCoverState.Holding : InfantryCoverState.WaitingForSafeMove,
            soldierId,
            completedMove ? "selected cover reached" : "cover move ended before arrival");
        state.RelocationPausedBySuppression = false;
        state.RelocationPausedByCloseFire = false;
        state.RelocateLastDistance = 0f;
        state.RelocateLastProgressAt = 0f;
        state.RelocateLastProgressPosition = default;
        var occupiedCoverPosition = state.ReservedCoverPosition;
        state.RelocateDestinationPointer = IntPtr.Zero;
        state.RelocateDestinationPosition = default;
        if (completedMove)
        {
            state.AttackProgressForced = false;
            state.AttackHaltStartedAt = TryGetAttackWaypoint(soldier, out _)
                ? Mathf.Max(now, 0.0001f)
                : 0f;
            if (state.ReservedCoverId != IntPtr.Zero)
            {
                state.ManeuverCoverAnchorId = state.ReservedCoverId;
                state.ManeuverCoverAnchorPosition = occupiedCoverPosition;
                if (state.DefensivePositionOwned)
                {
                    TryCaptureReservedDefensiveCoverAnchor(
                        soldier, state, soldierId, now);
                }
                if (!state.DefensiveCoverHold)
                {
                    state.HoldCoverUntil = now +
                                           InfantryCoverPolicy.MinimumManeuverCoverHoldSeconds;
                }
            }
        }
        var retryAt = completedMove
            ? now + InfantryCoverPolicy.RelocationCooldownSeconds
            : now + InfantryCoverPolicy.DecisionIntervalSeconds;
        state.NextRelocationAllowedAt = retryAt;
        if (!completedMove)
            state.NextDecisionAt = Mathf.Max(state.NextDecisionAt, retryAt);
        if (keepOccupiedCover && state.ReservedCoverId != IntPtr.Zero)
        {
            AiState.ReserveCover(
                state.ReservedCoverId,
                occupiedCoverPosition,
                soldierId,
                now + 2f);
        }
        else
        {
            state.ReservedCoverId = IntPtr.Zero;
            state.ReservedCoverPosition = default;
            AiState.ReleaseCoverReservation(soldierId);
        }
    }

    private static void RespondWithoutNewCover(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        Vector3 targetPosition,
        float now,
        bool preferProne = false)
    {
        // A contact is a fighting halt, not permission to continue jogging along a
        // native objective route. Moving fire remains available during a committed
        // cover dash or an explicitly authorized attack bound; without either, the
        // soldier stops, reduces his silhouette, observes, and returns fire.
        state.EngagementHoldUntil = float.PositiveInfinity;
        state.ContactCrouchOwned = true;
        var stationaryPose = preferProne && !IsOnUsableCover(soldier)
            ? SoldierPose.Prone
            : GetStationaryEngagementPose(soldier, state, targetPosition);
        StopTacticalMovement(
            ai,
            soldier,
            stationaryPose,
            Time.deltaTime);
        FaceThreatWhenStationary(ai, soldier, targetPosition);
        if (GetActionableTarget(ai, soldier) != null)
            GrantFirePermissionIfReady(ai, soldier);
    }

    private static bool HasCommittedDestination(Soldier soldier)
        => soldier.HasDestinationAssigned && !soldier.DestinationReached && soldier.DestinationDistance > 1.25f;

    /// <summary>
    /// Final locomotion watchdog for movement selected by the director. Native path
    /// distance can change while a blocked soldier's feet remain in place, so only
    /// real horizontal displacement counts as progress. A stall stops the animation,
    /// requests one new path, and imposes an increasingly long quiet hold before a
    /// retry. This prevents a permanent walking-in-place loop without manufacturing
    /// a stream of replacement destinations.
    /// </summary>
    internal static void ApplyMovementProgressWatchdog(
        SoldierAI ai,
        Soldier soldier,
        string movementSource,
        TacticalAction movementAction,
        float now)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        var watchdogMayOwnMovement =
            movementAction is TacticalAction.Move or TacticalAction.Native &&
            !string.Equals(movementSource, "external", StringComparison.Ordinal) &&
            !string.Equals(movementSource, "hazard", StringComparison.Ordinal) &&
            !string.Equals(movementSource, "tank-fear", StringComparison.Ordinal) &&
            !string.Equals(movementSource, "action-safety", StringComparison.Ordinal);
        if (!watchdogMayOwnMovement)
        {
            ResetMovementWatch(state);
            return;
        }

        var eligible = !soldier.IsOnVehicle() && HasCommittedDestination(soldier);
        var monitor = eligible && ai.moveCharacter;

        if (now < state.MovementStallHoldUntil)
        {
            StopDangerMovement(ai, soldier, SoldierPose.Crouch, Time.deltaTime);
            return;
        }

        if (state.MovementStallHoldUntil > 0f)
        {
            state.MovementStallHoldUntil = 0f;
            ResetMovementWatch(state, preserveFailures: true);
            if (eligible)
            {
                ai.moveCharacter = true;
                monitor = true;
            }
            else
                return;
        }

        Vector3 destination;
        try
        {
            destination = ai.MoveDestination;
        }
        catch
        {
            ResetMovementWatch(state);
            return;
        }

        var destinationChanged = !state.MovementWatchActive ||
                                 !string.Equals(
                                     state.MovementWatchSource, movementSource,
                                     StringComparison.Ordinal) ||
                                 HorizontalDistance(
                                     destination, state.MovementWatchDestination) >=
                                 MovementProgressWatchdogCore.DestinationChangeMeters;
        var physicalTravel = state.MovementWatchActive
            ? HorizontalDistance(soldier.transform.position, state.MovementWatchPosition)
            : 0f;
        var decision = MovementProgressWatchdogCore.Evaluate(new MovementProgressInput(
            monitor,
            ai.HasPathRequest,
            destinationChanged,
            physicalTravel,
            state.MovementWatchActive ? now - state.MovementWatchLastProgressAt : 0f));

        switch (decision)
        {
            case MovementProgressDecision.Reset:
                ResetMovementWatch(state);
                return;
            case MovementProgressDecision.Progressed:
                state.MovementWatchActive = true;
                state.MovementWatchPosition = soldier.transform.position;
                state.MovementWatchDestination = destination;
                state.MovementWatchLastProgressAt = now;
                state.MovementWatchSource = movementSource ?? string.Empty;
                if (physicalTravel >= MovementProgressWatchdogCore.ProgressEpsilonMeters)
                {
                    state.MovementStallFailures = 0;
                    state.HasMovementStallDestination = false;
                    state.MovementStallDestination = default;
                }
                return;
            case MovementProgressDecision.Halt:
                var wasRelocating = state.Relocating;
                if (wasRelocating)
                {
                    FinishRelocation(
                        ai, soldier, state, soldier.GetInstanceID(), now,
                        keepOccupiedCover: false, completedMove: false);
                }
                BeginMovementStallHold(
                    ai,
                    soldier,
                    state,
                    now,
                    $"{movementSource} destination made no physical progress",
                    refreshPath: !wasRelocating && !ai.HasPathRequest);
                return;
            default:
                return;
        }
    }

    private static void BeginMovementStallHold(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        float now,
        string reason,
        bool refreshPath = false)
    {
        try
        {
            var blockedDestination = ai.MoveDestination;
            if (IsFinite(blockedDestination))
            {
                var sameBlockedDestination = state.HasMovementStallDestination &&
                    HorizontalDistance(
                        blockedDestination, state.MovementStallDestination) <
                    MovementProgressWatchdogCore.DestinationChangeMeters;
                if (!sameBlockedDestination)
                    state.MovementStallFailures = 0;
                state.HasMovementStallDestination = true;
                state.MovementStallDestination = blockedDestination;
            }
        }
        catch
        {
            state.HasMovementStallDestination = false;
            state.MovementStallDestination = default;
        }

        state.MovementStallFailures = Math.Min(3, state.MovementStallFailures + 1);
        var holdSeconds = MovementProgressWatchdogCore.RecoverySeconds(
            state.MovementStallFailures);
        state.MovementStallHoldUntil = now + holdSeconds;
        state.NextRelocationAllowedAt = Mathf.Max(
            state.NextRelocationAllowedAt, state.MovementStallHoldUntil);
        state.NextDecisionAt = Mathf.Max(
            state.NextDecisionAt, state.MovementStallHoldUntil);
        ResetMovementWatch(state, preserveFailures: true);
        StopDangerMovement(ai, soldier, SoldierPose.Crouch, Time.deltaTime);
        if (refreshPath)
            RefreshPath(ai, "Stalled locomotion path refresh failed");
        AiState.Trace(
            $"Movement watchdog: soldier {soldier.GetInstanceID()} stopped for {holdSeconds:0.0}s; {reason}");
    }

    private static void ResetMovementWatch(
        ContactResponseState state,
        bool preserveFailures = false)
    {
        state.MovementWatchActive = false;
        state.MovementWatchPosition = default;
        state.MovementWatchDestination = default;
        state.MovementWatchLastProgressAt = 0f;
        state.MovementWatchSource = string.Empty;
        if (!preserveFailures)
        {
            state.MovementStallHoldUntil = 0f;
            state.MovementStallFailures = 0;
            state.HasMovementStallDestination = false;
            state.MovementStallDestination = default;
        }
    }

    private static float HorizontalDistance(Vector3 first, Vector3 second)
    {
        var x = first.x - second.x;
        var z = first.z - second.z;
        return Mathf.Sqrt(x * x + z * z);
    }

    internal static bool TryHoldMovementStall(
        SoldierAI ai,
        Soldier soldier,
        float now,
        float deltaTime)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        if (now >= state.MovementStallHoldUntil)
            return false;

        StopDangerMovement(ai, soldier, SoldierPose.Crouch, deltaTime);
        return true;
    }

    private static SoldierPose GetStationaryEngagementPose(
        Soldier soldier,
        ContactResponseState state,
        Vector3 targetPosition)
    {
        if (!IsOnUsableCover(soldier))
            return SoldierPose.Crouch;

        if (OwnsCurrentCoverClearancePose(soldier, state))
            return SoldierPose.Idle;

        if (TryGetCurrentCoverEvaluation(
                soldier,
                state,
                targetPosition,
                Time.time,
                out var evaluation))
        {
            if (evaluation.Pose == SoldierPose.Idle)
                ClaimCoverClearancePose(soldier, state);
            else
                ClearCoverClearancePose(state);
            return evaluation.Pose;
        }

        // Native objects can disappear while a soldier changes weapon. Preserve
        // the older muzzle-clearance fallback when the full geometry evaluation
        // cannot safely complete during that frame.
        if (!OwnsCurrentCoverClearancePose(soldier, state) && soldier.Pose == SoldierPose.Crouch)
        {
            try
            {
                var gun = soldier.GetHeldGun();
                if (gun != null)
                {
                    var origin = gun.GetBulletGenerationPosition();
                    if (HasNearMuzzleObstruction(soldier, origin, targetPosition - origin))
                        ClaimCoverClearancePose(soldier, state);
                }
            }
            catch
            {
                // Native objects can disappear while a soldier changes weapon or is
                // destroyed. The ordinary crouched posture remains the safe fallback.
            }
        }

        return OwnsCurrentCoverClearancePose(soldier, state)
            ? SoldierPose.Idle
            : SoldierPose.Crouch;
    }

    private static bool HasNearMuzzleObstruction(
        Soldier soldier,
        Vector3 origin,
        Vector3 direction)
    {
        try
        {
            var distance = direction.magnitude;
            if (distance <= 0.1f)
                return false;

            direction /= distance;
            origin += direction * 0.05f;
            var castDistance = Mathf.Min(CoverMuzzleClearanceDistance, distance - 0.05f);
            if (castDistance <= 0f ||
                !Physics.Raycast(
                    origin,
                    direction,
                    out var hit,
                    castDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore) ||
                hit.collider == null)
            {
                return false;
            }

            // A point-blank person is a valid target, not cover. A self-hit can occur
            // during an animation transition and likewise must not claim that the
            // parapet is obstructing the muzzle.
            var hitSoldier = hit.collider.GetComponentInParent<Soldier>();
            if (hitSoldier != null)
                return false;
            if (hit.collider.GetComponentInParent<Vehicle>() != null)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ClaimCoverClearancePose(Soldier soldier, ContactResponseState state)
    {
        try
        {
            var cover = soldier.targetDestination;
            if (cover == null || cover.Pointer == IntPtr.Zero)
                return;

            var newlyClaimed = !state.CoverClearancePoseOwned ||
                               state.CoverClearanceCoverId != cover.Pointer;
            state.CoverClearancePoseOwned = true;
            state.CoverClearanceCoverId = cover.Pointer;
            if (newlyClaimed)
            {
                AiState.Trace(
                    $"Cover firing clearance: soldier {soldier.GetInstanceID()} stood to clear a near muzzle obstruction");
            }
        }
        catch
        {
            ClearCoverClearancePose(state);
        }
    }

    private static bool OwnsCurrentCoverClearancePose(
        Soldier soldier,
        ContactResponseState state)
    {
        if (!Settings.ContactResponseEnabled.Value || !state.CoverClearancePoseOwned ||
            state.CoverClearanceCoverId == IntPtr.Zero || !IsOnUsableCover(soldier))
        {
            return false;
        }

        try
        {
            var cover = soldier.targetDestination;
            return cover != null && cover.Pointer == state.CoverClearanceCoverId;
        }
        catch
        {
            return false;
        }
    }

    private static void ClearCoverClearancePose(ContactResponseState state)
    {
        state.CoverClearancePoseOwned = false;
        state.CoverClearanceCoverId = IntPtr.Zero;
    }

    private static void ApplyContactMovementPose(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        float now)
    {
        state.ContactCrouchOwned = true;
        EnsureTacticalPose(ai, soldier, SoldierPose.Crouch);
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

    internal static void RestoreFireAfterOwnedInhibition(SoldierAI ai, Soldier soldier)
    {
        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);
        if (!state.FireRestorePending)
            return;

        if (state.FireInhibitedByMovement || state.FireInhibitedByRange ||
            state.FireInhibitedByArmoredTarget ||
            state.ExposedReloadProneOwned ||
            state.SuppressionFireInhibited ||
            MountedGunnerSuppression.IsFireInhibited(id) ||
            (Settings.DangerReactionsEnabled.Value &&
             (soldier.IsOnFire || AiState.IsFlameEvading(id, Time.time))))
        {
            return;
        }

        if (Settings.ContactResponseEnabled.Value && state.ContactResponseActive && state.Relocating)
        {
            return;
        }

        // This is a permission flag, not a request to shoot. Releasing it without
        // a current target is necessary so a later native acquisition is not
        // blocked by a stale false value owned by this mod.
        ai.allowFireAtEnemy = true;
        state.FireRestorePending = false;
    }

    private static void GrantFirePermissionIfReady(SoldierAI ai, Soldier soldier)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        if (state.ExposedReloadProneOwned || soldier.IsReloading)
            return;

        ai.allowFireAtEnemy = true;
    }

    private static void UpdatePinnedState(ContactResponseState state, int suppression, float now)
    {
        if (!Settings.DangerReactionsEnabled.Value)
        {
            state.Pinned = false;
            state.PinnedUntil = 0f;
            state.PinnedFireBlockedUntil = 0f;
            return;
        }

        if (suppression >= Settings.ProneSuppression.Value)
        {
            if (!state.Pinned)
                state.PinnedFireBlockedUntil = now + PinnedShockSeconds;
            state.Pinned = true;
            state.PinnedUntil = Mathf.Max(state.PinnedUntil, now + Settings.PinnedMinimumSeconds.Value);
            return;
        }

        if (state.Pinned && now >= state.PinnedUntil && suppression <= Settings.ProneReleaseSuppression.Value)
        {
            state.Pinned = false;
            state.PinnedFireBlockedUntil = 0f;
        }
    }

    internal static void SetTacticalPose(SoldierAI ai, Soldier soldier, SoldierPose pose)
    {
        if (pose == SoldierPose.Crouch)
        {
            var state = AiState.GetContactState(soldier.GetInstanceID());
            state.TacticalCrouchUntil = Mathf.Max(
                state.TacticalCrouchUntil,
                Time.time + TacticalCrouchPersistenceSeconds);
        }

        EnsureTacticalPose(ai, soldier, pose);
    }

    internal static void EnsureTacticalPose(SoldierAI ai, Soldier soldier, SoldierPose pose)
    {
        var soldierNeedsPose = true;
        try
        {
            soldierNeedsPose = soldier.Pose != pose;
        }
        catch
        {
            // If the native pose getter is unavailable during teardown, retain the
            // previous safe behavior and let SetPose perform the validation.
        }

        ai.pose = pose;
        ai.actualPose = pose;
        if (soldierNeedsPose)
            soldier.SetPose(pose);
    }

    internal static bool ShouldOwnCrouch(int soldierId, float now)
    {
        if (!AiState.ContactStates.TryGetValue(soldierId, out var state))
            return false;
        return ShouldOwnCrouch(state, now);
    }

    internal static bool ShouldOwnCoverPosture(int soldierId, float now)
    {
        if (!Settings.ContactResponseEnabled.Value ||
            !AiState.ContactStates.TryGetValue(soldierId, out var state) ||
            !state.HasThreatPosition)
        {
            return false;
        }

        return state.DefensiveCoverHold ||
               state.ContactCrouchOwned && now < state.ContactUntil;
    }

    private static bool ShouldOwnCrouch(ContactResponseState state, float now)
    {
        if (Settings.DangerReactionsEnabled.Value && state.SuppressionPoseOwned && !state.Pinned)
            return true;
        if (Settings.ContactResponseEnabled.Value && state.DefensiveCoverHold)
            return true;
        if (Settings.ContactResponseEnabled.Value && state.ContactCrouchOwned && now < state.ContactUntil)
            return true;
        return now < state.TacticalCrouchUntil;
    }

    internal static void MaintainOwnedPose(SoldierAI ai, Soldier soldier, float now)
    {
        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);
        if (ExposedReloadPosture.TryMaintain(ai, soldier, now, Time.deltaTime))
            return;

        if (IsPinned(id) && !AiState.IsFlameEvading(id, now))
        {
            EnsureTacticalPose(ai, soldier, SuppressionPose(soldier));
            return;
        }

        if (AiState.IsHidingFromTank(id, now))
        {
            EnsureTacticalPose(
                ai,
                soldier,
                IsOnUsableCover(soldier) ? StationaryHoldPose(soldier) : SoldierPose.Prone);
            return;
        }

        if (state.ContactCrouchOwned && now >= state.ContactUntil)
            state.ContactCrouchOwned = false;
        if (OwnsCurrentCoverClearancePose(soldier, state))
        {
            EnsureTacticalPose(ai, soldier, SoldierPose.Idle);
            return;
        }
        var coverPostureOwned = state.HasThreatPosition &&
                                (state.DefensiveCoverHold ||
                                 (state.ContactCrouchOwned && now < state.ContactUntil));
        if (coverPostureOwned &&
            TryGetCurrentCoverEvaluation(
                soldier,
                state,
                state.LastThreatPosition,
                now,
                out var coverEvaluation))
        {
            if (coverEvaluation.Pose == SoldierPose.Idle)
                ClaimCoverClearancePose(soldier, state);
            else
                ClearCoverClearancePose(state);
            EnsureTacticalPose(ai, soldier, coverEvaluation.Pose);
            return;
        }
        if (ShouldOwnCrouch(state, now))
            EnsureTacticalPose(ai, soldier, SoldierPose.Crouch);
    }

    internal static void StopTacticalMovement(
        SoldierAI ai,
        Soldier soldier,
        SoldierPose pose,
        float deltaTime)
    {
        AiState.GetContactState(soldier.GetInstanceID()).MovementInhibitedByContactResponse = true;
        ai.moveCharacter = false;
        EnsureTacticalPose(ai, soldier, pose);
        soldier.isSprinting = false;
        soldier.StopMove(pose, deltaTime);
    }

    internal static void StopDangerMovement(
        SoldierAI ai,
        Soldier soldier,
        SoldierPose pose,
        float deltaTime)
    {
        ai.moveCharacter = false;
        EnsureTacticalPose(ai, soldier, pose);
        soldier.isSprinting = false;
        soldier.StopMove(pose, deltaTime);
    }

    // Low-level soldier command executor. Tactical feature modules request these
    // mutations through GroundAiDirector so movement, pose, aim, and fire policy
    // cannot independently fight over the same native state.
    internal static void ExecuteFireInhibition(
        SoldierAI? ai,
        Soldier soldier,
        bool clearWeaponRange)
    {
        if (ai != null)
        {
            ai.allowFireAtEnemy = false;
            ai.aimingEnemy = false;
            if (clearWeaponRange)
                ai.targetInWeaponRange = false;
        }

        soldier.StopFire();
    }

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
        SetTacticalPose(ai, soldier, SoldierPose.Crouch);
        ai.moveCharacter = true;
    }

    private static void FaceThreatWhenStationary(SoldierAI ai, Soldier soldier, Vector3 targetPosition)
    {
        var direction = targetPosition - soldier.transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f)
        {
            direction.Normalize();
            AiState.GetContactState(soldier.GetInstanceID()).StationaryThreatFacingOwned = true;
            ai.moveLookingTarget = true;
            ai.fireDir = direction;
            soldier.RotateToward(direction, Time.deltaTime);
        }
    }

    internal static void ReleaseStationaryThreatFacingForMovement(SoldierAI ai, Soldier soldier)
        => ReleaseStationaryThreatFacing(
            ai,
            AiState.GetContactState(soldier.GetInstanceID()));

    private static void ReleaseStationaryThreatFacing(
        SoldierAI ai,
        ContactResponseState state)
    {
        if (!state.StationaryThreatFacingOwned)
            return;

        state.StationaryThreatFacingOwned = false;
        ai.moveLookingTarget = false;
    }
}

[HarmonyPatch(typeof(SoldierAI), nameof(SoldierAI.MoveOptimized))]
internal static class SoldierTacticalSprintPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SoldierAI __instance, ref bool sprint, float deltaTime)
    {
        if (!MultiplayerAuthority.CanMutateGameplay())
            return true;

        var soldier = __instance.GetSoldier();
        if (!AiOwnership.IsAutonomous(soldier) || soldier.IsOnVehicle())
            return true;

        var id = soldier.GetInstanceID();
        var now = Time.time;
        ContactResponse.UpdateSuppressionReaction(__instance, soldier, now, deltaTime);
        if (Settings.DangerReactionsEnabled.Value && soldier.IsOnFire)
        {
            sprint = false;
            soldier.StopFire();
            ContactResponse.StopDangerMovement(__instance, soldier, SoldierPose.Prone, deltaTime);
            return false;
        }

        if (ExposedReloadPosture.TryMaintain(__instance, soldier, now, deltaTime))
        {
            sprint = false;
            return false;
        }

        var flameEvading = Settings.DangerReactionsEnabled.Value &&
                           AiState.IsFlameEvading(id, now);
        if (ContactResponse.IsPinned(id) && !flameEvading)
        {
            sprint = false;
            var remainsStationary = ContactResponse.ApplyPinnedSuppression(
                __instance,
                soldier,
                AiState.GetContactState(id),
                now,
                deltaTime);
            if (remainsStationary)
                return false;
        }

        if (!flameEvading &&
            ContactResponse.TryHoldMovementStall(__instance, soldier, now, deltaTime))
        {
            sprint = false;
            return false;
        }

        if (!flameEvading &&
            ContactResponse.TryHoldTankCover(__instance, soldier, now, deltaTime))
        {
            sprint = false;
            return false;
        }

        var actualCharge = ContactResponse.IsActualCharge(soldier);
        var hazardEvading = flameEvading;
        var activeThreatMovement = (ContactResponse.HasActiveContact(id, now) ||
                                    IncomingFireAwareness.HasActiveCue(id, now)) &&
                                   !hazardEvading;
        if (ContactResponse.ShouldHoldDefensivePosition(soldier, now) && !hazardEvading)
        {
            sprint = false;
            ContactResponse.StopTacticalMovement(
                __instance,
                soldier,
                ContactResponse.StationaryHoldPose(soldier),
                deltaTime);
            return false;
        }

        if (ContactResponse.ShouldHoldEngagement(id, now) && !hazardEvading)
        {
            sprint = false;
            ContactResponse.StopTacticalMovement(
                __instance,
                soldier,
                ContactResponse.StationaryHoldPose(soldier),
                deltaTime);
            return false;
        }

        if (ContactResponse.ShouldHoldCover(id, now) && soldier.IsOnCover() &&
            !actualCharge && !hazardEvading)
        {
            sprint = false;
            ContactResponse.StopTacticalMovement(
                __instance,
                soldier,
                ContactResponse.StationaryHoldPose(soldier),
                deltaTime);
            return false;
        }

        var suppression = soldier.GetSuppressionValue();
        ApplyTacticalMovementPose(__instance, soldier, now, suppression, activeThreatMovement);

        UpdateMovingFireInhibition(__instance, soldier);
        ContactResponse.ReleaseStationaryThreatFacingForMovement(__instance, soldier);
        KnownTargetSuppressiveFire.InterruptForMovement(__instance, soldier);

        return true;
    }

    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance)
    {
        if (!MultiplayerAuthority.CanMutateGameplay())
            return;

        var soldier = __instance.GetSoldier();
        if (!AiOwnership.IsAutonomous(soldier) || soldier.IsOnVehicle())
            return;

        ContactResponse.MaintainOwnedPose(__instance, soldier, Time.time);
        UpdateMovingFireInhibition(__instance, soldier);
    }

    internal static void ApplyTacticalMovementPose(
        SoldierAI ai,
        Soldier soldier,
        float now,
        int suppression,
        bool activeThreatMovement)
    {
        var id = soldier.GetInstanceID();
        if (ContactResponse.IsPinned(id) && !AiState.IsFlameEvading(id, now))
        {
            ContactResponse.EnsureTacticalPose(ai, soldier, ContactResponse.SuppressionPose(soldier));
        }
        else
        {
            var state = AiState.GetContactState(id);
            if (activeThreatMovement)
            {
                if (ContactResponse.HasActiveContact(id, now))
                    state.ContactCrouchOwned = true;
                else
                    state.TacticalCrouchUntil = now + ContactResponse.TacticalCrouchPersistenceSeconds;
            }

            if ((Settings.DangerReactionsEnabled.Value &&
                 suppression >= Settings.CrouchSuppression.Value) ||
                activeThreatMovement || ContactResponse.ShouldOwnCrouch(id, now))
            {
                ContactResponse.EnsureTacticalPose(ai, soldier, SoldierPose.Crouch);
            }
        }
    }

    internal static void UpdateMovingFireInhibition(SoldierAI ai, Soldier soldier)
    {
        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);

        // A contact-owned hard stop is already cancelling locomotion this frame.
        // Residual velocity must not immediately relatch the rifle fire gate after
        // the close-engagement response granted permission to shoot.
        if (state.MovementInhibitedByContactResponse && !state.SuppressionMovementOwned)
        {
            if (state.FireInhibitedByMovement)
            {
                state.FireInhibitedByMovement = false;
                ContactResponse.RestoreFireAfterOwnedInhibition(ai, soldier);
            }
            return;
        }

        // Player-led squad members keep moveCharacter set while following their
        // leader, even when they have halted to engage. Use actual locomotion so
        // that follow intent cannot permanently suppress an otherwise valid shot.
        if (!soldier.IsMoving() || HandheldWeaponClassifier.AllowsMovingFire(soldier, ai))
        {
            if (state.FireInhibitedByMovement)
            {
                state.FireInhibitedByMovement = false;
                ContactResponse.RestoreFireAfterOwnedInhibition(ai, soldier);
            }
            return;
        }

        state.FireInhibitedByMovement = true;
        state.FireRestorePending = true;
        ai.allowFireAtEnemy = false;
        ai.aimingEnemy = false;
        soldier.StopFire();
    }
}

[HarmonyPatch(typeof(SoldierAI), nameof(SoldierAI.MoveFPSOptimized))]
internal static class SoldierTacticalFpsMovePatch
{
    [HarmonyPrefix]
    private static bool Prefix(SoldierAI __instance, float deltaTime)
    {
        if (!MultiplayerAuthority.CanMutateGameplay())
            return true;

        var soldier = __instance.GetSoldier();
        if (!AiOwnership.IsAutonomous(soldier) || soldier.IsOnVehicle())
            return true;

        var id = soldier.GetInstanceID();
        var now = Time.time;
        ContactResponse.UpdateSuppressionReaction(__instance, soldier, now, deltaTime);

        if (Settings.DangerReactionsEnabled.Value && soldier.IsOnFire)
        {
            soldier.StopFire();
            ContactResponse.StopDangerMovement(
                __instance,
                soldier,
                SoldierPose.Prone,
                deltaTime);
            return false;
        }

        // The low-node-count movement path must honor the same committed reload
        // ownership as MoveOptimized; otherwise it can raise the soldier back to
        // a fighting crouch between reload frames.
        if (ExposedReloadPosture.TryMaintain(__instance, soldier, now, deltaTime))
            return false;

        var flameEvading = Settings.DangerReactionsEnabled.Value &&
                           AiState.IsFlameEvading(id, now);
        if (ContactResponse.IsPinned(id) && !flameEvading)
        {
            var remainsStationary = ContactResponse.ApplyPinnedSuppression(
                __instance,
                soldier,
                AiState.GetContactState(id),
                now,
                deltaTime);
            if (remainsStationary)
                return false;
        }

        if (!flameEvading &&
            ContactResponse.TryHoldMovementStall(__instance, soldier, now, deltaTime))
            return false;

        if (!flameEvading &&
            ContactResponse.TryHoldTankCover(__instance, soldier, now, deltaTime))
            return false;

        var actualCharge = ContactResponse.IsActualCharge(soldier);
        var hazardEvading = flameEvading;
        var activeThreatMovement = (ContactResponse.HasActiveContact(id, now) ||
                                    IncomingFireAwareness.HasActiveCue(id, now)) &&
                                   !hazardEvading;

        if (ContactResponse.ShouldHoldDefensivePosition(soldier, now) && !hazardEvading)
        {
            ContactResponse.StopTacticalMovement(
                __instance,
                soldier,
                ContactResponse.StationaryHoldPose(soldier),
                deltaTime);
            return false;
        }

        if (ContactResponse.ShouldHoldEngagement(id, now) && !hazardEvading)
        {
            ContactResponse.StopTacticalMovement(
                __instance,
                soldier,
                ContactResponse.StationaryHoldPose(soldier),
                deltaTime);
            return false;
        }

        if (ContactResponse.ShouldHoldCover(id, now) && soldier.IsOnCover() &&
            !actualCharge && !hazardEvading)
        {
            ContactResponse.StopTacticalMovement(
                __instance,
                soldier,
                ContactResponse.StationaryHoldPose(soldier),
                deltaTime);
            return false;
        }

        SoldierTacticalSprintPatch.ApplyTacticalMovementPose(
            __instance,
            soldier,
            now,
            soldier.GetSuppressionValue(),
            activeThreatMovement);
        ContactResponse.ReleaseStationaryThreatFacingForMovement(__instance, soldier);
        KnownTargetSuppressiveFire.InterruptForMovement(__instance, soldier);
        return true;
    }

    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance)
    {
        if (!MultiplayerAuthority.CanMutateGameplay())
            return;

        var soldier = __instance.GetSoldier();
        if (!AiOwnership.IsAutonomous(soldier) || soldier.IsOnVehicle())
            return;

        SoldierTacticalSprintPatch.UpdateMovingFireInhibition(__instance, soldier);
        ContactResponse.MaintainOwnedPose(__instance, soldier, Time.time);
    }
}

[HarmonyPatch(typeof(SoldierAI), "GetFavouriteFightingPose")]
internal static class SoldierPinnedPosePatch
{
    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance, ref SoldierPose __result)
    {
        if (!MultiplayerAuthority.CanMutateGameplay())
            return;

        var soldier = __instance.GetSoldier();
        if (soldier == null)
            return;

        var id = soldier.GetInstanceID();
        var now = Time.time;
        if (ContactResponse.IsPinned(id) && !AiState.IsFlameEvading(id, now))
        {
            __result = ContactResponse.SuppressionPose(soldier);
            return;
        }

        var stationaryHoldPose = ContactResponse.StationaryHoldPose(soldier);
        if (ContactResponse.ShouldOwnCoverPosture(id, now))
        {
            // The cover geometry owner must win as one unit. Previously only its
            // standing result was respected here, while its prone result fell
            // through to the generic crouch owner, producing a prone/crouch loop.
            __result = stationaryHoldPose;
            return;
        }

        if (stationaryHoldPose == SoldierPose.Idle)
        {
            __result = SoldierPose.Idle;
            return;
        }

        if (ContactResponse.ShouldOwnCrouch(id, now) ||
            (Settings.DangerReactionsEnabled.Value &&
             soldier.GetSuppressionValue() >= Settings.CrouchSuppression.Value))
            __result = SoldierPose.Crouch;
    }
}

[HarmonyPatch(typeof(SoldierAI), "OnDestroy")]
internal static class SoldierAiDestroyPatch
{
    [HarmonyPrefix]
    private static void Prefix(SoldierAI __instance)
    {
        try
        {
            var soldier = __instance.GetSoldier();
            if (soldier != null)
                AiState.RemoveSoldier(soldier);
        }
        catch
        {
            // Cleanup must never interfere with the game's destruction path.
        }
    }
}
