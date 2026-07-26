using HarmonyLib;

namespace ER2RealismOverhaul;

/// <summary>
/// Records a bullet striking the locally controlled soldier's head, and starts
/// the blackout when that same call leaves the player dead. The native call has
/// already resolved which body part was hit and applies the damage itself, so a
/// kill from this hit lands inside it.
/// </summary>
[HarmonyPatch(typeof(Soldier), nameof(Soldier.DamageWithAnimation))]
internal static class PlayerHeadHitPatch
{
    [HarmonyPrefix]
    private static void Prefix(Soldier __instance, BodyPartType bPart, HitType hitType, out bool __state)
    {
        __state = false;
        try
        {
            if (bPart != BodyPartType.head || hitType != HitType.Projectile ||
                __instance == null || !Settings.HeadshotDeathBlackoutEnabled.Value)
            {
                return;
            }

            var player = Soldier.CurrentControlledSoldierOrNull();
            if (player == null || player.GetInstanceID() != __instance.GetInstanceID())
                return;

            __state = true;
            PlayerHeadshotBlackoutController.NotePlayerHeadHit();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not record a player head hit: {ex.Message}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(Soldier __instance, bool __state)
    {
        try
        {
            // The player check has to come from the prefix: the native death
            // sequence releases the controlled character, so by now the dying
            // soldier no longer reports as the player.
            if (!__state || __instance == null || __instance.IsAlive)
                return;

            PlayerHeadshotBlackoutController.NotePlayerKilled();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not start the headshot death blackout: {ex.Message}");
        }
    }
}

/// <summary>
/// Second entry point, for a kill that reaches <c>Soldier.Kill</c> without the
/// damage call above having finished — a delayed or synchronized death. This
/// runs as a prefix for the same reason: after it, the dying soldier no longer
/// reports as the player.
/// </summary>
[HarmonyPatch(typeof(Soldier), nameof(Soldier.Kill))]
internal static class PlayerHeadshotDeathPatch
{
    [HarmonyPrefix]
    private static void Prefix(Soldier __instance)
    {
        try
        {
            if (__instance == null || !Settings.HeadshotDeathBlackoutEnabled.Value)
                return;

            var player = Soldier.CurrentControlledSoldierOrNull();
            if (player == null || player.GetInstanceID() != __instance.GetInstanceID())
                return;

            PlayerHeadshotBlackoutController.NotePlayerKilled();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not start the headshot death blackout: {ex.Message}");
        }
    }
}
