using UnityEngine;

namespace ER2RealismOverhaul;

internal static class StaticAntiTankStaffing
{
    private static readonly Dictionary<int, float> NextAssignmentCheck = new();

    internal static void Update(Squad squad, float now)
    {
        if (!Settings.StaticAtStaffingEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            squad == null || squad.IsVehicleCrew)
        {
            return;
        }

        try
        {
            var leader = squad.Leader;
            if (leader == null || !leader.IsAI() || leader.IsFPSPlayer() || leader.IsOnVehicle())
                return;

            var squadId = ContactKnowledge.GetSquadId(squad);
            if (!AiState.CooldownReady(NextAssignmentCheck, squadId, now))
                return;
            NextAssignmentCheck[squadId] = now + Settings.StaticAtAssignmentCooldown.Value;

            if (!EnemyTankIsNear(leader))
                return;

            var weapon = FindAvailableStaticAtWeapon(leader);
            if (weapon == null)
                return;

            squad.SendUnitsToVehicle(weapon);
            AiState.Trace($"Static AT staffing: squad {squadId} assigned to {weapon.gameObject.name}");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Static AT staffing check failed: {ex.Message}");
        }
    }

    private static bool EnemyTankIsNear(Soldier leader)
    {
        var vehicles = Vehicle.allVehicles;
        if (vehicles == null)
            return false;

        var maxSqr = Settings.StaticAtEnemyTankRange.Value * Settings.StaticAtEnemyTankRange.Value;
        for (var i = 0; i < vehicles.Count; i++)
        {
            var vehicle = vehicles[i];
            if (vehicle == null || vehicle.life <= 0 || vehicle.GetComponent<VehicleTank>() == null)
                continue;
            if (SameFaction(vehicle.GetVehicleFaction(), leader.faction))
                continue;
            if ((vehicle.GetCenterOfUnit() - leader.transform.position).sqrMagnitude <= maxSqr)
                return true;
        }

        return false;
    }

    private static Vehicle? FindAvailableStaticAtWeapon(Soldier leader)
    {
        var vehicles = Vehicle.allVehicles;
        if (vehicles == null)
            return null;

        Vehicle? best = null;
        var bestSqr = Settings.StaticAtSearchRadius.Value * Settings.StaticAtSearchRadius.Value;
        for (var i = 0; i < vehicles.Count; i++)
        {
            var vehicle = vehicles[i];
            if (vehicle == null || vehicle.life <= 0 || !vehicle.IsStatic())
                continue;

            var vehicleFaction = vehicle.GetVehicleFaction();
            if (!string.IsNullOrEmpty(vehicleFaction) &&
                !string.Equals(vehicleFaction, Soldier.UnknownFaction, StringComparison.OrdinalIgnoreCase) &&
                !SameFaction(vehicleFaction, leader.faction))
            {
                continue;
            }

            var seat = vehicle.GetMainTurretSeat(false);
            if (seat == null || seat.unitSet != null)
                continue;

            var gun = vehicle.GetMainTurret(false)?.TryCast<TurretGun>();
            if (gun == null || gun.GetCaliber() < Settings.StaticAtMinimumCaliber.Value)
                continue;

            gun.CheckBullets(out _, out var hasArmorPiercing, out _, out _);
            if (!hasArmorPiercing)
                continue;

            var sqr = (vehicle.GetCenterOfUnit() - leader.transform.position).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;

            best = vehicle;
            bestSqr = sqr;
        }

        return best;
    }

    private static bool SameFaction(string? a, string? b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
