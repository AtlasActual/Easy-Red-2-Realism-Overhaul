using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class RagdollMomentum
{
    private static readonly Dictionary<int, int> AppliedFrameByManager = new();
    private static string _lastErrorSignature = string.Empty;

    internal static Vector3 Capture(RagdollManager manager)
    {
        if (!Settings.RagdollMomentumEnabled.Value || manager == null)
            return Vector3.zero;

        try
        {
            var controller = manager.GetComponentInParent<CharacterController>();
            if (controller == null)
            {
                var soldier = manager.GetComponentInParent<Soldier>();
                controller = soldier != null ? soldier.GetComponent<CharacterController>() : null;
            }

            if (controller == null)
                return Vector3.zero;

            var inherited = controller.velocity * Settings.RagdollMomentumMultiplier.Value;
            return Vector3.ClampMagnitude(
                inherited,
                Mathf.Max(0f, Settings.RagdollMomentumMaximumSpeed.Value));
        }
        catch (Exception ex)
        {
            ReportError(ex);
            return Vector3.zero;
        }
    }

    internal static void Apply(RagdollManager manager, Vector3 inheritedVelocity)
    {
        if (!Settings.RagdollMomentumEnabled.Value ||
            manager == null ||
            inheritedVelocity.sqrMagnitude < 0.0001f)
        {
            return;
        }

        try
        {
            var managerId = manager.GetInstanceID();
            if (AppliedFrameByManager.TryGetValue(managerId, out var frame) &&
                frame == Time.frameCount)
            {
                return;
            }

            AppliedFrameByManager[managerId] = Time.frameCount;
            foreach (var body in manager.GetComponentsInChildren<Rigidbody>(true))
            {
                if (body != null && !body.isKinematic)
                    body.velocity += inheritedVelocity;
            }

            if (AppliedFrameByManager.Count > 512)
            {
                var cutoff = Time.frameCount - 2;
                foreach (var stale in AppliedFrameByManager
                             .Where(pair => pair.Value < cutoff)
                             .Select(pair => pair.Key)
                             .ToArray())
                {
                    AppliedFrameByManager.Remove(stale);
                }
            }
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private static void ReportError(Exception exception)
    {
        var signature = exception.GetType().FullName + ": " + exception.Message;
        if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal))
            return;

        _lastErrorSignature = signature;
        Plugin.LogSource.LogWarning(
            $"Ragdoll momentum failed (further identical errors suppressed): {exception.Message}");
    }
}

[HarmonyPatch(typeof(RagdollManager), nameof(RagdollManager.Ragdolize), typeof(Vector3))]
internal static class DirectedRagdollMomentumPatch
{
    [HarmonyPrefix]
    private static void Prefix(RagdollManager __instance, out Vector3 __state)
    {
        __state = RagdollMomentum.Capture(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(RagdollManager __instance, Vector3 __state)
    {
        RagdollMomentum.Apply(__instance, __state);
    }
}

[HarmonyPatch(typeof(RagdollManager), nameof(RagdollManager.Ragdolize), new Type[] { })]
internal static class OrdinaryRagdollMomentumPatch
{
    [HarmonyPrefix]
    private static void Prefix(RagdollManager __instance, out Vector3 __state)
    {
        __state = RagdollMomentum.Capture(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(RagdollManager __instance, Vector3 __state)
    {
        RagdollMomentum.Apply(__instance, __state);
    }
}
