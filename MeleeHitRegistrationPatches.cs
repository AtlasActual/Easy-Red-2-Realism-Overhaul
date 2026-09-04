using System.Reflection;
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

/// <summary>
/// The melee coroutine is a compiler-generated nested class of Soldier whose
/// name carries an ordinal (for example <c>_MeleeDamageCR_d__362</c>) that
/// changes whenever the game adds or removes a Soldier member. It is resolved
/// by its stable prefix at patch time instead of being named at compile time,
/// so a game update that renumbers it disables nothing and one that removes it
/// disables only this module.
/// </summary>
[HarmonyPatch]
internal static class SoldierMeleeDamageCoroutinePatch
{
    private const string CoroutineTypePrefix = "_MeleeDamageCR_d__";
    private const string AttackerMemberName = "__4__this";

    private static readonly Dictionary<Type, MethodInfo> AttackerGetters = new();

    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var coroutineTypes = typeof(Soldier)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(type => type.Name.StartsWith(CoroutineTypePrefix, StringComparison.Ordinal))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

        if (coroutineTypes.Length == 0)
        {
            throw new InvalidOperationException(
                $"Soldier has no nested '{CoroutineTypePrefix}*' melee coroutine class in this game build.");
        }

        var targets = new List<MethodBase>();
        foreach (var coroutineType in coroutineTypes)
        {
            var moveNext = AccessTools.Method(coroutineType, "MoveNext");
            var attackerGetter = AccessTools.PropertyGetter(coroutineType, AttackerMemberName) ??
                                 AccessTools.Method(coroutineType, "get_" + AttackerMemberName);
            if (moveNext == null || attackerGetter == null)
            {
                throw new InvalidOperationException(
                    $"{coroutineType.FullName} lacks MoveNext or its '{AttackerMemberName}' attacker accessor.");
            }

            AttackerGetters[coroutineType] = attackerGetter;
            targets.Add(moveNext);
        }

        Plugin.LogSource.LogInfo(
            $"Melee hit registration bound to {string.Join(", ", coroutineTypes.Select(type => type.Name))}.MoveNext.");
        return targets;
    }

    [HarmonyPrefix]
    private static void Prefix(object __instance, out bool __state)
    {
        __state = false;
        if (!Settings.ImprovedMeleeHitRegistrationEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        var attacker = ResolveAttacker(__instance);
        __state = attacker != null && MeleeHitQueryScope.TryEnter(attacker);
    }

    [HarmonyFinalizer]
    private static void Finalizer(bool __state)
        => MeleeHitQueryScope.Exit(__state);

    private static Soldier? ResolveAttacker(object? coroutine)
    {
        if (coroutine == null ||
            !AttackerGetters.TryGetValue(coroutine.GetType(), out var getter))
        {
            return null;
        }

        try
        {
            return getter.Invoke(coroutine, null) as Soldier;
        }
        catch
        {
            return null;
        }
    }
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
