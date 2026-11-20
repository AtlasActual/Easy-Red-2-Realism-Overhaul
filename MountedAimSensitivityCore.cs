namespace ER2RealismOverhaul;

internal static class MountedAimSensitivityCore
{
    internal static float InputGain(float slider) => Math.Clamp(slider, 0.1f, 1f);

    // ManualRotate expects a fraction of the available mechanical travel this frame.
    internal static float ManualAxis(float degrees, float travel)
        => travel > 0f ? Math.Clamp(degrees / travel, -1f, 1f) : 0f;

    internal static float ZoomGain(float sightFov, float baseFov, float percent)
    {
        baseFov = Math.Clamp(baseFov, 1f, 179f);
        sightFov = Math.Clamp(sightFov, 1f, baseFov);
        const float halfRadians = MathF.PI / 360f;
        var opticalGain = MathF.Tan(sightFov * halfRadians) / MathF.Tan(baseFov * halfRadians);
        return 1f - (1f - opticalGain) * Math.Clamp(percent / 100f, 0f, 1f);
    }
}
