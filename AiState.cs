using UnityEngine;

namespace ER2RealismOverhaul;

internal static class AiState
{
    internal static readonly Dictionary<int, TargetMemoryState> TargetMemory = new();
    internal static readonly Dictionary<int, GunfireCue> GunfireCues = new();
    internal static readonly Dictionary<int, ContactResponseState> ContactStates = new();
    internal static readonly Dictionary<int, KnownTargetSuppressiveFireState> KnownTargetSuppressionStates = new();
    internal static readonly Dictionary<IntPtr, CoverReservation> CoverReservations = new();
    internal static readonly Dictionary<int, float> NextTankThreatCheck = new();
    internal static readonly Dictionary<int, float> TankCoverHideUntil = new();
    // HasNearbyTankThreat is queried every frame from CaptureSnapshot; these cache
    // its result on the same 0.75s cadence ApplyTankFear already uses for its own
    // identical scan, so the snapshot no longer forces an unthrottled per-frame scan.
    internal static readonly Dictionary<int, float> NextTankThreatSnapshotCheck = new();
    internal static readonly Dictionary<int, bool> CachedTankThreat = new();
    internal static readonly Dictionary<int, float> FlameEvasionUntil = new();
    internal static readonly Dictionary<int, float> NextTankTactic = new();
    internal static readonly Dictionary<int, float> NextSmokeAttempt = new();
    internal static readonly Dictionary<int, float> NextOrderGesture = new();
    internal static readonly Dictionary<int, float> NextGrenadeThrow = new();
    internal static readonly Dictionary<int, BattleChatterState> BattleChatterStates = new();
    internal static readonly Dictionary<int, float> AircraftEvasionUntil = new();
    internal static readonly Dictionary<int, Vector3> AircraftEvasionDirection = new();
    internal static readonly Dictionary<int, Flame> Flames = new();
    internal static readonly Dictionary<int, TankEngagementRuntimeState> TankEngagementStates = new();

    internal static TankEngagementRuntimeState GetTankEngagementState(int id)
    {
        if (TankEngagementStates.TryGetValue(id, out var state))
            return state;

        state = new TankEngagementRuntimeState();
        TankEngagementStates[id] = state;
        return state;
    }

    internal static ContactResponseState GetContactState(int id)
    {
        if (ContactStates.TryGetValue(id, out var state))
            return state;

        state = new ContactResponseState();
        ContactStates[id] = state;
        return state;
    }

    internal static TargetMemoryState GetTargetMemory(int id)
    {
        if (TargetMemory.TryGetValue(id, out var state))
            return state;

        state = new TargetMemoryState();
        TargetMemory[id] = state;
        return state;
    }

    internal static void RemoveSoldier(Soldier soldier)
    {
        var id = soldier.GetInstanceID();
        IntPtr soldierToken;
        try
        {
            soldierToken = soldier.Pointer;
        }
        catch
        {
            soldierToken = IntPtr.Zero;
        }
        GunfireAwareness.RemoveShooter(id, soldierToken);
        ReleaseCoverReservation(id);
        TargetMemory.Remove(id);
        ContactStates.Remove(id);
        KnownTargetSuppressiveFire.RemoveSoldier(id);
        NextTankThreatCheck.Remove(id);
        TankCoverHideUntil.Remove(id);
        NextTankThreatSnapshotCheck.Remove(id);
        CachedTankThreat.Remove(id);
        FlameEvasionUntil.Remove(id);
        NextOrderGesture.Remove(id);
        NextGrenadeThrow.Remove(id);
        BattleChatter.RemoveSoldier(soldier);
        MountedGunnerSuppression.RemoveSoldier(id);
        ContactKnowledge.RemoveSoldier(soldier);
        GroundAiDirector.ReleaseSoldier(id);
    }

    internal static bool CooldownReady(Dictionary<int, float> map, int id, float now)
        => !map.TryGetValue(id, out var readyAt) || now >= readyAt;

    internal static bool IsHidingFromTank(int soldierId, float now)
        => TankCoverHideUntil.TryGetValue(soldierId, out var until) && now < until;

    internal static bool IsFlameEvading(int soldierId, float now)
        => FlameEvasionUntil.TryGetValue(soldierId, out var until) && now < until;

    internal static bool CoverReservedByOther(
        IntPtr coverId,
        Vector3 coverPosition,
        int soldierId,
        float now,
        float minimumSpacing)
    {
        List<IntPtr>? expired = null;
        var reservedByOther = false;
        foreach (var pair in CoverReservations)
        {
            var reservation = pair.Value;
            if (reservation.ExpiresAt <= now)
            {
                (expired ??= new List<IntPtr>()).Add(pair.Key);
                continue;
            }

            if (reservation.SoldierId == soldierId)
                continue;

            if (pair.Key == coverId ||
                InfantryCoverDecisionCore.CoverPositionsConflict(
                    new MapPoint(coverPosition.x, coverPosition.z),
                    new MapPoint(reservation.Position.x, reservation.Position.z),
                    minimumSpacing))
            {
                reservedByOther = true;
            }
        }

        if (expired != null)
        {
            foreach (var expiredCoverId in expired)
                CoverReservations.Remove(expiredCoverId);
        }

        return reservedByOther;
    }

    internal static void ReserveCover(
        IntPtr coverId,
        Vector3 coverPosition,
        int soldierId,
        float expiresAt)
    {
        if (coverId == IntPtr.Zero)
            return;

        ReleaseCoverReservation(soldierId);
        CoverReservations[coverId] = new CoverReservation(
            soldierId, expiresAt, coverPosition);
    }

    internal static void ReleaseCoverReservation(int soldierId)
    {
        List<IntPtr>? releases = null;
        foreach (var pair in CoverReservations)
        {
            if (pair.Value.SoldierId == soldierId)
                (releases ??= new List<IntPtr>()).Add(pair.Key);
        }

        if (releases == null)
            return;

        foreach (var coverId in releases)
            CoverReservations.Remove(coverId);
    }

    internal static void Trace(string message)
    {
        AiDebugTelemetry.RecordTrace(message);
        if (Settings.VerboseLogging.Value)
            Plugin.LogSource.LogInfo(message);
    }

    internal static Vector3 HorizontalAway(Vector3 source, Vector3 danger)
    {
        var away = source - danger;
        away.y = 0f;
        return away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back;
    }
}

internal readonly record struct CoverReservation(
    int SoldierId,
    float ExpiresAt,
    Vector3 Position);

internal readonly record struct GunfireCue(
    IntPtr ShooterToken,
    string ShooterFaction,
    Vector3 Position,
    float ExpiresAt);

internal sealed class TargetMemoryState
{
    internal bool HasConfirmedTarget;
    internal IntPtr TargetToken;
    internal float LastObservedAt;
    internal bool HasConfirmedLastKnownPosition;
    internal IntPtr ConfirmedLastKnownTargetToken;
    internal Vector3 ConfirmedLastKnownPosition;
    internal float ConfirmedLastKnownObservedAt;
    internal Vector3 IncomingFirePosition;
    internal float IncomingFireUntil;
    internal IntPtr IncomingFireShooterToken;
    internal bool IncomingFireIsDirect;
    internal float NextGunfirePollAt;
    internal readonly Dictionary<IntPtr, TargetCandidateState> Candidates = new();
}

internal sealed class TargetCandidateState
{
    internal float LastSeenAt;
    internal float ObservedSeconds;
    internal Vector3 LastKnownPosition;
}

/// <summary>
/// Per-vehicle runtime for the tank engagement state machine (TankEngagementDecisionCore)
/// and its stall watchdog. A stale entry for a destroyed vehicle is harmless (the
/// instance id is never reused while alive), the same tradeoff already accepted by
/// the tank-tactics cooldown maps above.
/// </summary>
internal sealed class TankEngagementRuntimeState
{
    internal TankEngagementState State = TankEngagementState.Follow;
    internal float LastArmoredTargetSeenAt;
    internal float LastKnownDistance;
    internal bool LastKnownHullFacesThreat;

    internal float WatchdogNextSampleAt;
    internal bool WatchdogWindowActive;
    internal float WatchdogWindowStartAt;
    internal Vector3 WatchdogWindowStartPosition;
    internal bool WatchdogProgressAnchorSet;
    internal Vector3 WatchdogProgressAnchorPosition;
    internal int WatchdogFailedRecoveries;

    internal void ResetWatchdog()
    {
        WatchdogNextSampleAt = 0f;
        WatchdogWindowActive = false;
        WatchdogProgressAnchorSet = false;
        WatchdogFailedRecoveries = 0;
    }
}
