namespace ER2RealismOverhaul;

internal enum TankEngagementState
{
    Follow,
    Hold,
    Reverse
}

internal readonly record struct TankEngagementInput(
    bool HasArmoredTarget,
    float Distance,
    float TimeSinceTargetVisible,
    float LifeFraction,
    bool HullFacesThreat,
    bool ReverseAvailable,
    bool RearBlocked,
    bool ReverseTimerElapsed,
    float StandoffDistance,
    float ReverseDistance,
    float DamagedLifeFraction);

/// <summary>
/// Pure engagement state machine for a tank in direct armored contact. Replaces
/// the previous threshold spaghetti (a one-frame brake fighting per-frame path
/// resumption) with a persistent state and hysteresis, so distance dithering a
/// few meters around a boundary can never flip the state every evaluation.
/// </summary>
internal static class TankEngagementDecisionCore
{
    // Every exit threshold sits strictly outside its matching entry threshold.
    internal const float HoldReleaseDistanceMultiplier = 1.15f;
    internal const float HoldReleaseGraceSeconds = 3f;
    internal const float ReverseReleaseDistanceMultiplier = 1.25f;

    internal static TankEngagementState NextState(
        TankEngagementState current,
        TankEngagementInput input)
    {
        if (!IsFinite(input.Distance) || input.Distance < 0f)
            return TankEngagementState.Follow;

        switch (current)
        {
            case TankEngagementState.Hold:
                if (!input.HasArmoredTarget ||
                    input.Distance > input.StandoffDistance * HoldReleaseDistanceMultiplier ||
                    input.TimeSinceTargetVisible > HoldReleaseGraceSeconds)
                {
                    return TankEngagementState.Follow;
                }

                if ((input.Distance <= input.ReverseDistance ||
                     input.LifeFraction <= input.DamagedLifeFraction) &&
                    input.ReverseAvailable && !input.RearBlocked)
                {
                    return TankEngagementState.Reverse;
                }

                return TankEngagementState.Hold;

            case TankEngagementState.Reverse:
                // Target gone (killed, or unseen past the grace window): stop
                // retreating from nothing and resume native pathing. Mirrors the
                // Hold release so a Reverse state can never outlive its target and
                // strand the tank backing up forever.
                if (!input.HasArmoredTarget ||
                    input.TimeSinceTargetVisible > HoldReleaseGraceSeconds)
                {
                    return TankEngagementState.Follow;
                }

                // A blind reverse must never loop forever: losing the ability to
                // reverse, or discovering the rear is blocked, ends the retreat and
                // falls back to holding and fighting instead.
                if (input.RearBlocked || !input.ReverseAvailable ||
                    (input.ReverseTimerElapsed &&
                     input.Distance >= input.ReverseDistance * ReverseReleaseDistanceMultiplier))
                {
                    return TankEngagementState.Hold;
                }

                return TankEngagementState.Reverse;

            default:
                return input.HasArmoredTarget && input.Distance <= input.StandoffDistance
                    ? TankEngagementState.Hold
                    : TankEngagementState.Follow;
        }
    }

    internal static bool SuppressPathFollowing(TankEngagementState state)
        => state != TankEngagementState.Follow;

    internal static bool ShouldBrake(TankEngagementState state)
        => state == TankEngagementState.Hold;

    /// <summary>
    /// Hull rotation toward the threat is permitted only in Hold, only outside the
    /// close reverse band, and only while not already facing it. Rotating the hull
    /// through the enemy's firing line at close range, or while reversing, remains
    /// forbidden.
    /// </summary>
    internal static bool AllowHullRotation(
        TankEngagementState state,
        float distance,
        float reverseDistance,
        bool hullFacesThreat)
        => state == TankEngagementState.Hold && distance > reverseDistance && !hullFacesThreat;

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>
/// Pure predicates for the vehicle stall watchdog. A tank en route to a commander
/// destination that makes no real progress is either nudged with one straight
/// reverse or, after repeated failures, released so the commander can reassign it.
/// </summary>
internal static class TankStallWatchdogCore
{
    internal const float SampleIntervalSeconds = 5f;
    internal const float StallWindowSeconds = 15f;
    internal const float StallDisplacementMeters = 4f;
    internal const float NetProgressMeters = 10f;
    internal const int MaxRecoveryAttempts = 3;

    internal static bool IsStalled(float displacementSinceWindowStart, float secondsSinceWindowStart)
        => secondsSinceWindowStart >= StallWindowSeconds &&
           displacementSinceWindowStart < StallDisplacementMeters;

    internal static bool HasNetProgress(float displacementSinceLastRecovery)
        => displacementSinceLastRecovery >= NetProgressMeters;

    internal static bool ShouldGiveUp(int consecutiveFailedRecoveries)
        => consecutiveFailedRecoveries >= MaxRecoveryAttempts;
}
