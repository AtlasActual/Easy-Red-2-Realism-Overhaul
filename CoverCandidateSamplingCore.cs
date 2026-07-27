namespace ER2RealismOverhaul;

/// <summary>
/// Selects a bounded, deterministic subset from a distance-ordered cover list.
/// The nearest half preserves responsive movement while the distributed half
/// keeps deeper trenches and building slots eligible for detailed evaluation.
/// Results are ordered so a runtime geometry cutoff sees both groups instead
/// of spending its whole per-frame budget on the nearest candidates.
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
        var nearestIndices = new int[nearest];
        for (var i = 0; i < nearest; i++)
            nearestIndices[i] = i;

        var distributed = budget - nearest;
        var remaining = candidateCount - nearest;
        var distributedIndices = new int[distributed];
        for (var i = 0; i < distributed; i++)
        {
            var offset = distributed == 1
                ? (remaining - 1) / 2
                : (int)Math.Round(i * (remaining - 1d) / (distributed - 1d));
            distributedIndices[i] = nearest + offset;
        }

        var indices = new int[budget];
        var output = 0;
        if (nearest > 0)
            indices[output++] = nearestIndices[0];

        // Evaluate the broad sample outside-in. Even when runtime raycast work
        // stops after five candidates, the soldier compares nearby cover with
        // both deep and intermediate building/trench slots.
        var low = 0;
        var high = distributed - 1;
        while (low <= high)
        {
            indices[output++] = distributedIndices[high--];
            if (low <= high)
                indices[output++] = distributedIndices[low++];
        }

        for (var i = 1; i < nearest; i++)
            indices[output++] = nearestIndices[i];

        return indices;
    }
}
