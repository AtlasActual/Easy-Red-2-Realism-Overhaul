using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class AiRagdollWeight
{
    private static readonly HashSet<int> AppliedRagdolls = new();

    internal static void Apply(RagdollManager manager)
    {
        var multiplier = Settings.AiRagdollWeightMultiplier.Value;
        if (Mathf.Approximately(multiplier, 1f))
            return;

        var soldier = manager.GetComponentInParent<Soldier>();
        if (soldier == null || !soldier.IsAI() || !soldier.IsDead)
            return;

        var managerId = manager.GetInstanceID();
        if (!AppliedRagdolls.Add(managerId))
            return;

        var skeleton = manager.transform.Find("Skeleton");
        var root = skeleton != null ? skeleton.gameObject : manager.gameObject;
        var rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
        var affected = 0;

        foreach (var body in rigidbodies)
        {
            if (body == null || body.mass <= 0f)
                continue;

            body.mass *= multiplier;
            affected++;
        }

        if (affected == 0)
        {
            AppliedRagdolls.Remove(managerId);
            return;
        }

        AiState.Trace($"AI ragdoll weight: scaled {affected} bodies by {multiplier:0.00}x");
    }

    internal static void Forget(RagdollManager manager)
        => AppliedRagdolls.Remove(manager.GetInstanceID());
}

[HarmonyPatch(typeof(RagdollManager), nameof(RagdollManager.Ragdolize), new Type[] { })]
internal static class AiRagdollWeightPatch
{
    [HarmonyPostfix]
    private static void Postfix(RagdollManager __instance)
        => AiRagdollWeight.Apply(__instance);
}

[HarmonyPatch(typeof(RagdollManager), nameof(RagdollManager.Reset))]
internal static class AiRagdollWeightResetPatch
{
    [HarmonyPostfix]
    private static void Postfix(RagdollManager __instance)
        => AiRagdollWeight.Forget(__instance);
}

[HarmonyPatch(typeof(RagdollManager), nameof(RagdollManager.Deragdolize))]
internal static class AiRagdollWeightDeragdolizePatch
{
    [HarmonyPostfix]
    private static void Postfix(RagdollManager __instance)
        => AiRagdollWeight.Forget(__instance);
}
