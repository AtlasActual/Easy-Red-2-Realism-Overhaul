using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Thins out AI knee-mortar fire. The game treats every mortarman as an
/// independent shooter with no shared fire mission, so a section fires together
/// until dry, at a single downed man, and on top of nearby friendly soldiers.
/// A round is withheld when a friendly is near the impact point, and otherwise
/// fires with the configured likelihood.
/// </summary>
internal static class KneeMortarFireLimiter
{
    private const float MinimumFriendlyClearanceMeters = 25f;
    private static readonly Dictionary<int, float> HoldUntil = new();

    internal static bool ShouldWithhold(Soldier shooter, GenericGun gun)
    {
        var mortar = Il2CppTypeTests.As<KneeMortar>(gun);
        if (mortar == null)
            return false;

        var id = shooter.GetInstanceID();
        var now = Time.time;
        if (HoldUntil.TryGetValue(id, out var holdUntil) && now < holdUntil)
            return true;

        if (Settings.FriendlyFireChecksEnabled.Value)
        {
            var clearance = Mathf.Max(MinimumFriendlyClearanceMeters,
                Settings.GrenadeFriendlySafetyRadius.Value);
            if (CombatSafety.FriendlyNear(
                    EstimateImpact(shooter, mortar), AiState.FactionOf(shooter), clearance, shooter))
            {
                return true;
            }
        }

        if (ExplosiveLikelihoodCore.Passes(
                Settings.MortarFireLikelihood.Value, UnityEngine.Random.value))
        {
            return false;
        }

        HoldUntil[id] = now + ExplosiveLikelihoodCore.MortarRetrySeconds;
        return true;
    }

    private static Vector3 EstimateImpact(Soldier shooter, KneeMortar mortar)
    {
        var target = shooter.GetCurrentBestVisibleEnemy();
        if (TargetAcquisition.IsUsableTarget(target))
            return target.GetCenterOfUnit();

        var origin = shooter.GetCenterOfUnit();
        var direction = shooter.GetFireDir();
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-4f)
            return origin;

        return origin + direction.normalized * Mathf.Max(0f, mortar.CurrentRange);
    }

    internal static void RemoveSoldier(int id) => HoldUntil.Remove(id);

    internal static void ResetBattle() => HoldUntil.Clear();
}
