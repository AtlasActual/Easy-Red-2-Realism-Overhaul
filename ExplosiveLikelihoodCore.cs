namespace ER2RealismOverhaul;

/// <summary>
/// Decides whether an AI soldier acts on an explosive opportunity that already
/// passed every safety rule. The roll is made once per opportunity; a soldier who
/// fails it waits before the next roll, so the likelihood scales how often he fires
/// instead of eventually reaching certainty through repeated tries.
/// </summary>
internal static class ExplosiveLikelihoodCore
{
    // A failed mortar roll holds that mortarman for this long before the next one.
    internal const float MortarRetrySeconds = 10f;

    /// <param name="likelihood">0 never acts, 1 always acts.</param>
    /// <param name="roll">A uniform value in [0, 1).</param>
    internal static bool Passes(float likelihood, float roll)
    {
        if (!float.IsFinite(likelihood) || likelihood <= 0f)
            return false;

        return likelihood >= 1f || roll < likelihood;
    }
}
