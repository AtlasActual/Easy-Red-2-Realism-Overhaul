namespace ER2RealismOverhaul;

internal static class AiBehaviorTuningCore
{
    internal const float MinimumMultiplier = 0.5f;
    internal const float MaximumMultiplier = 1.5f;

    internal static float ClampMultiplier(float multiplier) =>
        Math.Clamp(multiplier, MinimumMultiplier, MaximumMultiplier);

    internal static float ScaleUp(float baseline, float multiplier) =>
        baseline * ClampMultiplier(multiplier);

    internal static float ScaleDown(float baseline, float multiplier) =>
        baseline / ClampMultiplier(multiplier);

    internal static int ScaleThreshold(int baseline, float multiplier, int minimum, int maximum) =>
        Math.Clamp(
            (int)MathF.Round(baseline * ClampMultiplier(multiplier)),
            minimum,
            maximum);
}
