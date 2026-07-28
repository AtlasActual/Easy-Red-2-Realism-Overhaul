using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace ER2RealismOverhaul;

internal static class StartupSplashSkipper
{
    private static int _startupLoadingScreenId;
    private static bool _gameSplashSkipped;

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

    internal static void BeginGameSplashSkip(LoadingScreen2 loadingScreen)
    {
        if (!LoadingScreen2.loading_first_time)
        {
            return;
        }

        _startupLoadingScreenId = loadingScreen.GetInstanceID();
        SkipGameSplash(loadingScreen);
    }

    internal static void MaintainGameSplashSkip(LoadingScreen2 loadingScreen)
    {
        if (_startupLoadingScreenId == 0 ||
            loadingScreen.GetInstanceID() != _startupLoadingScreenId)
        {
            return;
        }

        if (!LoadingScreen2.loading_first_time)
        {
            // showLogo is static, so restore its native default before any later
            // battle-loading instance can observe the startup-only override.
            LoadingScreen2.showLogo = true;
            _startupLoadingScreenId = 0;
            return;
        }

        SkipGameSplash(loadingScreen);
    }

    private static void SkipGameSplash(LoadingScreen2 loadingScreen)
    {
        try
        {
            // Easy Red 2 has a second, game-authored splash sequence in its first
            // LoadingScreen2 scene. Keep the coroutine and its asset loading intact.
            LoadingScreen2.showLogo = false;
            loadingScreen.allowSwitchSceneTime = 0f;

            var titleAnimation = loadingScreen.title_animation;
            if (titleAnimation != null)
            {
                titleAnimation.Stop();
                var titleObject = titleAnimation.gameObject;
                if (titleObject != null && titleObject.activeSelf)
                {
                    titleObject.SetActive(false);
                }
            }

            if (!_gameSplashSkipped)
            {
                _gameSplashSkipped = true;
                Plugin.LogSource.LogInfo(
                    "Skipped Easy Red 2's startup logo animation while retaining its asset loading sequence.");
            }
        }
        catch (Exception exception)
        {
            // Never let a cosmetic startup adjustment interfere with the game's loading path.
            Plugin.LogSource.LogWarning($"Could not skip Easy Red 2's startup logo animation: {exception}");
        }
    }
}

[HarmonyPatch(typeof(LoadingScreen2))]
internal static class LoadingScreenSplashPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(LoadingScreen2.Start))]
    private static void BeforeStart(LoadingScreen2 __instance)
    {
        StartupSplashSkipper.BeginGameSplashSkip(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(LoadingScreen2.Update))]
    private static void BeforeUpdate(LoadingScreen2 __instance)
    {
        // Start is a coroutine and may restore its serialized timing values after the first yield.
        StartupSplashSkipper.MaintainGameSplashSkip(__instance);
    }
}
