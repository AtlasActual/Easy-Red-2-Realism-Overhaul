using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(Soldier), nameof(Soldier.Suppress))]
internal static class IncomingFireCuePatch
{
    [HarmonyPrefix]
    private static void Prefix(Soldier __instance, int suppressionValueAdd, Soldier shooter)
    {
        if (IncomingFireAwareness.IsNonDirectionalSuppression ||
            !Settings.PerceptionEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            suppressionValueAdd <= 0)
        {
            return;
        }

        try
        {
            if (__instance == null || shooter == null ||
                !__instance.IsAlive || !shooter.IsAlive ||
                !AiOwnership.IsAutonomous(__instance) || __instance.IsOnVehicle() ||
                __instance.GetInstanceID() == shooter.GetInstanceID())
            {
                return;
            }

            var victimFaction = __instance.faction;
            var shooterFaction = shooter.faction;
            if (string.IsNullOrWhiteSpace(victimFaction) ||
                string.IsNullOrWhiteSpace(shooterFaction) ||
                string.Equals(victimFaction, Soldier.UnknownFaction, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(shooterFaction, Soldier.UnknownFaction, StringComparison.OrdinalIgnoreCase) ||
                !ResourcesManager.IsEnemyFaction(victimFaction, shooterFaction))
            {
                return;
            }

            IncomingFireAwareness.Record(
                __instance,
                shooter.GetCenterOfUnit(),
                shooter.Pointer,
                shooter.GetInstanceID(),
                Time.time);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Incoming-fire cue failed: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(GenericGun), nameof(GenericGun.Shoot))]
internal static class HandheldGunfireAwarenessPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        Creature user,
        Vector3 fireDir,
        bool isFakeShot,
        bool __runOriginal)
    {
        if (!__runOriginal)
            return;

        try
        {
            // Shoot is reached by both normal Fire and network-replayed FakeFire.
            // A fake/network shot is still an audible shot for the host's AI.
            var now = Time.time;
            GunfireAwareness.RecordShot(user, now);
            if (!isFakeShot && MultiplayerAuthority.CanMutateGameplay())
            {
                var shooter = user?.TryCast<Soldier>();
                if (shooter != null)
                    ContactResponse.RecordActualShot(shooter, fireDir, now);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Gunfire cue failed: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(TurretGun), nameof(TurretGun.Shoot))]
internal static class MountedGunfireAwarenessPatch
{
    [HarmonyPostfix]
    private static void Postfix(Creature user, bool __runOriginal)
    {
        if (!__runOriginal)
            return;

        try
        {
            GunfireAwareness.RecordShot(user, Time.time);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Mounted gunfire cue failed: {ex.Message}");
        }
    }
}

internal static class GunfireAwareness
{
    private const float CueLifetimeSeconds = 3f;
    private const float HearingRadius = 225f;
    private const float ListenerPollIntervalSeconds = 0.25f;
    private const int MaximumActiveShooters = 128;
    private const int MaximumListenerCueScansPerFrame = 16;
    private const float CuePruneCadenceSeconds = 0.25f;

    private static bool _disabled;
    private static float _nextCuePruneAt;
    private static int _listenerPollFrame = -1;
    private static int _listenerPollsThisFrame;

    internal static void RecordShot(Creature user, float now)
    {
        if (!EnsureEnabled())
            return;

        var shooter = user?.TryCast<Soldier>();
        if (shooter == null || !shooter.IsAlive)
            return;

        var faction = shooter.faction;
        if (string.IsNullOrWhiteSpace(faction) ||
            string.Equals(faction, Soldier.UnknownFaction, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var shooterToken = shooter.Pointer;
        if (shooterToken == IntPtr.Zero)
            return;

        var shooterId = shooter.GetInstanceID();
        PruneExpiredIfDue(now);
        if (!AiState.GunfireCues.ContainsKey(shooterId) &&
            AiState.GunfireCues.Count >= MaximumActiveShooters)
        {
            EvictOldestCue();
        }

        // One entry per shooter bounds automatic fire to a position/expiry refresh.
        AiState.GunfireCues[shooterId] = new GunfireCue(
            shooterToken,
            faction,
            shooter.GetCenterOfUnit(),
            now + CueLifetimeSeconds);
    }

    internal static void Poll(Soldier listener, float now)
    {
        if (!EnsureEnabled() || listener == null || !listener.IsAlive ||
            !AiOwnership.IsAutonomous(listener) || listener.IsOnVehicle())
        {
            return;
        }

        var listenerFaction = listener.faction;
        if (string.IsNullOrWhiteSpace(listenerFaction) ||
            string.Equals(listenerFaction, Soldier.UnknownFaction, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var listenerId = listener.GetInstanceID();
        var memory = AiState.GetTargetMemory(listenerId);
        if (now < memory.NextGunfirePollAt)
            return;

        // A shooter whose rounds are directly suppressing this soldier is more
        // actionable than a merely audible weapon elsewhere. This path does not
        // need to scan the shared cue table.
        if (memory.IncomingFireIsDirect && now < memory.IncomingFireUntil)
            return;

        // Reinforcements commonly enter combat on the same frame. Without a shared
        // budget they all scan the full shooter table together every quarter second,
        // causing a recurring hitch that grows with the number of active soldiers.
        if (!TryReserveListenerCueScan())
            return;

        // Keep each listener on a stable, slightly different cadence after its first
        // scan instead of relocking the whole formation to one update instant.
        memory.NextGunfirePollAt = now + ListenerPollIntervalSeconds +
                                   (listenerId & 7) * 0.0075f;

        PruneExpiredIfDue(now);
        var origin = listener.LookPosition();
        var nearestDistanceSqr = HearingRadius * HearingRadius;
        var found = false;
        var nearestShooterId = 0;
        GunfireCue nearest = default;

        foreach (var pair in AiState.GunfireCues)
        {
            var cue = pair.Value;
            if (pair.Key == listenerId || cue.ExpiresAt <= now ||
                !ResourcesManager.IsEnemyFaction(listenerFaction, cue.ShooterFaction))
            {
                continue;
            }

            var distanceSqr = (cue.Position - origin).sqrMagnitude;
            if (distanceSqr >= nearestDistanceSqr)
                continue;

            found = true;
            nearestDistanceSqr = distanceSqr;
            nearestShooterId = pair.Key;
            nearest = cue;
        }

        if (found)
        {
            IncomingFireAwareness.RecordHeardGunfire(
                listener,
                nearest.Position,
                nearest.ShooterToken,
                nearestShooterId,
                now,
                nearest.ExpiresAt);
        }
    }

    internal static void RemoveShooter(int shooterId, IntPtr shooterToken)
    {
        AiState.GunfireCues.Remove(shooterId);
        if (shooterToken == IntPtr.Zero)
            return;

        foreach (var memory in AiState.TargetMemory.Values)
        {
            if (memory.IncomingFireShooterToken == shooterToken)
                IncomingFireAwareness.Clear(memory);
        }
    }

    internal static void Disable()
    {
        if (_disabled)
            return;

        _disabled = true;
        ClearAll();
    }

    internal static void MarkEnabled()
    {
        _disabled = false;
    }

    internal static void ResetBattle()
    {
        ClearAll();
        _disabled = !Settings.PerceptionEnabled.Value ||
                    !MultiplayerAuthority.CanMutateGameplay();
        _nextCuePruneAt = 0f;
        _listenerPollFrame = -1;
        _listenerPollsThisFrame = 0;
    }

    private static bool EnsureEnabled()
    {
        if (!Settings.PerceptionEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            Disable();
            return false;
        }

        _disabled = false;
        return true;
    }

    private static void ClearAll()
    {
        AiState.GunfireCues.Clear();
        foreach (var memory in AiState.TargetMemory.Values)
            IncomingFireAwareness.Clear(memory);
    }

    private static bool TryReserveListenerCueScan()
    {
        var frame = Time.frameCount;
        if (frame != _listenerPollFrame)
        {
            _listenerPollFrame = frame;
            _listenerPollsThisFrame = 0;
        }

        if (_listenerPollsThisFrame >= MaximumListenerCueScansPerFrame)
            return false;

        _listenerPollsThisFrame++;
        return true;
    }

    private static void PruneExpiredIfDue(float now)
    {
        if (now < _nextCuePruneAt)
            return;

        _nextCuePruneAt = now + CuePruneCadenceSeconds;
        PruneExpired(now);
    }

    private static void PruneExpired(float now)
    {
        List<int>? expired = null;
        foreach (var pair in AiState.GunfireCues)
        {
            if (pair.Value.ExpiresAt <= now)
                (expired ??= new List<int>()).Add(pair.Key);
        }

        if (expired == null)
            return;

        foreach (var shooterId in expired)
            AiState.GunfireCues.Remove(shooterId);
    }

    private static void EvictOldestCue()
    {
        var found = false;
        var oldestShooterId = 0;
        var oldestExpiry = float.MaxValue;
        foreach (var pair in AiState.GunfireCues)
        {
            if (pair.Value.ExpiresAt >= oldestExpiry)
                continue;

            found = true;
            oldestShooterId = pair.Key;
            oldestExpiry = pair.Value.ExpiresAt;
        }

        if (found)
            AiState.GunfireCues.Remove(oldestShooterId);
    }
}

[HarmonyPatch(typeof(BattleManager), "Start")]
internal static class GunfireAwarenessBattleResetPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        InteropWrapperLifetime.ResetBattle();
        AiOwnership.ResetBattle();
        GunfireAwareness.ResetBattle();
        MountedGunnerSuppression.ResetBattle();
        KnownTargetSuppressiveFire.ResetBattle();
        ContactResponse.ResetBattleAttackEvidence();
        CasualtySuppression.ResetBattle();
    }
}

internal static class IncomingFireAwareness
{
    private const float CueLifetimeSeconds = 4f;
    private const float CandidateAttentionFreshSeconds = 1.5f;

    [ThreadStatic]
    private static int _nonDirectionalSuppressionDepth;

    internal static bool IsNonDirectionalSuppression => _nonDirectionalSuppressionDepth > 0;

    internal static void Record(
        Soldier soldier,
        Vector3 sourcePosition,
        IntPtr shooterToken,
        int shooterId,
        float now)
        => RecordCue(
            soldier,
            sourcePosition,
            shooterToken,
            shooterId,
            now,
            now + CueLifetimeSeconds,
            isDirect: true);

    internal static void RecordHeardGunfire(
        Soldier soldier,
        Vector3 sourcePosition,
        IntPtr shooterToken,
        int shooterId,
        float now,
        float expiresAt)
    {
        if (expiresAt <= now)
            return;

        RecordCue(
            soldier,
            sourcePosition,
            shooterToken,
            shooterId,
            now,
            expiresAt,
            isDirect: false);
    }

    private static void RecordCue(
        Soldier soldier,
        Vector3 sourcePosition,
        IntPtr shooterToken,
        int shooterId,
        float now,
        float expiresAt,
        bool isDirect)
    {
        GunfireAwareness.MarkEnabled();
        var state = AiState.GetTargetMemory(soldier.GetInstanceID());

        var isNewCue = now >= state.IncomingFireUntil;

        if (!isNewCue && state.IncomingFireIsDirect != isDirect)
        {
            if (state.IncomingFireIsDirect)
                return;

            // Direct suppression always replaces a merely heard shot.
        }

        // Repeated fire from the same source refreshes the cue. Competing sources
        // at the same priority only replace it when they are closer.
        if (!isNewCue && state.IncomingFireIsDirect == isDirect &&
            state.IncomingFireShooterToken != shooterToken)
        {
            var origin = soldier.LookPosition();
            var existingDistanceSqr = (state.IncomingFirePosition - origin).sqrMagnitude;
            var newDistanceSqr = (sourcePosition - origin).sqrMagnitude;
            if (existingDistanceSqr <= newDistanceSqr)
                return;
        }

        state.IncomingFirePosition = sourcePosition;
        state.IncomingFireUntil = expiresAt;
        state.IncomingFireShooterToken = shooterToken;
        state.IncomingFireIsDirect = isDirect;

        if (isNewCue)
        {
            AiState.Trace(
                $"Incoming fire: soldier {soldier.GetInstanceID()} orienting toward " +
                $"{(isDirect ? "direct" : "heard")} hostile shooter {shooterId}");
        }
    }

    internal static bool HasActiveCue(int soldierId, float now)
        => Settings.PerceptionEnabled.Value &&
           AiState.TargetMemory.TryGetValue(soldierId, out var state) &&
           now < state.IncomingFireUntil;

    internal static bool TryGetActiveDirectCue(
        int soldierId,
        float now,
        out Vector3 sourcePosition)
    {
        sourcePosition = default;
        if (!Settings.PerceptionEnabled.Value ||
            !AiState.TargetMemory.TryGetValue(soldierId, out var state) ||
            !state.IncomingFireIsDirect || now >= state.IncomingFireUntil)
        {
            return false;
        }

        sourcePosition = state.IncomingFirePosition;
        return true;
    }

    internal static void Update(SoldierAI ai, Soldier soldier, float now)
    {
        var soldierId = soldier.GetInstanceID();
        if (!AiState.TargetMemory.TryGetValue(soldierId, out var state) ||
            state.IncomingFireUntil <= 0f)
        {
            return;
        }

        if (!Settings.PerceptionEnabled.Value || now >= state.IncomingFireUntil ||
            !soldier.IsAlive || !AiOwnership.IsAutonomous(soldier) || soldier.IsOnVehicle())
        {
            Clear(state);
            return;
        }

        // Emergency movement and an actual weapon burst retain ownership. The cue
        // remains live and can turn the soldier as soon as that short action ends.
        if ((Settings.DangerReactionsEnabled.Value &&
             (soldier.IsOnFire || AiState.IsFlameEvading(soldierId, now))) ||
            ContactResponse.IsWeaponFiring(soldier))
        {
            return;
        }

        // Everything above is cue bookkeeping and must run for every soldier so stale
        // cues expire. Everything below reaches into the game — look position, target
        // resolution, movement test, body rotation — once per soldier per physics step
        // for every soldier currently under fire, which in a large battle is most of
        // them at once. It was the last per-soldier system with no per-frame ceiling,
        // measured at 34.8ms in one frame with 149 soldiers.
        //
        // A soldier over budget keeps his cue (it is time-based) and turns on a later
        // frame through the angle he would have covered anyway, so deferring costs
        // nothing observable.
        if (!TryTakeIncomingFireTurnBudget())
            return;

        var origin = soldier.LookPosition();
        var towardSource = state.IncomingFirePosition - origin;
        if (towardSource.sqrMagnitude <= 0.01f)
        {
            Clear(state);
            return;
        }

        // A closer incoming shooter may pull attention, but a nearer confirmed or
        // actively observed target wins. The shooter still has to enter the cone
        // and complete the normal visual acquisition delay before becoming a target.
        if (KnownTargetIsAtLeastAsClose(
                ai,
                soldier,
                state,
                origin,
                towardSource.sqrMagnitude,
                now))
            return;

        // Body rotation and locomotion cannot safely own different bearings in
        // this animation system. Keep the cue alive while moving, then turn and
        // perform the normal visual acquisition as soon as the soldier halts.
        if (soldier.IsMoving(0.15f))
            return;

        towardSource.y = 0f;
        if (towardSource.sqrMagnitude > 0.01f)
        {
            // Driven by real elapsed time rather than the fixed step, so the budget
            // changes WHEN a soldier turns, never how fast he turns. Clamped so a long
            // gap (a hitch, or a spell over budget) cannot snap him round in one step.
            var turnDelta = state.LastIncomingFireTurnAt > 0f
                ? Mathf.Clamp(now - state.LastIncomingFireTurnAt, Time.fixedDeltaTime, 0.1f)
                : Time.fixedDeltaTime;
            state.LastIncomingFireTurnAt = now;
            soldier.RotateToward(towardSource.normalized, turnDelta);
        }
    }

    // Ceiling on how many soldiers may run the incoming-fire orientation step in one
    // frame. Sized well above the number that can be turning at once in ordinary
    // fighting, so it binds only during a battle-wide volley — which is exactly the
    // burst it exists to flatten.
    private const int MaxIncomingFireTurnsPerFrame = 12;

    private static int _incomingFireTurnFrame = -1;
    private static int _incomingFireTurnsThisFrame;

    private static bool TryTakeIncomingFireTurnBudget()
    {
        var frame = Time.frameCount;
        if (frame != _incomingFireTurnFrame)
        {
            _incomingFireTurnFrame = frame;
            _incomingFireTurnsThisFrame = 0;
        }

        if (_incomingFireTurnsThisFrame >= MaxIncomingFireTurnsPerFrame)
            return false;

        _incomingFireTurnsThisFrame++;
        return true;
    }

    private static bool KnownTargetIsAtLeastAsClose(
        SoldierAI ai,
        Soldier soldier,
        TargetMemoryState state,
        Vector3 origin,
        float incomingDistanceSqr,
        float now)
    {
        if (state.HasConfirmedTarget)
        {
            var current = TargetAcquisition.ResolveObservedTarget(ai, soldier);
            if (TargetAcquisition.MatchesTarget(current, state.TargetToken) &&
                TargetAcquisition.TryGetTargetSnapshot(current, out _, out var targetPosition) &&
                (targetPosition - origin).sqrMagnitude <= incomingDistanceSqr)
            {
                return true;
            }
        }

        foreach (var pair in state.Candidates)
        {
            // The cue remains responsible for finishing the turn toward its own
            // shooter until that candidate completes acquisition.
            if (pair.Key == state.IncomingFireShooterToken)
                continue;

            var candidate = pair.Value;
            if (now - candidate.LastSeenAt <= CandidateAttentionFreshSeconds &&
                (candidate.LastKnownPosition - origin).sqrMagnitude <= incomingDistanceSqr)
            {
                return true;
            }
        }

        return false;
    }

    internal static void ApplyNonDirectionalSuppression(
        Soldier soldier,
        int amount,
        Soldier? responsible)
    {
        _nonDirectionalSuppressionDepth++;
        try
        {
            soldier.Suppress(amount, responsible);
        }
        finally
        {
            _nonDirectionalSuppressionDepth--;
        }
    }

    internal static void Clear(TargetMemoryState state)
    {
        state.IncomingFirePosition = default;
        state.IncomingFireUntil = 0f;
        state.IncomingFireShooterToken = IntPtr.Zero;
        state.IncomingFireIsDirect = false;
    }
}

[HarmonyPatch(typeof(SoldierAI), "FixedUpdate")]
internal static class IncomingFireOrientationPatch
{
    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance)
    {
        InteropWrapperLifetime.Retain(__instance);
        var __t = ModTimeProbe.Begin();
        var __a = ModTimeProbe.BeginAlloc();
        try
        {
            if (!MultiplayerAuthority.CanMutateGameplay() ||
                __instance == null)
            {
                return;
            }

            try
            {
                var soldier = __instance.GetSoldier();
                if (AiOwnership.IsAutonomous(soldier))
                {
                    // Acquisition is not aim ownership: run the close-range confirm
                    // tick even while KnownTargetSuppressiveFire owns aim, unlike the
                    // orientation update below.
                    CloseRangeAcquisitionTick.Update(__instance, soldier, Time.time);

                    if (!KnownTargetSuppressiveFire.OwnsAim(soldier.GetInstanceID(), Time.time))
                    {
                        IncomingFireAwareness.Update(__instance, soldier, Time.time);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"Incoming-fire orientation failed: {ex.Message}");
            }
        }
        finally
        {
            ModTimeProbe.EndSiteAlloc(ModTimeSite.IncomingFire, __a);
            ModTimeProbe.EndIncomingFire(__t);
        }
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.CanSee))]
internal static class SoldierVisibilityConePatch
{
    [HarmonyPrefix]
    private static bool Prefix(Creature __instance, Spottable seeTarget, ref bool __result)
    {
        if (!Settings.PerceptionEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            __instance == null)
        {
            return true;
        }

        try
        {
            var soldier = __instance.TryCast<Soldier>();
            if (ReferenceEquals(soldier, null) || soldier == null ||
                !AiOwnership.IsAutonomous(soldier) || soldier.IsOnVehicle())
            {
                return true;
            }

            if (!TargetAcquisition.TryGetTargetSnapshot(seeTarget, out _, out var targetPosition))
                return true;

            var distance = Vector3.Distance(soldier.LookPosition(), targetPosition);
            if (TargetAcquisition.IsInsideEffectiveFov(
                    soldier,
                    targetPosition,
                    distance,
                    TargetAcquisition.Suppression(soldier)))
            {
                return true;
            }

            // Feed the configured cone into the native scan before it minimizes
            // distance. It can then continue to the nearest LOS-valid target that
            // is actually detectable instead of returning one we reject afterward.
            __result = false;
            return false;
        }
        catch
        {
            // Fail open to the native visibility raycast during object teardown.
            return true;
        }
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.GetBestVisibleEnemy))]
internal static class SoldierTargetAcquisitionPatch
{
    [HarmonyPrefix]
    private static void Prefix(Soldier __instance, out Spottable? __state)
    {
        __state = null;
        if (!ShouldApply(__instance))
            return;

        try
        {
            if (!AiState.TargetMemory.TryGetValue(__instance.GetInstanceID(), out var memory) ||
                !memory.HasConfirmedTarget)
            {
                return;
            }

            var current = TargetAcquisition.GetUsableSoldierTarget(__instance);
            if (TargetAcquisition.MatchesTarget(current, memory.TargetToken))
            {
                __state = current;
                return;
            }

            var ai = __instance.aiController;
            var visible = ai == null ? null : TargetAcquisition.GetUsableAiTarget(ai);
            if (TargetAcquisition.MatchesTarget(visible, memory.TargetToken))
                __state = visible;
        }
        catch
        {
            // The postfix will fail closed if the soldier is being torn down.
        }
    }

    [HarmonyPostfix]
    private static void Postfix(
        Soldier __instance,
        ref float dist,
        ref Spottable __result,
        Spottable? __state)
    {
        if (!ShouldApply(__instance))
            return;

        try
        {
            var now = Time.time;
            if (!TargetAcquisition.TryGetTargetSnapshot(__result, out var targetToken, out var targetPosition))
            {
                // This is the result of a real native visibility scan. Unlike the
                // later SequentialUpdate null (which can be caused by this patch
                // withholding an unconfirmed candidate), it is genuine negative
                // evidence and must break any pending observation streak.
                TargetAcquisition.ResetCandidatesAfterNegativeObservation(__instance);
                TargetAcquisition.ExpireUnobserved(__instance, now);
                TargetAcquisition.PublishSoldierTarget(__instance, null);
                __result = null!;
                dist = float.MaxValue;
                return;
            }

            // The native scan has already selected the nearest LOS-valid target
            // inside our cone; this layer only applies target-specific acquisition.
            var target = __result;
            TargetAcquisition.RetainOnlyNativeCandidate(__instance, targetToken);
            var distance = Vector3.Distance(__instance.LookPosition(), targetPosition);
            var suppression = TargetAcquisition.Suppression(__instance);
            var insideFov = TargetAcquisition.IsInsideEffectiveFov(
                __instance, targetPosition, distance, suppression);
            if (insideFov && TargetAcquisition.TryConfirm(__instance, target, distance, suppression, now))
            {
                TargetAcquisition.RecordConfirmedNativeObservation(
                    __instance, targetToken, targetPosition, now);
                return;
            }

            if (!insideFov)
                TargetAcquisition.RejectCandidate(__instance, target);

            // Keep using a still-current confirmed target while a different target
            // earns its own reaction delay. Without this fallback, one sample of a
            // competing target makes the native caller discard the valid target.
            if (TargetAcquisition.TryRetainConfirmedTarget(
                    __instance, __state, now, out var confirmed, out var confirmedDistance))
            {
                TargetAcquisition.PublishSoldierTarget(__instance, confirmed);
                __result = confirmed;
                dist = confirmedDistance;
                return;
            }

            // Withhold the candidate before the native caller can install it as the
            // active target. This prevents aim rotation as well as premature fire.
            TargetAcquisition.PublishSoldierTarget(__instance, null);
            __result = null!;
            dist = float.MaxValue;
        }
        catch (Exception ex)
        {
            TargetAcquisition.PublishSoldierTarget(__instance, null);
            __result = null!;
            dist = float.MaxValue;
            Plugin.LogSource.LogWarning($"Target acquisition check failed: {ex.Message}");
        }
    }

    private static bool ShouldApply(Soldier? soldier)
        => Settings.PerceptionEnabled.Value &&
           MultiplayerAuthority.CanMutateGameplay() &&
           soldier != null &&
           AiOwnership.IsAutonomous(soldier) &&
           !soldier.IsOnVehicle();
}

[HarmonyPatch(typeof(SoldierAI), "SequentialUpdate")]
internal static class SoldierSequentialUpdatePatch
{
    [HarmonyPrefix]
    private static void Prefix(SoldierAI __instance)
    {
        InteropWrapperLifetime.Retain(__instance);
        var __t = ModTimeProbe.Begin();
        try
        {
            ModTimeProbe.Stage(SequentialStage.PrefixEntry);
            if (!MultiplayerAuthority.CanMutateGameplay())
                return;

            var soldier = __instance.GetSoldier();
            if (!AiOwnership.IsAutonomous(soldier) ||
                soldier.IsOnVehicle())
            {
                return;
            }

            // Establish and enforce the persistent defensive-position owner before
            // native SequentialUpdate gets a chance to turn HoldArea into another
            // walk. The lower movement executors below enforce the same invariant.
            ModTimeProbe.Stage(SequentialStage.PrefixDefensiveHold);
            if (ContactResponse.ShouldHoldDefensivePosition(soldier, Time.time))
            {
                ContactResponse.StopTacticalMovement(
                    __instance,
                    soldier,
                    Time.deltaTime,
                    "native-hold-defensive");
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning(
                $"Defensive position pre-update failed: {ex.Message}");
        }
        finally
        {
            ModTimeProbe.EndSequentialUpdate(__t);
        }
    }

    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            ModTimeProbe.Stage(SequentialStage.PostfixEntry);
            if (!MultiplayerAuthority.CanMutateGameplay())
            {
                GunfireAwareness.Disable();
                var inactiveSoldier = __instance.GetSoldier();
                if (inactiveSoldier != null)
                    KnownTargetSuppressiveFire.Disable(__instance, inactiveSoldier);
                return;
            }

            if (!Settings.PerceptionEnabled.Value)
                GunfireAwareness.Disable();

            var soldier = __instance.GetSoldier();
            if (soldier == null)
                return;

            var now = Time.time;

            if (!AiOwnership.IsAutonomous(soldier))
                return;

            if (soldier.IsOnVehicle())
            {
                ModTimeProbe.Stage(SequentialStage.PostfixVehicleSuspend);
                KnownTargetSuppressiveFire.Disable(__instance, soldier);
                AiState.TargetMemory.Remove(soldier.GetInstanceID());
                ContactResponse.SuspendForVehicle(__instance, soldier);
                return;
            }

            GroundAiDirector.UpdateSoldier(__instance, soldier, now);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Soldier tactical update failed: {ex.Message}");
        }
        finally
        {
            ModTimeProbe.EndSequentialUpdate(__t);
        }
    }

    internal static void ApplyPerception(SoldierAI ai, Soldier soldier)
    {
        var target = TargetAcquisition.ResolveObservedTarget(ai, soldier);
        if (!TargetAcquisition.TryGetTargetSnapshot(target, out var targetToken, out var targetPosition))
        {
            TargetAcquisition.ExpireUnobserved(soldier, Time.time);
            return;
        }

        var origin = soldier.LookPosition();
        var toTarget = targetPosition - origin;
        var distance = toTarget.magnitude;
        var id = soldier.GetInstanceID();
        var now = Time.time;
        var suppression = TargetAcquisition.Suppression(soldier);
        var effectiveFov = Settings.HorizontalFov.Value *
                           Mathf.Lerp(1f, Settings.SuppressedFovMultiplier.Value, suppression);
        var effectivePeripheralDistance = TargetAcquisition.EffectivePeripheralDistance(suppression);
        var effectiveMemory = Settings.TargetMemorySeconds.Value *
                              Mathf.Lerp(1f, Settings.SuppressedMemoryMultiplier.Value, suppression);
        var insideFov = distance <= effectivePeripheralDistance ||
                        Vector3.Angle(soldier.transform.forward, toTarget) <= effectiveFov * 0.5f;

        if (insideFov)
        {
            if (!TargetAcquisition.TryConfirm(soldier, target!, distance, suppression, now))
                return;

            // Some native update paths publish a target on Soldier before mirroring
            // it to SoldierAI. Mirror only the confirmed target; SoldierAI fires
            // through visibleTarget when contact response is disabled.
            if (!TargetAcquisition.MatchesTarget(ai.visibleTarget, targetToken))
                ai.visibleTarget = target;

            var memory = AiState.GetTargetMemory(id);
            memory.LastObservedAt = now;
            return;
        }

        if (!AiState.TargetMemory.TryGetValue(id, out var remembered) || remembered.TargetToken != targetToken)
        {
            if (remembered != null)
                TargetAcquisition.Forget(remembered, targetToken);
            TargetAcquisition.RejectCandidate(soldier, target!);
            ClearTarget(ai, soldier, targetToken, id, distance, "never observed inside FOV");
            return;
        }

        if (now - remembered.LastObservedAt < effectiveMemory)
            return;

        TargetAcquisition.Forget(remembered, targetToken);
        ClearTarget(ai, soldier, targetToken, id, distance, "memory expired");
    }

    private static void ClearTarget(
        SoldierAI ai,
        Soldier soldier,
        IntPtr targetToken,
        int id,
        float distance,
        string reason)
    {
        if (TargetAcquisition.MatchesTarget(ai.visibleTarget, targetToken))
            ai.visibleTarget = null;
        if (TargetAcquisition.MatchesTarget(soldier.CurrentVisibleTarget, targetToken))
            soldier.SetBestVisibleEnemy(null);

        // Stop the stale shot, but do not latch allowFireAtEnemy off. The native AI
        // remains free to scan, acquire, and fire at the next confirmed target.
        GroundAiDirector.ExecuteSoldierStopFire(soldier);
        AiState.Trace($"FOV: soldier {id} rejected out-of-cone target at {distance:0}m ({reason})");
    }

    // Tank-ness never changes for a live vehicle, so a stale cache entry is harmless;
    // the alternative is a GetComponent call per vehicle per frame on AIVehicle.Update.
    // Component lookups cross into il2cpp and are the cost this cache exists to remove.
    private static readonly Dictionary<int, bool> TankVehicleCache = new();

    internal static bool IsTankCached(Vehicle vehicle)
    {
        var id = vehicle.GetInstanceID();
        if (TankVehicleCache.TryGetValue(id, out var isTank))
            return isTank;

        isTank = vehicle.GetComponent<VehicleTank>() != null;
        TankVehicleCache[id] = isTank;
        return isTank;
    }

    internal static void ClearVehicleCaches()
    {
        TankVehicleCache.Clear();
    }
}

internal static class TargetAcquisition
{
    // Native visibility queries are staggered, so valid successive samples credit
    // their full wall-clock gap. Only a gap beyond this bound resets acquisition.
    private const float CandidateStaleSeconds = 30f;

    internal static Spottable? ResolveObservedTarget(SoldierAI ai, Soldier soldier)
    {
        var visible = GetUsableAiTarget(ai);
        var current = GetUsableSoldierTarget(soldier);
        if (AiState.TargetMemory.TryGetValue(soldier.GetInstanceID(), out var memory) &&
            memory.HasConfirmedTarget)
        {
            if (MatchesTarget(visible, memory.TargetToken))
                return visible;
            if (MatchesTarget(current, memory.TargetToken))
                return current;
        }

        return visible ?? current;
    }

    internal static Spottable? GetUsableAiTarget(SoldierAI ai)
    {
        try
        {
            var target = ai.visibleTarget;
            if (IsUsableTarget(target))
                return target;
            if (target != null)
                ai.visibleTarget = null;
        }
        catch (NullReferenceException)
        {
            TryClearAiTarget(ai);
        }
        catch (Il2CppException)
        {
            TryClearAiTarget(ai);
        }
        catch (ObjectCollectedException)
        {
            TryClearAiTarget(ai);
        }

        return null;
    }

    internal static Spottable? GetUsableSoldierTarget(Soldier soldier)
    {
        try
        {
            var target = soldier.CurrentVisibleTarget;
            if (IsUsableTarget(target))
                return target;
            if (target != null)
                soldier.SetBestVisibleEnemy(null);
        }
        catch (NullReferenceException)
        {
            TryClearSoldierTarget(soldier);
        }
        catch (Il2CppException)
        {
            TryClearSoldierTarget(soldier);
        }
        catch (ObjectCollectedException)
        {
            TryClearSoldierTarget(soldier);
        }

        return null;
    }

    internal static bool IsUsableTarget(Spottable? target)
    {
        if (target == null)
            return false;

        try
        {
            return !target.WasCollected && target.Pointer != IntPtr.Zero;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
    }

    internal static bool MatchesTarget(Spottable? target, IntPtr targetToken)
    {
        if (targetToken == IntPtr.Zero || !IsUsableTarget(target))
            return false;

        try
        {
            return target!.Pointer == targetToken;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
    }

    internal static bool TryGetTargetSnapshot(
        Spottable? target,
        out IntPtr targetToken,
        out Vector3 targetPosition)
    {
        targetToken = IntPtr.Zero;
        targetPosition = default;
        if (!IsUsableTarget(target))
            return false;

        try
        {
            targetToken = target!.Pointer;
            targetPosition = target.GetCenterOfUnit();
            return targetToken != IntPtr.Zero;
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
    }

    internal static float Suppression(Soldier soldier)
        => Settings.SuppressionAwarenessEnabled.Value
            ? Mathf.Clamp01(soldier.GetSuppressionValue() / 255f)
            : 0f;

    internal static float EffectivePeripheralDistance(float suppression)
    {
        var distance = Settings.PeripheralAwarenessDistance.Value *
                       Mathf.Lerp(1f, Settings.SuppressedPeripheralMultiplier.Value, suppression);

        if (!Settings.CloseQuartersEnabled.Value)
            return distance;

        // Suppression can never blind a soldier to a threat within the
        // configurable minimum awareness floor, and the floor never raises
        // awareness above the unsuppressed distance.
        return Mathf.Min(
            Settings.PeripheralAwarenessDistance.Value,
            Mathf.Max(Settings.MinimumPeripheralAwarenessMeters.Value, distance));
    }

    internal static bool IsInsideEffectiveFov(
        Soldier soldier,
        Vector3 targetPosition,
        float distance,
        float suppression)
    {
        var effectivePeripheralDistance = EffectivePeripheralDistance(suppression);
        if (distance <= effectivePeripheralDistance)
            return true;

        var direction = targetPosition - soldier.LookPosition();
        var effectiveFov = Settings.HorizontalFov.Value *
                           Mathf.Lerp(1f, Settings.SuppressedFovMultiplier.Value, suppression);
        return Vector3.Angle(soldier.transform.forward, direction) <= effectiveFov * 0.5f;
    }

    internal static bool TryConfirm(
        Soldier soldier,
        Spottable target,
        float distance,
        float suppression,
        float now)
    {
        var soldierId = soldier.GetInstanceID();
        if (!TryGetTargetSnapshot(target, out var targetToken, out var targetPosition))
            return false;
        var state = AiState.GetTargetMemory(soldierId);
        var effectiveMemory = Settings.TargetMemorySeconds.Value *
                              Mathf.Lerp(1f, Settings.SuppressedMemoryMultiplier.Value, suppression);

        // A positive observation of the already-confirmed target is current proof,
        // even when native visibility scans are farther apart than the memory timer.
        if (state.HasConfirmedTarget && state.TargetToken == targetToken)
        {
            state.LastObservedAt = now;
            return true;
        }

        if (state.HasConfirmedTarget && now - state.LastObservedAt > effectiveMemory)
        {
            var expiredToken = state.TargetToken;
            state.HasConfirmedTarget = false;
            state.TargetToken = IntPtr.Zero;
            AiState.Trace($"Acquisition: soldier {soldierId} forgot target {expiredToken} before evaluating {targetToken}");
        }

        if (!state.Candidates.TryGetValue(targetToken, out var candidate))
        {
            candidate = new TargetCandidateState
            {
                LastSeenAt = now,
                LastKnownPosition = targetPosition,
                Target = target,
                FirstSeenAt = now
            };
            state.Candidates[targetToken] = candidate;
            AiState.Trace($"Acquisition: soldier {soldierId} began observing target {targetToken} at {distance:0}m");
        }
        else
        {
            candidate.ObservedSeconds = TargetConfirmationCore.AccrueObservation(
                candidate.ObservedSeconds, candidate.LastSeenAt, now);
        }

        candidate.LastSeenAt = now;
        candidate.LastKnownPosition = targetPosition;
        if (!IsUsableTarget(candidate.Target))
            candidate.Target = target;
        var requiredObservationSeconds = RequiredObservationSeconds(distance, suppression);
        if (state.IncomingFireShooterToken == targetToken &&
            now < state.IncomingFireUntil)
        {
            requiredObservationSeconds *= 0.5f;
        }

        if (candidate.ObservedSeconds < requiredObservationSeconds)
            return false;

        state.HasConfirmedTarget = true;
        state.TargetToken = targetToken;
        state.LastObservedAt = now;
        if (state.IncomingFireShooterToken == targetToken)
            IncomingFireAwareness.Clear(state);
        state.Candidates.Clear();
        AiState.Trace($"Acquisition: soldier {soldierId} confirmed target {targetToken} after " +
                      $"{candidate.ObservedSeconds:0.0}s observed at {distance:0}m");
        return true;
    }

    internal static void ExpireUnobserved(Soldier soldier, float now)
    {
        var soldierId = soldier.GetInstanceID();
        if (!AiState.TargetMemory.TryGetValue(soldierId, out var state))
            return;

        var suppression = Suppression(soldier);
        var effectiveMemory = Settings.TargetMemorySeconds.Value *
                              Mathf.Lerp(1f, Settings.SuppressedMemoryMultiplier.Value, suppression);
        if (state.HasConfirmedTarget && now - state.LastObservedAt > effectiveMemory)
        {
            state.HasConfirmedTarget = false;
            state.TargetToken = IntPtr.Zero;
            AiState.Trace($"Acquisition: soldier {soldierId} forgot an unobserved target");
        }

        if (state.Candidates.Count > 0)
        {
            List<IntPtr>? stale = null;
            foreach (var pair in state.Candidates)
            {
                if (now - pair.Value.LastSeenAt > CandidateStaleSeconds)
                    (stale ??= new List<IntPtr>()).Add(pair.Key);
            }

            if (stale != null)
                foreach (var token in stale)
                    state.Candidates.Remove(token);
        }
    }

    internal static void ResetCandidatesAfterNegativeObservation(Soldier soldier)
    {
        var soldierId = soldier.GetInstanceID();
        if (!AiState.TargetMemory.TryGetValue(soldierId, out var state) ||
            state.Candidates.Count == 0)
        {
            return;
        }

        var now = Time.time;
        List<IntPtr>? stale = null;
        foreach (var pair in state.Candidates)
        {
            // A candidate seen this recently survives one stale negative native
            // scan; a fresher positive raycast (e.g. the close-range fast tick)
            // outweighs one staggered negative sample.
            if (now - pair.Value.LastSeenAt > TargetConfirmationCore.RecentPositiveObservationGraceSeconds)
                (stale ??= new List<IntPtr>()).Add(pair.Key);
        }

        if (stale == null)
            return;

        foreach (var token in stale)
            state.Candidates.Remove(token);
        AiState.Trace($"Acquisition: soldier {soldierId} lost sight of pending candidates");
    }

    internal static void RetainOnlyNativeCandidate(Soldier soldier, IntPtr targetToken)
    {
        var soldierId = soldier.GetInstanceID();
        if (!AiState.TargetMemory.TryGetValue(soldierId, out var state) ||
            state.Candidates.Count == 0)
        {
            return;
        }

        state.Candidates.TryGetValue(targetToken, out var retained);
        if (retained != null && state.Candidates.Count == 1)
            return;

        // The native scan reports one nearest candidate. Observation time from a
        // different target must not be banked while attention switches away.
        state.Candidates.Clear();
        if (retained != null)
            state.Candidates[targetToken] = retained;
    }

    internal static void RejectCandidate(Soldier soldier, Spottable target)
    {
        var soldierId = soldier.GetInstanceID();
        if (!TryGetTargetSnapshot(target, out var targetToken, out _))
            return;
        if (!AiState.TargetMemory.TryGetValue(soldierId, out var state) ||
            !state.Candidates.Remove(targetToken))
            return;

        AiState.Trace($"Acquisition: soldier {soldierId} rejected out-of-FOV candidate {targetToken}");
    }

    internal static bool TryRetainConfirmedTarget(
        Soldier soldier,
        Spottable? priorConfirmed,
        float now,
        out Spottable target,
        out float distance)
    {
        target = null!;
        distance = float.MaxValue;
        var soldierId = soldier.GetInstanceID();
        if (!AiState.TargetMemory.TryGetValue(soldierId, out var state) || !state.HasConfirmedTarget)
            return false;

        if (!MatchesTarget(priorConfirmed, state.TargetToken))
            return false;

        var effectiveMemory = Settings.TargetMemorySeconds.Value *
                              Mathf.Lerp(1f, Settings.SuppressedMemoryMultiplier.Value, Suppression(soldier));
        if (now - state.LastObservedAt > effectiveMemory)
        {
            state.HasConfirmedTarget = false;
            state.TargetToken = IntPtr.Zero;
            return false;
        }

        target = priorConfirmed!;
        if (!TryGetTargetSnapshot(priorConfirmed, out _, out var targetPosition))
        {
            state.HasConfirmedTarget = false;
            state.TargetToken = IntPtr.Zero;
            return false;
        }
        distance = Vector3.Distance(soldier.LookPosition(), targetPosition);
        return true;
    }

    internal static void PublishSoldierTarget(Soldier soldier, Spottable? target)
    {
        try
        {
            // Match the native visibility scan's direct target-slot assignment.
            // SetBestVisibleEnemy also drives an unrelated synchronized action.
            soldier.CurrentVisibleTarget = target;
        }
        catch (NullReferenceException)
        {
            // The owning soldier is being destroyed.
        }
        catch (Il2CppException)
        {
            // The owning soldier is being destroyed.
        }
        catch (ObjectCollectedException)
        {
            // The owning soldier is being destroyed.
        }
    }

    internal static void Forget(TargetMemoryState state, IntPtr targetToken)
    {
        if (state.HasConfirmedTarget && state.TargetToken == targetToken)
        {
            state.HasConfirmedTarget = false;
            state.TargetToken = IntPtr.Zero;
        }
        state.Candidates.Remove(targetToken);
    }

    private static void TryClearAiTarget(SoldierAI ai)
    {
        try
        {
            ai.visibleTarget = null;
        }
        catch
        {
            // The owning AI is being destroyed; no target reference can be cleared safely.
        }
    }

    private static void TryClearSoldierTarget(Soldier soldier)
    {
        try
        {
            soldier.SetBestVisibleEnemy(null);
        }
        catch
        {
            // The owning soldier is being destroyed; no target reference can be cleared safely.
        }
    }

    private static float RequiredObservationSeconds(float distance, float suppression)
    {
        var distanceFactor = Mathf.Clamp01(distance / Settings.DistantTargetAcquisitionRange.Value);
        var seconds = Mathf.Lerp(
            Settings.CloseTargetAcquisitionSeconds.Value,
            Settings.DistantTargetAcquisitionSeconds.Value,
            distanceFactor);

        if (Settings.CloseQuartersEnabled.Value && distance < Settings.ContactImmediateFireDistance.Value)
        {
            // Point-blank threats should be identified faster than the general
            // "close" acquisition time; lerp down toward the point-blank value
            // as distance shrinks below the immediate-fire distance.
            var pointBlankFactor = Mathf.Clamp01(distance / Settings.ContactImmediateFireDistance.Value);
            seconds = Mathf.Lerp(Settings.PointBlankAcquisitionSeconds.Value, seconds, pointBlankFactor);
        }

        // Suppression slows interpretation of new visual information while the
        // configurable awareness floors still allow eventual acquisition.
        return seconds * Mathf.Lerp(1f, 1.75f, suppression);
    }

    internal static void RecordConfirmedNativeObservation(
        Soldier soldier,
        IntPtr targetToken,
        Vector3 targetPosition,
        float now)
    {
        var state = AiState.GetTargetMemory(soldier.GetInstanceID());
        if (!state.HasConfirmedTarget || state.TargetToken != targetToken ||
            targetToken == IntPtr.Zero)
        {
            return;
        }

        // Callers may reach TryConfirm through retained target memory. This stamp is
        // deliberately separate and is written only by a positive native visibility
        // scan, keeping the last-known point frozen after line of sight is lost.
        state.HasConfirmedLastKnownPosition = true;
        state.ConfirmedLastKnownTargetToken = targetToken;
        state.ConfirmedLastKnownPosition = targetPosition;
        state.ConfirmedLastKnownObservedAt = now;
    }
}

/// <summary>
/// Decouples close-range confirmation from the staggered native visibility scan.
/// A pending candidate inside <see cref="Settings.ContactImmediateFireDistance"/>
/// is re-checked with a direct native <c>CanSee</c> raycast on its own short
/// cadence, so the ~0.6s close acquisition delay is no longer stretched by the
/// gaps between <c>SoldierAI.SequentialUpdate</c> samples at high AI counts.
/// First sighting itself stays native-scan-bound; this only speeds up the
/// interval between first sighting and confirmation.
/// </summary>
internal static class CloseRangeAcquisitionTick
{
    // Perf budget: at most one poll per soldier per this interval, and only for
    // soldiers that already have a pending close-range candidate.
    private const float PollIntervalSeconds = 0.2f;

    // This tick raycasts (CanSee) once per close candidate. A fixed poll interval lets
    // every soldier's next poll drift into the same frame, and the whole battle's
    // line-of-sight tests then land together — measured at 67.7ms in a single frame.
    // Two bounds fix that without dropping any check: a per-soldier offset so polls
    // cannot stay synchronized, and a per-frame ceiling that spreads a synchronized wave
    // across consecutive frames.
    private const int MaxCloseConfirmScansPerFrame = 8;
    private static int _closeConfirmScanFrame = -1;
    private static int _closeConfirmScansThisFrame;

    // Deterministic per-soldier spread (no RNG, so multiplayer peers stay in step),
    // adding up to one extra poll interval so neighbours cannot share a due frame.
    private static float NextCloseConfirmPollDelay(int soldierId)
        => PollIntervalSeconds * (1f + (soldierId & 7) * 0.125f);

    private static bool TryTakeCloseConfirmBudget()
    {
        var frame = Time.frameCount;
        if (frame != _closeConfirmScanFrame)
        {
            _closeConfirmScanFrame = frame;
            _closeConfirmScansThisFrame = 0;
        }

        if (_closeConfirmScansThisFrame >= MaxCloseConfirmScansPerFrame)
            return false;

        _closeConfirmScansThisFrame++;
        return true;
    }

    internal static void Update(SoldierAI ai, Soldier soldier, float now)
    {
        if (!Settings.PerceptionEnabled.Value || soldier.IsOnVehicle())
            return;

        var soldierId = soldier.GetInstanceID();
        if (!AiState.TargetMemory.TryGetValue(soldierId, out var state) ||
            state.HasConfirmedTarget ||
            state.Candidates.Count == 0)
        {
            return;
        }

        if (now < state.NextCloseConfirmPollAt)
            return;

        var closeDistance = Settings.ContactImmediateFireDistance.Value;
        var closeRangeSqr = closeDistance * closeDistance;
        Vector3 origin;
        try
        {
            origin = soldier.LookPosition();
        }
        catch (NullReferenceException) { return; }
        catch (Il2CppException) { return; }
        catch (ObjectCollectedException) { return; }

        List<IntPtr>? closeCandidates = null;
        foreach (var pair in state.Candidates)
        {
            if ((pair.Value.LastKnownPosition - origin).sqrMagnitude <= closeRangeSqr)
                (closeCandidates ??= new List<IntPtr>()).Add(pair.Key);
        }

        if (closeCandidates == null)
        {
            // Nothing to raycast: reschedule normally without consuming budget.
            state.NextCloseConfirmPollAt = now + NextCloseConfirmPollDelay(soldierId);
            return;
        }

        // Over budget: leave the poll DUE so this soldier retries next frame rather than
        // losing its turn. Nothing is skipped, it is only deferred.
        if (!TryTakeCloseConfirmBudget())
            return;

        state.NextCloseConfirmPollAt = now + NextCloseConfirmPollDelay(soldierId);

        foreach (var token in closeCandidates)
        {
            if (!state.Candidates.TryGetValue(token, out var candidate))
                continue;

            if (!TargetAcquisition.TryGetTargetSnapshot(
                    candidate.Target, out var targetToken, out var targetPosition) ||
                targetToken != token)
            {
                continue;
            }

            var target = candidate.Target!;
            bool canSee;
            try
            {
                canSee = soldier.CanSee(target);
            }
            catch (NullReferenceException) { continue; }
            catch (Il2CppException) { continue; }
            catch (ObjectCollectedException) { continue; }

            if (!canSee)
                continue;

            var distance = Vector3.Distance(origin, targetPosition);
            var suppression = TargetAcquisition.Suppression(soldier);
            if (!TargetAcquisition.TryConfirm(soldier, target, distance, suppression, now))
                continue;

            TargetAcquisition.RecordConfirmedNativeObservation(soldier, targetToken, targetPosition, now);
            TargetAcquisition.PublishSoldierTarget(soldier, target);
            if (!TargetAcquisition.MatchesTarget(ai.visibleTarget, targetToken))
                ai.visibleTarget = target;
            AiState.Trace(
                $"Acquisition: soldier {soldierId} fast-confirmed close target {targetToken} " +
                $"at {distance:0}m via close-range tick");
            return;
        }
    }
}
