using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

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

internal static class SoldierFireDanger
{
    private static readonly Dictionary<int, float> NextCheck = new();
    private static readonly Dictionary<int, HazardStamp> ActiveHazards = new();

    internal static bool TrySense(Soldier soldier, float now, out Vector3 escape)
    {
        escape = default;
        if (!Settings.DangerReactionsEnabled.Value || !MultiplayerAuthority.CanMutateGameplay())
            return false;

        try
        {
            if (!AiOwnership.IsAutonomous(soldier) || soldier.IsOnVehicle())
                return false;

            var id = soldier.GetInstanceID();
            if (soldier.IsOnFire)
            {
                escape = soldier.transform.position;
                ActiveHazards[id] = new HazardStamp(escape, now + 0.35f);
                return true;
            }

            if (!AiState.CooldownReady(NextCheck, id, now))
            {
                if (ActiveHazards.TryGetValue(id, out var active) && active.ValidUntil > now)
                {
                    escape = active.Escape;
                    return true;
                }
                return false;
            }
            NextCheck[id] = now + 0.2f;

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
            {
                ActiveHazards.Remove(id);
                return false;
            }

            var position = soldier.transform.position;
            escape = position + AiState.HorizontalAway(position, danger.transform.position) *
                Settings.FlameEscapeDistance.Value;
            ActiveHazards[id] = new HazardStamp(escape, now + 0.35f);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Fire danger sensing failed: {ex.Message}");
            return false;
        }
    }

    internal static void Execute(SoldierAI ai, Soldier soldier, Vector3 escape, float now)
    {
        try
        {
            if (soldier.IsOnFire)
            {
                GroundAiDirector.ExecuteSoldierFireInhibition(ai, soldier);
                ContactResponse.StopDangerMovement(
                    ai, soldier, SoldierPose.Prone, Time.deltaTime);
                return;
            }

            var id = soldier.GetInstanceID();
            AiState.FlameEvasionUntil[id] = now + 1.5f;
            var contact = AiState.GetContactState(id);
            contact.SuppressionMovementOwned = false;
            contact.MovementInhibitedByContactResponse = false;
            GroundAiDirector.ExecuteHazardEscape(ai, soldier, escape);
            AiState.Trace($"Fire danger: soldier {id} diverting around active flame");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Fire reaction failed: {ex.Message}");
        }
    }

    internal static void Remove(int soldierId)
    {
        NextCheck.Remove(soldierId);
        ActiveHazards.Remove(soldierId);
    }

    internal static void Reset()
    {
        NextCheck.Clear();
        ActiveHazards.Clear();
    }

    private readonly record struct HazardStamp(Vector3 Escape, float ValidUntil);
}
