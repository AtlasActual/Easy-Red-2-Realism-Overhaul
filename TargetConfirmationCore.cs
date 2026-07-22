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

    // Native visibility samples arrive roughly once a second or faster; crediting
    // more than this per sample would count unobserved time as observation.
    internal const float MaxSampleCreditSeconds = 1.5f;

    internal static float AccrueObservation(float observedSeconds, float lastSeenAt, float now)
    {
        var gap = Math.Max(0f, now - lastSeenAt);
        if (gap > ContinuityBreakSeconds)
            return 0f;
        return observedSeconds + Math.Min(gap, MaxSampleCreditSeconds);
    }
}
