namespace ER2RealismOverhaul;

/// <summary>
/// Decides whether this process may simulate a human or AI aircraft. Player
/// flight belongs to the client that owns the native vehicle synchronizer;
/// experimental AI flight remains authoritative-host only.
/// </summary>
internal static class AircraftFlightOwnershipCore
{
    internal static bool CanSimulate(
        bool enabled,
        bool isLocallyControlledVehicle,
        bool hasHumanDriver,
        bool usesRealisticControls,
        bool multiplayerIntent,
        bool inNetworkRoom,
        bool ownsNetworkSynchronizer)
    {
        if (!enabled ||
            !isLocallyControlledVehicle ||
            !hasHumanDriver ||
            !usesRealisticControls)
        {
            return false;
        }

        if (!multiplayerIntent && !inNetworkRoom)
            return true;

        // Fail closed while joining and during ownership transfer. Once in the
        // room, exactly one client may drive the native synchronized rigidbody.
        return inNetworkRoom && ownsNetworkSynchronizer;
    }

    internal static bool CanSimulateAi(
        bool enabled,
        bool experimentalAiEnabled,
        bool hasAiDriver,
        bool multiplayerIntent,
        bool inNetworkRoom,
        bool isMasterClient)
    {
        if (!enabled || !experimentalAiEnabled || !hasAiDriver)
            return false;

        if (!multiplayerIntent && !inNetworkRoom)
            return true;

        // Fail closed while joining. In a room, only the master simulates the
        // shared AI rigidbody so clients cannot apply duplicate tuning.
        return inNetworkRoom && isMasterClient;
    }
}
