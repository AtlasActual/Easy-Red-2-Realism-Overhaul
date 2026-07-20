namespace ER2RealismOverhaul;

/// <summary>
/// Selects a bounded, deterministic subset from a distance-ordered cover list.
/// The nearest half preserves responsive movement while the distributed half
/// keeps deeper trenches and building slots eligible for detailed evaluation.
/// </summary>
internal static class CoverCandidateSamplingCore
{
    internal static int[] SelectIndices(int candidateCount, int budget, int nearestCount)
    {
        if (candidateCount <= 0 || budget <= 0)
            return Array.Empty<int>();

        budget = Math.Min(candidateCount, budget);
        if (candidateCount == budget)
            return Enumerable.Range(0, candidateCount).ToArray();

        var nearest = Math.Min(Math.Max(0, nearestCount), budget);
        var indices = new int[budget];
        for (var i = 0; i < nearest; i++)
            indices[i] = i;

        var distributed = budget - nearest;
        var remaining = candidateCount - nearest;
        for (var i = 0; i < distributed; i++)
        {
            var offset = distributed == 1
                ? (remaining - 1) / 2
                : (int)Math.Round(i * (remaining - 1d) / (distributed - 1d));
            indices[nearest + i] = nearest + offset;
        }

        return indices;
    }
}
