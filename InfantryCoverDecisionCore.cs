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
    bool PreferProtectionOverFiringLine = false);

internal readonly record struct CoverPostureInput(
    int ProtectedSamples,
    int TotalSamples,
    bool CanFire,
    float BallisticProtectionFraction = float.NaN);

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

    internal static float Score(CoverSelectionMode mode, CoverScoreInput input)
    {
        var distanceMeters = MathF.Sqrt(Math.Max(0f, input.DistanceSqr));
        var protectionFraction = float.IsNaN(input.PrimaryProtectionFraction) ||
                                 float.IsInfinity(input.PrimaryProtectionFraction)
            ? 0f
            : Math.Clamp(input.PrimaryProtectionFraction, 0f, 1f);
        var protectionPenalty = (1f - protectionFraction) *
                                (input.PreferProtectionOverFiringLine ? 1600f : 700f);
        var firePenalty = input.AssignedPoseCanFire
            ? 0f
            : input.StandingCanFire
                ? mode == CoverSelectionMode.Urgent
                    ? 30f
                    : input.PreferProtectionOverFiringLine ? 30f : 120f
                : mode == CoverSelectionMode.Urgent
                    ? input.PreferProtectionOverFiringLine ? 50f : 80f
                    : input.PreferProtectionOverFiringLine ? 120f : 500f;

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
                   firePenalty;
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
                   firePenalty;
        }

        return (input.PreferProtectionOverFiringLine
                   ? distanceMeters * 2f
                   : input.DistanceSqr * 0.65f) +
               input.StandingPosePenalty +
               input.UnprotectedSecondaryThreats * 350f +
               input.ExposedRouteMeters * 35f +
               protectionPenalty +
               firePenalty;
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
