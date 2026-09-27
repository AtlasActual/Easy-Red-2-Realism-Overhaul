namespace ER2RealismOverhaul;

internal readonly record struct FragGrenadeDecisionInput(
    bool IsAvailableForThrow,
    bool IsPinned,
    bool HasFragmentationGrenade,
    bool HasConfirmedVisibleInfantry,
    bool CooldownReady,
    bool IsMoving,
    bool IsAssaulting,
    bool IsProne,
    float DistanceMeters,
    float MinimumRangeMeters,
    float MaximumRangeMeters,
    float NativeThrowRangeMeters);

internal static class FragGrenadeDecisionCore
{
    // Native SequentialUpdate only attempts frags above 20 suppression and after
    // a 34% roll. A calm soldier should also use a safe, confirmed opportunity.
    // Blast clearance and the arc are checked by the runtime before the throw.
    internal static bool ShouldAttempt(in FragGrenadeDecisionInput input)
        => input.IsAvailableForThrow &&
           !input.IsPinned &&
           input.HasFragmentationGrenade &&
           input.HasConfirmedVisibleInfantry &&
           input.CooldownReady &&
           CanUsePlatform(input.IsMoving, input.IsAssaulting, input.IsProne) &&
           float.IsFinite(input.DistanceMeters) &&
           float.IsFinite(input.MinimumRangeMeters) &&
           float.IsFinite(input.MaximumRangeMeters) &&
           float.IsFinite(input.NativeThrowRangeMeters) &&
           input.DistanceMeters >= input.MinimumRangeMeters &&
           input.DistanceMeters <= input.MaximumRangeMeters &&
           input.DistanceMeters < input.NativeThrowRangeMeters;

    internal static bool CanUsePlatform(bool moving, bool assaulting, bool prone)
        => !moving || (assaulting && !prone);
}
