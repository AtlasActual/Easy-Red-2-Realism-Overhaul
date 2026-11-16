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

    /// <summary>
    /// Roles for the squads attacking one objective, kept stable across plan passes.
    /// A squad keeps the role it already had on this objective, so a death or a new
    /// arrival does not turn a flanking squad around mid-manoeuvre. One squad holds
    /// the direct pressure axis; new flankers go to whichever side has fewer.
    /// </summary>
    /// <param name="squadIds">Squads assigned to the objective, in a stable order.</param>
    /// <param name="previous">Roles these squads held on this same objective last pass.</param>
    internal static Dictionary<int, ObjectiveAttackRole> SelectRoles(
        IReadOnlyList<int> squadIds,
        int objectiveId,
        IReadOnlyDictionary<int, ObjectiveAttackRole> previous)
    {
        var roles = new Dictionary<int, ObjectiveAttackRole>();
        if (squadIds.Count == 0)
            return roles;

        var frontal = new ObjectiveAttackRole(false, 0, 0f);
        if (squadIds.Count == 1)
        {
            roles[squadIds[0]] = frontal;
            return roles;
        }

        // Keep the existing pressure squad. Otherwise give the role to a squad that
        // has not started a flank, and only as a last resort turn a flanker inward.
        int? keptPressure = null;
        int? fresh = null;
        foreach (var id in squadIds)
        {
            if (previous.TryGetValue(id, out var role))
            {
                if (!role.IsFlank && keptPressure == null)
                    keptPressure = id;
            }
            else if (fresh == null)
            {
                fresh = id;
            }
        }

        var pressureId = keptPressure ?? fresh ?? squadIds[0];

        roles[pressureId] = frontal;
        var firstSide = (objectiveId & 1) == 0 ? -1 : 1;
        var left = 0;
        var right = 0;
        foreach (var id in squadIds)
        {
            if (id == pressureId || !previous.TryGetValue(id, out var role) || !role.IsFlank)
                continue;

            roles[id] = role;
            if (role.Side < 0) left++;
            else right++;
        }

        foreach (var id in squadIds)
        {
            if (roles.ContainsKey(id))
                continue;

            var side = left == right ? firstSide : left < right ? -1 : 1;
            roles[id] = new ObjectiveAttackRole(true, side, FlankAngleDegrees);
            if (side < 0) left++;
            else right++;
        }

        return roles;
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
