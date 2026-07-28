using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class CoverOccupancy
{
    // Cover props and terrain colliders share this all-layer non-alloc query with
    // soldiers. A small buffer can fill with geometry before the occupying soldier
    // is returned, making a genuinely occupied slot appear empty.
    private const int NearbyColliderCapacity = 96;

    [ThreadStatic]
    private static Collider[]? _nearbyColliders;

    internal static bool IsOccupiedByOther(Vector3 coverPosition, Soldier soldier)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            var radius = InfantryCoverPolicy.OccupancyRadiusMeters;
            var nearby = _nearbyColliders ??= new Collider[NearbyColliderCapacity];
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
        finally
        {
            ModTimeProbe.EndSub(ModSubSite.Occupancy, __t);
        }
    }

    /// <summary>
    /// Nearest already-halted friendly within <paramref name="radius"/> (plan 018 item 3).
    /// Only an ALREADY-HALTED neighbour makes a doorway stack - two men walking the same
    /// path are not crowding anything yet - so the movement arbiter's own MovementHalted
    /// output is the filter. Same bounded overlap query as the occupancy check above, and
    /// it runs only on the rising edge of a fighting halt behind a long cooldown.
    /// </summary>
    internal static bool TryFindHaltedNeighbour(
        Vector3 position,
        Soldier soldier,
        float radius,
        out Vector3 neighbourPosition)
    {
        neighbourPosition = default;
        var __t = ModTimeProbe.Begin();
        try
        {
            var nearby = _nearbyColliders ??= new Collider[NearbyColliderCapacity];
            var count = Physics.OverlapSphereNonAlloc(
                position,
                radius,
                nearby,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            var nearestSqr = radius * radius;
            var found = false;
            for (var index = 0; index < count; index++)
            {
                var collider = nearby[index];
                if (collider == null)
                    continue;

                var other = collider.GetComponentInParent<Soldier>();
                if (other == null || other.GetInstanceID() == soldier.GetInstanceID() ||
                    !other.IsAlive || other.IsOnVehicle() ||
                    !other.gameObject.activeInHierarchy ||
                    !string.Equals(other.faction, soldier.faction, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!AiState.ContactStates.TryGetValue(
                        other.GetInstanceID(), out var otherState) ||
                    !otherState.MovementHalted)
                {
                    continue;
                }

                var otherPosition = other.transform.position;
                var distanceSqr = (otherPosition - position).sqrMagnitude;
                if (distanceSqr > nearestSqr)
                    continue;

                nearestSqr = distanceSqr;
                neighbourPosition = otherPosition;
                found = true;
            }

            return found;
        }
        finally
        {
            ModTimeProbe.EndSub(ModSubSite.Occupancy, __t);
        }
    }
}

internal static partial class ContactResponse
{
    private readonly record struct CoarseCoverCandidate(
        AiDestination Destination,
        Vector3 Position,
        float DistanceSqr);

    private static int _coverSearchFrame = -1;

    // Stutter-probe marker: the frame on which the last detailed cover search ran.
    internal static int LastCoverSearchFrame => _coverSearchFrame;

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

    // Global per-frame budget for full posture re-evaluations (EvaluateCoverGeometry
    // inside TryGetCurrentCoverEvaluation), matching the TryBeginDetailedCoverSearch
    // gate pattern. Bounds the per-frame cost of a mass-contact cache expiry wave; a
    // soldier over budget reuses its stale cached evaluation and retries next frame.
    private const int PostureEvaluationBudgetPerFrame = 3;

    private static int _postureEvalFrame = -1;

    private static int _postureEvalsThisFrame;

    // Stutter-probe markers: the frame on which posture evaluations last ran, and how
    // many ran on it. Diagnostic-only.
    internal static int LastPostureEvalFrame => _postureEvalFrame;

    internal static int LastPostureEvalCount => _postureEvalsThisFrame;

    private static bool TryBeginPostureEvaluation()
    {
        var frame = Time.frameCount;
        if (frame != _postureEvalFrame)
        {
            _postureEvalFrame = frame;
            _postureEvalsThisFrame = 0;
        }

        if (_postureEvalsThisFrame >= PostureEvaluationBudgetPerFrame)
            return false;

        _postureEvalsThisFrame++;
        return true;
    }

    private const float CrouchedCoverBodyHeight = 0.75f;

    private const float StandingCoverBodyHeight = 1.15f;

    private const float ProneCoverMuzzleHeight = 0.42f;

    private const float CrouchedCoverMuzzleHeight = 1.12f;

    private const float StandingCoverMuzzleHeight = 1.58f;

    private const float CoverShoulderHalfWidth = 0.28f;

    // The stable posture axis (ThreatAxisStabilityCore) now absorbs bearing jitter, so
    // the cached ballistic posture evaluation can live much longer and tolerate a wider
    // bearing spread before it is recomputed. This is the main reduction in per-soldier
    // posture raycasting.
    private const float CoverPostureCacheSeconds = 4f;

    private const float CoverPostureDirectionDotTolerance = 0.9f;

    private const float MovingBodyHeight = 0.9f;

    private const float ThreatEndpointTolerance = 1.25f;

    private const int MaximumCoverThreats = 4;

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

        // The ballistic sampler classified a real barrier on at least one silhouette
        // ray in at least one posture, so its "protects / does not protect" verdict is
        // trustworthy. When no posture saw any classifiable barrier the verdict is
        // "could not classify", and an authored slot keeps its authored pose instead.
        internal bool ClassificationSucceeded =>
            Standing.HasClassifiedObstruction ||
            Crouched.HasClassifiedObstruction ||
            Prone.HasClassifiedObstruction;
    }

    private readonly record struct CurrentCoverEvaluation(
        bool IsProtective,
        SoldierPose Pose);

    private static IntPtr CurrentCoverId(Soldier soldier)
    {
        try
        {
            var cover = soldier.targetDestination;
            return cover == null || cover.WasCollected || !soldier.IsOnCover()
                ? IntPtr.Zero
                : cover.Pointer;
        }
        catch (NullReferenceException)
        {
            return IntPtr.Zero;
        }
        catch (Il2CppException)
        {
            return IntPtr.Zero;
        }
        catch (ObjectCollectedException)
        {
            return IntPtr.Zero;
        }
    }

    internal static bool ShouldKeepReachedCover(Soldier soldier)
    {
        // Calls made by the director's sole cover executor are intentional tactical
        // transitions and must pass through the native CoverPosition method.
        if (OwnsCoverAssignmentWrite(soldier))
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
        if (OwnsCoverAssignmentWrite(soldier))
            return false;

        var protectedAssignment =
            GroundAiDirector.HasProtectedInfantryAssignment(soldier);
        var state = AiState.GetContactState(soldierId);
        var defensivePositionOwned =
            RefreshDefensivePositionOwnership(soldier, state) ||
            HasActivePlayerHoldPositionControl(soldier, state);
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
        var state = AiState.GetContactState(soldier.GetInstanceID());
        var frame = Time.frameCount;
        if (state.LastUsableCoverFrame == frame)
            return state.UsableCoverCached;

        var result = EvaluateUsableCover(soldier);
        state.LastUsableCoverFrame = frame;
        state.UsableCoverCached = result;
        return result;
    }

    private static bool EvaluateUsableCover(Soldier soldier)
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

    private static bool IsCurrentCoverProtective(
        Soldier soldier,
        ContactResponseState state,
        Vector3 threatPosition,
        float now)
        // A relocation decision reads IsProtective; it must never flip to "unprotected"
        // just because the per-frame geometry budget was full, so a first evaluation on
        // this path runs over budget rather than deferring. These callers live on the
        // staggered director update, so they do not cluster the way the pose path does.
        => TryGetCurrentCoverEvaluation(
               soldier, state, threatPosition, now, out var evaluation,
               mayDeferFirstEval: false) &&
           evaluation.IsProtective;

    private static bool TryGetCurrentCoverEvaluation(
        Soldier soldier,
        ContactResponseState state,
        Vector3 threatPosition,
        float now,
        out CurrentCoverEvaluation evaluation,
        bool mayDeferFirstEval = true)
    {
        evaluation = default;
        if (!IsOnUsableCover(soldier))
            return false;

        try
        {
            var cover = soldier.targetDestination;
            if (cover == null || cover.WasCollected || cover.Pointer == IntPtr.Zero)
                return false;

            var soldierPosition = soldier.transform.position;
            var toThreat = threatPosition - soldierPosition;
            toThreat.y = 0f;
            var threatDistance = toThreat.magnitude;
            if (threatDistance < 0.1f)
                return false;
            var observedAxis = toThreat / threatDistance;

            // Stabilize the bearing the posture is evaluated against. Alternating
            // attackers on separated bearings would otherwise flip the ballistic
            // evaluation crouch<->prone every time the active target changed.
            var axisResult = ThreatAxisStabilityCore.Update(
                new ThreatAxisStabilityCore.State(
                    new MapPoint(state.PostureThreatAxis.x, state.PostureThreatAxis.z),
                    new MapPoint(
                        state.PostureThreatPendingAxis.x, state.PostureThreatPendingAxis.z),
                    state.PostureThreatPendingSince),
                new MapPoint(observedAxis.x, observedAxis.z),
                now);
            state.PostureThreatAxis =
                new Vector3(axisResult.State.Axis.X, 0f, axisResult.State.Axis.Z);
            state.PostureThreatPendingAxis = new Vector3(
                axisResult.State.PendingAxis.X, 0f, axisResult.State.PendingAxis.Z);
            state.PostureThreatPendingSince = axisResult.State.PendingSince;

            var stableAxis = state.PostureThreatAxis;
            // Before the stable axis is first established, fall back to the raw bearing.
            if (stableAxis.sqrMagnitude < 0.5f)
                stableAxis = observedAxis;
            var stableThreatPosition = soldierPosition + stableAxis * threatDistance;

            // A material rotation of the stable axis forces a fresh evaluation; a small
            // drift keeps the cached one, which is what reduces posture raycasting.
            var cacheMatches = !axisResult.AxisChangedMaterially &&
                               state.EvaluatedCoverPostureId == cover.Pointer &&
                               now < state.EvaluatedCoverPostureUntil &&
                               state.EvaluatedCoverThreatDirection.sqrMagnitude > 0.5f &&
                               Vector3.Dot(
                                   state.EvaluatedCoverThreatDirection,
                                   stableAxis) >= CoverPostureDirectionDotTolerance;
            if (cacheMatches)
            {
                evaluation = new CurrentCoverEvaluation(
                    state.EvaluatedCoverIsProtective,
                    state.EvaluatedCoverPosture);
                return true;
            }

            // A soldier that already has a cached evaluation for this exact cover
            // (even if it is stale by time or by axis drift) only refreshes when the
            // global per-frame posture-evaluation budget has room; otherwise it
            // reuses that stale answer this frame and retries on a later frame once
            // a slot frees. A soldier with no cached evaluation at all for this cover
            // (first contact) bypasses the budget entirely: a first evaluation is
            // never blocked, only refreshes are. Reusing the stale answer is safe
            // because it was the live evaluation moments ago and the stable posture
            // axis above already damps the bearing this posture is evaluated against.
            var hasStaleCachedEvaluation =
                state.EvaluatedCoverPostureId == cover.Pointer &&
                state.EvaluatedCoverThreatDirection.sqrMagnitude > 0.5f;
            // The per-frame geometry budget now gates first evaluations too, not only
            // refreshes. A mass-contact wave used to make every newly-covered soldier
            // run a full first EvaluateCoverGeometry on the same frame (each ~12
            // ballistic penetration lines + 3 fire-lane casts) — unbudgeted and, because
            // the budget counter only ticks on refreshes, invisible to the probe. That
            // is the measured TacticalMove spike. Over budget, a soldier with a recent
            // cached answer reuses it; a first evaluation on a pose-maintenance path
            // falls back to its safe crouch/native pose for this frame and retries next
            // frame once a slot frees.
            if (!TryBeginPostureEvaluation())
            {
                if (hasStaleCachedEvaluation)
                {
                    evaluation = new CurrentCoverEvaluation(
                        state.EvaluatedCoverIsProtective,
                        state.EvaluatedCoverPosture);
                    return true;
                }

                if (mayDeferFirstEval)
                    return false;
            }

            var geometry = EvaluateCoverGeometry(
                soldierPosition, stableThreatPosition, true);

            // A soldier occupying an authored trench/window slot whose material the
            // penetration sampler could not classify keeps the pose the slot was built
            // for (crouch at the parapet, stand at the window) rather than lying prone
            // below it, blind. A confidently-measured penetrable barrier still yields
            // prone. The soldier is on soldier.targetDestination here, which is the
            // authored cover node the search/native AI accepted.
            var authoredPose = ToCoverPostureChoice(SafeGetCoverPose(cover));
            var finalChoice = AuthoredPoseFallbackCore.ResolvePose(
                geometry.Choice,
                geometry.IsProtective,
                geometry.ClassificationSucceeded,
                true,
                authoredPose);
            var isProtective = AuthoredPoseFallbackCore.ResolveProtective(
                geometry.IsProtective, geometry.ClassificationSucceeded, true);
            var pose = ToSoldierPose(finalChoice);
            state.EvaluatedCoverPostureId = cover.Pointer;
            state.EvaluatedCoverPosture = pose;
            state.EvaluatedCoverIsProtective = isProtective;
            state.EvaluatedCoverThreatDirection = stableAxis;
            // Per-soldier jitter spreads cache expiries over ~1.4s so a mass contact
            // event (barrage, contact wave) that synchronizes many soldiers' caches
            // cannot make them all re-evaluate on the same frame 4s later.
            state.EvaluatedCoverPostureUntil = now + CoverPostureCacheSeconds +
                                                (soldier.GetInstanceID() & 7) * 0.2f;
            evaluation = new CurrentCoverEvaluation(isProtective, pose);
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

    private static void ResetCoverPostureEvaluation(ContactResponseState state)
    {
        state.EvaluatedCoverPostureId = IntPtr.Zero;
        state.EvaluatedCoverPosture = SoldierPose.Crouch;
        state.EvaluatedCoverIsProtective = false;
        state.EvaluatedCoverThreatDirection = default;
        state.EvaluatedCoverPostureUntil = 0f;
        state.PostureThreatAxis = default;
        state.PostureThreatPendingAxis = default;
        state.PostureThreatPendingSince = 0f;
    }

    internal static bool IsWeaponFiring(Soldier soldier)
    {
        try
        {
            var gun = soldier.GetHeldGun();
            return gun != null && gun.IsFiring;
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
            var ai = soldier.aiController;
            if (ai != null)
                ApplyArbitratedPose(
                    ai, soldier, Time.time, resolveDecisionTail: true, SoldierPose.Idle, "blocked-shot-stand");
        }

        // Withhold the obstructed round. Once the standing animation clears a low
        // parapet, the following trigger attempt proceeds normally. If even the
        // standing muzzle remains blocked, continuing to shoot the wall is never a
        // useful fallback.
        return true;
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
        var towardThreat = targetPosition - position;
        towardThreat.y = 0f;
        if (towardThreat.sqrMagnitude < 0.01f)
            return null;
        towardThreat.Normalize();

        var knownThreats = new List<Vector3>(MaximumCoverThreats) { targetPosition };
        if (directFirePosition.HasValue)
            AppendDistinctThreat(knownThreats, directFirePosition.Value);

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
                // HARD filter: small on purpose (plan 016 review). Its only job is
                // "two soldiers must not claim the same physical slot". Dispersion is
                // delivered by the soft crowding penalty in the scoring loop below,
                // which can never make a candidate ineligible — a wide exclusion here
                // would strand soldiers in the open whenever a trench or building has
                // its slots a couple of metres apart.
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
                // A forward corridor replaces the old strict "closer to the waypoint"
                // rule so attackers can use flanking cover and off-axis doorways
                // within a bounded backtrack, not only a small forward half-moon.
                if (enforceAttackProgress &&
                    !AttackCoverCorridorCore.Accepts(
                        new MapPoint(position.x, position.z),
                        new MapPoint(coverPosition.x, coverPosition.z),
                        new MapPoint(attackWaypoint.x, attackWaypoint.z)))
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

        // CoverManager ordering is not stable on every map. Sampling it as if it
        // were distance-ordered can accidentally exclude an entire building cluster
        // on one faction's side of the objective.
        coarseCandidates.Sort(static (left, right) =>
        {
            var distance = left.DistanceSqr.CompareTo(right.DistanceSqr);
            if (distance != 0)
                return distance;
            return left.Destination.Pointer.ToInt64()
                .CompareTo(right.Destination.Pointer.ToInt64());
        });

        AiDestination? best = null;
        var bestScore = float.MaxValue;
        AiDestination? bestAuthoredFallback = null;
        var bestAuthoredFallbackScore = float.MaxValue;
        var defensiveOccupation = selectionMode == CoverSelectionMode.DefensiveOccupation;
        var detailedIndices = CoverCandidateSamplingCore.SelectIndices(
            coarseCandidates.Count,
            defensiveOccupation
                ? InfantryCoverPolicy.DefensiveDetailedCandidateLimit
                : InfantryCoverPolicy.DetailedCandidateLimit,
            defensiveOccupation
                ? InfantryCoverPolicy.DefensiveNearestDetailedCandidateCount
                : InfantryCoverPolicy.NearestDetailedCandidateCount);
        var detailedEvaluated = 0;
        foreach (var detailedIndex in detailedIndices)
        {
            // Stop scoring once this frame's geometry allowance is gone, keeping whatever
            // is best so far. The shortlist presents nearby and distributed candidates
            // early, so this cutoff still compares local cover with deeper building and
            // trench slots.
            if (detailedEvaluated >= MinimumDetailedCandidates && SearchGeometryBudgetSpent())
                break;

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
                detailedEvaluated++;
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
                // Crowding term (plan 016): count other soldiers' active reservations
                // within dispersion range of this candidate, reusing the reservation
                // map already consulted above - no extra physics or allocation inside
                // this budgeted detailed loop.
                var nearbyReservations = AiState.CountNearbyReservations(
                    coverPosition,
                    soldier.GetInstanceID(),
                    now,
                    InfantryCoverPolicy.CoverDispersionSpacingMeters);
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
                    PreferProtectionOverFiringLine: defensiveOccupation,
                    NearbyReservationCount: nearbyReservations);
                var score = InfantryCoverDecisionCore.Score(selectionMode, scoreInput);
                if (!InfantryCoverDecisionCore.IsRouteAcceptable(selectionMode, scoreInput))
                {
                    // A native cover node that is valid for the threat direction is
                    // still preferable to leaving the soldier at an arbitrary open
                    // arrival point when penetration geometry cannot classify any
                    // sampled barrier. It remains a fallback only: every measured,
                    // materially protective position wins before this path is used.
                    if (!InfantryCoverDecisionCore.IsAuthoredFallbackEligible(
                            geometry.Standing.HasClassifiedObstruction,
                            geometry.Crouched.HasClassifiedObstruction,
                            geometry.Prone.HasClassifiedObstruction))
                    {
                        continue;
                    }

                    var nativePosePenalty = candidate.GetCoverPose() == SoldierPose.Idle
                        ? 450f
                        : 0f;
                    var fallbackScore = score + nativePosePenalty;
                    if (fallbackScore < bestAuthoredFallbackScore)
                    {
                        bestAuthoredFallback = candidate;
                        bestAuthoredFallbackScore = fallbackScore;
                    }
                    continue;
                }

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

        var usedAuthoredFallback = false;
        if (InfantryCoverDecisionCore.ShouldUseAuthoredFallback(
                best != null, bestAuthoredFallback != null))
        {
            best = bestAuthoredFallback;
            bestScore = bestAuthoredFallbackScore;
            usedAuthoredFallback = true;
        }

        AiState.Trace(
            $"Cover inventory: soldier {soldier.GetInstanceID()} mode={selectionMode} " +
            $"raw={rawExamined} eligible={coarseCandidates.Count} detailed={detailedIndices.Length} " +
            $"selected={(best == null ? "none" : best.Pointer.ToString())} " +
            $"fortified={bestFortifiedScore:0.00} fallback={usedAuthoredFallback}");
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

    /// <summary>
    /// Full ballistic geometry costs ~5.6ms measured — 12 penetration lines plus 3
    /// fire-lane casts. The detailed cover search runs one per CANDIDATE, up to 12
    /// (20 for defensive occupation), all inside a single frame: 67-112ms of raycasting
    /// for one soldier looking for cover. That is the largest single-frame cost in this
    /// mod, and it is event-driven rather than headcount-driven — which is why it shows
    /// up the same at maximum AI as in a small battle. It depends on somebody needing
    /// cover, not on how many soldiers exist.
    /// </summary>
    private const int SearchGeometryBudgetPerFrame = 5;

    // Never choose cover from one or two candidates just because the frame was busy; a
    // search that scored six good positions instead of twelve picks slightly worse cover,
    // which is a far better outcome than a 112ms stall.
    private const int MinimumDetailedCandidates = 3;

    private static int _geometryFrame = -1;
    private static int _geometryRunsThisFrame;

    private static bool SearchGeometryBudgetSpent()
    {
        var frame = Time.frameCount;
        if (frame != _geometryFrame)
        {
            _geometryFrame = frame;
            _geometryRunsThisFrame = 0;
        }

        return _geometryRunsThisFrame >= SearchGeometryBudgetPerFrame;
    }

    private static CoverGeometryEvaluation EvaluateCoverGeometry(
        Vector3 coverPosition,
        Vector3 threatPosition,
        bool evaluateFiringQuality)
    {
        var frame = Time.frameCount;
        if (frame != _geometryFrame)
        {
            _geometryFrame = frame;
            _geometryRunsThisFrame = 0;
        }

        _geometryRunsThisFrame++;
        ModTimeProbe.CountCoverGeometryRun();
        var __t = ModTimeProbe.Begin();
        try
        {
            return EvaluateCoverGeometryCore(
                coverPosition, threatPosition, evaluateFiringQuality);
        }
        finally
        {
            ModTimeProbe.EndSub(ModSubSite.CoverGeometry, __t);
        }
    }

    private static CoverGeometryEvaluation EvaluateCoverGeometryCore(
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

        var torso = EvaluateCoverLine(
            coverPosition + Vector3.up * torsoHeight, threatPosition);
        var head = EvaluateCoverLine(
            coverPosition + Vector3.up * headHeight, threatPosition);
        var leftShoulder = EvaluateCoverLine(
            coverPosition + Vector3.up * shoulderHeight + lateral, threatPosition);
        var rightShoulder = EvaluateCoverLine(
            coverPosition + Vector3.up * shoulderHeight - lateral, threatPosition);
        var protectedSamples = 0;
        if (BallisticCoverDecisionCore.IsMeaningfulRay(torso.Protection))
            protectedSamples++;
        if (BallisticCoverDecisionCore.IsMeaningfulRay(head.Protection))
            protectedSamples++;
        if (BallisticCoverDecisionCore.IsMeaningfulRay(leftShoulder.Protection))
            protectedSamples++;
        if (BallisticCoverDecisionCore.IsMeaningfulRay(rightShoulder.Protection))
            protectedSamples++;

        var ballisticProtection =
            (torso.Protection + head.Protection +
             leftShoulder.Protection + rightShoulder.Protection) * 0.25f;
        var hasClassifiedObstruction = torso.HasObstruction || head.HasObstruction ||
                                       leftShoulder.HasObstruction ||
                                       rightShoulder.HasObstruction;
        return new CoverPostureInput(
            protectedSamples, 4, false, ballisticProtection, hasClassifiedObstruction);
    }

    // Distinguishes "measured, penetrable barrier" (HasObstruction true, low protection)
    // from "could not classify" (HasObstruction false), which the authored-pose fallback
    // needs. Kept separate from ProtectionFromThreat, whose callers only need the value.
    private static (float Protection, bool HasObstruction) EvaluateCoverLine(
        Vector3 bodyPosition,
        Vector3 threatPosition)
    {
        try
        {
            var line = BulletPenetration.EvaluateOrdinaryCoverLine(
                bodyPosition, threatPosition);
            return (line.ProtectionFraction, line.HasObstruction);
        }
        catch (NullReferenceException)
        {
            return (0f, false);
        }
        catch (Il2CppException)
        {
            return (0f, false);
        }
        catch (ObjectCollectedException)
        {
            return (0f, false);
        }
    }

    private static CoverPostureChoice ToCoverPostureChoice(SoldierPose pose)
        => pose switch
        {
            SoldierPose.Prone => CoverPostureChoice.Prone,
            SoldierPose.Crouch => CoverPostureChoice.Crouched,
            _ => CoverPostureChoice.Standing
        };

    private static SoldierPose SafeGetCoverPose(AiDestination cover)
    {
        try
        {
            return cover.GetCoverPose();
        }
        catch (NullReferenceException)
        {
            return SoldierPose.Crouch;
        }
        catch (Il2CppException)
        {
            return SoldierPose.Crouch;
        }
        catch (ObjectCollectedException)
        {
            return SoldierPose.Crouch;
        }
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
        catch (NullReferenceException)
        {
            // Unknown geometry is not credited as protection.
            return 0f;
        }
        catch (Il2CppException)
        {
            // Unknown geometry is not credited as protection.
            return 0f;
        }
        catch (ObjectCollectedException)
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
        catch (NullReferenceException)
        {
            // Do not deliberately choose a firing pose whose lane could not be
            // evaluated. The destination can still win in urgent survival mode.
            return false;
        }
        catch (Il2CppException)
        {
            // Do not deliberately choose a firing pose whose lane could not be
            // evaluated. The destination can still win in urgent survival mode.
            return false;
        }
        catch (ObjectCollectedException)
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
            if (collider == null ||
                collider.GetComponentInParent<Soldier>() != null ||
                BulletPenetration.IsNonProtectiveCoverScreen(collider))
            {
                continue;
            }

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
        state.ConsecutiveCoverSearchFailures = 0;
        state.LastOutgoingShotWasStationary = false;
        state.RelocationPausedBySuppression = false;
        state.EngagementHoldUntil = 0f;
        state.RelocateUntil = relocateUntil;
        state.RelocateLastDistance = soldier.DestinationDistance;
        state.RelocateLastProgressAt = now;
        state.RelocateLastDestinationProgressAt = now;
        state.RelocatePathRetryUsed = false;
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
        // Leaving a position ends the hold on it. Stated here rather than only in
        // ReleaseManeuverCoverForAuthorizedMove (which keeps an anchored defender's hold)
        // because CoverHold outranks CommittedMove: a stale hold would otherwise halt the
        // dash it was just released for. FinishRelocation re-arms the hold on arrival, and
        // UpdateDefensiveCoverHold re-asserts an anchor's own hold on its next tick.
        state.HoldCoverUntil = 0f;
        state.ContactCrouchOwned = true;
        ApplyMovementDecision(
            ai, soldier, Time.deltaTime, now, MovementOwner.CommittedMove,
            "cover-move");
        // The moving-fire rule is the arbiter's rank f; this dash no longer re-states it.
        ApplyArbitratedPose(
            ai, soldier, now, resolveDecisionTail: true, SoldierPose.Crouch, "cover-move");
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
        state.ContactCrouchOwned = true;
        state.SuppressionPoseOwned = suppressed;
        ApplyMovementDecision(
            ai, soldier, Time.deltaTime, now, MovementOwner.OrderedMove,
            "attack-advance");
        soldier.isSprinting = false;

        if (state.SuppressionFireInhibited && now >= state.PinnedFireBlockedUntil)
            state.SuppressionFireInhibited = false;

        ApplyArbitratedPose(
            ai,
            soldier,
            now,
            resolveDecisionTail: true,
            state.Pinned ||
            (Settings.DangerReactionsEnabled.Value &&
             soldier.GetSuppressionValue() >= AiBehaviorTuning.ProneSuppressionThreshold)
                ? SoldierPose.Prone
                : SoldierPose.Crouch,
            "attack-advance");

        if (wasHoldingMovement)
            RefreshPath(ai, "Attack objective path resume failed");
        if (newlyForced)
        {
            AiState.Trace(
                $"Attack progress: soldier {soldierId} resumed toward the objective after the maximum combat halt");
        }
    }

    private static bool IsAttackingSquadSoldier(Soldier soldier)
    {
        try
        {
            return soldier.joinedSquad != null &&
                   GroundAiDirector.IsAttackingFaction(AiState.FactionOf(soldier));
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
        state.RelocateLastDistance = 0f;
        state.RelocateLastProgressAt = 0f;
        state.RelocateLastDestinationProgressAt = 0f;
        state.RelocatePathRetryUsed = false;
        state.RelocateLastProgressPosition = default;
        var occupiedCoverPosition = state.ReservedCoverPosition;
        state.RelocateDestinationPointer = IntPtr.Zero;
        state.RelocateDestinationPosition = default;
        if (completedMove)
        {
            // Plan 028: keyed on "his squad is going somewhere", not on the attackFromSide
            // waypoint. The ORDER-only form is deliberate here - he has just arrived, so
            // his destination reads as reached and the committed-destination form would
            // hand every arriving soldier the long defender hold.
            var hasMovementOrder = HasMovingSquadOrder(soldier);
            state.AttackProgressForced = false;
            state.AttackHaltStartedAt = hasMovementOrder ? Mathf.Max(now, 0.0001f) : 0f;
            if (state.ReservedCoverId != IntPtr.Zero)
            {
                state.ManeuverCoverAnchorId = state.ReservedCoverId;
                state.ManeuverCoverAnchorPosition = occupiedCoverPosition;
                if (OwnsTacticalDefensivePosition(state))
                {
                    TryCaptureReservedDefensiveCoverAnchor(
                        soldier, state, soldierId, now);
                }
                if (!state.DefensiveCoverHold)
                {
                    // Attackers still wait for covering fire before the next bound —
                    // that gate is enforced by ShouldAuthorizeAttackBound after this
                    // hold expires, not by an unreachable +inf timer (plan 012). D5
                    // (plan 015): a soldier with somewhere to be rearms this on a short
                    // assault tempo; one who is staying put keeps the long defender hold.
                    state.HoldCoverUntil = now + (hasMovementOrder
                        ? InfantryCoverPolicy.MinimumAttackCoverHoldSeconds
                        : InfantryCoverPolicy.MinimumManeuverCoverHoldSeconds);
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
                now + InfantryCoverPolicy.CoverReservationLeaseSeconds);
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
        // D4 (plan 015): every caller of this method only runs while target != null
        // in UpdateInternal, so the hold is bounded to the contact's own
        // persistence timer instead of +inf.
        state.EngagementHoldUntil = state.ContactUntil;
        state.ContactCrouchOwned = true;
        StopTacticalMovement(
            ai,
            soldier,
            Time.deltaTime,
            "respond-nocover");
        FaceThreatWhenStationary(ai, soldier, targetPosition);
    }
}
