using HarmonyLib;

namespace ER2RealismOverhaul;

/// <summary>
/// The native GenericGun.Fire silently returns without shooting while
/// Soldier.IsOnVehicle() is true (currentVehicleSeat != null). A seat reference
/// that survives a vehicle exit therefore kills infantry fire for the rest of
/// the session with no error and the trigger animation still playing. This
/// patch never blocks anything: it detects an orphaned seat reference on any
/// soldier at the moment they try to fire and clears it through the game's own
/// SetOnVehicle exit path. An enabled infantry character controller proves the
/// soldier is already on foot even when both stale seat references still agree.
/// A genuinely seated soldier
/// (seat occupant is this soldier on a live vehicle) is left alone — native
/// intentionally disallows handheld fire from vehicle seats.
/// </summary>
[HarmonyPatch(typeof(GenericGun), nameof(GenericGun.Fire))]
internal static class WedgedSeatFireRepairPatch
{
    private static bool _loggedRepair;

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Creature user)
    {
        try
        {
            var soldier = user as Soldier;
            if (soldier == null || !soldier.IsOnVehicle())
                return;

            var seat = soldier.currentVehicleSeat;
            var vehicle = seat?.GetSeatVehicle();
            var occupant = seat?.unitSet;
            var occupantMatches = occupant != null &&
                                  occupant.GetInstanceID() == soldier.GetInstanceID();
            var infantryControllerActive = soldier.m_controller != null &&
                                           soldier.m_controller.enabled;
            var genuinelySeated = seat != null && vehicle != null &&
                                  occupantMatches && !infantryControllerActive;
            if (genuinelySeated)
                return;

            // Let the native exit path clean up UI, animation and seat ownership
            // when this soldier is still the recorded occupant. If another unit
            // now owns the seat, only discard this soldier's stale backlink.
            if (occupant == null || occupantMatches)
            {
                try
                {
                    soldier.SetOnVehicle(null, vehicle);
                }
                catch
                {
                    // The direct backlink clear below is the minimum required
                    // for GenericGun.Fire to proceed and remains safe on foot.
                }
            }

            soldier.currentVehicleSeat = null;
            if (!_loggedRepair)
            {
                _loggedRepair = true;
                Plugin.LogSource.LogWarning(
                    "Cleared an orphaned vehicle-seat reference on an on-foot soldier that was silently blocking handheld weapon fire; further repairs will not be logged.");
            }
        }
        catch
        {
            // The repair must never interfere with firing, whatever state a
            // partially destroyed seat or vehicle leaves behind.
        }
    }
}
