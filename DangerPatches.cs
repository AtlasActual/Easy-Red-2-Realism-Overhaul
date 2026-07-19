using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(SoldierAI), "OnSuppressed")]
internal static class SoldierSuppressedPatch
{
    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance)
    {
        if (!MultiplayerAuthority.CanMutateGameplay())
            return;

        try
        {
            var soldier = __instance.GetSoldier();
            if (soldier == null || !soldier.IsAI() || soldier.IsFPSPlayer() || soldier.IsOnVehicle())
                return;

            var now = Time.time;
            ContactResponse.UpdateSuppressionReaction(__instance, soldier, now, Time.deltaTime);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Suppression reaction failed: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(Flame), "OnEnable")]
internal static class FlameEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(Flame __instance)
    {
        if (__instance != null && MultiplayerAuthority.CanMutateGameplay())
            AiState.Flames[__instance.GetInstanceID()] = __instance;
    }
}

[HarmonyPatch(typeof(SoldierAI), "FixedUpdate")]
internal static class SoldierFireDangerPatch
{
    private static readonly Dictionary<int, float> NextCheck = new();

    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance)
    {
        if (!Settings.DangerReactionsEnabled.Value || !MultiplayerAuthority.CanMutateGameplay())
            return;

        try
        {
            var soldier = __instance.GetSoldier();
            if (soldier == null || !soldier.IsAI() || soldier.IsFPSPlayer() || soldier.IsOnVehicle())
                return;

            var id = soldier.GetInstanceID();
            var now = Time.time;
            if (!AiState.CooldownReady(NextCheck, id, now))
                return;
            NextCheck[id] = now + 0.2f;

            if (soldier.IsOnFire)
            {
                soldier.StopFire();
                ContactResponse.StopDangerMovement(
                    __instance,
                    soldier,
                    SoldierPose.Prone,
                    Time.fixedDeltaTime);
                return;
            }

            Flame? danger = null;
            var closestSqr = float.MaxValue;
            List<int>? stale = null;
            foreach (var pair in AiState.Flames)
            {
                var flame = pair.Value;
                if (flame == null || flame.gameObject == null || !flame.gameObject.activeInHierarchy)
                {
                    (stale ??= new List<int>()).Add(pair.Key);
                    continue;
                }

                var radius = Mathf.Max(0.5f, flame.damageRadius) + Settings.FlameSafetyMargin.Value;
                var sqr = (soldier.transform.position - flame.transform.position).sqrMagnitude;
                if (sqr <= radius * radius && sqr < closestSqr)
                {
                    danger = flame;
                    closestSqr = sqr;
                }
            }

            if (stale != null)
                foreach (var key in stale)
                    AiState.Flames.Remove(key);

            if (danger == null)
                return;

            var position = soldier.transform.position;
            var escape = position + AiState.HorizontalAway(position, danger.transform.position) * Settings.FlameEscapeDistance.Value;
            // Publish the emergency owner before issuing movement so suppression
            // and contact hooks in the same frame yield to the lethal flame hazard.
            AiState.FlameEvasionUntil[id] = now + 1.5f;
            var contact = AiState.GetContactState(id);
            contact.SuppressionMovementOwned = false;
            contact.MovementInhibitedByContactResponse = false;
            __instance.moveCharacter = true;
            ContactResponse.SetTacticalPose(__instance, soldier, SoldierPose.Crouch);
            __instance.MoveDirectlyToward(escape, 1.5f);
            AiState.Trace($"Fire danger: soldier {id} diverting around active flame");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Fire reaction failed: {ex.Message}");
        }
    }
}
