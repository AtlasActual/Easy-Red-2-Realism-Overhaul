using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Global defender emplacement allocator. It inventories a complete objective
/// position, protects the mobile reserve, and sends one selected soldier through
/// the native vehicle destination API for each viable gun.
/// </summary>
internal static class StaticAntiTankStaffing
{
    private const float ObjectiveDefensiveMarginMeters = 12f;
    private const float GlobalInventoryCadenceSeconds = 0.75f;
    private const float TransitProgressEpsilonMeters = 0.75f;
    private const float UnreachableTransitSeconds = 18f;
    private const float PlayerOrderReleaseGraceSeconds = 12f;

    private static readonly Dictionary<int, WeaponAssignment> AssignmentsByWeapon = new();
    private static readonly Dictionary<int, int> WeaponBySoldier = new();
    private static readonly Dictionary<int, PlayerWeaponOverride> PlayerOverridesByWeapon = new();
    private static float _nextInventoryAt;
    private static bool _updating;

    internal static void Update(Squad squad, float now)
    {
        // Static weapons are part of commander doctrine. There is deliberately no
        // independent enable switch that can silently disable a defensive position.
        if (!Settings.CommanderEnabled.Value || !MultiplayerAuthority.CanMutateGameplay() ||
            squad == null || squad.IsVehicleCrew || !GroundAiDirector.IsDefendingSquad(squad) ||
            now < _nextInventoryAt || _updating)
        {
            return;
        }

        _nextInventoryAt = now + GlobalInventoryCadenceSeconds;
        _updating = true;
        try
        {
            InventoryAndAllocate(squad, now);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Defensive emplacement allocation failed: {ex.Message}");
        }
        finally
        {
            _updating = false;
        }
    }

    internal static void OnPostureOrObjectiveChanged(string faction, int revision)
    {
        foreach (var assignment in AssignmentsByWeapon.Values.Where(assignment =>
                     string.Equals(assignment.Faction, faction, StringComparison.OrdinalIgnoreCase) &&
                     assignment.ObjectiveRevision != revision).ToArray())
        {
            Release(assignment, "posture/objective change");
        }
        _nextInventoryAt = 0f;
    }

    internal static void YieldToPlayerOrder(Squad squad, Vehicle weapon, float now)
    {
        if (squad == null || weapon == null || !weapon.IsStatic())
            return;

        var assignmentsToRelease = new Dictionary<int, WeaponAssignment>();
        var weaponId = weapon.GetInstanceID();
        if (AssignmentsByWeapon.TryGetValue(weaponId, out var weaponAssignment))
            assignmentsToRelease[weaponAssignment.WeaponId] = weaponAssignment;

        for (var index = 0; index < squad.CountMembers; index++)
        {
            var member = squad.GetMember(index);
            if (member == null ||
                !WeaponBySoldier.TryGetValue(member.GetInstanceID(), out var assignedWeaponId) ||
                !AssignmentsByWeapon.TryGetValue(assignedWeaponId, out var memberAssignment))
            {
                continue;
            }

            assignmentsToRelease[memberAssignment.WeaponId] = memberAssignment;
        }

        foreach (var assignment in assignmentsToRelease.Values)
            ReleaseForPlayerOrder(assignment);

        PlayerOverridesByWeapon[weaponId] = new PlayerWeaponOverride(
            weaponId,
            ContactKnowledge.GetSquadId(squad),
            squad,
            weapon,
            now,
            now);
        AiState.Trace(
            $"Player vehicle order superseded defender gun leases for weapon {weaponId}");
    }

    internal static bool IsPlayerOrderedEntrant(Vehicle weapon, Soldier soldier)
    {
        if (weapon == null || soldier == null ||
            !PlayerOverridesByWeapon.TryGetValue(weapon.GetInstanceID(), out var playerOrder) ||
            playerOrder.SquadId != ContactKnowledge.GetSquadId(soldier))
        {
            return false;
        }

        return Time.time - playerOrder.OrderedAt <= PlayerOrderReleaseGraceSeconds ||
               MemberIsUsingWeapon(soldier, weapon);
    }

    internal static void ResetBattle()
    {
        foreach (var assignment in AssignmentsByWeapon.Values.ToArray())
            Release(assignment, "battle reset");
        AssignmentsByWeapon.Clear();
        WeaponBySoldier.Clear();
        PlayerOverridesByWeapon.Clear();
        _nextInventoryAt = 0f;
        _updating = false;
    }

    private static void InventoryAndAllocate(Squad trigger, float now)
    {
        RefreshPlayerOverrides(now);
        var faction = trigger.Leader?.faction ?? string.Empty;
        if (string.IsNullOrWhiteSpace(faction) ||
            !TryGetDefensiveArea(trigger, out var center, out var radius))
        {
            return;
        }

        var revision = GroundAiDirector.CurrentObjectiveRevision(faction);
        RefreshAssignments(faction, revision, now);
        var squads = CollectDefensiveSquads(faction, center, radius);
        if (squads.Count == 0)
            return;

        var armor = CollectReportedArmor(faction, center);
        var runtimeWeapons = CollectWeapons(faction, center, radius, armor);
        if (runtimeWeapons.Count == 0)
            return;

        var squadCandidates = new List<DefenderSquadCandidate>(squads.Count);
        var crewCandidates = new List<DefenderCrewCandidate>();
        foreach (var candidateSquad in squads)
        {
            var combatReadyOnFoot = 0;
            var effectiveStrength = 0f;
            for (var index = 0; index < candidateSquad.CountMembers; index++)
            {
                var member = candidateSquad.GetMember(index);
                if (member == null || !member.CanFight())
                    continue;
                // CanFight is the stable public readiness signal exposed by the
                // game. Keep the allocator package-free instead of depending on
                // a private health field that changes between game builds.
                effectiveStrength += 1f;
                if (member.IsOnVehicle() || WeaponBySoldier.ContainsKey(member.GetInstanceID()))
                    continue;

                combatReadyOnFoot++;
                if (!AiOwnership.IsAutonomous(member))
                    continue;
                crewCandidates.Add(new DefenderCrewCandidate(
                    member.GetInstanceID(),
                    ContactKnowledge.GetSquadId(candidateSquad),
                    candidateSquad.Leader != null &&
                    candidateSquad.Leader.GetInstanceID() == member.GetInstanceID(),
                    member.IsMedic(),
                    member.IsRadioman(),
                    member.IsATUnit(),
                    ReachableForAssignment(member, center, radius)));
            }

            squadCandidates.Add(new DefenderSquadCandidate(
                ContactKnowledge.GetSquadId(candidateSquad),
                effectiveStrength,
                combatReadyOnFoot,
                GroundAiDirector.IsReserveSquad(candidateSquad),
                !CommanderMvp.OwnsSquad(candidateSquad)));
        }

        var weaponCandidates = runtimeWeapons.Select(runtime => new DefensiveWeaponCandidate(
            runtime.Id,
            true,
            runtime.HasArmorPiercing,
            runtime.Caliber,
            runtime.AmmunitionScore,
            runtime.ThreatCoverage,
            runtime.InfantryCoverage)).ToArray();
        var plan = DefenderAllocationCore.Allocate(
            squadCandidates, crewCandidates, weaponCandidates, armor.Count > 0);

        var soldiers = crewCandidates.ToDictionary(candidate => candidate.SoldierId);
        var squadsById = squads.ToDictionary(ContactKnowledge.GetSquadId);
        var weaponsById = runtimeWeapons.ToDictionary(candidate => candidate.Id);
        foreach (var assignment in plan.WeaponAssignments)
        {
            if (!soldiers.ContainsKey(assignment.SoldierId) ||
                !squadsById.TryGetValue(assignment.SquadId, out var donor) ||
                !weaponsById.TryGetValue(assignment.WeaponId, out var weaponInfo))
            {
                continue;
            }

            var soldier = FindMember(donor, assignment.SoldierId);
            if (soldier == null || !GroundAiDirector.ProtectStaticWeaponAssignment(
                    soldier, weaponInfo.Weapon, revision, now))
            {
                continue;
            }

            var distance = Vector3.Distance(soldier.transform.position, weaponInfo.Position);
            var stamp = new WeaponAssignment(
                weaponInfo.Id,
                soldier.GetInstanceID(),
                faction,
                revision,
                soldier,
                weaponInfo.Weapon,
                now,
                now,
                distance);
            AssignmentsByWeapon[weaponInfo.Id] = stamp;
            WeaponBySoldier[soldier.GetInstanceID()] = weaponInfo.Id;

            var vehicleDestination = weaponInfo.Weapon.TryCast<AiDestination>();
            if (vehicleDestination == null ||
                !GroundAiDirector.ExecuteProtectedInfantryAssignment(
                    soldier, weaponInfo.Weapon, () => soldier.CoverPosition(vehicleDestination)))
            {
                Release(stamp, "native destination rejected");
                continue;
            }

            AiState.Trace(
                $"Defender gun lease: soldier {stamp.SoldierId} squad {assignment.SquadId} -> " +
                $"weapon {stamp.WeaponId} AP={weaponInfo.HasArmorPiercing} caliber={weaponInfo.Caliber:F0}");
        }
    }

    private static List<Squad> CollectDefensiveSquads(
        string faction,
        Vector3 objective,
        float objectiveRadius)
    {
        var result = new List<Squad>();
        var all = Squad.AllSquads;
        if (all == null)
            return result;

        foreach (var pair in all)
        {
            var squad = pair.Value;
            var leader = squad?.Leader;
            if (squad == null || leader == null || squad.IsVehicleCrew ||
                !SameFaction(leader.faction, faction) || !CommanderMvp.OwnsSquad(squad) ||
                !TryGetDefensiveArea(squad, out var squadCenter, out var squadRadius))
            {
                continue;
            }

            var centersClose = HorizontalDistance(squadCenter, objective) <=
                               Mathf.Max(objectiveRadius, squadRadius);
            if (centersClose)
                result.Add(squad);
        }

        return result.GroupBy(ContactKnowledge.GetSquadId).Select(group => group.First())
            .OrderBy(ContactKnowledge.GetSquadId).ToList();
    }

    private static List<RuntimeWeapon> CollectWeapons(
        string faction,
        Vector3 center,
        float radius,
        IReadOnlyList<Vehicle> reportedArmor)
    {
        var result = new List<RuntimeWeapon>();
        var vehicles = Vehicle.allVehicles;
        if (vehicles == null)
            return result;

        for (var index = 0; index < vehicles.Count; index++)
        {
            var weapon = vehicles[index];
            if (weapon == null || PlayerOverridesByWeapon.ContainsKey(weapon.GetInstanceID()) ||
                !TryDescribeViableWeapon(weapon, faction, center, radius, reportedArmor, out var info) ||
                AssignmentsByWeapon.ContainsKey(info.Id))
            {
                continue;
            }
            result.Add(info);
        }

        return result.OrderBy(info => info.Id).ToList();
    }

    private static bool TryDescribeViableWeapon(
        Vehicle weapon,
        string faction,
        Vector3 center,
        float radius,
        IReadOnlyList<Vehicle> reportedArmor,
        out RuntimeWeapon info)
    {
        info = default;
        if (weapon == null || weapon.life <= 0 || !weapon.IsStatic())
            return false;

        var weaponFaction = weapon.GetVehicleFaction();
        if (!string.IsNullOrWhiteSpace(weaponFaction) &&
            !string.Equals(weaponFaction, Soldier.UnknownFaction, StringComparison.OrdinalIgnoreCase) &&
            !SameFaction(weaponFaction, faction))
        {
            return false;
        }

        var seat = weapon.GetMainTurretSeat(false);
        if (seat == null || seat.unitSet != null)
            return false;
        var gun = weapon.GetMainTurret(false)?.TryCast<TurretGun>();
        if (gun == null)
            return false;
        gun.CheckBullets(out var primary, out var armorPiercing, out var secondary, out var special);
        if (!primary && !armorPiercing && !secondary && !special)
            return false;

        var position = weapon.GetCenterOfUnit();
        if (!IsFinite(position) || !DefensivePositioningCore.IsInsideArea(
                new MapPoint(position.x, position.z),
                new MapPoint(center.x, center.z),
                radius))
        {
            return false;
        }

        var threatCoverage = 0f;
        foreach (var tank in reportedArmor)
        {
            var distance = HorizontalDistance(position, tank.GetCenterOfUnit());
            threatCoverage = Mathf.Max(threatCoverage,
                1f - Mathf.Clamp01(distance / Mathf.Max(1f, Settings.StaticAtEnemyTankRange.Value)));
        }

        var ammoKinds = (primary ? 1 : 0) + (armorPiercing ? 1 : 0) +
                        (secondary ? 1 : 0) + (special ? 1 : 0);
        info = new RuntimeWeapon(
            weapon.GetInstanceID(),
            weapon,
            position,
            armorPiercing,
            Mathf.Max(0f, gun.GetCaliber()),
            ammoKinds / 4f,
            threatCoverage,
            Mathf.Clamp01(0.65f + gun.GetCaliber() / 200f));
        return true;
    }

    private static List<Vehicle> CollectReportedArmor(string faction, Vector3 center)
    {
        var result = new List<Vehicle>();
        var vehicles = Vehicle.allVehicles;
        if (vehicles == null)
            return result;
        var maximum = Mathf.Max(1f, Settings.StaticAtEnemyTankRange.Value);
        for (var index = 0; index < vehicles.Count; index++)
        {
            var vehicle = vehicles[index];
            if (vehicle == null || vehicle.life <= 0 || vehicle.GetComponent<VehicleTank>() == null ||
                !ResourcesManager.IsEnemyFaction(faction, vehicle.GetVehicleFaction()) ||
                HorizontalDistance(center, vehicle.GetCenterOfUnit()) > maximum)
            {
                continue;
            }
            result.Add(vehicle);
        }
        return result;
    }

    private static void RefreshAssignments(string faction, int revision, float now)
    {
        foreach (var assignment in AssignmentsByWeapon.Values.Where(assignment =>
                     SameFaction(assignment.Faction, faction)).ToArray())
        {
            if (assignment.ObjectiveRevision != revision ||
                assignment.Soldier == null || !assignment.Soldier.CanFight() ||
                assignment.Weapon == null || assignment.Weapon.life <= 0 ||
                !WeaponStillHasAmmunition(assignment.Weapon) ||
                !CommanderMvp.OwnsSquad(assignment.Soldier.joinedSquad))
            {
                Release(assignment, "invalid, empty, dead, or externally owned");
                continue;
            }

            if (assignment.Soldier.IsOnVehicle())
            {
                var mounted = assignment.Soldier.GetCurrentVehicle();
                if (mounted != null && mounted.GetInstanceID() == assignment.WeaponId)
                {
                    assignment.LastProgressAt = now;
                    assignment.LastDistance = 0f;
                    continue;
                }

                Release(assignment, "soldier mounted another vehicle");
                continue;
            }

            var seat = assignment.Weapon.GetMainTurretSeat(false);
            var seatOccupantId = seat?.unitSet != null
                ? seat.unitSet.GetInstanceID()
                : 0;
            if (StaticWeaponAssignmentCore.SeatPreventsTransit(
                    seat != null,
                    seatOccupantId,
                    assignment.SoldierId))
            {
                Release(assignment, "station occupied externally");
                continue;
            }

            // Lethal danger interrupts movement but does not consume the protected
            // assignment. The progress timeout resumes once the hazard clears.
            if (assignment.Soldier.IsOnFire ||
                AiState.IsFlameEvading(assignment.SoldierId, now))
            {
                assignment.LastProgressAt = now;
                continue;
            }

            var distance = Vector3.Distance(
                assignment.Soldier.transform.position, assignment.Weapon.GetCenterOfUnit());
            var destinationTargetsAssignedWeapon = MemberIsUsingWeapon(
                assignment.Soldier, assignment.Weapon);
            if (StaticWeaponAssignmentCore.ShouldReassertDestination(
                    mountedOnAssignedWeapon: false,
                    destinationTargetsAssignedWeapon: destinationTargetsAssignedWeapon,
                    emergencyInterrupted: false))
            {
                var vehicleDestination = assignment.Weapon.TryCast<AiDestination>();
                if (vehicleDestination == null ||
                    !GroundAiDirector.ExecuteProtectedInfantryAssignment(
                        assignment.Soldier,
                        assignment.Weapon,
                        () => assignment.Soldier.CoverPosition(vehicleDestination)))
                {
                    Release(assignment, "protected destination could not be restored");
                    continue;
                }

                assignment.LastDistance = distance;
                assignment.LastProgressAt = now;
                AiState.Trace(
                    $"Defender gun lease restored: soldier {assignment.SoldierId} -> weapon {assignment.WeaponId}");
            }

            if (distance + TransitProgressEpsilonMeters < assignment.LastDistance)
            {
                assignment.LastDistance = distance;
                assignment.LastProgressAt = now;
            }
            else if (now - assignment.LastProgressAt >= UnreachableTransitSeconds)
            {
                Release(assignment, "unreachable path timeout");
            }
        }
    }

    private static void RefreshPlayerOverrides(float now)
    {
        foreach (var playerOrder in PlayerOverridesByWeapon.Values.ToArray())
        {
            if (playerOrder.Weapon == null || playerOrder.Weapon.life <= 0 ||
                playerOrder.Squad == null)
            {
                PlayerOverridesByWeapon.Remove(playerOrder.WeaponId);
                continue;
            }

            var active = false;
            for (var index = 0; index < playerOrder.Squad.CountMembers; index++)
            {
                var member = playerOrder.Squad.GetMember(index);
                if (member != null && MemberIsUsingWeapon(member, playerOrder.Weapon))
                {
                    active = true;
                    break;
                }
            }

            if (active)
            {
                playerOrder.LastObservedAt = now;
                continue;
            }

            if (now - playerOrder.LastObservedAt > PlayerOrderReleaseGraceSeconds)
                PlayerOverridesByWeapon.Remove(playerOrder.WeaponId);
        }
    }

    private static bool MemberIsUsingWeapon(Soldier soldier, Vehicle weapon)
    {
        try
        {
            var currentVehicle = soldier.IsOnVehicle() ? soldier.GetCurrentVehicle() : null;
            if (currentVehicle != null &&
                currentVehicle.GetInstanceID() == weapon.GetInstanceID())
            {
                return true;
            }

            var destinationVehicle = soldier.VehicleToOperate();
            return destinationVehicle != null &&
                   destinationVehicle.GetInstanceID() == weapon.GetInstanceID();
        }
        catch
        {
            return false;
        }
    }

    private static bool WeaponStillHasAmmunition(Vehicle weapon)
    {
        var gun = weapon.GetMainTurret(false)?.TryCast<TurretGun>();
        if (gun == null)
            return false;
        gun.CheckBullets(out var primary, out var armorPiercing, out var secondary, out var special);
        return primary || armorPiercing || secondary || special;
    }

    private static bool TryGetDefensiveArea(Squad squad, out Vector3 center, out float radius)
    {
        center = default;
        radius = 0f;
        try
        {
            if (GroundAiDirector.TryGetCommanderDefensiveArea(
                    squad, out center, out var commanderRadius))
            {
                radius = Mathf.Max(
                    Mathf.Max(55f, Settings.StaticAtSearchRadius.Value),
                    Mathf.Max(0f, commanderRadius));
                return true;
            }

            if (squad.order != Order.defend)
                return false;
            center = squad.moveOrderPosition;
            if (!IsFinite(center) || !float.IsFinite(squad.moveOrderRadius))
                return false;
            radius = Mathf.Max(
                Mathf.Max(55f, Settings.StaticAtSearchRadius.Value),
                Mathf.Max(0f, squad.moveOrderRadius) + ObjectiveDefensiveMarginMeters);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ReachableForAssignment(Soldier soldier, Vector3 center, float radius)
        => soldier != null && IsFinite(soldier.transform.position) &&
           HorizontalDistance(soldier.transform.position, center) <= radius + 35f;

    private static Soldier? FindMember(Squad squad, int soldierId)
    {
        for (var index = 0; index < squad.CountMembers; index++)
        {
            var member = squad.GetMember(index);
            if (member != null && member.GetInstanceID() == soldierId)
                return member;
        }
        return null;
    }

    private static void Release(WeaponAssignment assignment, string reason)
    {
        AssignmentsByWeapon.Remove(assignment.WeaponId);
        WeaponBySoldier.Remove(assignment.SoldierId);
        GroundAiDirector.ReleaseInfantryAssignment(assignment.Soldier);
        AiState.Trace(
            $"Defender gun vacancy: weapon {assignment.WeaponId}, soldier {assignment.SoldierId}, {reason}");
    }

    private static void ReleaseForPlayerOrder(WeaponAssignment assignment)
    {
        Release(assignment, "superseded by player vehicle order");
        try
        {
            if (assignment.Soldier == null || assignment.Soldier.IsOnVehicle())
                return;

            var destinationVehicle = assignment.Soldier.VehicleToOperate();
            if (destinationVehicle != null &&
                destinationVehicle.GetInstanceID() == assignment.WeaponId)
            {
                // CoverPosition(null) is the native way to leave an AiDestination.
                // It also gives the vehicle back any seat claimed while the old
                // defender was travelling, before SendUnitsToVehicle counts space.
                ContactResponse.ExecuteOwnedCoverWrite(
                    assignment.Soldier,
                    () => assignment.Soldier.CoverPosition(null!));
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning(
                $"Could not clear superseded gun destination {assignment.WeaponId}: {ex.Message}");
        }
    }

    private static float HorizontalDistance(Vector3 first, Vector3 second)
    {
        var x = first.x - second.x;
        var z = first.z - second.z;
        return Mathf.Sqrt(x * x + z * z);
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private static bool SameFaction(string? first, string? second)
        => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private readonly record struct RuntimeWeapon(
        int Id,
        Vehicle Weapon,
        Vector3 Position,
        bool HasArmorPiercing,
        float Caliber,
        float AmmunitionScore,
        float ThreatCoverage,
        float InfantryCoverage);

    private sealed class WeaponAssignment
    {
        internal readonly int WeaponId;
        internal readonly int SoldierId;
        internal readonly string Faction;
        internal readonly int ObjectiveRevision;
        internal readonly Soldier Soldier;
        internal readonly Vehicle Weapon;
        internal readonly float AssignedAt;
        internal float LastProgressAt;
        internal float LastDistance;

        internal WeaponAssignment(
            int weaponId,
            int soldierId,
            string faction,
            int objectiveRevision,
            Soldier soldier,
            Vehicle weapon,
            float assignedAt,
            float lastProgressAt,
            float lastDistance)
        {
            WeaponId = weaponId;
            SoldierId = soldierId;
            Faction = faction;
            ObjectiveRevision = objectiveRevision;
            Soldier = soldier;
            Weapon = weapon;
            AssignedAt = assignedAt;
            LastProgressAt = lastProgressAt;
            LastDistance = lastDistance;
        }
    }

    private sealed class PlayerWeaponOverride
    {
        internal readonly int WeaponId;
        internal readonly int SquadId;
        internal readonly Squad Squad;
        internal readonly Vehicle Weapon;
        internal readonly float OrderedAt;
        internal float LastObservedAt;

        internal PlayerWeaponOverride(
            int weaponId,
            int squadId,
            Squad squad,
            Vehicle weapon,
            float orderedAt,
            float lastObservedAt)
        {
            WeaponId = weaponId;
            SquadId = squadId;
            Squad = squad;
            Weapon = weapon;
            OrderedAt = orderedAt;
            LastObservedAt = lastObservedAt;
        }
    }
}

[HarmonyPatch(typeof(Squad), nameof(Squad.SendUnitsToVehicle))]
internal static class PlayerStaticWeaponOrderPatch
{
    [HarmonyPrefix]
    private static void Prefix(Squad __instance, Vehicle v)
    {
        if (!MultiplayerAuthority.CanMutateGameplay() || __instance == null || v == null ||
            !CommanderMvp.HasPlayerOwnership(__instance) || !v.IsStatic())
        {
            return;
        }

        try
        {
            StaticAntiTankStaffing.YieldToPlayerOrder(__instance, v, Time.time);
        }
        catch (Exception ex)
        {
            // Player input must still reach the native method if cleanup encounters
            // an unexpected game object from a mission or another mod.
            Plugin.LogSource.LogWarning(
                $"Could not release static weapon for player order: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(Vehicle), nameof(Vehicle.GetOnVehicleBestPos))]
internal static class PlayerStaticWeaponSeatPriorityPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Vehicle __instance, Soldier soldier, ref bool __result)
    {
        if (!MultiplayerAuthority.CanMutateGameplay() || __instance == null || soldier == null ||
            !__instance.IsStatic() ||
            !StaticAntiTankStaffing.IsPlayerOrderedEntrant(__instance, soldier))
        {
            return true;
        }

        try
        {
            var mainSeat = __instance.GetMainTurretSeat(false, out var seatId);
            if (mainSeat == null ||
                (mainSeat.unitSet != null &&
                 mainSeat.unitSet.GetInstanceID() != soldier.GetInstanceID()))
            {
                return true;
            }

            __instance.SetFaction(soldier.faction);
            __instance.GetOnVehicle(seatId, soldier);
            __result = true;
            return false;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning(
                $"Could not prioritize static weapon gunner seat: {ex.Message}");
            return true;
        }
    }
}
