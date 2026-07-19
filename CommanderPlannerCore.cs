using System;
using System.Collections.Generic;
using System.Linq;

namespace ER2RealismOverhaul;

internal enum CommanderRole
{
    Assault,
    Flank,
    SupportByFire,
    Reserve,
    AntiTank
}

internal enum CommanderAction
{
    Hold,
    Prepare,
    Attack
}

internal enum CommanderAntiTankAction
{
    Ambush,
    Hunt
}

internal enum CommanderContactType
{
    Infantry,
    GroundVehicle
}

internal readonly record struct MapPoint(float X, float Z)
{
    internal bool IsFinite => float.IsFinite(X) && float.IsFinite(Z);
}

internal readonly record struct CommanderSquadSnapshot(
    int Id,
    MapPoint Position,
    float EffectiveStrength,
    float PeakStrength,
    float Suppression,
    bool Eligible,
    bool PlayerControlled,
    bool ScriptLocked);

internal readonly record struct CommanderReportSnapshot(
    int TargetId,
    MapPoint Position,
    CommanderContactType Type,
    float AgeSeconds,
    float Confidence,
    float EstimatedPower);

internal readonly record struct CommanderAntiTankTask(
    int SquadId,
    int TargetId,
    MapPoint TargetPosition,
    CommanderAntiTankAction Action);

internal readonly record struct CommanderFireMissionSolution(
    MapPoint Aim,
    int FriendliesAtReportedTarget,
    float ShiftMeters);

internal readonly record struct CommanderAxisCandidate(
    int Id,
    MapPoint StagingPoint,
    float BearingDegrees,
    float TerrainScore,
    float CongestionScore,
    float ExposureScore);

internal sealed record CommanderPlanInput(
    MapPoint ObjectivePosition,
    bool OffensiveOperation,
    bool SmokeRequired,
    bool SmokeReady,
    IReadOnlyList<CommanderSquadSnapshot>? Squads,
    IReadOnlyList<CommanderReportSnapshot>? Reports,
    IReadOnlyList<CommanderAxisCandidate>? Axes);

internal readonly record struct CommanderDirective(
    int SquadId,
    CommanderRole Role,
    CommanderAction Action,
    int? AxisId,
    MapPoint Destination);

internal readonly record struct CommanderPlanMetrics(
    int EligibleSquadCount,
    int ReserveSquadCount,
    int FreshReportCount,
    float TotalEffectiveStrength,
    float CommittedEffectiveStrength,
    float EnemyEstimatedPower,
    float StrengthRatio,
    float AverageSuppression,
    bool SmokeBlocked);

internal sealed record CommanderPlan(
    int? MainAxisId,
    int? FlankAxisId,
    CommanderPlanMetrics Metrics,
    bool AttackAuthorized,
    IReadOnlyList<CommanderDirective> Directives);

internal static class CommanderPlannerCore
{
    internal const float MinimumAttackRatio = 1.30f;
    internal const float MaximumAverageSuppression = 0.35f;
    internal const float MaximumReportAgeSeconds = 20f;
    internal const float MinimumReportConfidence = 0.25f;
    internal const float MinimumSquadStrengthFraction = 0.50f;
    internal const float MaximumOperationalSquadSuppression = 0.60f;
    internal const float ReserveFraction = 0.20f;
    internal const float MinimumAxisSeparationDegrees = 45f;
    internal const float MaximumAntiTankReportAgeSeconds = 12f;
    internal const float MinimumAntiTankReportConfidence = 0.35f;
    internal const float MaximumAntiTankResponseDistance = 300f;
    internal const float MinimumAntiTankHuntDistance = 75f;
    internal const float MaximumAntiTankHuntDistance = 130f;
    internal const float MaximumAntiTankHuntReportAgeSeconds = 6f;
    internal const float MinimumAntiTankHuntConfidence = 0.70f;
    internal const float MinimumAntiTankHuntStrengthFraction = 0.75f;
    internal const float MaximumAntiTankHuntSuppression = 0.35f;
    internal const float AntiTankIsolationRadius = 90f;

    private static readonly float[] FireMissionRetargetRadii = { 20f, 35f, 50f, 65f, 80f };
    private const int FireMissionRetargetDirections = 16;

    internal static CommanderPlan Plan(CommanderPlanInput? input)
    {
        if (input == null || !input.ObjectivePosition.IsFinite)
            return EmptyPlan();

        var squads = OperationalSquads(input.Squads);
        if (squads.Count == 0)
            return EmptyPlan();

        var axes = UsableAxes(input.Axes);
        var mainAxis = axes.Count > 0 ? axes[0] : (CommanderAxisCandidate?)null;
        var flankAxis = SelectFlankAxis(axes, mainAxis);
        var roles = AllocateRoles(squads, flankAxis);
        var reports = FreshReports(input.Reports);

        var totalStrength = SafeSum(squads.Select(squad => squad.EffectiveStrength));
        var committedSquads = squads.Where(squad => roles[squad.Id] != CommanderRole.Reserve).ToArray();
        var committedStrength = SafeSum(committedSquads.Select(squad => squad.EffectiveStrength));
        var enemyPower = SafeSum(reports.Select(report => report.EstimatedPower));
        var suppression = WeightedSuppression(committedSquads);
        var strengthRatio = SafeRatio(committedStrength, MathF.Max(1f, enemyPower));

        var hasUsableAttackAxis = committedSquads.Any(squad =>
            roles[squad.Id] == CommanderRole.Assault && mainAxis != null ||
            roles[squad.Id] == CommanderRole.Flank && flankAxis != null);
        var attackGateOpen = input.OffensiveOperation &&
                             hasUsableAttackAxis &&
                             strengthRatio >= MinimumAttackRatio &&
                             suppression <= MaximumAverageSuppression;
        var smokeBlocked = attackGateOpen && input.SmokeRequired && !input.SmokeReady;
        var attackAuthorized = attackGateOpen && !smokeBlocked;

        var directives = new List<CommanderDirective>(squads.Count);
        foreach (var squad in squads.OrderBy(squad => squad.Id))
        {
            var role = roles[squad.Id];
            var (axis, destination) = DirectiveDestination(squad, role, mainAxis, flankAxis);
            var hasRoleAxis = role switch
            {
                CommanderRole.Assault => mainAxis != null,
                CommanderRole.Flank => flankAxis != null,
                _ => false
            };
            var action = hasRoleAxis && attackAuthorized
                ? CommanderAction.Attack
                : hasRoleAxis && smokeBlocked
                    ? CommanderAction.Prepare
                    : CommanderAction.Hold;

            directives.Add(new CommanderDirective(squad.Id, role, action, axis?.Id, destination));
        }

        var metrics = new CommanderPlanMetrics(
            squads.Count,
            roles.Count(pair => pair.Value == CommanderRole.Reserve),
            reports.Count,
            totalStrength,
            committedStrength,
            enemyPower,
            strengthRatio,
            suppression,
            smokeBlocked);

        return new CommanderPlan(mainAxis?.Id, flankAxis?.Id, metrics, attackAuthorized, directives);
    }

    internal static CommanderAntiTankTask? SelectAntiTankTask(
        IReadOnlyList<CommanderSquadSnapshot>? squads,
        IReadOnlyCollection<int>? antiTankSquadIds,
        IReadOnlyList<CommanderReportSnapshot>? reports)
    {
        if (antiTankSquadIds == null || antiTankSquadIds.Count == 0 ||
            reports == null || reports.Count == 0)
        {
            return null;
        }

        var capableIds = new HashSet<int>(antiTankSquadIds);
        var capableSquads = OperationalSquads(squads)
            .Where(squad => capableIds.Contains(squad.Id))
            .ToArray();
        if (capableSquads.Length == 0)
            return null;

        var armorReports = FreshReports(reports
                .Where(report => report.Type == CommanderContactType.GroundVehicle)
                .ToArray())
            .Where(report => report.AgeSeconds <= MaximumAntiTankReportAgeSeconds &&
                             report.Confidence >= MinimumAntiTankReportConfidence)
            .ToArray();
        if (armorReports.Length == 0)
            return null;

        var maximumDistanceSquared = MaximumAntiTankResponseDistance *
                                     MaximumAntiTankResponseDistance;
        var selected = capableSquads
            .SelectMany(squad => armorReports.Select(report => new
            {
                Squad = squad,
                Report = report,
                DistanceSquared = DistanceSquared(squad.Position, report.Position)
            }))
            .Where(pair => float.IsFinite(pair.DistanceSquared) &&
                           pair.DistanceSquared <= maximumDistanceSquared)
            .OrderBy(pair => pair.DistanceSquared)
            .ThenBy(pair => pair.Report.AgeSeconds)
            .ThenByDescending(pair => pair.Report.Confidence)
            .ThenBy(pair => pair.Squad.Suppression)
            .ThenByDescending(pair => pair.Squad.EffectiveStrength / pair.Squad.PeakStrength)
            .ThenBy(pair => pair.Report.TargetId)
            .ThenBy(pair => pair.Squad.Id)
            .FirstOrDefault();
        if (selected == null)
            return null;

        var isolated = armorReports.All(report =>
            report.TargetId == selected.Report.TargetId ||
            DistanceSquared(report.Position, selected.Report.Position) >
            AntiTankIsolationRadius * AntiTankIsolationRadius);
        var hunt = selected.DistanceSquared >=
                   MinimumAntiTankHuntDistance * MinimumAntiTankHuntDistance &&
                   selected.DistanceSquared <=
                   MaximumAntiTankHuntDistance * MaximumAntiTankHuntDistance &&
                   selected.Report.AgeSeconds <= MaximumAntiTankHuntReportAgeSeconds &&
                   selected.Report.Confidence >= MinimumAntiTankHuntConfidence &&
                   selected.Squad.EffectiveStrength / selected.Squad.PeakStrength >=
                   MinimumAntiTankHuntStrengthFraction &&
                   selected.Squad.Suppression <= MaximumAntiTankHuntSuppression &&
                   isolated;

        return new CommanderAntiTankTask(
            selected.Squad.Id,
            selected.Report.TargetId,
            selected.Report.Position,
            hunt ? CommanderAntiTankAction.Hunt : CommanderAntiTankAction.Ambush);
    }

    internal static CommanderFireMissionSolution? SelectFireMissionAim(
        MapPoint reportedTarget,
        IReadOnlyList<MapPoint>? friendlyPositions,
        float clearanceMeters,
        float maximumShiftMeters)
    {
        if (!reportedTarget.IsFinite || friendlyPositions == null ||
            !float.IsFinite(clearanceMeters) || clearanceMeters <= 0f ||
            !float.IsFinite(maximumShiftMeters) || maximumShiftMeters <= 0f ||
            friendlyPositions.Any(position => !position.IsFinite))
        {
            return null;
        }

        var clearanceSquared = clearanceMeters * clearanceMeters;
        var friendliesAtTarget = friendlyPositions.Count(position =>
            DistanceSquared(position, reportedTarget) <= clearanceSquared);
        if (friendliesAtTarget == 0)
            return new CommanderFireMissionSolution(reportedTarget, 0, 0f);

        MapPoint? best = null;
        var bestShift = float.MaxValue;
        var bestNearestFriendlySquared = float.MinValue;
        foreach (var radius in FireMissionRetargetRadii)
        {
            if (radius > maximumShiftMeters)
                continue;

            for (var direction = 0; direction < FireMissionRetargetDirections; direction++)
            {
                var angle = direction * (2f * MathF.PI / FireMissionRetargetDirections);
                var candidate = new MapPoint(
                    reportedTarget.X + MathF.Cos(angle) * radius,
                    reportedTarget.Z + MathF.Sin(angle) * radius);
                var nearestFriendlySquared = friendlyPositions.Count == 0
                    ? float.MaxValue
                    : friendlyPositions.Min(position => DistanceSquared(position, candidate));
                if (nearestFriendlySquared <= clearanceSquared)
                    continue;

                if (radius < bestShift ||
                    MathF.Abs(radius - bestShift) < 0.001f &&
                    nearestFriendlySquared > bestNearestFriendlySquared)
                {
                    best = candidate;
                    bestShift = radius;
                    bestNearestFriendlySquared = nearestFriendlySquared;
                }
            }
        }

        return best == null
            ? null
            : new CommanderFireMissionSolution(best.Value, friendliesAtTarget, bestShift);
    }

    private static List<CommanderSquadSnapshot> OperationalSquads(
        IReadOnlyList<CommanderSquadSnapshot>? source)
    {
        if (source == null || source.Count == 0)
            return new List<CommanderSquadSnapshot>();

        return source
            .Where(IsOperational)
            .GroupBy(squad => squad.Id)
            .Select(group => group
                .OrderByDescending(squad => squad.EffectiveStrength)
                .ThenBy(squad => squad.Suppression)
                .ThenBy(squad => squad.Position.X)
                .ThenBy(squad => squad.Position.Z)
                .First())
            .OrderBy(squad => squad.Id)
            .ToList();
    }

    private static bool IsOperational(CommanderSquadSnapshot squad)
    {
        return squad.Eligible &&
               !squad.PlayerControlled &&
               !squad.ScriptLocked &&
               squad.Position.IsFinite &&
               float.IsFinite(squad.EffectiveStrength) &&
               float.IsFinite(squad.PeakStrength) &&
               float.IsFinite(squad.Suppression) &&
               squad.EffectiveStrength > 0f &&
               squad.PeakStrength > 0f &&
               squad.EffectiveStrength / squad.PeakStrength >= MinimumSquadStrengthFraction &&
               squad.Suppression <= MaximumOperationalSquadSuppression;
    }

    private static List<CommanderReportSnapshot> FreshReports(
        IReadOnlyList<CommanderReportSnapshot>? source)
    {
        if (source == null || source.Count == 0)
            return new List<CommanderReportSnapshot>();

        return source
            .Where(report => report.Position.IsFinite &&
                             float.IsFinite(report.AgeSeconds) &&
                             float.IsFinite(report.Confidence) &&
                             float.IsFinite(report.EstimatedPower) &&
                             report.AgeSeconds >= 0f &&
                             report.AgeSeconds <= MaximumReportAgeSeconds &&
                             report.Confidence >= MinimumReportConfidence &&
                             report.EstimatedPower > 0f)
            .GroupBy(report => report.TargetId)
            .Select(group => group
                .OrderBy(report => report.AgeSeconds)
                .ThenByDescending(report => report.Confidence)
                .ThenByDescending(report => report.EstimatedPower)
                .ThenBy(report => report.Type)
                .ThenBy(report => report.Position.X)
                .ThenBy(report => report.Position.Z)
                .First())
            .OrderBy(report => report.TargetId)
            .ToList();
    }

    private static List<CommanderAxisCandidate> UsableAxes(
        IReadOnlyList<CommanderAxisCandidate>? source)
    {
        if (source == null || source.Count == 0)
            return new List<CommanderAxisCandidate>();

        return source
            .Where(axis => axis.StagingPoint.IsFinite &&
                           float.IsFinite(axis.BearingDegrees) &&
                           float.IsFinite(axis.TerrainScore) &&
                           float.IsFinite(axis.CongestionScore) &&
                           float.IsFinite(axis.ExposureScore))
            .GroupBy(axis => axis.Id)
            .Select(group => group
                .OrderByDescending(AxisScore)
                .ThenBy(axis => NormalizeDegrees(axis.BearingDegrees))
                .ThenBy(axis => axis.StagingPoint.X)
                .ThenBy(axis => axis.StagingPoint.Z)
                .First())
            .OrderByDescending(AxisScore)
            .ThenBy(axis => axis.Id)
            .ToList();
    }

    private static CommanderAxisCandidate? SelectFlankAxis(
        IReadOnlyList<CommanderAxisCandidate> axes,
        CommanderAxisCandidate? mainAxis)
    {
        if (mainAxis == null)
            return null;

        foreach (var axis in axes)
        {
            if (axis.Id != mainAxis.Value.Id &&
                AxisSeparation(axis.BearingDegrees, mainAxis.Value.BearingDegrees) >=
                MinimumAxisSeparationDegrees)
            {
                return axis;
            }
        }

        return null;
    }

    private static Dictionary<int, CommanderRole> AllocateRoles(
        IReadOnlyList<CommanderSquadSnapshot> squads,
        CommanderAxisCandidate? flankAxis)
    {
        var roles = new Dictionary<int, CommanderRole>(squads.Count);
        var remaining = new HashSet<int>(squads.Select(squad => squad.Id));
        var reserveCount = squads.Count >= 4
            ? (int)Math.Ceiling(squads.Count * ReserveFraction)
            : 0;

        foreach (var squad in squads
                     .OrderByDescending(squad => squad.EffectiveStrength / squad.PeakStrength)
                     .ThenBy(squad => squad.Suppression)
                     .ThenByDescending(squad => squad.EffectiveStrength)
                     .ThenBy(squad => squad.Id)
                     .Take(reserveCount))
        {
            roles[squad.Id] = CommanderRole.Reserve;
            remaining.Remove(squad.Id);
        }

        if (squads.Count >= 3 && remaining.Count > 0)
        {
            var support = squads
                .Where(squad => remaining.Contains(squad.Id))
                .OrderByDescending(squad => squad.EffectiveStrength * (1f - Clamp01(squad.Suppression)))
                .ThenBy(squad => squad.Id)
                .First();
            roles[support.Id] = CommanderRole.SupportByFire;
            remaining.Remove(support.Id);
        }

        if (squads.Count >= 2 && remaining.Count > 0)
        {
            var flank = squads
                .Where(squad => remaining.Contains(squad.Id))
                .OrderBy(squad => squad.Suppression)
                .ThenByDescending(squad => squad.EffectiveStrength / squad.PeakStrength)
                .ThenBy(squad => flankAxis == null
                    ? 0f
                    : DistanceSquared(squad.Position, flankAxis.Value.StagingPoint))
                .ThenBy(squad => squad.Id)
                .First();
            roles[flank.Id] = CommanderRole.Flank;
            remaining.Remove(flank.Id);
        }

        foreach (var squadId in remaining)
            roles[squadId] = CommanderRole.Assault;

        return roles;
    }

    private static (CommanderAxisCandidate? Axis, MapPoint Destination) DirectiveDestination(
        CommanderSquadSnapshot squad,
        CommanderRole role,
        CommanderAxisCandidate? mainAxis,
        CommanderAxisCandidate? flankAxis)
    {
        return role switch
        {
            CommanderRole.Assault when mainAxis != null =>
                (mainAxis, mainAxis.Value.StagingPoint),
            CommanderRole.Flank when flankAxis != null =>
                (flankAxis, flankAxis.Value.StagingPoint),
            CommanderRole.SupportByFire when mainAxis != null =>
                (mainAxis, mainAxis.Value.StagingPoint),
            _ => (null, squad.Position)
        };
    }

    private static float WeightedSuppression(IReadOnlyList<CommanderSquadSnapshot> squads)
    {
        if (squads.Count == 0)
            return 0f;

        double weighted = 0d;
        double strength = 0d;
        foreach (var squad in squads)
        {
            weighted += squad.EffectiveStrength * Clamp01(squad.Suppression);
            strength += squad.EffectiveStrength;
        }

        if (strength <= 0d)
            return 0f;
        return (float)Math.Clamp(weighted / strength, 0d, 1d);
    }

    private static float SafeSum(IEnumerable<float> values)
    {
        double total = 0d;
        foreach (var value in values)
        {
            if (!float.IsFinite(value) || value <= 0f)
                continue;
            total += value;
            if (total >= float.MaxValue)
                return float.MaxValue;
        }

        return (float)total;
    }

    private static float SafeRatio(float numerator, float denominator)
    {
        if (numerator <= 0f || !float.IsFinite(numerator) ||
            denominator <= 0f || !float.IsFinite(denominator))
        {
            return 0f;
        }

        var ratio = (double)numerator / denominator;
        return ratio >= float.MaxValue ? float.MaxValue : (float)ratio;
    }

    private static float AxisScore(CommanderAxisCandidate axis)
    {
        return Clamp01(axis.TerrainScore) -
               Clamp01(axis.CongestionScore) -
               Clamp01(axis.ExposureScore);
    }

    private static float AxisSeparation(float first, float second)
    {
        var delta = MathF.Abs(NormalizeDegrees(first) - NormalizeDegrees(second));
        return MathF.Min(delta, 360f - delta);
    }

    private static float NormalizeDegrees(float degrees)
    {
        var normalized = degrees % 360f;
        return normalized < 0f ? normalized + 360f : normalized;
    }

    private static float DistanceSquared(MapPoint first, MapPoint second)
    {
        var dx = first.X - second.X;
        var dz = first.Z - second.Z;
        return dx * dx + dz * dz;
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static CommanderPlan EmptyPlan()
    {
        return new CommanderPlan(
            null,
            null,
            new CommanderPlanMetrics(0, 0, 0, 0f, 0f, 0f, 0f, 0f, false),
            false,
            Array.Empty<CommanderDirective>());
    }
}
