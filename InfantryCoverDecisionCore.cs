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
    bool UrgentDecisionDue);

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
    // radius of this candidate (plan 016). A light tie-breaking term only - it must
    // never outweigh a genuine protection difference, so its weight in Score() stays
    // well below the smallest meaningful protectionPenalty swing.
    int NearbyReservationCount = 0);

internal readonly record struct CoverPostureInput(
    int ProtectedSamples,
    int TotalSamples,
    bool CanFire,
    float BallisticProtectionFraction = float.NaN,
    bool HasClassifiedObstruction = false);

internal static class InfantryCoverDecisionCore
{
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

            // A soldier may rise behind genuinely protective low cover to clear
            // the weapon. A narrow object that does not protect a crouched body
            // cannot justify standing in the open merely because the muzzle clears.
            if (standing.CanFire)
                return CoverPostureChoice.Standing;

            return HasMeaningfulProtection(prone) &&
                   ProtectionFraction(prone) > ProtectionFraction(crouched)
                ? CoverPostureChoice.Prone
                : CoverPostureChoice.Crouched;
        }

        if (HasMeaningfulProtection(prone))
            return CoverPostureChoice.Prone;

        if (HasMeaningfulProtection(standing) && standing.CanFire)
            return CoverPostureChoice.Standing;

        // Nothing is genuine cover. Minimize the exposed silhouette instead of
        // treating a tree-sized obstruction as permission to kneel in the open.
        return CoverPostureChoice.Prone;
    }

    internal static CoverPostureInput SelectCoverPostureInput(
        CoverPostureChoice choice,
        CoverPostureInput standing,
        CoverPostureInput crouched,
        CoverPostureInput prone)
        => choice switch
        {
            CoverPostureChoice.Standing => standing,
            CoverPostureChoice.Crouched => crouched,
            _ => prone
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

    internal static CoverNeedDecision EvaluateNeed(CoverNeedInput input)
    {
        if (input.HasUsableCover && !input.MayAdvanceFromCover)
            return Hold("current cover still protects the soldier");

        if (input.Suppressed)
            return Hold("suppression makes leaving more dangerous than staying");

        if (input.CloseThreat)
            return Hold("the close threat must be engaged before moving");

        var urgent = input.CoverCompromised || input.UnderDirectFire;
        if (input.AttackAdvanceBlocked && !urgent)
            return Wait(CoverSelectionMode.Normal, "advance lacks established covering fire");

        if (urgent)
        {
            return input.UrgentDecisionDue
                ? Search(CoverSelectionMode.Urgent, "exposed position is under direct threat")
                : Wait(CoverSelectionMode.Urgent, "urgent cover choice is already being acted on");
        }

        return input.NormalDecisionDue
            ? Search(CoverSelectionMode.Normal, "deliberate cover assessment is due")
            : Wait(CoverSelectionMode.Normal, "waiting for the next deliberate assessment");
    }

    internal static bool IsRouteAcceptable(CoverSelectionMode mode, CoverScoreInput input)
    {
        // A destination that does not protect against the primary threat is not
        // cover. Urgency permits a short exposed dash because remaining exposed
        // can be worse. The same rule applies when an exposed defender is taking
        // an initial fortified position: an uncovered route must not condemn him
        // to remain indefinitely at an uncovered arrival point.
        if (!input.PrimaryThreatProtected)
            return false;

        return mode != CoverSelectionMode.Normal || input.ExposedRouteFraction <= 0.34f;
    }

    internal static bool ShouldUseAuthoredFallback(
        bool hasMeasuredProtectiveSelection,
        bool hasValidAuthoredCandidate)
        => !hasMeasuredProtectiveSelection && hasValidAuthoredCandidate;

    internal static float Score(CoverSelectionMode mode, CoverScoreInput input)
    {
        var distanceMeters = MathF.Sqrt(Math.Max(0f, input.DistanceSqr));
        var protectionFraction = float.IsNaN(input.PrimaryProtectionFraction) ||
                                 float.IsInfinity(input.PrimaryProtectionFraction)
            ? 0f
            : Math.Clamp(input.PrimaryProtectionFraction, 0f, 1f);
        var protectionPenalty = (1f - protectionFraction) *
                                (input.PreferProtectionOverFiringLine ? 1600f : 700f);
        // Crowding is a tie-breaker (plan 016), not a survival factor: 25 per
        // squadmate already reserved nearby is small next to any real protection
        // difference (a 0.05 protection swing alone moves protectionPenalty by
        // 35-80), so it nudges the 2nd-9th soldier off an equally good slot without
        // ever pulling anyone off genuinely better cover.
        var crowdingPenalty = Math.Max(0, input.NearbyReservationCount) * 25f;
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
                    // A slot from which no pose can fire is a blind hole. Defenders
                    // used to pick these systematically (penalty 120) and never shot
                    // back; it now costs the same as an attacker's Normal no-fire slot
                    // (500), so a protected slot with an actual firing lane wins.
                    : 500f;

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

        return (input.PreferProtectionOverFiringLine
                   ? distanceMeters * 2f
                   : input.DistanceSqr * 0.65f) +
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

    // Candidates that are not strict progress must still lie inside a forward wedge
    // so a soldier does not peel sideways or rearward into off-axis cover that
    // stalls the assault. Measured as the angle between the soldier->candidate and
    // soldier->waypoint bearings.
    internal const float ForwardCorridorHalfAngleDegrees = 40f;

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
        if (float.IsNaN(soldierToWaypoint) || float.IsNaN(candidateToWaypoint))
            return false;

        // (a) Bounded backtracking. A large increase in distance to the waypoint is
        // a retreat and is rejected before any bearing consideration.
        if (candidateToWaypoint > soldierToWaypoint + MaximumBacktrackMeters)
            return false;

        // Strict forward progress is always acceptable: closing on the waypoint is
        // exactly the attack's intent, regardless of lateral offset.
        if (candidateToWaypoint < soldierToWaypoint)
            return true;

        // (b) A non-progress candidate must fall inside the forward corridor.
        var toWaypointX = waypoint.X - soldier.X;
        var toWaypointZ = waypoint.Z - soldier.Z;
        var waypointStepSqr = toWaypointX * toWaypointX + toWaypointZ * toWaypointZ;
        if (waypointStepSqr <= 0f)
            return true;

        var dot = toCandidateX * toWaypointX + toCandidateZ * toWaypointZ;
        if (dot <= 0f)
            return false;

        var cosLimit = MathF.Cos(ForwardCorridorHalfAngleDegrees * MathF.PI / 180f);
        var magnitudeProduct = MathF.Sqrt(candidateStepSqr * waypointStepSqr);
        // cos(angle) >= cos(limit) keeps the candidate within the half-angle. The
        // comparison is scaled by the magnitude product to avoid an extra divide,
        // and both sides are non-negative here (dot > 0).
        return dot >= cosLimit * magnitudeProduct;
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

/// <summary>
/// Bounds the tank-fear "no reachable cover" wait. A soldier who conclusively cannot
/// reach tank-masked cover must resume his orders instead of freezing prone in the
/// open indefinitely. The streak counts only conclusive urgent-search misses; a gap
/// wider than one search cycle between misses means the soldier stopped open-field
/// waiting (reached cover, the tank left, the order changed), so the next miss starts
/// a fresh streak. That self-reset re-arms the hide automatically on a materially new
/// situation, and a successful cover commit re-arms it explicitly — no external reset
/// plumbing, so the whole decision stays pure and testable.
/// </summary>
internal static class TankCoverWaitCore
{
    // Two conclusive urgent-search misses (~one full urgent reassessment cycle of
    // prone waiting) is enough to establish that no tank-masked cover is reachable.
    internal const int MaxConsecutiveFailuresBeforeResume = 2;

    // A gap wider than this between misses means the soldier was not continuously
    // open-field waiting; the next miss starts a fresh streak. Must exceed the urgent
    // reassessment interval so consecutive real searches keep accumulating.
    internal const float FailureStreakResetSeconds = 6f;

    internal static int RecordFailure(int previousFailures, float lastFailureAt, float now)
    {
        if (previousFailures <= 0 || lastFailureAt <= 0f ||
            !float.IsFinite(lastFailureAt) || now - lastFailureAt > FailureStreakResetSeconds)
        {
            return 1;
        }

        return previousFailures + 1;
    }

    internal static bool ShouldResumeOrders(int consecutiveFailures)
        => consecutiveFailures >= MaxConsecutiveFailuresBeforeResume;
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
