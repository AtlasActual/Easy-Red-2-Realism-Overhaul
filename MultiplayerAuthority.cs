namespace ER2RealismOverhaul;

internal static class MultiplayerAuthority
{
    private static bool _loggedFailure;

    internal static bool CanMutateGameplay()
    {
        try
        {
            // Lua_API.isOnline() is only PhotonNetwork.InRoom. During a real
            // multiplayer battle load it therefore reports "offline" while Photon
            // is still joining, which used to authorize every gameplay patch on a
            // remote client. MatchData records the multiplayer intent before that
            // transition starts, so fail closed until the room is established and
            // then permit only its master client.
            var matchData = MatchData.data;
            var multiplayerIntent = matchData != null && matchData.isMultiplayer;
            return GroundAuthorityCore.CanMutate(
                multiplayerIntent,
                Photon.Pun.PhotonNetwork.InRoom,
                Photon.Pun.PhotonNetwork.IsMasterClient);
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
