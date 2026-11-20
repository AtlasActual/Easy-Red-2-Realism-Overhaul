using Il2CppInterop.Runtime;
using System.Runtime.InteropServices;
using UnityEngine;
using Position = System.Numerics.Vector3;

namespace ER2RealismOverhaul;

/// <summary>A volley shares one read of the native roster and soldier positions.</summary>
internal static class FiringLaneRoster
{
    private struct Sample
    {
        internal Soldier? Soldier;
        internal int Id;
        internal string? Faction;
        internal Position? Center;
    }

    private static readonly List<Sample> Samples = new();
    private static (int Frame, double Time, bool FixedStep, IntPtr Roster, int Version) _stamp;
    private static int _count;

    internal static bool IsBlocked(
        Soldier shooter, Vector3 origin, Vector3 direction, float radius, float distance)
    {
        var creatures = Creature.aliveCreatures;
        if (creatures == null)
            return false;

        // Physics can catch up with multiple ticks in one rendered frame. A spawn or
        // removal also invalidates the sample even if the roster count stays the same.
        var stamp = (Time.frameCount, Time.timeAsDouble, Time.inFixedTimeStep,
            creatures.Pointer, creatures._version);
        if (stamp != _stamp)
        {
            Samples.Clear();
            _count = creatures.Count;
            _stamp = stamp;
        }

        var shooterId = shooter.GetInstanceID();
        var faction = AiState.FactionOf(shooter, shooterId);
        var vehicle = shooter.GetCurrentVehicle();
        var start = new Position(origin.x, origin.y, origin.z);
        var forward = new Position(direction.x, direction.y, direction.z);
        var radiusSquared = radius * radius;
        for (var i = 0; i < _count; i++)
        {
            // Populate only as far as a shot actually looks. An ally beside the
            // first shooter's muzzle must not require a whole-battle snapshot.
            if (i == Samples.Count)
                Samples.Add(ReadSoldier(creatures, i));
            ref var sample = ref CollectionsMarshal.AsSpan(Samples)[i];
            var friendly = sample.Soldier;
            if (friendly is null || sample.Id == shooterId || !CombatSafety.SameFaction(sample.Faction, faction))
                continue;

            try
            {
                if (!sample.Center.HasValue)
                {
                    var center = friendly.GetCenterOfUnit();
                    sample.Center = new Position(center.x, center.y, center.z);
                }
                var offset = sample.Center.Value - start;
                var along = Position.Dot(offset, forward);
                if (along <= 0.35f || along >= distance ||
                    (offset - forward * along).LengthSquared() > radiusSquared)
                    continue;

                // Recheck life and seats before a potential blocker withholds fire.
                if (friendly != null && friendly.IsAlive &&
                    (vehicle == null || friendly.GetCurrentVehicle() != vehicle))
                    return true;
            }
            catch (ObjectCollectedException) { sample = default; }
            catch (Il2CppException) { sample = default; }
        }
        return false;
    }

    private static Sample ReadSoldier(Il2CppSystem.Collections.Generic.List<Creature> creatures, int index)
    {
        try
        {
            var soldier = Il2CppTypeTests.As<Soldier>(creatures[index]);
            if (soldier != null && soldier.IsAlive)
            {
                var id = soldier.GetInstanceID();
                return new Sample { Soldier = soldier, Id = id, Faction = AiState.FactionOf(soldier, id) };
            }
        }
        catch (ObjectCollectedException) { }
        catch (Il2CppException) { }
        return default;
    }

    internal static void Reset()
    {
        _stamp = default;
        _count = 0;
        Samples.Clear();
    }
}
