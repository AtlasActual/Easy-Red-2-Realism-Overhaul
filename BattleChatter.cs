using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class BattleChatterState
{
    internal int LastTargetToken;
    internal float LastContactAt;
    internal float NextLineAt;
    internal float NextRoutineAt;
    internal int PreviousSuppression;
    internal bool WasMoving;
}

internal static class BattleChatter
{
    private const float ContactResetSeconds = 10f;
    private static readonly Dictionary<int, float> NextSquadLineAt = new();

    internal static void Update(SoldierAI ai, Soldier soldier, float now)
    {
        if (!Settings.BattleChatterEnabled.Value)
            return;

        var id = soldier.GetInstanceID();
        if (!AiState.BattleChatterStates.TryGetValue(id, out var state))
        {
            state = new BattleChatterState
            {
                PreviousSuppression = soldier.GetSuppressionValue(),
                WasMoving = soldier.IsMoving(),
                NextRoutineAt = now + RoutineInterval()
            };
            AiState.BattleChatterStates[id] = state;
        }

        var target = ai.visibleTarget ?? soldier.CurrentVisibleTarget;
        if (target != null)
        {
            var targetToken = target.Pointer.GetHashCode();
            var isNewContact = targetToken != state.LastTargetToken ||
                               now - state.LastContactAt >= ContactResetSeconds;
            state.LastTargetToken = targetToken;
            state.LastContactAt = now;

            var contactLine = ContactLine(target);
            if (isNewContact && contactLine.HasValue &&
                UnityEngine.Random.value <= Settings.ChatterContactCalloutChance.Value)
            {
                TrySay(soldier, contactLine.Value, state, now, "new contact");
            }
        }
        else if (now - state.LastContactAt >= ContactResetSeconds)
        {
            state.LastTargetToken = 0;
        }

        var suppression = soldier.GetSuppressionValue();
        if (suppression >= Settings.CrouchSuppression.Value &&
            state.PreviousSuppression < Settings.CrouchSuppression.Value &&
            UnityEngine.Random.value <= Settings.ChatterRoutineCalloutChance.Value)
        {
            TrySay(soldier, VoiceManager.VoiceClip.imUnderFire, state, now, "suppressed");
        }

        var moving = soldier.IsMoving();
        if (moving && !state.WasMoving && ContactResponse.HasActiveContact(id, now) &&
            UnityEngine.Random.value <= Settings.ChatterRoutineCalloutChance.Value)
        {
            TrySay(soldier, VoiceManager.VoiceClip.imMoving, state, now, "combat movement");
        }

        state.PreviousSuppression = suppression;
        state.WasMoving = moving;

        if (now < state.NextRoutineAt)
            return;

        state.NextRoutineAt = now + RoutineInterval();
        if (UnityEngine.Random.value > Settings.ChatterRoutineCalloutChance.Value)
            return;

        if (target != null)
        {
            if (suppression >= Settings.CrouchSuppression.Value)
                TrySay(soldier, VoiceManager.VoiceClip.imUnderFire, state, now, "combat pressure");
            else if (ContactResponse.IsWeaponFiring(soldier) || soldier.IsGunner() || soldier.hasMg)
                TrySay(soldier, VoiceManager.VoiceClip.coveringFire, state, now, "covering fire");
            return;
        }

        // Quiet movement chatter is intentionally limited to a leader/follower
        // acknowledgement, and never plays from stationary idle soldiers.
        if (!moving || soldier.joinedSquad == null)
            return;

        var line = soldier.joinedSquad.Leader != null &&
                   soldier.joinedSquad.Leader.GetInstanceID() == id
            ? VoiceManager.VoiceClip.followMe
            : VoiceManager.VoiceClip.yesSir;
        TrySay(soldier, line, state, now, "movement acknowledgement");
    }

    internal static void RemoveSoldier(Soldier soldier)
    {
        AiState.BattleChatterStates.Remove(soldier.GetInstanceID());
    }

    private static VoiceManager.VoiceClip? ContactLine(Spottable target)
    {
        // The native bank has no general aircraft-spotted category. Silence is
        // preferable to misidentifying an aircraft as infantry.
        if (target.IsAirVehicle())
            return null;

        if (!target.IsWheeledVehicleOrTank())
            return VoiceManager.VoiceClip.enemyInfantrySpotted;

        var vehicle = target.TryCast<Vehicle>();
        return vehicle != null && vehicle.IsArtillery()
            ? VoiceManager.VoiceClip.enemyArtillerySpotted
            : VoiceManager.VoiceClip.enemyTankSpotted;
    }

    private static bool TrySay(
        Soldier soldier,
        VoiceManager.VoiceClip line,
        BattleChatterState state,
        float now,
        string reason)
    {
        if (now < state.NextLineAt || soldier.IsVoiceLocked || !soldier.IsAlive ||
            !soldier.gameObject.activeInHierarchy)
        {
            return false;
        }

        var squadId = soldier.joinedSquad != null
            ? ContactKnowledge.GetSquadId(soldier.joinedSquad)
            : -soldier.GetInstanceID();
        if (NextSquadLineAt.TryGetValue(squadId, out var squadReadyAt) && now < squadReadyAt)
            return false;

        soldier.Say(line);
        state.NextLineAt = now + Jitter(Settings.ChatterIndividualCooldownSeconds.Value);
        NextSquadLineAt[squadId] = now + Jitter(Settings.ChatterSquadCooldownSeconds.Value);
        AiState.Trace($"Battle chatter: soldier {soldier.GetInstanceID()} said {line} ({reason})");
        return true;
    }

    private static float RoutineInterval()
    {
        var minimum = Settings.ChatterRoutineMinimumSeconds.Value;
        var maximum = Mathf.Max(minimum, Settings.ChatterRoutineMaximumSeconds.Value);
        return UnityEngine.Random.Range(minimum, maximum);
    }

    private static float Jitter(float duration)
        => duration * UnityEngine.Random.Range(0.85f, 1.15f);
}
