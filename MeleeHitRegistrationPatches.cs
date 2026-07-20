using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Marks only an invocation of the native melee coroutine's MoveNext method.
/// The damage query occurs after the coroutine's opening wait, rather than in
/// the Soldier.Melee call that starts it.
/// </summary>
internal static class MeleeHitQueryScope
{
    [ThreadStatic]
    private static Soldier? _activeAttacker;

    internal static Soldier? ActiveAttacker => _activeAttacker;

    internal static bool TryEnter(Soldier attacker)
    {
        if (_activeAttacker != null)
            return false;

        _activeAttacker = attacker;
        return true;
    }

    internal static void Exit(bool entered)
    {
        if (entered)
            _activeAttacker = null;
    }
}

[HarmonyPatch(typeof(Soldier._MeleeDamageCR_d__362), "MoveNext")]
internal static class SoldierMeleeDamageCoroutinePatch
{
    [HarmonyPrefix]
    private static void Prefix(
        Soldier._MeleeDamageCR_d__362 __instance,
        out bool __state)
    {
        var attacker = __instance.__4__this;
        __state = Settings.ImprovedMeleeHitRegistrationEnabled.Value &&
                  MultiplayerAuthority.CanMutateGameplay() &&
                  attacker != null &&
                  MeleeHitQueryScope.TryEnter(attacker);
    }

    [HarmonyFinalizer]
    private static void Finalizer(bool __state)
        => MeleeHitQueryScope.Exit(__state);
}

/// <summary>
/// The stock melee query has a 0.25 m radius and extends only 0.37 m for a
/// normal strike (0.73 m with a bayonet). Enlarge that one native query while
/// leaving target validation, body-part damage, attribution, effects, and
/// synchronization in the base game.
/// </summary>
[HarmonyPatch(
    typeof(Physics),
    nameof(Physics.OverlapCapsule),
    new[] { typeof(Vector3), typeof(Vector3), typeof(float) })]
internal static class MeleeOverlapCapsulePatch
{
    [HarmonyPrefix]
    private static void Prefix(ref Vector3 point0, ref Vector3 point1, ref float radius)
    {
        var attacker = MeleeHitQueryScope.ActiveAttacker;
        if (attacker == null)
            return;

        var forward = point1 - point0;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = attacker.transform.forward;
            forward.y = 0f;
        }

        if (forward.sqrMagnitude >= 0.0001f)
        {
            point1 += forward.normalized *
                      Mathf.Max(0f, Settings.MeleeAdditionalReach.Value);
        }

        radius = Mathf.Max(radius, Settings.MeleeMinimumSweepRadius.Value);
    }
}
