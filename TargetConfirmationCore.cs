namespace ER2RealismOverhaul;

/// <summary>
/// Governs how a target candidate's observed-time bank accrues across native
/// visibility samples. A confirmation must reflect near-continuous observation,
/// not the sum of isolated glimpses separated by long unwatched gaps.
/// </summary>
internal static class TargetConfirmationCore
{
    // A positive sample more than this long after the previous one means the
    // target was not being watched in between; the streak restarts.
    internal const float ContinuityBreakSeconds = 5f;

    // A candidate seen this recently survives a single stale negative native
    // scan; a fresh positive raycast (e.g. the fast close-range tick) beats one
    // staggered negative sample instead of losing its whole observation streak.
    internal const float RecentPositiveObservationGraceSeconds = 0.5f;

    // Native visibility samples arrive roughly once a second or faster; crediting
    // more than this per sample would count unobserved time as observation.
    internal const float MaxSampleCreditSeconds = 1.5f;

    internal static bool CanReuseConfirmedTarget(
        bool hasConfirmedTarget,
        bool isSameTarget,
        bool requiresReacquisition,
        bool hasIndependentVisualProof)
        => hasConfirmedTarget &&
           isSameTarget &&
           (!requiresReacquisition || hasIndependentVisualProof);

    internal static bool HasRecentIndependentVisualProof(
        bool isSameTarget,
        float proofAt,
        float now)
        => isSameTarget &&
           proofAt >= 0f &&
           now >= proofAt &&
           now - proofAt <= RecentPositiveObservationGraceSeconds;

    internal static float AccrueObservation(float observedSeconds, float lastSeenAt, float now)
    {
        var gap = Math.Max(0f, now - lastSeenAt);
        if (gap > ContinuityBreakSeconds)
            return 0f;
        return observedSeconds + Math.Min(gap, MaxSampleCreditSeconds);
    }
}

/// <summary>
/// Defines the deliberately narrow exception to normal target-priority switching:
/// a soldier finishes an already-confirmed close infantry engagement while that
/// target remains alive and visible.
/// </summary>
internal static class CloseTargetCommitmentCore
{
    internal static bool ShouldRetain(
        bool closeQuartersEnabled,
        bool hasConfirmedTarget,
        bool isSameTarget,
        bool isLivingInfantry,
        bool isVisible,
        float distance,
        float closeRange)
        => closeQuartersEnabled &&
           hasConfirmedTarget &&
           isSameTarget &&
           isLivingInfantry &&
           isVisible &&
           distance <= Math.Max(0f, closeRange);
}

/// <summary>
/// Keeps the coarse direction of direct hostile fire longer than a precise visual
/// target. This is danger memory, not permission to aim at or fire on an unseen unit.
/// </summary>
internal static class DirectThreatMemoryCore
{
    internal const float MinimumRetentionSeconds = 15f;
    internal const float AdditionalRetentionSeconds = 5f;

    internal static float RetentionSeconds(float configuredTargetMemorySeconds)
        => Math.Max(
            MinimumRetentionSeconds,
            Math.Max(0f, configuredTargetMemorySeconds) + AdditionalRetentionSeconds);
}
