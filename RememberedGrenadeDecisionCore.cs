namespace ER2RealismOverhaul;

internal readonly record struct RememberedGrenadeDecisionInput(
    bool SystemEnabled,
    bool HasAuthority,
    bool IsAutonomous,
    bool IsAlive,
    bool IsStationary,
    bool IsAvailableForThrow,
    bool HasFragmentationGrenade,
    bool HasConfirmedTarget,
    bool LastKnownPositionMatchesTarget,
    float ObservationAgeSeconds,
    float MemorySeconds,
    bool HasDirectSight,
    bool IsWithinRange,
    bool IsBlastAreaClear,
    bool HasClearThrowArc);

internal static class RememberedGrenadeDecisionCore
{
    internal static bool ShouldAttempt(in RememberedGrenadeDecisionInput input)
        => input.SystemEnabled &&
           input.HasAuthority &&
           input.IsAutonomous &&
           input.IsAlive &&
           input.IsStationary &&
           input.IsAvailableForThrow &&
           input.HasFragmentationGrenade &&
           input.HasConfirmedTarget &&
           input.LastKnownPositionMatchesTarget &&
           input.ObservationAgeSeconds >= 0f &&
           input.ObservationAgeSeconds <= input.MemorySeconds &&
           !input.HasDirectSight &&
           input.IsWithinRange &&
           input.IsBlastAreaClear &&
           input.HasClearThrowArc;
}
