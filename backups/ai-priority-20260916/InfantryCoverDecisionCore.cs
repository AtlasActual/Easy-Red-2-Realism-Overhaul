namespace ER2RealismOverhaul;

internal enum InfantryCoverState
{
    Holding,
    WaitingForSafeMove,
    Moving
}

internal enum CoverSelectionMode
{
    Normal,
    Urgent,
    DefensiveOccupation
}

internal enum CoverPostureChoice
{
    Standing,
    Crouched,
    Prone
}

internal readonly record struct CoverNeedInput(
    bool HasUsableCover,
    bool MayAdvanceFromCover,
    bool CoverCompromised,
    bool UnderDirectFire,
    bool Suppressed,
    bool CloseThreat,
    bool AttackAdvanceBlocked,
    bool NormalDecisionDue,
    bool UrgentDecisionDue,
    bool FiringPhaseActive = false);

internal readonly record struct CoverNeedDecision(
    InfantryCoverState State,
    CoverSelectionMode SelectionMode,
    bool ShouldSearch,
    string Reason);

internal readonly record struct CoverScoreInput(
    float DistanceSqr,
    float StandingPosePenalty,
    bool PrimaryThreatProtected,
    int UnprotectedSecondaryThreats,
    bool AssignedPoseCanFire,
    bool StandingCanFire,
    float ExposedRouteMeters,
    float ExposedRouteFraction,
    float PrimaryProtectionFraction = 1f,
    bool PreferProtectionOverFiringLine = false,
    // Count of other soldiers' active cover reservations within the dispersion
    // radius of this candidate (plan 016). Ordinary maneuver uses a light
    // tie-breaker; initial defensive occupation spreads more assertively among
    // equivalently protective firing positions.
    int NearbyReservationCount = 0);

internal readonly record struct CoverPositionQuality(
    bool IsProtective,
    bool IsMeasured,
    float ProtectionFraction,
    bool HasFiringLane);

internal readonly record struct CoverPostureInput(
    int ProtectedSamples,
    int TotalSamples,
    bool CanFire,
    float BallisticProtectionFraction = float.NaN,
    bool HasClassifiedObstruction = false);

internal static class InfantryCoverDecisionCore
{
    internal const float SameFloorVerticalToleranceMeters = 1.75f;

    internal const float DefensiveUpgradeMaximumDistanceMeters = 20f;

    internal static float NextDefensiveReassessmentAt(float now, int soldierId)
        => now + 30f + (uint)soldierId % 11u;

    internal static bool ShouldReassessDefensiveCover(
        bool anchored, bool relocating, bool calm, float now, float nextCheckAt,
        float nextRelocationAllowedAt)
        => anchored && !relocating && calm && nextCheckAt > 0f &&
           now >= nextCheckAt && now >= nextRelocationAllowedAt;

    internal static bool IsWorthDefensiveRelocation(
        CoverPositionQuality current, CoverPositionQuality candidate,
        float distanceMeters, float exposedRouteFraction)
    {
        // Optional improvements must prove a substantial gain. Unknown native
        // fallback quality is not evidence for leaving an established position.
        if (!current.IsMeasured || !candidate.IsMeasured || !candidate.IsProtective ||
            !float.IsFinite(current.ProtectionFraction) ||
            !float.IsFinite(candidate.ProtectionFraction) ||
            !float.IsFinite(distanceMeters) || !float.IsFinite(exposedRouteFraction) ||
            distanceMeters < 3f || distanceMeters > DefensiveUpgradeMaximumDistanceMeters ||
            exposedRouteFraction < 0f || exposedRouteFraction > 0.35f)
            return false;

        var requiredGain = current.HasFiringLane && !candidate.HasFiringLane ? 0.30f : 0.20f;
        return candidate.ProtectionFraction - current.ProtectionFraction >= requiredGain - 0.0001f;
    }

    internal static bool OwnsDefensiveCoverAssignment(
        bool positionOwned, bool relocating, bool anchored)
        => positionOwned && (relocating || anchored);

    internal static bool ShouldHoldDefensivePosition(
        bool positionOwned, bool relocating, bool anchored)
        => positionOwned && !relocating && anchored;

    internal static bool CanAnchorDefensiveCover(bool evaluationSucceeded, bool protective)
        // Visibility to one enemy (or a predicted point beyond several walls) is
        // firing quality, not proof that a trench/window/sandbag slot is unusable.
        => evaluationSucceeded && protective;

    internal static bool ShouldHaltForCloseThreat(
        bool closeThreat, bool allowsMovingFire, bool firingPhaseActive)
        // This is only the immediate firing response, not a standing veto on
        // looking for cover. The normal FSM retains protective positions and
        // validates a destination before granting a move out of open ground.
        => closeThreat && !allowsMovingFire && firingPhaseActive;

    internal static CoverPostureChoice SelectReloadPosture(
        CoverPostureChoice coverPosture)
        => coverPosture == CoverPostureChoice.Prone
            ? CoverPostureChoice.Prone
            : CoverPostureChoice.Crouched;

    internal static bool ShouldAcquireReloadPosture(
        bool isReloading,
        bool alreadyOwned,
        bool onUsableCover)
        => isReloading && !alreadyOwned && onUsableCover;

    internal static bool ShouldTreatCurrentCoverAsUsable(
        bool onUsableNativeCover,
        bool insideDefensiveArea,
        bool protectsFromCurrentThreat)
    {
        _ = insideDefensiveArea;
        // Being inside the objective radius does not turn an exposed authored node
        // into cover. Only geometrically protective positions are accepted here;
        // already-established defensive anchors use the separate stable-anchor rule.
        return onUsableNativeCover && protectsFromCurrentThreat;
    }

    internal static bool ShouldKeepDefensiveCoverAnchor(
        bool defendOrderActive,
        bool anchorInsideArea,
        bool coverKnownCompromised,
        bool withinAnchorLeash)
        => defendOrderActive &&
           anchorInsideArea &&
           !coverKnownCompromised &&
           withinAnchorLeash;

    internal static bool ShouldClaimReachedDefensiveSlot(
        bool defensivePositionOwned,
        bool hasReservedSlot,
        bool atReservedSlot,
        bool nativeCoverReported,
        bool destinationEnded)
        => defensivePositionOwned && hasReservedSlot && atReservedSlot &&
           (nativeCoverReported || destinationEnded);

    internal static bool ShouldBlockNativeCoverClear(
        bool protectedAssignment,
        bool defensivePositionOwned,
        bool relocating,
        bool anchored,
        bool reachedCoverHold)
        => protectedAssignment ||
           defensivePositionOwned && (relocating || anchored) ||
           reachedCoverHold;

    internal static bool ShouldReleaseUnoccupiedReservation(
        bool relocating,
        bool nativeCoverReported,
        bool stableAnchor)
        => !relocating && !nativeCoverReported && !stableAnchor;

    internal static bool HasStableReservationAnchor(
        bool defensiveHold,
        bool defensiveAnchor,
        bool maneuverAnchor)
        => defensiveHold || defensiveAnchor || maneuverAnchor;

    internal static bool ShouldSeekInitialDefensiveCover(
        bool positionOwned,
        bool hasStableAnchor,
        bool relocating,
        bool decisionDue,
        bool hasActionableContact)
    {
        // Contact changes the threat axis used to score candidates, but it must not
        // veto the defender's initial occupation of a protected fighting position.
        _ = hasActionableContact;
        return positionOwned && !hasStableAnchor && !relocating && decisionDue;
    }

    internal static bool HasMeaningfulProtection(CoverPostureInput posture)
    {
        if (posture.TotalSamples <= 0 || posture.ProtectedSamples < 0 ||
            posture.ProtectedSamples > posture.TotalSamples)
        {
            return false;
        }

        // Head, torso, and both shoulders are sampled. At least three of those
        // four regions must be protected, and the material across the whole
        // silhouette must absorb a meaningful share of ordinary rifle energy.
        // This stops foliage, glass, and thin props from becoming "cover" merely
        // because they intersect all four visibility rays.
        return posture.ProtectedSamples * 4 >= posture.TotalSamples * 3 &&
               ProtectionFraction(posture) >=
               BallisticCoverDecisionCore.MeaningfulPostureProtection;
    }

    internal static float ProtectionFraction(CoverPostureInput posture)
    {
        if (!float.IsNaN(posture.BallisticProtectionFraction) &&
            !float.IsInfinity(posture.BallisticProtectionFraction))
        {
            return Math.Clamp(posture.BallisticProtectionFraction, 0f, 1f);
        }

        return posture.TotalSamples > 0 && posture.ProtectedSamples >= 0
            ? Math.Clamp(posture.ProtectedSamples / (float)posture.TotalSamples, 0f, 1f)
            : 0f;
    }

    internal static CoverPostureChoice SelectCoverPosture(
        CoverPostureInput standing,
        CoverPostureInput crouched,
        CoverPostureInput prone)
    {
        var crouchProtects = HasMeaningfulProtection(crouched);
        if (crouchProtects)
        {
            if (crouched.CanFire)
                return CoverPostureChoice.Crouched;

            // Keep the smallest firing silhouette. Standing is a last-resort
            // clearance posture only when neither crouch nor prone can use the lane.
            if (HasMeaningfulProtection(prone) && prone.CanFire)
                return CoverPostureChoice.Prone;

            // A soldier may rise behind genuinely protective low cover to clear the
            // weapon only after both lower firing postures have failed. A narrow
            // object that does not protect a crouched body cannot justify standing in
            // the open merely because the muzzle clears.
            if (standing.CanFire)
                return CoverPostureChoice.Standing;

            return HasMeaningfulProtection(prone) &&
                   ProtectionFraction(prone) > ProtectionFraction(crouched)
                ? CoverPostureChoice.Prone
                : CoverPostureChoice.Crouched;
        }

        if (HasMeaningfulProtection(prone))
        {
            if (prone.CanFire)
                return CoverPostureChoice.Prone;

            // This cover only protects the prone silhouette, but the weapon cannot
            // use that lane. Standing remains legal solely as the measured firing-
            // clearance fallback after both lower poses have failed.
            if (standing.CanFire)
                return CoverPostureChoice.Standing;

            return CoverPostureChoice.Prone;
        }

        if (HasMeaningfulProtection(standing) && standing.CanFire)
            return CoverPostureChoice.Standing;

        // Nothing here is genuine cover, so this evaluation cannot author a prone
        // cover posture. The ordinary stationary fighting fallback is crouched.
        return CoverPostureChoice.Crouched;
    }

    internal static CoverPostureChoice FallbackStationaryPosture(bool hasUsableCover)
        => CoverPostureChoice.Crouched;

    internal static CoverPostureInput SelectCoverPostureInput(
        CoverPostureChoice choice,
        CoverPostureInput standing,
        CoverPostureInput crouched,
        CoverPostureInput prone)
        => choice switch
        {
            CoverPostureChoice.Standing => standing,
            CoverPostureChoice.Crouched => crouched,
            CoverPostureChoice.Prone => prone,
            _ => crouched
        };

    internal static bool ShouldForceAttackProgress(
        bool hasAttackOrder,
        bool hasDestination,
        float haltStartedAt,
        float now,
        float maximumHaltSeconds)
    {
        if (!hasAttackOrder || !hasDestination || haltStartedAt <= 0f ||
            float.IsNaN(haltStartedAt) || float.IsInfinity(haltStartedAt) ||
            float.IsNaN(now) || float.IsInfinity(now) ||
            float.IsNaN(maximumHaltSeconds) || float.IsInfinity(maximumHaltSeconds))
        {
            return false;
        }

        return now - haltStartedAt >= Math.Max(0f, maximumHaltSeconds);
    }

    internal static bool ShouldLeaveCurrentCoverForAttackBound(
        CoverPositionQuality current,
        CoverPositionQuality candidate,
        bool forcedByDeadline)
    {
        // The halt deadline remains the hard liveness guarantee. Before that deadline,
        // coordinated fire authorizes a bound only when the selected destination does
        // not trade a known protective firing position for materially worse ground.
        if (forcedByDeadline)
            return true;
        if (!current.IsProtective)
            return true;
        if (!candidate.IsProtective)
            return false;

        // Do not churn from measured protection into an authored slot whose material
        // could not be classified, or between two equally uncertain authored slots.
        if (!candidate.IsMeasured)
            return false;

        const float MaterialProtectionLoss = 0.12f;
        if (current.IsMeasured &&
            candidate.ProtectionFraction + MaterialProtectionLoss <
            current.ProtectionFraction)
        {
            return false;
        }

        // A blind destination is not an improvement for a soldier already able to
        // engage from protection. The maximum halt can still move him through it.
        return !current.HasFiringLane || candidate.HasFiringLane;
    }

    internal static bool ShouldKeepMovingDuringDeferredCoverSearch(
        bool searchDeferred,
        bool hasUsableCover,
        bool closeThreat,
        bool hasMovementOrder,
        bool hasDestination,
        bool firingCommitActive = false)
        => searchDeferred &&
           !hasUsableCover &&
           !closeThreat &&
           hasMovementOrder &&
           hasDestination &&
           !firingCommitActive;

    internal static CoverNeedDecision EvaluateNeed(CoverNeedInput input)
    {
        if (input.HasUsableCover && !input.MayAdvanceFromCover)
            return Hold("current cover still protects the soldier");

        if (input.Suppressed)
            return Hold("suppression makes leaving more dangerous than staying");

        if (input.CloseThreat && input.FiringPhaseActive)
            return Hold("the initial close-contact firing phase is still active");

        // After the bounded initial response, proximity is not protection. An
        // exposed soldier may seek a protective position even without an attack
        // advance authorization or an immediate firing lane at the destination.
        // Urgent cadence/backoff still bounds searches; suppression still wins.
        var urgent = input.CoverCompromised ||
                     input.UnderDirectFire ||
                     !input.HasUsableCover;
        if (input.AttackAdvanceBlocked && !urgent)
            return Wait(CoverSelectionMode.Normal, "advance lacks established covering fire");

        if (urgent)
        {
            return input.UrgentDecisionDue
                ? Search(
                    CoverSelectionMode.Urgent,
                    input.CoverCompromised || input.UnderDirectFire
                        ? "exposed position is under direct threat"
                        : "confirmed threat warrants a move out of the open")
                : Wait(CoverSelectionMode.Urgent, "urgent cover choice is already being acted on");
        }

        return input.NormalDecisionDue
            ? Search(CoverSelectionMode.Normal, "deliberate cover assessment is due")
            : Wait(CoverSelectionMode.Normal, "waiting for the next deliberate assessment");
    }

    internal static bool IsRouteAcceptable(CoverSelectionMode mode, CoverScoreInput input)
    {
        // Exposure is sampled along a straight chord, NOT the native navigation
        // route through doors and trench entrances. It can rank destinations, but
        // cannot veto a protected destination based on a route nobody will take.
        _ = mode;
        return input.PrimaryThreatProtected;
    }

    internal static bool ShouldUseAuthoredFallback(
        bool hasMeasuredProtectiveSelection,
        bool hasValidAuthoredCandidate)
        => !hasMeasuredProtectiveSelection && hasValidAuthoredCandidate;

    internal static bool IsAuthoredFallbackEligible(
        bool standingHasClassifiedObstruction,
        bool crouchedHasClassifiedObstruction,
        bool proneHasClassifiedObstruction)
        // The authored fallback exists for a trench/building slot whose material
        // could not be classified. A measured weak obstacle is evidence against
        // the slot, not a reason to trust its native cover label.
        => !standingHasClassifiedObstruction &&
           !crouchedHasClassifiedObstruction &&
           !proneHasClassifiedObstruction;

    internal static float Score(CoverSelectionMode mode, CoverScoreInput input)
    {
        var distanceMeters = MathF.Sqrt(Math.Max(0f, input.DistanceSqr));
        var protectionFraction = float.IsNaN(input.PrimaryProtectionFraction) ||
                                 float.IsInfinity(input.PrimaryProtectionFraction)
            ? 0f
            : Math.Clamp(input.PrimaryProtectionFraction, 0f, 1f);
        var protectionPenalty = (1f - protectionFraction) *
                                (input.PreferProtectionOverFiringLine ? 1600f : 700f);
        // Defenders inventory a whole position once, so use that opportunity to
        // spread them across equivalent firing cover instead of forming a knot
        // around the first good node. Protection still dominates: a 0.1 defensive
        // protection advantage is worth 200 points below, well over this penalty.
        var crowdingWeight = mode == CoverSelectionMode.DefensiveOccupation ? 60f : 25f;
        var crowdingPenalty =
            Math.Max(0, input.NearbyReservationCount) * crowdingWeight;
        var firePenalty = input.AssignedPoseCanFire
            ? 0f
            : input.StandingCanFire
                ? mode == CoverSelectionMode.Urgent
                    ? 30f
                    // A protection-first defender who can fire only from a standing
                    // rise still loses real time exposed; the old 30 was too weak to
                    // pull him off a slot where he never returns fire at all.
                    : input.PreferProtectionOverFiringLine ? 90f : 120f
                : mode == CoverSelectionMode.Urgent
                    ? input.PreferProtectionOverFiringLine ? 50f : 80f
                    // Defensive cover may be screened by intervening buildings.
                    // Prefer a firing lane among comparable slots, without making
                    // current enemy visibility outweigh materially better protection.
                    : mode == CoverSelectionMode.DefensiveOccupation ? 120f : 500f;

        if (mode == CoverSelectionMode.DefensiveOccupation)
        {
            // Defensive occupation is protection-first. Distance and the exposed
            // part of the one-time route still matter, but neither may outweigh a
            // large improvement in body protection. A firing lane is useful only
            // after the position can keep its occupant alive.
            return distanceMeters * 2f +
                   input.StandingPosePenalty * 0.4f +
                   input.UnprotectedSecondaryThreats * 400f +
                   input.ExposedRouteMeters * 4f +
                   protectionPenalty * 1.25f +
                   firePenalty +
                   crowdingPenalty;
        }

        if (mode == CoverSelectionMode.Urgent)
        {
            return (input.PreferProtectionOverFiringLine
                       ? distanceMeters * 2.5f
                       : input.DistanceSqr) +
                   input.StandingPosePenalty * 0.4f +
                   input.UnprotectedSecondaryThreats * 200f +
                   input.ExposedRouteMeters * 18f +
                   protectionPenalty * 0.5f +
                   firePenalty +
                   crowdingPenalty;
        }

        // Deliberate movement compares nearby positions by travel distance rather
        // than squared distance. This keeps proximity relevant without letting a
        // marginal obstruction beat substantially safer cover only a short bound
        // farther away. Urgent movement above retains its strong short-dash bias.
        return distanceMeters * (input.PreferProtectionOverFiringLine ? 2f : 6f) +
               input.StandingPosePenalty +
               input.UnprotectedSecondaryThreats * 350f +
               input.ExposedRouteMeters * 35f +
               protectionPenalty +
               firePenalty +
               crowdingPenalty;
    }

    internal static bool CoverPositionsConflict(
        MapPoint first,
        MapPoint second,
        float minimumSpacing)
    {
        if (!IsFinite(first.X) || !IsFinite(first.Z) ||
            !IsFinite(second.X) || !IsFinite(second.Z) ||
            !IsFinite(minimumSpacing) || minimumSpacing <= 0f)
        {
            return false;
        }

        var deltaX = first.X - second.X;
        var deltaZ = first.Z - second.Z;
        return deltaX * deltaX + deltaZ * deltaZ <= minimumSpacing * minimumSpacing;
    }

    internal static bool CoverPositionsConflict(
        MapPoint first,
        float firstElevation,
        MapPoint second,
        float secondElevation,
        float minimumSpacing)
    {
        if (!IsFinite(firstElevation) || !IsFinite(secondElevation) ||
            MathF.Abs(firstElevation - secondElevation) >
            SameFloorVerticalToleranceMeters)
        {
            return false;
        }

        return CoverPositionsConflict(first, second, minimumSpacing);
    }

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);

    private static CoverNeedDecision Hold(string reason)
        => new(InfantryCoverState.Holding, CoverSelectionMode.Normal, false, reason);

    private static CoverNeedDecision Wait(CoverSelectionMode mode, string reason)
        => new(InfantryCoverState.WaitingForSafeMove, mode, false, reason);

    private static CoverNeedDecision Search(CoverSelectionMode mode, string reason)
        => new(InfantryCoverState.WaitingForSafeMove, mode, true, reason);
}

/// <summary>
/// Pure rule that lets a defender relocate off an anchored cover slot that a real,
/// currently-engaged enemy can shoot through. A defensive anchor is deliberately
/// sticky so a wandering active-target bearing cannot churn it (that stickiness is
/// what prevents stance/relocation flicker under alternating attackers). But an anchor
/// chosen against a predicted approach axis leaves the defender on the wrong side of
/// cover when the real attack arrives from a genuinely different, sustained direction.
/// The protection verdict is measured against the anti-flicker stabilized posture axis,
/// so only a durable rotation - not bearing noise - can flip it; and an inconclusive
/// evaluation (for example a building slot that does not report native cover) never
/// releases the anchor. The result authorizes exactly one relocation to face the real
/// enemy; the defender then re-anchors on the new protective slot.
/// </summary>
internal static class DefensiveAnchorReevaluationCore
{
    internal static bool ShouldReleaseForRealThreat(
        bool hasThreatMemory,
        bool engagedRecently,
        bool coverEvaluationSucceeded,
        bool coverProtectsAgainstStableThreat)
        => hasThreatMemory && engagedRecently && coverEvaluationSucceeded &&
           !coverProtectsAgainstStableThreat;
}

/// <summary>
/// Pure, Unity-free stabilizer for the threat bearing used to pick a soldier's cover
/// posture. Multiple attackers on separated bearings (which the flanking behavior now
/// produces) made the "active target" bearing alternate frame to frame, so the
/// ballistic posture evaluation flipped crouch&lt;-&gt;prone with it. This maintains one
/// stable posture axis: a small drift follows the target smoothly without disturbing
/// downstream consumers, a genuinely new flank threat (large divergence) is adopted at
/// once, and an intermediate new bearing must persist before it rotates the axis.
/// </summary>
internal static class ThreatAxisStabilityCore
{
    // A moderately divergent bearing must persist this long before it becomes the new
    // posture axis, so a briefly-visible second attacker cannot reorient the stance.
    internal const float SustainedRotationSeconds = 2.5f;

    // Beyond this angle the new bearing is a genuinely new flank threat and is adopted
    // immediately - waiting would leave the soldier oriented on the wrong enemy.
    internal const float ImmediateDivergenceDegrees = 60f;

    // Within this angle the bearing is the same threat wandering slightly; the axis
    // follows it smoothly and cached posture evaluations are left intact.
    internal const float SmoothDriftDegrees = 25f;

    // How far the stable axis is nudged toward a small drift each update. A partial
    // blend keeps the axis from snapping while still tracking a slowly moving target.
    private const float SmoothDriftBlend = 0.34f;

    internal readonly record struct State(
        MapPoint Axis,
        MapPoint PendingAxis,
        float PendingSince);

    internal readonly record struct Result(State State, bool AxisChangedMaterially);

    internal static Result Update(State previous, MapPoint observed, float now)
    {
        // A non-finite or zero-length bearing carries no direction and must never
        // rotate the axis or reset the persistence clock.
        if (!TryNormalize(observed, out var observedAxis) || !float.IsFinite(now))
            return new Result(previous, false);

        // First acquisition: adopt the observed bearing as the stable axis.
        if (!TryNormalize(previous.Axis, out var stableAxis))
            return new Result(new State(observedAxis, default, 0f), true);

        var angle = AngleDegrees(stableAxis, observedAxis);

        if (angle <= SmoothDriftDegrees)
        {
            // Same threat wandering: follow smoothly, drop any pending rotation, and
            // report no material change so the cached posture evaluation survives.
            var blended = BlendTowards(stableAxis, observedAxis, SmoothDriftBlend);
            return new Result(new State(blended, default, 0f), false);
        }

        if (angle >= ImmediateDivergenceDegrees)
            return new Result(new State(observedAxis, default, 0f), true);

        // Intermediate divergence: rotate only once the same new bearing has persisted.
        if (TryNormalize(previous.PendingAxis, out var pendingAxis) &&
            AngleDegrees(pendingAxis, observedAxis) <= SmoothDriftDegrees)
        {
            if (now - previous.PendingSince >= SustainedRotationSeconds)
                return new Result(new State(observedAxis, default, 0f), true);

            return new Result(
                new State(stableAxis, previous.PendingAxis, previous.PendingSince),
                false);
        }

        // A new pending bearing restarts the persistence clock.
        return new Result(new State(stableAxis, observedAxis, now), false);
    }

    private static bool TryNormalize(MapPoint value, out MapPoint unit)
    {
        unit = default;
        if (!value.IsFinite)
            return false;

        var magnitude = MathF.Sqrt(value.X * value.X + value.Z * value.Z);
        if (!float.IsFinite(magnitude) || magnitude < 1e-4f)
            return false;

        unit = new MapPoint(value.X / magnitude, value.Z / magnitude);
        return true;
    }

    private static MapPoint BlendTowards(MapPoint from, MapPoint to, float t)
    {
        var x = from.X + (to.X - from.X) * t;
        var z = from.Z + (to.Z - from.Z) * t;
        return TryNormalize(new MapPoint(x, z), out var unit) ? unit : to;
    }

    private static float AngleDegrees(MapPoint a, MapPoint b)
    {
        var dot = Math.Clamp(a.X * b.X + a.Z * b.Z, -1f, 1f);
        return MathF.Acos(dot) * (180f / MathF.PI);
    }
}

/// <summary>
/// Pure hysteresis for a cover *downgrade* - dropping to prone - that comes from a
/// cover re-evaluation rather than from suppression or pinning. A protective-&gt;not
/// protective evaluation flip must not instantly drop a defender from an owned crouch
/// to prone below the parapet, blind; the flip has to persist first. Suppression and
/// pinning keep their instant reaction and never reach this gate.
/// </summary>
internal static class CoverPostureDowngradeCore
{
    internal const float MinimumDowngradeHoldSeconds = 2f;

    internal static bool IsDowngrade(TacticalStance current, TacticalStance proposed)
        => proposed == TacticalStance.Prone && current != TacticalStance.Prone;

    // True => accept the proposed prone posture. A downgrade is accepted only once the
    // flip has persisted; any non-downgrade proposal is never gated here.
    internal static bool ShouldAccept(
        TacticalStance current,
        TacticalStance proposed,
        float firstProposedAt,
        float now)
    {
        if (!IsDowngrade(current, proposed))
            return true;

        // Invalid or not-yet-established timing keeps the safer, still-firing pose.
        if (!float.IsFinite(firstProposedAt) || !float.IsFinite(now) ||
            now < firstProposedAt)
        {
            return false;
        }

        return now - firstProposedAt >= MinimumDowngradeHoldSeconds;
    }
}

/// <summary>
/// Pure decision that keeps a soldier in the pose an authored fighting position was
/// built for when the penetration sampler cannot classify the barrier there. A trench
/// or window slot the cover search accepted must keep its authored crouch/stand at the
/// parapet instead of the prone fallback the ballistic evaluation returns for
/// unclassifiable geometry. A confidently-measured, penetrable barrier is a genuine
/// "this does not protect you" and still yields prone.
/// </summary>
internal static class AuthoredPoseFallbackCore
{
    internal static CoverPostureChoice ResolvePose(
        CoverPostureChoice ballisticChoice,
        bool ballisticProtectionFound,
        bool classificationSucceeded,
        bool onAuthoredCover,
        CoverPostureChoice authoredPose)
        => UseAuthoredPose(ballisticProtectionFound, classificationSucceeded, onAuthoredCover)
            ? authoredPose
            : ballisticChoice;

    internal static bool ResolveProtective(
        bool ballisticProtective,
        bool classificationSucceeded,
        bool onAuthoredCover)
        => ballisticProtective ||
           UseAuthoredPose(ballisticProtective, classificationSucceeded, onAuthoredCover);

    private static bool UseAuthoredPose(
        bool ballisticProtectionFound,
        bool classificationSucceeded,
        bool onAuthoredCover)
        // The authored pose is trusted only when this is authored cover the search
        // accepted AND no classifiable barrier was measured to judge it. A measured,
        // penetrable barrier is a real negative result and keeps the prone fallback.
        => onAuthoredCover && !ballisticProtectionFound && !classificationSucceeded;
}

/// <summary>
/// Pure, Unity-free rule that decides whether an attacker may peel off to a cover
/// candidate without abandoning the assault. It replaces the old strict "must be
/// closer to the waypoint" test, which combined with a tiny search radius let
/// attackers use only a small forward half-moon of cover and never a flanking
/// position or an off-axis doorway.
/// </summary>
internal static class AttackCoverCorridorCore
{
    // A candidate may sit slightly behind the soldier's current progress toward the
    // attack waypoint, but a real retreat is never cover worth taking on an attack.
    internal const float MaximumBacktrackMeters = 8f;

    // A candidate essentially on top of the soldier is the soldier's own footprint;
    // it neither worsens progress nor has a meaningful bearing.
    internal const float NearSoldierAcceptRadiusMeters = 2f;

    internal static bool Accepts(MapPoint soldier, MapPoint candidate, MapPoint waypoint)
    {
        if (!soldier.IsFinite || !candidate.IsFinite || !waypoint.IsFinite)
            return false;

        var toCandidateX = candidate.X - soldier.X;
        var toCandidateZ = candidate.Z - soldier.Z;
        var candidateStepSqr = toCandidateX * toCandidateX + toCandidateZ * toCandidateZ;
        if (candidateStepSqr <=
            NearSoldierAcceptRadiusMeters * NearSoldierAcceptRadiusMeters)
        {
            return true;
        }

        var soldierToWaypoint = Distance(soldier, waypoint);
        var candidateToWaypoint = Distance(candidate, waypoint);
        if (!float.IsFinite(soldierToWaypoint) || !float.IsFinite(candidateToWaypoint))
            return false;

        // A nearby doorway may be sideways or behind the soldier. Requiring a
        // forward bearing in addition to this distance bound made the advertised
        // backtrack allowance ineffective at exactly those urban entrances.
        return candidateToWaypoint <= soldierToWaypoint + MaximumBacktrackMeters;
    }

    private static float Distance(MapPoint from, MapPoint to)
    {
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}

/// <summary>
/// Pure backoff schedule for a soldier that repeatedly fails to find reachable,
/// protective cover. A single miss keeps the normal decision cadence; sustained
/// misses stretch the next assessment so the soldier stays down and fights instead
/// of oscillating search -> fail -> move -> halt -> forced release -> search (the
/// "ant milling" a soldier with no reachable cover otherwise produces).
/// </summary>
internal static class CoverSearchBackoffCore
{
    internal const float SecondFailureDelaySeconds = 20f;
    internal const float SustainedFailureDelaySeconds = 30f;
    private const float DefaultBaseIntervalSeconds = 12f;

    internal static float NextDecisionDelaySeconds(
        float baseIntervalSeconds,
        int consecutiveFailures)
    {
        if (!float.IsFinite(baseIntervalSeconds) || baseIntervalSeconds <= 0f)
            return DefaultBaseIntervalSeconds;

        if (consecutiveFailures <= 1)
            return baseIntervalSeconds;
        if (consecutiveFailures == 2)
            return Math.Max(baseIntervalSeconds, SecondFailureDelaySeconds);
        return Math.Max(baseIntervalSeconds, SustainedFailureDelaySeconds);
    }
}

internal static class PlayerHoldPositionCore
{
    internal const float CenterChangeToleranceMeters = 1f;
    internal const float RadiusChangeToleranceMeters = 0.5f;

    internal static bool OrderChanged(
        bool positionOwned,
        MapPoint previousCenter,
        float previousRadius,
        MapPoint currentCenter,
        float currentRadius)
    {
        if (!positionOwned || !IsFinite(previousCenter) || !IsFinite(currentCenter) ||
            !IsFinite(previousRadius) || !IsFinite(currentRadius) ||
            previousRadius < 0f || currentRadius < 0f)
        {
            return true;
        }

        var dx = previousCenter.X - currentCenter.X;
        var dz = previousCenter.Z - currentCenter.Z;
        return dx * dx + dz * dz >
                   CenterChangeToleranceMeters * CenterChangeToleranceMeters ||
               MathF.Abs(previousRadius - currentRadius) >
                   RadiusChangeToleranceMeters;
    }

    internal static bool ShouldSeekCover(
        bool insideOrderedArea,
        bool relocating,
        bool hasStableAnchor,
        bool decisionDue)
        => insideOrderedArea && !relocating && !hasStableAnchor && decisionDue;

    private static bool IsFinite(MapPoint point)
        => IsFinite(point.X) && IsFinite(point.Z);

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);
}
