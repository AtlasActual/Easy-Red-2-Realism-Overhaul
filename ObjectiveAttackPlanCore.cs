using System;
using System.Collections.Generic;
using System.Linq;

namespace ER2RealismOverhaul;

internal readonly record struct ObjectiveAttackTarget(int Id, bool FriendlySecured);

internal readonly record struct ObjectiveAttackRole(
    bool IsFlank,
    int Side,
    float AngleDegrees);

internal static class ObjectiveAttackPlanCore
{
    internal const float FlankAngleDegrees = 58f;

    /// <summary>
    /// Inputs retain the caller's distance ranking. Cover held ground first,
    /// but reserve a slot for an unfinished objective so scarce squads can
    /// still advance the phase. Each remaining slot opens another attack.
    /// </summary>
    internal static List<int> SelectTargets(
        int squadCount,
        IReadOnlyList<ObjectiveAttackTarget> objectives)
    {
        var targetCount = Math.Min(Math.Max(0, squadCount), objectives.Count);
        if (targetCount == 0)
            return new List<int>();

        var hasUnfinished = objectives.Any(objective => !objective.FriendlySecured);
        var garrisonLimit = hasUnfinished ? targetCount - 1 : targetCount;
        var held = objectives.Where(objective => objective.FriendlySecured)
            .Take(garrisonLimit).Select(objective => objective.Id).ToList();
        var selected = new HashSet<int>(held);
        foreach (var objective in objectives.Where(objective => !objective.FriendlySecured)
                     .Take(targetCount - held.Count))
        {
            selected.Add(objective.Id);
        }

        // Keep the distance order for deterministic surplus distribution.
        return objectives.Where(objective => selected.Contains(objective.Id))
            .Select(objective => objective.Id).ToList();
    }

    /// <summary>
    /// One squad secures each selected point. Surplus squads reinforce the
    /// unfinished objectives rather than following captured points' garrisons.
    /// If the whole phase is secured, everyone holds until the phase changes.
    /// </summary>
    internal static Dictionary<int, int> BuildCapacities(
        int squadCount,
        IReadOnlyList<ObjectiveAttackTarget> targets)
    {
        if (squadCount <= 0)
            return new Dictionary<int, int>();

        var capacities = targets.ToDictionary(target => target.Id, _ => 1);
        if (targets.Count == 0)
            return capacities;

        var attacks = targets.Where(target => !target.FriendlySecured).ToList();
        var recipients = attacks.Count > 0 ? attacks : targets.ToList();
        var remaining = squadCount - targets.Count;
        for (var index = 0; index < remaining; index++)
            capacities[recipients[index % recipients.Count].Id]++;

        return capacities;
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
