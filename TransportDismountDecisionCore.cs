namespace ER2RealismOverhaul;

internal enum TransportThreatType
{
    Infantry,
    GroundVehicle
}

internal readonly record struct TransportThreatInput(
    TransportThreatType Type,
    float Distance,
    float Age,
    float Confidence,
    bool DirectlyVisible);

internal static class TransportDismountDecisionCore
{
    internal const float VisibleInfantryDistance = 160f;
    internal const float VisibleGroundVehicleDistance = 300f;
    internal const float ReportedInfantryDistance = 140f;
    internal const float ReportedGroundVehicleDistance = 260f;
    internal const float MaximumReportAge = 12f;
    internal const float MinimumReportConfidence = 0.30f;
    internal const float SafeUnloadSpeed = 3.5f;
    internal const float MaximumBrakingSeconds = 1.5f;

    internal static bool IsImminent(TransportThreatInput input)
    {
        if (!IsFinite(input.Distance) || input.Distance < 0f ||
            !IsFinite(input.Age) || input.Age < 0f ||
            !IsFinite(input.Confidence) || input.Confidence < 0f)
        {
            return false;
        }

        if (input.DirectlyVisible)
        {
            var visibleDistance = input.Type == TransportThreatType.GroundVehicle
                ? VisibleGroundVehicleDistance
                : VisibleInfantryDistance;
            return input.Distance <= visibleDistance;
        }

        if (input.Age > MaximumReportAge || input.Confidence < MinimumReportConfidence)
            return false;

        var reportedDistance = input.Type == TransportThreatType.GroundVehicle
            ? ReportedGroundVehicleDistance
            : ReportedInfantryDistance;
        return input.Distance <= reportedDistance;
    }

    internal static bool ReadyToUnload(
        float speed,
        float brakingSeconds,
        bool underIncomingFire)
    {
        if (!IsFinite(speed) || speed < 0f ||
            !IsFinite(brakingSeconds) || brakingSeconds < 0f)
        {
            return false;
        }

        return underIncomingFire ||
               speed <= SafeUnloadSpeed ||
               brakingSeconds >= MaximumBrakingSeconds;
    }

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);
}
