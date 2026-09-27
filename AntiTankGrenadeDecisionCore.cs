namespace ER2RealismOverhaul;

internal readonly record struct AntiTankGrenadeDecisionInput(
    bool IsAvailableForThrow,
    bool IsPinned,
    bool HasAntiTankGrenade,
    float DistanceMeters,
    float ThrowRangeMeters,
    bool TankVisible,
    bool FriendlyBesideTank);

/// <summary>
/// The game only considers an anti-tank grenade on an update where the soldier's
/// selected target is the vehicle itself, inside 6 m to the grenade's ballistic range,
/// and then only on a one-in-three roll. This mod's target commitment keeps a nearby
/// rifleman as the selected target, so a tank rolling through a position was almost
/// never that target. The window and the range stay native; only "the tank must be my
/// current target" is replaced with "I can see an enemy tank in reach".
/// </summary>
internal static class AntiTankGrenadeDecisionCore
{
    // SoldierAI.SequentialUpdate: distFromTarget > 6 before an AT throw.
    internal const float MinimumThrowMeters = 6f;
    // A shaped-charge grenade, not a fragmentation one: only men at the tank itself.
    internal const float FriendlyClearanceMeters = 5f;

    internal static bool ShouldThrow(in AntiTankGrenadeDecisionInput input)
        => input.IsAvailableForThrow &&
           !input.IsPinned &&
           input.HasAntiTankGrenade &&
           float.IsFinite(input.DistanceMeters) &&
           float.IsFinite(input.ThrowRangeMeters) &&
           input.DistanceMeters > MinimumThrowMeters &&
           input.DistanceMeters < input.ThrowRangeMeters &&
           input.TankVisible &&
           !input.FriendlyBesideTank;
}
