namespace ER2RealismOverhaul;

/// <summary>
/// Pure rule for which projectiles the prop-penetration system may take over.
///
/// A projectile that functions on contact - a rocket, a HEAT warhead, a rifle grenade -
/// belongs entirely to the base game. Carrying one through a fence and re-spawning it past
/// the exit face would move the blast to the wrong side of the obstacle and give the round
/// a second exhaust trail. Shell-type tests alone do not catch this: the game's IsHe()
/// matches HE, APHE and Rocket but not HEAT, so HEAT warheads used to fall through to the
/// ordinary-round path. The projectile's own behaviour and blast radius are the reliable
/// signal, and they also cover modded ammunition that picks an unexpected shell type.
/// </summary>
internal static class ProjectilePenetrationEligibilityCore
{
    internal static bool Detonates(bool explodesOnImpact, float explosionRadiusMeters)
    {
        return explodesOnImpact ||
               (float.IsFinite(explosionRadiusMeters) && explosionRadiusMeters > 0f);
    }
}
