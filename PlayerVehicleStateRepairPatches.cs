using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// The native GenericGun.Fire silently returns without shooting while
/// Soldier.IsOnVehicle() is true (currentVehicleSeat != null). A seat reference
/// that survives a vehicle exit therefore kills the player's fire for the rest
/// of the session with no error and the trigger animation still playing. This
/// patch never blocks anything: it only detects an orphaned seat reference on
/// the locally controlled soldier at the moment they try to fire and clears it
/// through the game's own SetOnVehicle exit path. A genuinely seated soldier
/// (seat occupant is this soldier on a live vehicle) is left alone — native
/// intentionally disallows handheld fire from vehicle seats.
/// </summary>
[HarmonyPatch(typeof(GenericGun), nameof(GenericGun.Fire))]
internal static class WedgedSeatFireRepairPatch
{
    private static float _nextAttemptAt;
    private static bool _loggedRepair;

    [HarmonyPrefix]
    private static void Prefix(Creature user)
    {
        try
        {
            var soldier = user as Soldier;
            if (soldier == null || !soldier.IsOnVehicle())
                return;

            var local = Soldier.CurrentControlledSoldierOrNull();
            if (local == null || local.GetInstanceID() != soldier.GetInstanceID())
                return;

            var now = Time.unscaledTime;
            if (now < _nextAttemptAt)
                return;
            _nextAttemptAt = now + 1f;

            var seat = soldier.currentVehicleSeat;
            if (seat == null)
                return;

            var vehicle = seat.GetSeatVehicle();
            var occupant = seat.unitSet;
            var genuinelySeated = vehicle != null && occupant != null &&
                                  occupant.GetInstanceID() == soldier.GetInstanceID();
            if (genuinelySeated)
                return;

            soldier.SetOnVehicle(null, vehicle);
            if (!_loggedRepair)
            {
                _loggedRepair = true;
                Plugin.LogSource.LogWarning(
                    "Cleared an orphaned vehicle-seat reference on the local player that was silently blocking weapon fire; further repairs will not be logged.");
            }
        }
        catch
        {
            // The repair must never interfere with firing, whatever state a
            // partially destroyed seat or vehicle leaves behind.
        }
    }
}
