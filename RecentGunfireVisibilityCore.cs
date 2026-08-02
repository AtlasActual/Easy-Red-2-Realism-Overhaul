namespace ER2RealismOverhaul;

internal static class RecentGunfireVisibilityCore
{
    internal static bool ShouldCheckExactShooter(
        bool cueActive,
        int cueShooterId,
        int candidateShooterId,
        bool candidateAlive,
        bool candidateHostile,
        bool insideFieldOfView)
        => cueActive &&
           cueShooterId != 0 &&
           cueShooterId == candidateShooterId &&
           candidateAlive &&
           candidateHostile &&
           insideFieldOfView;

    internal static bool HasVisualContact(
        bool nativeVisible,
        bool triggerIgnoringRayHitExactShooter)
        => nativeVisible || triggerIgnoringRayHitExactShooter;
}
