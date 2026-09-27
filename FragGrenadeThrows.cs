using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Supplies visible infantry throw opportunities independently of the native
/// suppression/random gate. Soldier.Throw still owns inventory, animation,
/// ballistics and replication, and SafeAiGrenadeThrowPatch authorizes each throw.
/// </summary>
internal static class FragGrenadeThrows
{
    private const float AttemptIntervalSeconds = 1.5f;
    private const float NoGrenadeRetrySeconds = 10f;
    private static readonly Dictionary<int, float> NextAttemptAt = new();

    internal static void Schedule(SoldierAI ai, Soldier soldier, float now)
    {
        if (!Settings.SafeAiGrenadeThrowsEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() || !AiOwnership.IsAutonomous(soldier))
        {
            return;
        }

        var id = soldier.GetInstanceID();
        if (!AiState.CooldownReady(NextAttemptAt, id, now) ||
            !AiState.CooldownReady(AiState.NextGrenadeThrow, id, now))
        {
            return;
        }

        // Different retry periods keep a squad from repeatedly probing in lockstep.
        NextAttemptAt[id] = now + AttemptIntervalSeconds + (uint)id % 11 * 0.05f;
        try
        {
            if (!soldier.IsAlive || soldier.IsOnVehicle() || soldier.IsOnFire ||
                soldier.IsThrowing || !soldier.CanPlaySpecialAnim() ||
                ContactResponse.IsPinned(id) || AiState.IsFlameEvading(id, now) ||
                RememberedGrenadeThrows.IsActive(id) ||
                soldier.LuaSoldier?.HasScriptAssigned() == true)
            {
                return;
            }

            // Use only this soldier's acquired contact; never search the global
            // enemy list or turn a shared report into personal visual knowledge.
            var target = TargetAcquisition.ResolveObservedTarget(ai, soldier);
            var infantry = Il2CppTypeTests.As<Soldier>(target);
            if (infantry == null || !infantry.IsAlive || infantry.IsOnVehicle())
            {
                return;
            }

            var faction = AiState.FactionOf(soldier);
            var targetFaction = AiState.FactionOf(infantry);
            if (string.IsNullOrEmpty(faction) || string.IsNullOrEmpty(targetFaction) ||
                ResourcesManager.IsSameFactionOrCivilian(targetFaction, faction))
            {
                return;
            }

            if (Settings.PerceptionEnabled.Value &&
                (!AiState.TargetMemory.TryGetValue(id, out var memory) ||
                 !memory.HasConfirmedTarget || memory.RequiresTargetReacquisition ||
                 !TargetAcquisition.MatchesTarget(target, memory.TargetToken)))
            {
                return;
            }

            var position = infantry.GetCenterOfUnit();
            var distance = Vector3.Distance(soldier.GetCenterOfUnit(), position);
            if (distance < Settings.GrenadeMinimumRange.Value ||
                distance > Settings.GrenadeMaximumRange.Value)
            {
                return;
            }

            var grenade = RememberedGrenadeThrows.FindFragmentationGrenade(soldier);
            if (grenade == null || grenade.Pointer == IntPtr.Zero)
            {
                NextAttemptAt[id] = now + NoGrenadeRetrySeconds;
                return;
            }

            var squad = soldier.joinedSquad;
            var assaulting = squad != null &&
                             (squad.order == Order.charge || squad.order == Order.attackFromSide);
            var input = new FragGrenadeDecisionInput(
                IsAvailableForThrow: true,
                IsPinned: false,
                HasFragmentationGrenade: true,
                HasConfirmedVisibleInfantry: soldier.CanSee(target),
                CooldownReady: true,
                soldier.IsMoving(), assaulting, soldier.m_pose == SoldierPose.Prone,
                distance, Settings.GrenadeMinimumRange.Value, Settings.GrenadeMaximumRange.Value,
                soldier.GetGrenadeThrowRange(grenade));
            if (!FragGrenadeDecisionCore.ShouldAttempt(input))
                return;
            if (!RememberedGrenadeThrows.TryBuildThrowArc(soldier, position, out _))
            {
                AiDebugTelemetry.RecordDecision(AiDebugCategory.Danger, id,
                    "Frag grenade rejected: blocked throw arc");
                return;
            }

            // The shared prefix rechecks friendly clearance, range and cooldown.
            // Passing the target uses the game's aimed ballistic throw.
            soldier.Throw(grenade, target);
            if (soldier.IsThrowing)
                AiState.Trace($"Frag grenade: soldier {id} threw at visible infantry {distance:0}m away");
        }
        catch (NullReferenceException) { }
        catch (Il2CppException) { }
        catch (ObjectCollectedException) { }
    }

    internal static void RemoveSoldier(int soldierId)
        => NextAttemptAt.Remove(soldierId);

    internal static void ResetBattle()
        => NextAttemptAt.Clear();
}
