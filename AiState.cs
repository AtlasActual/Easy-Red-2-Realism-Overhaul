using System.Runtime.CompilerServices;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Interpolated-string handler for <see cref="AiState.Trace"/>. Its constructor reports
/// through <c>isEnabled</c> whether a trace will be consumed at all; when it will not, the
/// compiler skips the interpolation entirely rather than composing a string for
/// <see cref="AiState.Trace"/> to throw away. The traces sit on per-soldier perception,
/// cover, and acquisition paths, so with the overlay and verbose logging both off — the
/// normal configuration — that composition was the bulk of the mod's remaining garbage.
/// </summary>
[InterpolatedStringHandler]
internal ref struct TraceMessageHandler
{
    private DefaultInterpolatedStringHandler _inner;
    private readonly bool _enabled;

    public TraceMessageHandler(int literalLength, int formattedCount, out bool isEnabled)
    {
        _enabled = AiState.TraceEnabled;
        isEnabled = _enabled;
        _inner = _enabled
            ? new DefaultInterpolatedStringHandler(literalLength, formattedCount)
            : default;
    }

    public void AppendLiteral(string value) => _inner.AppendLiteral(value);

    public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);

    public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);

    internal string GetMessage() => _enabled ? _inner.ToStringAndClear() : string.Empty;
}

internal static class AiState
{
    internal static readonly Dictionary<int, TargetMemoryState> TargetMemory = new();
    internal static readonly Dictionary<int, GunfireCue> GunfireCues = new();
    internal static readonly Dictionary<int, ContactResponseState> ContactStates = new();
    internal static readonly Dictionary<int, KnownTargetSuppressiveFireState> KnownTargetSuppressionStates = new();
    internal static readonly Dictionary<int, RememberedGrenadeThrowState> RememberedGrenadeThrowStates = new();
    internal static readonly Dictionary<IntPtr, CoverReservation> CoverReservations = new();
    internal static readonly Dictionary<int, float> FlameEvasionUntil = new();
    internal static readonly Dictionary<int, float> NextTankTactic = new();
    internal static readonly Dictionary<int, float> NextSmokeAttempt = new();
    internal static readonly Dictionary<int, float> NextOrderGesture = new();
    internal static readonly Dictionary<int, float> NextGrenadeThrow = new();
    internal static readonly Dictionary<int, BattleChatterState> BattleChatterStates = new();
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

    /// <summary>
    /// A soldier's faction, read once and reused. <c>Soldier.faction</c> is an il2cpp
    /// string field, so every read marshals a FRESH managed string — measured at 3.2-4.0MB
    /// per 30 seconds from a single call site on the per-decision path, twenty to a
    /// hundred times more garbage than every other stage of the tactical pipeline
    /// combined. Faction cannot change for a living soldier, and the instance id used to
    /// key this is an interop call that returns an int rather than allocating.
    /// </summary>
    private static readonly Dictionary<int, string> SoldierFactions = new();

    internal static string FactionOf(Soldier soldier)
    {
        var id = soldier.GetInstanceID();
        if (SoldierFactions.TryGetValue(id, out var faction))
            return faction;

        faction = soldier.faction ?? string.Empty;
        SoldierFactions[id] = faction;
        return faction;
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

        RemoveSoldierById(id, soldierToken);
    }

    /// <summary>
    /// Purge every per-soldier map by instance id alone. Cleanup used to run only from
    /// SoldierAI.OnDestroy behind a `GetSoldier() != null` check, which a soldier whose
    /// native object had already been released always failed — so its entries survived
    /// the rest of the battle. That showed up as 130 contact states for 56 living
    /// soldiers, and the covering-fire scan walks that map once per soldier, so the leak
    /// made the scan dearer the longer a battle ran.
    /// </summary>
    internal static void RemoveSoldierById(int id, IntPtr soldierToken)
    {
        BattleChatterStates.Remove(id);
        SoldierFactions.Remove(id);
        GunfireAwareness.RemoveShooter(id, soldierToken);
        ReleaseCoverReservation(id);
        TargetMemory.Remove(id);
        ContactStates.Remove(id);
        KnownTargetSuppressiveFire.RemoveSoldier(id);
        RememberedGrenadeThrows.RemoveSoldier(id);
        FlameEvasionUntil.Remove(id);
        NextOrderGesture.Remove(id);
        NextGrenadeThrow.Remove(id);
        MountedGunnerSuppression.RemoveSoldier(id);
        GroundAiDirector.ReleaseSoldier(id);
    }

    internal static bool CooldownReady(Dictionary<int, float> map, int id, float now)
        => !map.TryGetValue(id, out var readyAt) || now >= readyAt;

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

    // Crowding count for cover scoring (plan 016). Reuses the existing reservation
    // map the way CoverReservedByOther does - no physics, no allocation - so it is
    // safe to call from the already-budgeted detailed candidate loop. Read-only: it
    // does not prune expired entries, since CoverReservedByOther already does that
    // sweep on the same per-soldier decision.
    internal static int CountNearbyReservations(
        Vector3 coverPosition,
        int soldierId,
        float now,
        float radius)
    {
        var count = 0;
        foreach (var pair in CoverReservations)
        {
            var reservation = pair.Value;
            if (reservation.ExpiresAt <= now || reservation.SoldierId == soldierId)
                continue;

            if (InfantryCoverDecisionCore.CoverPositionsConflict(
                    new MapPoint(coverPosition.x, coverPosition.z),
                    new MapPoint(reservation.Position.x, reservation.Position.z),
                    radius))
            {
                count++;
            }
        }

        return count;
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

    internal static bool TryReserveCover(
        IntPtr coverId,
        Vector3 coverPosition,
        int soldierId,
        float now,
        float expiresAt,
        float minimumSpacing)
    {
        if (coverId == IntPtr.Zero ||
            CoverReservedByOther(
                coverId,
                coverPosition,
                soldierId,
                now,
                minimumSpacing))
        {
            return false;
        }

        ReserveCover(coverId, coverPosition, soldierId, expiresAt);
        return true;
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

    /// <summary>
    /// Whether anything would actually consume a trace. Callers build their message by
    /// string interpolation, so the message is composed — with number formatting — BEFORE
    /// <see cref="Trace"/> is entered and can discard it. With the overlay and verbose
    /// logging both off (the normal configuration) that work was pure garbage, generated
    /// per soldier on the perception and cover paths, and it is what remained of the
    /// allocation feeding the collections that the stutter frames coincide with.
    /// Guard interpolated call sites with this.
    /// </summary>
    internal static bool TraceEnabled =>
        AiDebugTelemetry.CaptureEnabled || Settings.VerboseLogging.Value;

    internal static void Trace(string message)
    {
        AiDebugTelemetry.RecordTrace(message);
        if (Settings.VerboseLogging.Value)
            Plugin.LogSource.LogInfo(message);
    }

    /// <summary>
    /// Overload every interpolated trace binds to. The handler decides up front whether
    /// anything will read the message, and the compiler skips evaluating and formatting
    /// the holes when it will not — so a disabled trace costs a boolean check rather than
    /// a formatted string. Callers need no guard, and new trace sites get this for free.
    /// </summary>
    internal static void Trace(TraceMessageHandler message)
    {
        if (!TraceEnabled)
            return;

        Trace(message.GetMessage());
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
    internal float NextNearbyTargetShareAt;
    internal IntPtr ReportedTargetToken;
    internal Vector3 ReportedTargetPosition;
    internal float ReportedTargetAvailableAt;
    internal float ReportedTargetUntil;
    internal float NextCloseConfirmPollAt;
    internal float NextCloseDiscoveryPollAt;
    // When this soldier last ran the incoming-fire orientation step. That step is now
    // budgeted per frame, so the turn is driven by the time actually elapsed since the
    // last one rather than by a fixed step — a soldier who waited a frame then turns
    // through the angle he would have covered anyway, at the same rate.
    internal float LastIncomingFireTurnAt;
    internal float LastReportedTargetTurnAt;
    internal readonly Dictionary<IntPtr, TargetCandidateState> Candidates = new();
}

internal sealed class TargetCandidateState
{
    internal float LastSeenAt;
    internal float ObservedSeconds;
    internal Vector3 LastKnownPosition;
    internal Spottable? Target;
    internal float FirstSeenAt;
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
