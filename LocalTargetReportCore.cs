namespace ER2RealismOverhaul;

/// <summary>
/// Deterministic policy for local contact callouts. Runtime code supplies squared
/// distances so the hot path never needs a square root merely to test a radius or
/// choose which of two reported contacts deserves attention.
/// </summary>
internal static class LocalTargetReportCore
{
    internal static bool IsInsideSharingRadius(float distanceSquared, float radiusMeters)
        => radiusMeters > 0f && distanceSquared <= radiusMeters * radiusMeters;

    internal static bool ShouldAcceptReport(
        bool currentReportActive,
        bool isSameTarget,
        float currentTargetDistanceSquared,
        float incomingTargetDistanceSquared)
        => !currentReportActive ||
           isSameTarget ||
           incomingTargetDistanceSquared < currentTargetDistanceSquared;
}
