using Corvostudio.Weapons;
using HarmonyLib;

namespace ER2RealismOverhaul;

internal static class ProjectileImpactSuppressionScope
{
    [ThreadStatic]
    private static int _depth;

    internal static bool IsActive => _depth > 0;

    internal static void Enter() => _depth++;

    internal static void Exit()
    {
        if (_depth > 0)
            _depth--;
    }
}

[HarmonyPatch(typeof(Projectile), nameof(Projectile.SuppressBots))]
internal static class ProjectileImpactSuppressionScopePatch
{
    [HarmonyPrefix]
    private static void Prefix() => ProjectileImpactSuppressionScope.Enter();

    [HarmonyPostfix]
    private static void Postfix() => ProjectileImpactSuppressionScope.Exit();
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.Suppress))]
internal static class ProjectileImpactSuppressionAmountPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Soldier __instance, ref int suppressionValueAdd)
    {
        if (!ProjectileImpactSuppressionScope.IsActive ||
            suppressionValueAdd <= 0 ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        try
        {
            if (__instance != null && AiOwnership.IsAutonomous(__instance))
            {
                suppressionValueAdd = AiBehaviorTuningCore.CapProjectileImpactSuppression(
                    suppressionValueAdd,
                    Settings.ProjectileImpactSuppression.Value);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Projectile-impact suppression scaling failed: {ex.Message}");
        }
    }
}
