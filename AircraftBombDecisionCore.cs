namespace ER2RealismOverhaul;

internal readonly record struct AircraftBombReleaseInput(
    bool HasValidPredictedImpact,
    bool HasUsableGroundTarget,
    bool TargetAlive,
    bool TargetHostile,
    float HorizontalMissMeters,
    float MaximumTargetMissMeters);

internal static class AircraftBombDecisionCore
{
    internal static bool TargetSupportsRelease(AircraftBombReleaseInput input)
        => input.HasValidPredictedImpact &&
           input.HasUsableGroundTarget &&
           input.TargetAlive &&
           input.TargetHostile &&
           float.IsFinite(input.HorizontalMissMeters) &&
           float.IsFinite(input.MaximumTargetMissMeters) &&
           input.HorizontalMissMeters >= 0f &&
           input.MaximumTargetMissMeters > 0f &&
           input.HorizontalMissMeters <= input.MaximumTargetMissMeters;
}
