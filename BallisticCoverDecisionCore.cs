namespace ER2RealismOverhaul;

internal readonly record struct BallisticBarrierInput(
    float SurfaceCost,
    float ResistancePerMeter,
    float ThicknessMeters,
    bool IsHardStop = false,
    bool HasMeasuredExit = true);

internal static class BallisticCoverDecisionCore
{
    // The penetration system starts a representative ordinary rifle round at
    // this budget before applying the user-configured strength multiplier.
    internal const float RepresentativeOrdinaryRoundBudget = 0.82f;

    // Obscuration is not protection. A sampled body region only counts as
    // protected when the intervening material removes most of an ordinary
    // rifle round's usable penetration energy.
    internal const float MeaningfulRayProtection = 0.60f;
    internal const float MeaningfulPostureProtection = 0.60f;

    internal static float ResistanceCost(
        float surfaceCost,
        float resistancePerMeter,
        float thicknessMeters)
    {
        if (!IsFinite(surfaceCost) || !IsFinite(resistancePerMeter) ||
            !IsFinite(thicknessMeters))
        {
            return 0f;
        }

        return Math.Max(0f, surfaceCost) +
               Math.Max(0f, resistancePerMeter) * Math.Max(0f, thicknessMeters);
    }

    internal static float ProtectionFromResistance(
        float initialBudget,
        float cumulativeResistance,
        bool isHardStop = false)
    {
        if (isHardStop)
            return 1f;
        if (!IsFinite(initialBudget) || initialBudget <= 0f ||
            !IsFinite(cumulativeResistance) || cumulativeResistance <= 0f)
        {
            return 0f;
        }

        return Math.Clamp(cumulativeResistance / initialBudget, 0f, 1f);
    }

    internal static float RateProtection(
        float initialBudget,
        IReadOnlyList<BallisticBarrierInput> barriers)
    {
        if (barriers == null || barriers.Count == 0)
            return 0f;

        var cumulativeResistance = 0f;
        for (var i = 0; i < barriers.Count; i++)
        {
            var barrier = barriers[i];
            if (barrier.IsHardStop || !barrier.HasMeasuredExit)
                return 1f;

            cumulativeResistance += ResistanceCost(
                barrier.SurfaceCost,
                barrier.ResistancePerMeter,
                barrier.ThicknessMeters);
        }

        return ProtectionFromResistance(initialBudget, cumulativeResistance);
    }

    internal static bool IsMeaningfulRay(float protectionFraction)
        => IsFinite(protectionFraction) &&
           protectionFraction >= MeaningfulRayProtection;

    internal static bool IsNonProtectiveScreenDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return false;

        var normalized = description.ToLowerInvariant();
        return normalized.Contains("barbed", StringComparison.Ordinal) ||
               normalized.Contains("barbwire", StringComparison.Ordinal) ||
               normalized.Contains("barb_wire", StringComparison.Ordinal) ||
               normalized.Contains("razorwire", StringComparison.Ordinal) ||
               normalized.Contains("razor_wire", StringComparison.Ordinal) ||
               normalized.Contains("chainlink", StringComparison.Ordinal) ||
               normalized.Contains("chain_link", StringComparison.Ordinal) ||
               normalized.Contains("chain link", StringComparison.Ordinal) ||
               normalized.Contains("wire", StringComparison.Ordinal);
    }

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);
}
