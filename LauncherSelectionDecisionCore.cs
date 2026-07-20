namespace ER2RealismOverhaul;

internal static class LauncherSelectionDecisionCore
{
    internal static bool ShouldDeferSelection(
        bool isLowVelocityLauncher,
        bool hasTargetSnapshot,
        float horizontalDistanceMeters,
        float maximumEngagementDistanceMeters)
    {
        if (!isLowVelocityLauncher || !hasTargetSnapshot ||
            !float.IsFinite(horizontalDistanceMeters) ||
            !float.IsFinite(maximumEngagementDistanceMeters) ||
            maximumEngagementDistanceMeters <= 0f)
        {
            return false;
        }

        return horizontalDistanceMeters > maximumEngagementDistanceMeters;
    }
}
