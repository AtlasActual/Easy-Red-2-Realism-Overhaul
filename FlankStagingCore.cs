namespace ER2RealismOverhaul;

/// <summary>
/// Geometry and release rules for a flanking squad's staging point.
///
/// The native Squad.AttackFromSide sends the squad straight to the position it is
/// given and ignores its direction argument (measured in game: every flank order
/// produced a waypoint exactly on the objective centre). A flank therefore has to be
/// a real detour: the squad is first sent to a point off to one side and short of the
/// objective, and only then ordered onto the objective from that side.
/// </summary>
internal static class FlankStagingCore
{
    internal const int DefaultWidthPercent = 40;
    internal const float MinimumLateralMeters = 50f;
    internal const float MaximumLateralMeters = 150f;
    internal const float ArrivalMeters = 25f;

    // A flanker out of contact that gets no closer to its staging point for this
    // long is treated as stuck (unreachable point, blocked path). Time in contact
    // never counts: a flank that is fighting keeps its detour.
    internal const float StuckSeconds = 30f;
    internal const float ProgressMeters = 5f;

    // Closer than this beyond the objective edge, a detour would only waste time.
    internal const float MinimumDetourDistanceMeters = 60f;

    /// <summary>
    /// The staging point for a squad flanking an objective, or false when the squad
    /// is too close for a detour or flanking is turned off.
    /// </summary>
    /// <param name="side">-1 for left, +1 for right, relative to the squad's heading.</param>
    /// <param name="widthPercent">Sideways offset as a share of the squad's distance.</param>
    internal static bool TryComputeStagingPoint(
        MapPoint squad,
        MapPoint objective,
        float objectiveRadius,
        int side,
        int widthPercent,
        out MapPoint staging)
    {
        staging = default;
        if (side == 0 || widthPercent <= 0 || !squad.IsFinite || !objective.IsFinite ||
            !float.IsFinite(objectiveRadius))
        {
            return false;
        }

        var dx = objective.X - squad.X;
        var dz = objective.Z - squad.Z;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        var radius = MathF.Max(0f, objectiveRadius);
        if (distance < radius + MinimumDetourDistanceMeters)
            return false;

        var forwardX = dx / distance;
        var forwardZ = dz / distance;
        // Right of the heading: forward rotated -90 degrees about the vertical axis.
        var rightX = forwardZ;
        var rightZ = -forwardX;

        var lateral = Math.Clamp(distance * widthPercent / 100f,
            MinimumLateralMeters, MaximumLateralMeters);
        // Stop short of the objective so the final approach comes in from the side,
        // never from behind the squad's own line or past the objective.
        var setback = MathF.Min(MathF.Max(radius, distance * 0.35f), distance - 1f);

        staging = new MapPoint(
            objective.X - forwardX * setback + rightX * side * lateral,
            objective.Z - forwardZ * setback + rightZ * side * lateral);
        return staging.IsFinite;
    }

    /// <summary>
    /// Advances the stuck clock for one plan pass. Contact, or getting at least
    /// <see cref="ProgressMeters"/> closer than ever before, restarts it.
    /// </summary>
    internal static (float BestDistance, float LastProgressAt) TrackProgress(
        float bestDistance, float lastProgressAt, float distanceToStaging, bool engaged, float now)
    {
        if (!float.IsFinite(distanceToStaging))
            return (bestDistance, lastProgressAt);

        if (distanceToStaging <= bestDistance - ProgressMeters)
            return (distanceToStaging, now);

        return engaged ? (bestDistance, now) : (bestDistance, lastProgressAt);
    }

    internal enum Release
    {
        None,
        Arrived,
        PastStagingPoint,
        Stuck
    }

    /// <summary>Whether the squad should leave its staging leg for the objective, and why.</summary>
    internal static Release ShouldRelease(
        MapPoint squad,
        MapPoint staging,
        MapPoint objective,
        float secondsWithoutProgress)
    {
        if (DistanceSquared(squad, staging) <= ArrivalMeters * ArrivalMeters)
            return Release.Arrived;

        // Already nearer the objective than the staging point is: going back out
        // would be a retreat, so attack from where the squad is.
        if (DistanceSquared(squad, objective) < DistanceSquared(staging, objective))
            return Release.PastStagingPoint;

        return !float.IsFinite(secondsWithoutProgress) || secondsWithoutProgress >= StuckSeconds
            ? Release.Stuck
            : Release.None;
    }

    /// <summary>
    /// Signed sideways distance of a point from the line start -> objective,
    /// positive to the right of that heading. Used to report how far off-axis a
    /// flank actually got before its final attack.
    /// </summary>
    internal static float LateralOffset(MapPoint start, MapPoint objective, MapPoint point)
    {
        var dx = objective.X - start.X;
        var dz = objective.Z - start.Z;
        var length = MathF.Sqrt(dx * dx + dz * dz);
        if (length < 0.01f)
            return 0f;

        return ((point.X - start.X) * dz - (point.Z - start.Z) * dx) / length;
    }

    private static float DistanceSquared(MapPoint a, MapPoint b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }
}
