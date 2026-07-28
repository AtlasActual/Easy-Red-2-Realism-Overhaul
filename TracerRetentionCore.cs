namespace ER2RealismOverhaul;

internal static class TracerRetentionCore
{
    internal static bool ShouldKeep(
        bool baseGameUsesTracer,
        bool recognizedMachineGun,
        float retention,
        float randomSample)
    {
        if (!baseGameUsesTracer || !recognizedMachineGun)
            return false;

        if (retention <= 0f)
            return false;
        if (retention >= 1f)
            return true;

        return randomSample < retention;
    }
}
