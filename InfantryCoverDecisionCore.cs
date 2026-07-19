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
    Urgent
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
    float ExposedRouteFraction);

internal readonly record struct CoverPostureInput(
    int ProtectedSamples,
    int TotalSamples,
    bool CanFire);

internal static class InfantryCoverDecisionCore
{
    internal static bool HasMeaningfulProtection(CoverPostureInput posture)
    {
        if (posture.TotalSamples <= 0 || posture.ProtectedSamples < 0 ||
            posture.ProtectedSamples > posture.TotalSamples)
        {
            return false;
        }

        // Head, torso, and both shoulders are sampled. At least three of those
        // four regions must be masked before a position counts as real cover.
        return posture.ProtectedSamples * 4 >= posture.TotalSamples * 3;
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
                   prone.ProtectedSamples > crouched.ProtectedSamples
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
        // can be worse; a deliberate move requires most of the route to be masked.
        if (!input.PrimaryThreatProtected)
            return false;

        return mode == CoverSelectionMode.Urgent || input.ExposedRouteFraction <= 0.34f;
    }

    internal static float Score(CoverSelectionMode mode, CoverScoreInput input)
    {
        var firePenalty = input.AssignedPoseCanFire
            ? 0f
            : input.StandingCanFire
                ? mode == CoverSelectionMode.Urgent ? 30f : 120f
                : mode == CoverSelectionMode.Urgent ? 80f : 1200f;

        if (mode == CoverSelectionMode.Urgent)
        {
            return input.DistanceSqr +
                   input.StandingPosePenalty * 0.4f +
                   input.UnprotectedSecondaryThreats * 200f +
                   input.ExposedRouteMeters * 18f +
                   firePenalty;
        }

        return input.DistanceSqr * 0.65f +
               input.StandingPosePenalty +
               input.UnprotectedSecondaryThreats * 350f +
               input.ExposedRouteMeters * 35f +
               firePenalty;
    }

    private static CoverNeedDecision Hold(string reason)
        => new(InfantryCoverState.Holding, CoverSelectionMode.Normal, false, reason);

    private static CoverNeedDecision Wait(CoverSelectionMode mode, string reason)
        => new(InfantryCoverState.WaitingForSafeMove, mode, false, reason);

    private static CoverNeedDecision Search(CoverSelectionMode mode, string reason)
        => new(InfantryCoverState.WaitingForSafeMove, mode, true, reason);
}
