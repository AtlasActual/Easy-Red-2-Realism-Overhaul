using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Defines the safety boundary for every patch that is allowed to control an AI
/// soldier. IsFPSPlayer only describes the active camera mode; controller-owned
/// soldiers can still have an AI controller while using another player camera.
/// </summary>
internal static class AiOwnership
{
    // The squad verdict changes only when a player switches soldier or joins a
    // squad, so a quarter second of staleness is invisible while keeping the
    // per-soldier gate down to one dictionary probe.
    private const float SquadVerdictSeconds = 0.25f;

    private static int _localSoldierFrame = -1;
    private static int _localSoldierId;
    private static bool _loggedWedgedControlFlags;
    private static bool _loggedSquadTestFailure;

    private static readonly Dictionary<IntPtr, bool> SquadVerdicts = new();
    private static float _squadVerdictsExpireAt = -1f;

    /// <summary>
    /// True when the game's AI controller drives this soldier rather than a human.
    /// This answers the control-flag question only; call <see cref="IsAutonomous"/>
    /// to decide whether the mod may change how the soldier behaves.
    /// </summary>
    internal static bool IsAiControlled([NotNullWhen(true)] Soldier? soldier)
    {
        if (soldier == null)
            return false;

        try
        {
            if (!soldier.IsAI() || soldier.IsPlayer())
                return false;

            // Vehicle entry/exit can leave IsPlayer/IsAI inconsistent on the locally
            // controlled soldier; that soldier must never be treated as autonomous,
            // or the AI fire/movement modules would eat the player's own actions.
            if (soldier.GetInstanceID() == GetLocalControlledSoldierId())
            {
                if (!_loggedWedgedControlFlags)
                {
                    _loggedWedgedControlFlags = true;
                    Plugin.LogSource.LogWarning(
                        "Locally controlled soldier reported IsAI && !IsPlayer; refusing autonomous ownership (native control flags wedged).");
                }
                return false;
            }

            return true;
        }
        catch (Il2CppInterop.Runtime.Il2CppException)
        {
            return false;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    /// <summary>
    /// The single gate for every behavior module: true only for AI the mod owns.
    /// A soldier sharing a squad with a player is excluded, so the player's own
    /// squadmates run the vanilla AI and answer to nothing but the player's orders.
    /// This is structural, not a setting.
    /// </summary>
    internal static bool IsAutonomous([NotNullWhen(true)] Soldier? soldier)
        => IsAiControlled(soldier) && !IsInPlayerSquad(soldier);

    internal static bool IsInPlayerSquad(Soldier? soldier)
    {
        if (soldier == null)
            return false;

        try
        {
            return IsPlayerSquad(soldier.joinedSquad);
        }
        catch (Exception ex)
        {
            // A soldier disappearing mid-test is not one to start steering.
            ReportSquadTestFailure(ex);
            return true;
        }
    }

    /// <summary>
    /// True when a human commands this squad, in single player or as any client of
    /// a hosted session. Squadless soldiers are never player-led. Every failure
    /// answers "player squad" so an unreadable squad is left to the native AI.
    /// </summary>
    internal static bool IsPlayerSquad(Squad? squad)
    {
        if (squad == null)
            return false;

        try
        {
            var now = Time.unscaledTime;
            if (now >= _squadVerdictsExpireAt)
            {
                // Wholesale clearing keeps the map bounded and stops a recycled squad
                // pointer from carrying a stale verdict for longer than one interval.
                SquadVerdicts.Clear();
                _squadVerdictsExpireAt = now + SquadVerdictSeconds;
            }

            var key = squad.Pointer;
            if (SquadVerdicts.TryGetValue(key, out var cached))
                return cached;

            var verdict = HasPlayerMember(squad);
            SquadVerdicts[key] = verdict;
            return verdict;
        }
        catch (Exception ex)
        {
            // Every behavior gate funnels through here, so this must never throw into
            // a patched native method.
            ReportSquadTestFailure(ex);
            return true;
        }
    }

    private static bool HasPlayerMember(Squad squad)
    {
        // Native check: the locally controlled soldier's own squad.
        if (squad.IsPlayerInSquad())
            return true;

        // Remote players only exist online, and the component walk is the expensive
        // half of this test, so single player never pays for it.
        if (!Lua_API.isOnline())
            return false;

        for (var index = 0; index < squad.CountMembers; index++)
        {
            var member = squad.GetMember(index);
            if (member == null)
                continue;

            var sync = member.GetComponent<SyncSoldier>();
            if (sync != null && sync.IsControlledByAPlayer())
                return true;
        }

        return false;
    }

    private static void ReportSquadTestFailure(Exception exception)
    {
        if (_loggedSquadTestFailure)
            return;

        _loggedSquadTestFailure = true;
        Plugin.LogSource.LogWarning(
            "Could not determine squad player membership; affected AI will run vanilla " +
            $"(further identical warnings suppressed): {exception.Message}");
    }

    private static int GetLocalControlledSoldierId()
    {
        var frame = UnityEngine.Time.frameCount;
        if (frame != _localSoldierFrame)
        {
            _localSoldierFrame = frame;
            var local = Soldier.CurrentControlledSoldierOrNull();
            _localSoldierId = local != null ? local.GetInstanceID() : 0;
        }

        return _localSoldierId;
    }
}
