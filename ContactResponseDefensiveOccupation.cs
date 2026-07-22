using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static partial class ContactResponse
{
    private const float DefensiveAreaToleranceMeters = 10f;

    private const float PlayerHoldAreaToleranceMeters = 3f;

    private const float DefensiveSearchMarginMeters = 12f;

    private static bool TryEstablishInitialDefensivePosition(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        Spottable? target,
        Vector3 targetPosition,
        int soldierId,
        float now)
    {
        var positionOwned = ShouldControlDefensivePosition(soldier);
        if (!positionOwned)
            return false;

        UpdateDefensiveCoverHold(soldier, state, soldierId, now);
        var hasStableAnchor = state.DefensiveCoverHold || state.HasDefensiveCoverAnchor;
        if (hasStableAnchor)
        {
            SetCoverState(state, InfantryCoverState.Holding, soldierId,
                "holding protected defensive fighting position");
            state.EngagementHoldUntil = float.PositiveInfinity;
            state.ContactCrouchOwned = true;
            var pose = target != null
                ? GetStationaryEngagementPose(soldier, state, targetPosition)
                : StationaryHoldPose(soldier);
            StopTacticalMovement(ai, soldier, pose, Time.deltaTime);
            if (target != null)
            {
                FaceThreatWhenStationary(ai, soldier, targetPosition);
                GrantFirePermissionIfReady(ai, soldier);
            }
            return true;
        }

        if (state.Relocating)
            return true;

        var decisionDue = now >= state.NextDecisionAt &&
                          now >= state.NextRelocationAllowedAt;
        if (InfantryCoverDecisionCore.ShouldSeekInitialDefensiveCover(
                positionOwned,
                hasStableAnchor,
                state.Relocating,
                decisionDue,
                target != null) &&
            TryGetDefensiveArea(soldier, out var center, out var radius))
        {
            state.NextDecisionAt = now + InfantryCoverPolicy.DecisionIntervalSeconds;
            var threatPosition = target != null
                ? targetPosition
                : GetDefensiveApproachPoint(soldier, state, center, radius, now);
            var cover = FindCover(
                soldier,
                threatPosition,
                Mathf.Max(55f, radius + DefensiveSearchMarginMeters),
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
            }
            else if (cover != null &&
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
        SetCoverState(state, InfantryCoverState.WaitingForSafeMove, soldierId,
            "holding arrival point while no defensive cover slot is available");
        state.EngagementHoldUntil = float.PositiveInfinity;
        state.ContactCrouchOwned = true;
        StopTacticalMovement(ai, soldier, SoldierPose.Prone, Time.deltaTime);
        if (target != null)
        {
            FaceThreatWhenStationary(ai, soldier, targetPosition);
            GrantFirePermissionIfReady(ai, soldier);
        }
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

    private static bool ShouldControlTacticalPosition(Soldier soldier)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        return RefreshDefensivePositionOwnership(soldier, state) ||
               HasActivePlayerHoldPositionControl(soldier, state);
    }

    private static bool RefreshDefensivePositionOwnership(
        Soldier soldier,
        ContactResponseState state)
    {
        try
        {
            var squad = soldier.joinedSquad;
            var squadId = squad == null ? 0 : ContactKnowledge.GetSquadId(squad);
            var revision = GroundAiDirector.CurrentObjectiveRevision(soldier.faction);
            var hasStationaryArea = squad != null &&
                                    GroundAiDirector.TryGetCommanderDefensiveArea(
                                        squad, out _, out _);
            var eligible = squad != null &&
                           AiOwnership.IsAutonomous(soldier) &&
                           soldier.IsAlive &&
                           !soldier.IsOnVehicle() &&
                           GroundAiDirector.OwnsSquad(squad) &&
                           hasStationaryArea &&
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

    internal static bool ShouldHoldDefensivePosition(Soldier soldier, float now)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        return (RefreshDefensivePositionOwnership(soldier, state) ||
                HasActivePlayerHoldPositionControl(soldier, state)) &&
               !state.Relocating &&
               !soldier.IsOnFire &&
               !AiState.IsFlameEvading(soldier.GetInstanceID(), now);
    }

    internal static void ResetDefensivePositionOwnership(int soldierId)
    {
        if (AiState.ContactStates.TryGetValue(soldierId, out var state))
        {
            ReleaseDefensivePositionOwnership(state, soldierId);
            ReleasePlayerHoldPositionOwnership(state, soldierId);
        }
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
        catch (NullReferenceException)
        {
            center = default;
            radius = 0f;
            return false;
        }
        catch (Il2CppException)
        {
            center = default;
            radius = 0f;
            return false;
        }
        catch (ObjectCollectedException)
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
        if (!AiOwnership.IsAutonomous(soldier) ||
            !TryGetPlayerHoldOrder(soldier, out var center, out var radius))
        {
            if (state.PlayerHoldPositionOwned)
                ReleasePlayerHoldPositionOwnership(state, soldierId);
            return false;
        }

        var orderChanged = PlayerHoldPositionCore.OrderChanged(
            state.PlayerHoldPositionOwned,
            new MapPoint(state.PlayerHoldCenter.x, state.PlayerHoldCenter.z),
            state.PlayerHoldRadius,
            new MapPoint(center.x, center.z),
            radius);
        var insideOrderedArea = IsInsidePlayerHoldOrder(
                                    soldier.transform.position, center, radius) ||
                                !orderChanged && state.HasDefensiveCoverAnchor &&
                                IsInsidePlayerHoldOrder(
                                    state.DefensiveCoverAnchorPosition, center, radius) &&
                                HorizontalDistanceSqr(
                                    soldier.transform.position,
                                    state.DefensiveCoverAnchorPosition) <=
                                InfantryCoverPolicy.DefensiveAnchorLeashMeters *
                                InfantryCoverPolicy.DefensiveAnchorLeashMeters;
        var wasControllingMovement = state.Relocating ||
                                     state.MovementInhibitedByContactResponse ||
                                     state.EngagementHoldUntil > 0f ||
                                     state.HoldCoverUntil > 0f;
        if (!insideOrderedArea && !orderChanged && state.Relocating)
        {
            // The selected slot is inside the ordered area, but navigation around
            // walls may briefly leave its edge. Keep the one reserved transit alive.
            return false;
        }
        if (!insideOrderedArea)
        {
            // Do not let an old armor/contact hold stop the native squad route. The
            // game's formation logic remains responsible for each member's exact
            // destination within the player's ordered area.
            if (state.Relocating)
                FinishRelocation(ai, soldier, state, soldierId, now,
                    keepOccupiedCover: false, completedMove: false,
                    markFailedCover: false);
            ReleasePlayerHoldPositionOwnership(state, soldierId);
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

        if (orderChanged)
        {
            if (state.Relocating)
                FinishRelocation(ai, soldier, state, soldierId, now,
                    keepOccupiedCover: false, completedMove: false,
                    markFailedCover: false);
            if (state.PlayerHoldPositionOwned)
                ReleasePlayerHoldPositionOwnership(state, soldierId);
            state.PlayerHoldPositionOwned = true;
            state.PlayerHoldCenter = center;
            state.PlayerHoldRadius = radius;
            state.NextDecisionAt = 0f;
            state.NextRelocationAllowedAt = 0f;
        }

        UpdateDefensiveCoverHold(soldier, state, soldierId, now);
        if (state.DefensiveCoverHold || state.HasDefensiveCoverAnchor)
        {
            SetCoverState(state, InfantryCoverState.Holding, soldierId,
                "holding protected position inside player hold area");
            state.EngagementHoldUntil = float.PositiveInfinity;
            state.ContactCrouchOwned = true;
            var anchorPose = target != null
                ? GetStationaryEngagementPose(soldier, state, targetPosition)
                : StationaryHoldPose(soldier);
            StopTacticalMovement(ai, soldier, anchorPose, Time.deltaTime);
            if (target != null)
            {
                FaceThreatWhenStationary(ai, soldier, targetPosition);
                GrantFirePermissionIfReady(ai, soldier);
            }
            return true;
        }

        // The normal relocation executor below owns transit and arrival latching.
        // Returning false here lets it finish the already-selected cover move.
        if (state.Relocating)
            return false;

        if (PlayerHoldPositionCore.ShouldSeekCover(
                insideOrderedArea,
                state.Relocating,
                state.DefensiveCoverHold || state.HasDefensiveCoverAnchor,
                now >= state.NextDecisionAt && now >= state.NextRelocationAllowedAt))
        {
            state.NextDecisionAt = now + InfantryCoverPolicy.DecisionIntervalSeconds;
            var threatPosition = target != null
                ? targetPosition
                : GetDefensiveApproachPoint(soldier, state, center, radius, now);
            var cover = FindCover(
                soldier,
                threatPosition,
                Mathf.Max(55f, radius + DefensiveSearchMarginMeters),
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
            }
            else if (cover != null &&
                     BeginRelocation(ai, soldier, state, cover, soldierId, now))
            {
                SetCoverState(state, InfantryCoverState.Moving, soldierId,
                    "taking reserved cover inside player hold area");
                return true;
            }
        }

        // Open ground is only a temporary fallback. Stay quiet and retry on the
        // bounded decision cadence instead of accepting exposure or wandering.
        SetCoverState(state, InfantryCoverState.WaitingForSafeMove, soldierId,
            "holding locally while waiting for protected player-hold cover");
        state.EngagementHoldUntil = float.PositiveInfinity;
        state.ContactCrouchOwned = true;
        var pose = target != null && IsOnUsableCover(soldier)
            ? GetStationaryEngagementPose(soldier, state, targetPosition)
            : SoldierPose.Prone;
        StopTacticalMovement(ai, soldier, pose, Time.deltaTime);
        if (target != null)
        {
            FaceThreatWhenStationary(ai, soldier, targetPosition);
            GrantFirePermissionIfReady(ai, soldier);
        }
        return true;
    }

    private static bool HasActivePlayerHoldPositionControl(
        Soldier soldier,
        ContactResponseState state)
    {
        if (!state.PlayerHoldPositionOwned || !AiOwnership.IsAutonomous(soldier) ||
            !TryGetPlayerHoldOrder(soldier, out var center, out var radius))
        {
            return false;
        }

        return !PlayerHoldPositionCore.OrderChanged(
            true,
            new MapPoint(state.PlayerHoldCenter.x, state.PlayerHoldCenter.z),
            state.PlayerHoldRadius,
            new MapPoint(center.x, center.z),
            radius);
    }

    private static bool OwnsTacticalDefensivePosition(ContactResponseState state)
        => state.DefensivePositionOwned || state.PlayerHoldPositionOwned;

    private static void ReleasePlayerHoldPositionOwnership(
        ContactResponseState state,
        int soldierId)
    {
        state.PlayerHoldPositionOwned = false;
        state.PlayerHoldCenter = default;
        state.PlayerHoldRadius = 0f;
        if (!state.DefensivePositionOwned)
            ReleaseDefensiveCoverHold(state, soldierId);
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
        catch (NullReferenceException)
        {
            return false;
        }
        catch (Il2CppException)
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

            // A defender anchored against a predicted approach axis ends up on the
            // wrong side of cover when the real attack arrives from a sustained
            // different direction. When a currently-engaged live enemy is measured to
            // defeat the anchored cover (against the anti-flicker stabilized axis, so
            // only a durable rotation flips it), release the anchor for one relocation.
            var anchorDefeatedByRealThreat = false;
            if (state.HasThreatPosition && now < state.ContactUntil)
            {
                // Anchor release is a relocation decision, not a pose choice: run the
                // first evaluation even if the per-frame geometry budget is full so a
                // full budget can never delay or flip an anchor-defeat verdict. This
                // path is on the staggered director update, so it does not cluster.
                var evaluationSucceeded = TryGetCurrentCoverEvaluation(
                    soldier, state, state.LastThreatPosition, now, out var anchorEvaluation,
                    mayDeferFirstEval: false);
                anchorDefeatedByRealThreat =
                    DefensiveAnchorReevaluationCore.ShouldReleaseForRealThreat(
                        hasThreatMemory: true,
                        engagedRecently: true,
                        evaluationSucceeded,
                        evaluationSucceeded && anchorEvaluation.IsProtective);
            }

            if (!anchorDefeatedByRealThreat &&
                InfantryCoverDecisionCore.ShouldKeepDefensiveCoverAnchor(
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

        if (!defendOrderActive || state.Relocating || !IsOnUsableCover(soldier) ||
            !IsInsideDefensiveArea(soldier, state.ReservedCoverPosition))
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

    private static bool TryCaptureReservedDefensiveCoverAnchor(
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now)
    {
        if (!OwnsTacticalDefensivePosition(state) ||
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
        catch (NullReferenceException)
        {
            // A transient wrapper lookup must not discard a previously vetted
            // building or trench slot at the instant the soldier reaches it.
        }
        catch (Il2CppException)
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

        var withinAuthoredAnchor = state.ManeuverCoverAnchorId != IntPtr.Zero &&
                                   HorizontalDistanceSqr(
                                       soldier.transform.position,
                                       state.ManeuverCoverAnchorPosition) <=
                                   InfantryCoverPolicy.DefensiveAnchorLeashMeters *
                                   InfantryCoverPolicy.DefensiveAnchorLeashMeters;
        if (!IsOnUsableCover(soldier) && !withinAuthoredAnchor)
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
                // Attackers still wait for covering fire before the next bound —
                // that gate is enforced by ShouldAuthorizeAttackBound after this
                // hold expires, not by an unreachable +inf timer (plan 012).
                state.HoldCoverUntil = state.ManeuverCoverMinimumHoldUntil;
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
        catch (NullReferenceException)
        {
            // A transient native-wrapper failure is not enough evidence to abandon
            // a fortification. The next successful update can still invalidate it.
            return false;
        }
        catch (Il2CppException)
        {
            // A transient native-wrapper failure is not enough evidence to abandon
            // a fortification. The next successful update can still invalidate it.
            return false;
        }
    }

    private static bool IsInsideDefensiveArea(Soldier soldier)
        => IsInsideDefensiveArea(soldier, soldier.transform.position);

    private static bool IsInsideDefensiveArea(Soldier soldier, Vector3 position)
    {
        if (!TryGetDefensiveArea(soldier, out var center, out var radius))
            return false;

        return DefensivePositioningCore.IsInsideArea(
            new MapPoint(position.x, position.z),
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
}
