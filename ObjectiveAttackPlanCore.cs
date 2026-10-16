using System;
using System.Collections.Generic;
using System.Linq;

namespace ER2RealismOverhaul;

internal readonly record struct ObjectiveAttackTarget(int Id, bool FriendlySecured, bool Pressured = false);

internal readonly record struct ObjectiveAttackRole(
    bool IsFlank,
    int Side,
    float AngleDegrees);

internal static class ObjectiveAttackPlanCore
{
    internal const float FlankAngleDegrees = 58f;
    internal const int DefaultGarrisonPercent = 20;

    internal static int CalculateGarrisonBudget(
        int squadCount, int garrisonPercent, int capturedObjectiveCount = 0)
    {
        if (squadCount <= 1 || garrisonPercent <= 0)
            return 0;

        // Round the percentage down: cover captured points when possible, but
        // always retain an assault squad. Zero explicitly disables garrisons.
        return Math.Min(squadCount - 1,
            Math.Max(Math.Max(0, capturedObjectiveCount),
                (int)((long)squadCount * Math.Clamp(garrisonPercent, 0, 100) / 100)));
    }

    /// <summary>
    /// Inputs retain the caller's distance ranking. Share the percentage budget
    /// across held points with minimum coverage, and use the remaining squads on
    /// unfinished objectives. Under scarcity, pressured held points take priority.
    /// </summary>
    internal static List<int> SelectTargets(
        int squadCount,
        IReadOnlyList<ObjectiveAttackTarget> objectives,
        int garrisonPercent = DefaultGarrisonPercent)
    {
        var targetCount = Math.Min(Math.Max(0, squadCount), objectives.Count);
        if (targetCount == 0)
            return new List<int>();

        var hasUnfinished = objectives.Any(objective => !objective.FriendlySecured);
        var heldCount = objectives.Count(objective => objective.FriendlySecured);
        var garrisonBudget = hasUnfinished && heldCount > 0
            ? CalculateGarrisonBudget(squadCount, garrisonPercent, heldCount)
            : heldCount > 0 ? squadCount : 0;
        var held = objectives.Where(objective => objective.FriendlySecured)
            .OrderByDescending(objective => objective.Pressured)
            .Take(garrisonBudget).Select(objective => objective.Id).ToList();
        var selected = new HashSet<int>(held);
        foreach (var objective in objectives.Where(objective => !objective.FriendlySecured)
                     .Take(squadCount - garrisonBudget))
        {
            selected.Add(objective.Id);
        }

        // Preserve the caller's distance ranking for target and squad assignment.
        return objectives.Where(objective => selected.Contains(objective.Id))
            .Select(objective => objective.Id).ToList();
    }

    /// <summary>
    /// Give each selected held point one squad, then prioritize pressured points
    /// for surplus guards without borrowing assault squads. Share the remaining
    /// strength evenly across selected unfinished objectives.
    /// If the whole phase is secured, everyone holds until the phase changes.
    /// </summary>
    internal static Dictionary<int, int> BuildCapacities(
        int squadCount,
        IReadOnlyList<ObjectiveAttackTarget> targets,
        int garrisonPercent = DefaultGarrisonPercent)
    {
        if (squadCount <= 0)
            return new Dictionary<int, int>();

        var capacities = targets.ToDictionary(target => target.Id, _ => 1);
        if (targets.Count == 0)
            return capacities;

        var attacks = targets.Where(target => !target.FriendlySecured).ToList();
        var held = targets.Where(target => target.FriendlySecured).ToList();
        var garrisonBudget = attacks.Count == 0 ? squadCount
            : held.Count == 0 ? 0
            : Math.Clamp(CalculateGarrisonBudget(squadCount, garrisonPercent, held.Count),
                held.Count, squadCount - attacks.Count);
        var pressured = held.Where(target => target.Pressured).ToList();
        DistributeSurplus(pressured.Count > 0 ? pressured : held, garrisonBudget - held.Count);
        DistributeSurplus(attacks, squadCount - garrisonBudget - attacks.Count);

        return capacities;

        void DistributeSurplus(List<ObjectiveAttackTarget> recipients, int remaining)
        {
            // Changes in the faction centroid must not swap surplus capacities
            // between the same objectives and redirect otherwise valid orders.
            recipients.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < remaining; index++)
                capacities[recipients[index % recipients.Count].Id]++;
        }
    }

    internal static ObjectiveAttackRole SelectRole(
        int squadIndex,
        int squadCount,
        int objectiveId)
    {
        if (squadCount < 2 || squadIndex == 0)
            return new ObjectiveAttackRole(false, 0, 0f);

        var firstSide = (objectiveId & 1) == 0 ? -1 : 1;
        var side = (squadIndex - 1) % 2 == 0 ? firstSide : -firstSide;
        return new ObjectiveAttackRole(true, side, FlankAngleDegrees);
    }
}
