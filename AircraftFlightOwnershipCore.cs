namespace ER2RealismOverhaul;

/// <summary>
/// Decides whether this process may simulate the locally controlled human
/// aircraft. Flight belongs to the client that owns the native vehicle
/// synchronizer; AI and remote-player aircraft remain native.
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
}
