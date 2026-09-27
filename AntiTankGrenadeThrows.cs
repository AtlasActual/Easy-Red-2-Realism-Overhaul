using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Runtime adapter for <see cref="AntiTankGrenadeDecisionCore"/>. It makes the same
/// native call the game makes, <c>Soldier.Throw(grenade, vehicle)</c>, so the throw
/// animation, the ballistic solution and the inventory removal are all the game's own.
/// </summary>
internal static class AntiTankGrenadeThrows
{
    private const float AttemptIntervalSeconds = 1.5f;
    private const float NoGrenadeRetrySeconds = 10f;
    private const float AfterThrowSeconds = 8f;
    private const float ArmorRefreshSeconds = 0.5f;
    // Beyond any hand throw. Inside it the inventory and visibility probes are paid for.
    private const float ConsiderationMeters = 40f;

    private readonly record struct ArmorContact(Vehicle Vehicle, Vector3 Position, string Faction);

    private static readonly List<ArmorContact> Armor = new();
    private static readonly Dictionary<int, float> NextAttemptAt = new();
    private static float _armorRefreshedAt = float.NegativeInfinity;
    private static int _throwingSoldierId;

    internal static void Schedule(Soldier soldier, float now)
    {
        if (!Settings.AntiTankGrenadeThrowsEnabled.Value)
            return;

        var id = soldier.GetInstanceID();
        if (!AiState.CooldownReady(NextAttemptAt, id, now))
            return;
        NextAttemptAt[id] = now + AttemptIntervalSeconds;

        try
        {
            RefreshArmor(now);
            if (Armor.Count == 0)
                return;

            var faction = AiState.FactionOf(soldier);
            var position = soldier.GetCenterOfUnit();
            if (!TryFindNearestEnemyTank(position, faction, out var tank, out var distance))
                return;

            var available = soldier.IsAlive && !soldier.IsOnVehicle() && !soldier.IsOnFire &&
                            !soldier.IsThrowing && !soldier.IsReloading;
            if (!available)
                return;

            // FindItemOfType hands back the declared VirtualItem wrapper whatever the
            // runtime class is, so the result is only ever tested for presence.
            var grenade = soldier.inventory?.FindItemOfType<VirtualATGrenade>();
            if (grenade == null || grenade.Pointer == IntPtr.Zero)
            {
                NextAttemptAt[id] = now + NoGrenadeRetrySeconds;
                return;
            }

            var vehicle = tank.Vehicle.TryCast<Spottable>();
            if (vehicle == null)
                return;

            var input = new AntiTankGrenadeDecisionInput(
                available,
                ContactResponse.IsPinned(id),
                HasAntiTankGrenade: true,
                distance,
                soldier.GetGrenadeThrowRange(grenade),
                soldier.CanSee(vehicle),
                CombatSafety.FriendlyNear(
                    tank.Position, faction,
                    AntiTankGrenadeDecisionCore.FriendlyClearanceMeters, soldier));
            if (!AntiTankGrenadeDecisionCore.ShouldThrow(input))
                return;

            _throwingSoldierId = id;
            try
            {
                soldier.Throw(grenade, vehicle);
            }
            finally
            {
                _throwingSoldierId = 0;
            }

            if (!soldier.IsThrowing)
                return;

            NextAttemptAt[id] = now + AfterThrowSeconds;
            AiState.Trace($"Anti-tank grenade: soldier {id} threw at a tank {distance:0}m away");
        }
        // A tank or soldier torn down mid-check is simply no throw this time.
        catch (NullReferenceException) { }
        catch (Il2CppException) { }
        catch (ObjectCollectedException) { }
    }

    /// <summary>
    /// True inside this module's own <c>Soldier.Throw</c> call. The explosive-grenade
    /// safety gate is written around fragmentation throws at infantry (minimum range,
    /// an 11 m friendly radius, a shared cooldown) and must not re-judge a throw that
    /// already passed the anti-tank rules.
    /// </summary>
    internal static bool OwnsThrow(Soldier soldier)
        => _throwingSoldierId != 0 && soldier.GetInstanceID() == _throwingSoldierId;

    internal static void RemoveSoldier(int soldierId)
        => NextAttemptAt.Remove(soldierId);

    internal static void ResetBattle()
    {
        Armor.Clear();
        NextAttemptAt.Clear();
        _armorRefreshedAt = float.NegativeInfinity;
    }

    private static bool TryFindNearestEnemyTank(
        Vector3 position, string faction, out ArmorContact nearest, out float distance)
    {
        nearest = default;
        var nearestSqr = ConsiderationMeters * ConsiderationMeters;
        var found = false;
        foreach (var contact in Armor)
        {
            var distanceSqr = (contact.Position - position).sqrMagnitude;
            if (distanceSqr >= nearestSqr ||
                ResourcesManager.IsSameFactionOrCivilian(contact.Faction, faction))
            {
                continue;
            }

            nearestSqr = distanceSqr;
            nearest = contact;
            found = true;
        }

        distance = found ? Mathf.Sqrt(nearestSqr) : float.PositiveInfinity;
        return found;
    }

    // One walk of the vehicle list serves every soldier for the next half second.
    private static void RefreshArmor(float now)
    {
        if (now - _armorRefreshedAt < ArmorRefreshSeconds)
            return;

        _armorRefreshedAt = now;
        Armor.Clear();
        var vehicles = Vehicle.allVehicles;
        if (vehicles == null)
            return;

        for (var index = 0; index < vehicles.Count; index++)
        {
            var vehicle = vehicles[index];
            if (vehicle == null || vehicle.life <= 0 || vehicle.IsDisabled() ||
                !SoldierSequentialUpdatePatch.IsTankCached(vehicle))
            {
                continue;
            }

            // An abandoned tank reports the civilian placeholder and threatens nobody.
            var vehicleFaction = vehicle.GetVehicleFaction();
            if (string.IsNullOrWhiteSpace(vehicleFaction))
                continue;

            Armor.Add(new ArmorContact(vehicle, vehicle.GetCenterOfUnit(), vehicleFaction));
        }
    }
}
