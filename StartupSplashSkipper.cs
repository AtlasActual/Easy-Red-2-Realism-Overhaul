using UnityEngine.Rendering;

namespace ER2RealismOverhaul;

internal static class StartupSplashSkipper
{
    internal static void TrySkip()
    {
        try
        {
            SplashScreen.Stop(SplashScreen.StopBehavior.StopImmediate);
            Plugin.LogSource.LogInfo("Requested an immediate stop for Unity's built-in splash sequence.");
        }
        catch (Exception exception)
        {
            // Splash behavior is cosmetic and must never prevent the gameplay patches from loading.
            Plugin.LogSource.LogWarning($"Could not skip the Unity startup splash sequence: {exception}");
        }
    }
}
