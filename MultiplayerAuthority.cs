namespace ER2RealismOverhaul;

using UnityEngine;

internal static class MultiplayerAuthority
{
    private static bool _loggedFailure;
    private static bool _cachedAuthority;
    private static float _nextCheckAt = -1f;

    internal static bool CanMutateGameplay()
    {
        try
        {
            var now = Time.unscaledTime;
            if (now < _nextCheckAt)
                return _cachedAuthority;

            _nextCheckAt = now + 0.25f;
            _cachedAuthority = !Lua_API.isOnline() || Lua_API.isMasterClient();
            return _cachedAuthority;
        }
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.LogSource.LogWarning(
                    $"Could not determine multiplayer authority; gameplay patches will stay inactive: {ex.Message}");
            }

            return false;
        }
    }
}
