namespace ER2RealismOverhaul;

internal static class AiBehaviorTuningCore
{
    internal const float MinimumMultiplier = 0.5f;
    internal const float MaximumMultiplier = 1.5f;

    internal const int NativeMaximumSuppression = 100;
    internal const int NativeProjectileImpactSuppression = 65;
    internal const int DefaultProjectileImpactSuppression = 12;
    internal const int DefaultCrouchSuppressionThreshold = 35;
    internal const int DefaultCrouchSuppressionReleaseThreshold = 15;
    internal const int DefaultPinSuppressionThreshold = 80;
    internal const int DefaultPinReleaseSuppressionThreshold = 45;
    internal const int DefaultMountedGunnerDuckSuppressionThreshold = 80;
    internal const int DefaultMountedGunnerRiseSuppressionThreshold = 45;

    private const float CrouchBandPenalty = 0.15f;

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

    internal static int CapProjectileImpactSuppression(int amount, int configuredCap)
    {
        if (amount <= 0)
            return amount;

        return Math.Min(amount, Math.Clamp(configuredCap, 1, NativeMaximumSuppression));
    }

    internal static bool ShouldOwnCrouchedFightingPose(
        bool currentlyOwned,
        int suppression,
        int enterThreshold,
        int releaseThreshold,
        bool minimumHoldElapsed)
    {
        var enter = Math.Clamp(enterThreshold, 1, NativeMaximumSuppression);
        var release = Math.Clamp(releaseThreshold, 0, enter - 1);
        if (suppression >= enter)
            return true;
        if (!currentlyOwned)
            return false;

        return suppression > release || !minimumHoldElapsed;
    }

    internal static float SuppressionPenaltyStrength(
        int suppression,
        int crouchThreshold,
        int pinThreshold)
    {
        if (suppression <= 0)
            return 0f;

        var crouch = Math.Max(1, crouchThreshold);
        var pin = Math.Max(crouch + 1, pinThreshold);
        if (suppression < crouch)
        {
            var lowBand = Math.Clamp(suppression / (float)crouch, 0f, 1f);
            return CrouchBandPenalty * lowBand * lowBand;
        }

        var highBand = Math.Clamp((suppression - crouch) / (float)(pin - crouch), 0f, 1f);
        return CrouchBandPenalty + (1f - CrouchBandPenalty) * highBand * highBand;
    }
}
