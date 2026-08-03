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

            var shooterTarget = shooter.TryCast<Spottable>();
            IncomingFireAwareness.Record(
                __instance,
                shooter.GetCenterOfUnit(),
                shooterTarget?.Pointer ?? shooter.Pointer,
                shooter.GetInstanceID(),
                shooterTarget,
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

        var shooterTarget = shooter.TryCast<Spottable>();
        var shooterToken = shooterTarget?.Pointer ?? shooter.Pointer;
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
            shooterTarget,
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
                nearest.Shooter,
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
            if (memory.IncomingFireShooterId == shooterId ||
                memory.IncomingFireShooterToken == shooterToken)
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
        RecentGunfireVisibility.ResetBattle();
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
        GunfireAwareness.ResetBattle();
        MountedGunnerSuppression.ResetBattle();
        KnownTargetSuppressiveFire.ResetBattle();
        RememberedGrenadeThrows.ResetBattle();
        ContactResponse.ResetBattleAttackEvidence();
        CasualtySuppression.ResetBattle();
    }
}

internal static class IncomingFireAwareness
{
    private const float CandidateAttentionFreshSeconds = 1.5f;
    private const float ExactShooterVisibilitySeconds = 3f;

    [ThreadStatic]
    private static int _nonDirectionalSuppressionDepth;

    internal static bool IsNonDirectionalSuppression => _nonDirectionalSuppressionDepth > 0;

    internal static void Record(
        Soldier soldier,
        Vector3 sourcePosition,
        IntPtr shooterToken,
        int shooterId,
        Spottable? shooterTarget,
        float now)
        => RecordCue(
            soldier,
            sourcePosition,
            shooterToken,
            shooterId,
            shooterTarget,
            now,
            now + DirectThreatMemoryCore.RetentionSeconds(
                AiBehaviorTuning.TargetMemorySeconds),
            isDirect: true);

    internal static void RecordHeardGunfire(
        Soldier soldier,
        Vector3 sourcePosition,
        IntPtr shooterToken,
        int shooterId,
        Spottable? shooterTarget,
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
            shooterTarget,
            now,
            expiresAt,
            isDirect: false);
    }

    private static void RecordCue(
        Soldier soldier,
        Vector3 sourcePosition,
        IntPtr shooterToken,
        int shooterId,
        Spottable? shooterTarget,
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
            state.IncomingFireShooterId != shooterId)
        {
            var origin = soldier.LookPosition();
            var existingDistanceSqr = (state.IncomingFirePosition - origin).sqrMagnitude;
            var newDistanceSqr = (sourcePosition - origin).sqrMagnitude;
            if (existingDistanceSqr <= newDistanceSqr)
                return;
        }

        state.IncomingFirePosition = sourcePosition;
        state.IncomingFireUntil = expiresAt;
        state.IncomingFireVisibilityUntil = Mathf.Min(
            expiresAt,
            now + ExactShooterVisibilitySeconds);
        state.IncomingFireShooterToken = shooterToken;
        state.IncomingFireShooterId = shooterId;
        state.IncomingFireShooter = shooterTarget;
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

        // Native CanSee includes trigger colliders in its ray. Bush prefabs use
        // oversized trigger spheres for concealment, so a crouched or prone shooter
        // can be physically hidden from the native ray even after firing. Re-check
        // only this exact recent shooter with triggers ignored; solid cover remains
        // fully blocking and the normal FOV/reaction delay still applies.
        RecentGunfireVisibility.Update(ai, soldier, state, origin, now);

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
        state.IncomingFireVisibilityUntil = 0f;
        state.IncomingFireShooterToken = IntPtr.Zero;
        state.IncomingFireShooterId = 0;
        state.IncomingFireShooter = null;
        state.IncomingFireIsDirect = false;
        state.NextIncomingFireVisibilityAt = 0f;
    }
}

internal static class RecentGunfireVisibility
{
    private const float VisibilityPollSeconds = 0.12f;
    private const float RayOriginAdvanceMeters = 0.4f;
    private const float RayEndAllowanceMeters = 0.5f;

    private static bool _loggedActive;
    private static bool _loggedTriggerBypass;
    private static bool _loggedSolidBlocker;

    internal static void ResetBattle()
    {
        _loggedActive = false;
        _loggedTriggerBypass = false;
        _loggedSolidBlocker = false;
    }

    internal static void Update(
        SoldierAI ai,
        Soldier listener,
        TargetMemoryState state,
        Vector3 origin,
        float now)
    {
        if (now < state.NextIncomingFireVisibilityAt ||
            !TryResolveExactShooter(state, now, out var target, out var shooter))
        {
            return;
        }

        if (!TargetAcquisition.TryGetTargetSnapshot(
                target, out var targetToken, out var targetPosition))
        {
            return;
        }

        var distance = Vector3.Distance(origin, targetPosition);
        var suppression = TargetAcquisition.Suppression(listener);
        var insideFov = TargetAcquisition.IsInsideEffectiveFov(
            listener, targetPosition, distance, suppression);
        var hostile = ResourcesManager.IsEnemyFaction(
            AiState.FactionOf(listener), AiState.FactionOf(shooter));
        if (!RecentGunfireVisibilityCore.ShouldCheckExactShooter(
                cueActive: true,
                cueShooterId: state.IncomingFireShooterId,
                candidateShooterId: shooter.GetInstanceID(),
                candidateAlive: shooter.IsAlive,
                candidateHostile: hostile,
                insideFieldOfView: insideFov))
        {
            return;
        }

        state.NextIncomingFireVisibilityAt = now + VisibilityPollSeconds +
                                               (listener.GetInstanceID() & 3) * 0.01f;
        if (!_loggedActive)
        {
            _loggedActive = true;
            Plugin.LogSource.LogInfo(
                "Recent-gunfire exact-shooter visibility check is active; " +
                "Unity trigger volumes are ignored while solid cover remains blocking.");
        }

        var nativeVisible = false;
        try
        {
            nativeVisible = listener.CanSee(target);
        }
        catch (NullReferenceException) { }
        catch (Il2CppException) { }
        catch (ObjectCollectedException) { }

        var triggerIgnoringRayHitTarget = false;
        RaycastHit firstSolidHit = default;
        if (!nativeVisible)
        {
            triggerIgnoringRayHitTarget = TriggerIgnoringRayHitsExactTarget(
                listener,
                targetToken,
                origin,
                targetPosition,
                out firstSolidHit);
        }

        if (!RecentGunfireVisibilityCore.HasVisualContact(
                nativeVisible, triggerIgnoringRayHitTarget))
        {
            LogFirstSolidBlocker(firstSolidHit);
            return;
        }

        if (triggerIgnoringRayHitTarget && !_loggedTriggerBypass)
        {
            _loggedTriggerBypass = true;
            Plugin.LogSource.LogInfo(
                "Recent-gunfire visibility reached the exact shooter after ignoring " +
                "trigger-only concealment; normal target reaction timing still applies.");
        }

        TargetAcquisition.RecordIndependentVisualProof(state, targetToken, now);
        TargetAcquisition.RetainOnlyNativeCandidate(listener, targetToken);
        if (!TargetAcquisition.TryConfirm(
                listener,
                target,
                distance,
                suppression,
                now,
                allowIndependentVisualContinuity: true))
        {
            return;
        }

        TargetAcquisition.RecordConfirmedNativeObservation(
            listener, targetToken, targetPosition, now);
        TargetAcquisition.PublishSoldierTarget(listener, target);
        if (!TargetAcquisition.MatchesTarget(ai.visibleTarget, targetToken))
            ai.visibleTarget = target;
    }

    private static bool TryResolveExactShooter(
        TargetMemoryState state,
        float now,
        out Spottable target,
        out Soldier shooter)
    {
        target = null!;
        shooter = null!;

        var cueActive = now < state.IncomingFireVisibilityUntil;
        var candidate = state.IncomingFireShooter;
        if (AiState.GunfireCues.TryGetValue(
                state.IncomingFireShooterId, out var gunfireCue) &&
            gunfireCue.ExpiresAt > now &&
            gunfireCue.ShooterToken == state.IncomingFireShooterToken)
        {
            cueActive = true;
            if (!TargetAcquisition.IsUsableTarget(candidate))
                candidate = gunfireCue.Shooter;
        }

        if (!cueActive || !TargetAcquisition.IsUsableTarget(candidate))
            return false;

        try
        {
            var candidateSoldier = candidate!.TryCast<Soldier>();
            if (candidateSoldier == null ||
                candidateSoldier.GetInstanceID() != state.IncomingFireShooterId)
            {
                return false;
            }

            target = candidate;
            shooter = candidateSoldier;
            return true;
        }
        catch (NullReferenceException) { return false; }
        catch (Il2CppException) { return false; }
        catch (ObjectCollectedException) { return false; }
    }

    private static bool TriggerIgnoringRayHitsExactTarget(
        Soldier listener,
        IntPtr targetToken,
        Vector3 origin,
        Vector3 targetPosition,
        out RaycastHit firstSolidHit)
    {
        firstSolidHit = default;
        var direction = targetPosition - origin;
        var distance = direction.magnitude;
        if (distance <= 0.01f)
            return false;

        direction /= distance;
        var advance = Mathf.Min(RayOriginAdvanceMeters, distance * 0.5f);
        origin += direction * advance;
        distance -= advance;

        if (!Physics.Raycast(
                origin,
                direction,
                out firstSolidHit,
                distance + RayEndAllowanceMeters,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) ||
            firstSolidHit.collider == null)
        {
            return false;
        }

        try
        {
            var hitTarget = Creature.GetConnectedSpottable(
                firstSolidHit.collider.transform);
            return TargetAcquisition.MatchesTarget(hitTarget, targetToken);
        }
        catch (NullReferenceException) { return false; }
        catch (Il2CppException) { return false; }
        catch (ObjectCollectedException) { return false; }
    }

    private static void LogFirstSolidBlocker(RaycastHit hit)
    {
        if (_loggedSolidBlocker || hit.collider == null)
            return;

        _loggedSolidBlocker = true;
        try
        {
            var gameObject = hit.collider.gameObject;
            Plugin.LogSource.LogInfo(
                $"Recent-gunfire visibility sample remained blocked by solid " +
                $"'{gameObject.name}' on layer {gameObject.layer} " +
                $"({hit.collider.GetType().Name}).");
        }
        catch
        {
            Plugin.LogSource.LogInfo(
                "Recent-gunfire visibility sample remained blocked by a solid collider.");
        }
    }
}

/// <summary>
/// Shares a confirmed observer's frozen last-known target position with autonomous
/// friendly infantry in local voice range. A report can turn a recipient toward the
/// contact, but it never writes a visible/confirmed target or bypasses normal LOS and
/// acquisition time.
/// </summary>
internal static class NearbyTargetKnowledge
{
    private const float CalloutDelaySeconds = 0.2f;
    private const float ReportLifetimeSeconds = 4f;
    private const float ReportRefreshSeconds = 0.75f;
    private const float DirectObservationPrioritySeconds = 1.5f;
    private const int MaxReportTurnsPerFrame = 12;

    private static readonly Il2CppSystem.Collections.Generic.List<Creature> NearbyFriendlies = new();
    private static int _reportTurnFrame = -1;
    private static int _reportTurnsThisFrame;

    internal static void PublishConfirmedObservation(
        Soldier reporter,
        IntPtr targetToken,
        Vector3 targetPosition,
        float now)
    {
        if (!Settings.PerceptionEnabled.Value ||
            targetToken == IntPtr.Zero ||
            reporter == null ||
            !reporter.IsAlive ||
            !AiOwnership.IsAutonomous(reporter) ||
            reporter.IsOnVehicle())
        {
            return;
        }

        var reporterId = reporter.GetInstanceID();
        var reporterMemory = AiState.GetTargetMemory(reporterId);
        if (now < reporterMemory.NextNearbyTargetShareAt)
            return;

        var faction = AiState.FactionOf(reporter);
        if (string.IsNullOrWhiteSpace(faction) ||
            string.Equals(faction, Soldier.UnknownFaction, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var reporterPosition = reporter.GetCenterOfUnit();
            var sharingRadius = Settings.NearbyTargetSharingRadius.Value;
            NearbyFriendlies.Clear();
            var octree = Creature.creaturesOctatree;
            if (octree == null ||
                !octree.GetNearbyNonAlloc(reporterPosition, sharingRadius, NearbyFriendlies))
            {
                return;
            }

            reporterMemory.NextNearbyTargetShareAt = now + ReportRefreshSeconds;
            for (var index = 0; index < NearbyFriendlies.Count; index++)
            {
                var recipient = NearbyFriendlies[index]?.TryCast<Soldier>();
                if (recipient == null ||
                    recipient.GetInstanceID() == reporterId ||
                    !recipient.IsAlive ||
                    recipient.IsOnVehicle() ||
                    !AiOwnership.IsAutonomous(recipient) ||
                    !ResourcesManager.IsSameFaction(faction, AiState.FactionOf(recipient)))
                {
                    continue;
                }

                var recipientPosition = recipient.GetCenterOfUnit();
                if (!LocalTargetReportCore.IsInsideSharingRadius(
                        (recipientPosition - reporterPosition).sqrMagnitude,
                        sharingRadius))
                {
                    continue;
                }

                var memory = AiState.GetTargetMemory(recipient.GetInstanceID());
                if (memory.HasConfirmedTarget)
                    continue;

                var currentReportActive =
                    memory.ReportedTargetToken != IntPtr.Zero &&
                    now < memory.ReportedTargetUntil;
                var isSameTarget = memory.ReportedTargetToken == targetToken;
                if (!LocalTargetReportCore.ShouldAcceptReport(
                        currentReportActive,
                        isSameTarget,
                        (memory.ReportedTargetPosition - recipientPosition).sqrMagnitude,
                        (targetPosition - recipientPosition).sqrMagnitude))
                {
                    continue;
                }

                memory.ReportedTargetToken = targetToken;
                memory.ReportedTargetPosition = targetPosition;
                memory.ReportedTargetAvailableAt = now + CalloutDelaySeconds;
                memory.ReportedTargetUntil = now + ReportLifetimeSeconds;
            }
        }
        catch (NullReferenceException) { }
        catch (Il2CppException) { }
        catch (ObjectCollectedException) { }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Nearby target callout failed: {ex.Message}");
        }
        finally
        {
            NearbyFriendlies.Clear();
        }
    }

    internal static void Update(Soldier soldier, float now)
    {
        var soldierId = soldier.GetInstanceID();
        if (!AiState.TargetMemory.TryGetValue(soldierId, out var state) ||
            state.ReportedTargetToken == IntPtr.Zero)
        {
            return;
        }

        if (!Settings.PerceptionEnabled.Value ||
            now >= state.ReportedTargetUntil ||
            !soldier.IsAlive ||
            !AiOwnership.IsAutonomous(soldier) ||
            soldier.IsOnVehicle())
        {
            Clear(state);
            return;
        }

        if (now < state.ReportedTargetAvailableAt)
            return;

        if (state.HasConfirmedTarget)
        {
            if (state.TargetToken == state.ReportedTargetToken)
                Clear(state);
            return;
        }

        // Direct observation and incoming rounds both outrank a second-hand voice
        // report. The callout remains alive and can be considered if those cues end.
        var hasFreshObservedCandidate = false;
        foreach (var candidate in state.Candidates.Values)
        {
            if (now - candidate.LastSeenAt <= DirectObservationPrioritySeconds)
            {
                hasFreshObservedCandidate = true;
                break;
            }
        }

        if (hasFreshObservedCandidate ||
            now < state.IncomingFireUntil ||
            (Settings.DangerReactionsEnabled.Value &&
             (soldier.IsOnFire || AiState.IsFlameEvading(soldierId, now))) ||
            ContactResponse.IsWeaponFiring(soldier) ||
            soldier.IsMoving(0.15f))
        {
            return;
        }

        var towardReport = state.ReportedTargetPosition - soldier.LookPosition();
        towardReport.y = 0f;
        if (towardReport.sqrMagnitude <= 0.01f ||
            Vector3.Angle(soldier.transform.forward, towardReport) <= 2f ||
            !TryTakeReportTurnBudget())
        {
            return;
        }

        var turnDelta = state.LastReportedTargetTurnAt > 0f
            ? Mathf.Clamp(now - state.LastReportedTargetTurnAt, Time.fixedDeltaTime, 0.1f)
            : Time.fixedDeltaTime;
        state.LastReportedTargetTurnAt = now;
        soldier.RotateToward(towardReport.normalized, turnDelta);
    }

    private static bool TryTakeReportTurnBudget()
    {
        var frame = Time.frameCount;
        if (frame != _reportTurnFrame)
        {
            _reportTurnFrame = frame;
            _reportTurnsThisFrame = 0;
        }

        if (_reportTurnsThisFrame >= MaxReportTurnsPerFrame)
            return false;

        _reportTurnsThisFrame++;
        return true;
    }

    private static void Clear(TargetMemoryState state)
    {
        state.ReportedTargetToken = IntPtr.Zero;
        state.ReportedTargetPosition = default;
        state.ReportedTargetAvailableAt = 0f;
        state.ReportedTargetUntil = 0f;
        state.LastReportedTargetTurnAt = 0f;
    }
}

[HarmonyPatch(typeof(SoldierAI), "FixedUpdate")]
internal static class IncomingFireOrientationPatch
{
    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance)
    {
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
                        NearbyTargetKnowledge.Update(soldier, Time.time);
                        IncomingFireAwareness.Update(__instance, soldier, Time.time);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"Perception orientation failed: {ex.Message}");
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
    private static bool Prefix(
        Creature __instance,
        Spottable seeTarget,
        ref bool __result)
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
            var hasNativeTarget = TargetAcquisition.TryGetTargetSnapshot(
                __result, out var targetToken, out var targetPosition);
            var nativeDistance = hasNativeTarget
                ? Vector3.Distance(__instance.LookPosition(), targetPosition)
                : float.MaxValue;

            // Native priority scoring may nominate a different enemy on every scan.
            // At close range, keep finishing the already-confirmed infantry target
            // while it remains alive and directly visible. Medium/far contacts and
            // invalid, hidden, dead, or departing close targets still use the native
            // result and the normal confirmation path below.
            if ((!hasNativeTarget ||
                 !TargetAcquisition.MatchesTarget(__state, targetToken)) &&
                TargetAcquisition.TryRetainCloseCombatTarget(
                    __instance,
                    __state,
                    nativeDistance,
                    now,
                    out var committedTarget,
                    out var committedDistance))
            {
                TargetAcquisition.PublishSoldierTarget(__instance, committedTarget);
                __result = committedTarget;
                dist = committedDistance;
                return;
            }

            if (!hasNativeTarget)
            {
                // This is the result of a real native visibility scan. Unlike the
                // later SequentialUpdate null (which can be caused by this patch
                // withholding an unconfirmed candidate), it is genuine negative
                // evidence and must break any pending observation streak.
                TargetAcquisition.ResetCandidatesAfterNegativeObservation(__instance);
                TargetAcquisition.ExpireUnobserved(__instance, now);
                // A recent exact-shooter ray is stronger evidence than the native
                // vegetation query. Bridge only that short independent proof; ordinary
                // target memory is never allowed to turn a negative scan into vision.
                if (TargetAcquisition.TryRetainRecentIndependentVisualTarget(
                        __instance,
                        __state,
                        now,
                        out var independentlyVisible,
                        out var independentlyVisibleDistance))
                {
                    TargetAcquisition.PublishSoldierTarget(__instance, independentlyVisible);
                    __result = independentlyVisible;
                    dist = independentlyVisibleDistance;
                    return;
                }

                TargetAcquisition.RequireReacquisitionAfterNegativeObservation(__instance);
                TargetAcquisition.PublishSoldierTarget(__instance, null);
                __result = null!;
                dist = float.MaxValue;
                return;
            }

            // The native scan has already selected the nearest LOS-valid target
            // inside our cone; this layer only applies target-specific acquisition.
            var target = __result;
            TargetAcquisition.RetainOnlyNativeCandidate(__instance, targetToken);
            var distance = nativeDistance;
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
                {
                    RememberedGrenadeThrows.Disable(__instance, inactiveSoldier);
                    KnownTargetSuppressiveFire.Disable(__instance, inactiveSoldier);
                }
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
                RememberedGrenadeThrows.Disable(__instance, soldier);
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
        var effectiveFov = AiBehaviorTuning.HorizontalFov *
                           Mathf.Lerp(1f, Settings.SuppressedFovMultiplier.Value, suppression);
        var effectivePeripheralDistance = TargetAcquisition.EffectivePeripheralDistance(suppression);
        var effectiveMemory = AiBehaviorTuning.TargetMemorySeconds *
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
            if (memory.RequiresTargetReacquisition)
                return null;
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
            ? AiBehaviorTuningCore.SuppressionPenaltyStrength(
                soldier.GetSuppressionValue(),
                AiBehaviorTuning.CrouchSuppressionThreshold,
                AiBehaviorTuning.ProneSuppressionThreshold)
            : 0f;

    internal static float EffectivePeripheralDistance(float suppression)
    {
        var distance = AiBehaviorTuning.PeripheralAwarenessDistance *
                       Mathf.Lerp(1f, Settings.SuppressedPeripheralMultiplier.Value, suppression);

        if (!Settings.CloseQuartersEnabled.Value)
            return distance;

        // Suppression can never blind a soldier to a threat within the
        // configurable minimum awareness floor, and the floor never raises
        // awareness above the unsuppressed distance.
        return Mathf.Min(
            AiBehaviorTuning.PeripheralAwarenessDistance,
            Mathf.Max(AiBehaviorTuning.MinimumPeripheralAwarenessDistance, distance));
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
        var effectiveFov = AiBehaviorTuning.HorizontalFov *
                           Mathf.Lerp(1f, Settings.SuppressedFovMultiplier.Value, suppression);
        return Vector3.Angle(soldier.transform.forward, direction) <= effectiveFov * 0.5f;
    }

    internal static bool TryConfirm(
        Soldier soldier,
        Spottable target,
        float distance,
        float suppression,
        float now,
        bool allowIndependentVisualContinuity = false)
    {
        var soldierId = soldier.GetInstanceID();
        if (!TryGetTargetSnapshot(target, out var targetToken, out var targetPosition))
            return false;
        var state = AiState.GetTargetMemory(soldierId);
        var effectiveMemory = AiBehaviorTuning.TargetMemorySeconds *
                              Mathf.Lerp(1f, Settings.SuppressedMemoryMultiplier.Value, suppression);

        // A positive observation of the already-confirmed target is current proof,
        // even when native visibility scans are farther apart than the memory timer.
        if (TargetConfirmationCore.CanReuseConfirmedTarget(
                state.HasConfirmedTarget,
                state.TargetToken == targetToken,
                state.RequiresTargetReacquisition,
                allowIndependentVisualContinuity))
        {
            state.RequiresTargetReacquisition = false;
            state.LastObservedAt = now;
            // Refreshing the active target must not erase a different visible
            // candidate while it earns its own reaction delay.
            state.Candidates.Remove(targetToken);
            return true;
        }

        if (state.HasConfirmedTarget && now - state.LastObservedAt > effectiveMemory)
        {
            var expiredToken = state.TargetToken;
            ClearConfirmedTarget(state);
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
        state.RequiresTargetReacquisition = false;
        // Keep an exact-shooter cue until its own short visibility lifetime ends.
        // That lets the fixed-update visibility path maintain a confirmed shooter
        // through trigger-only vegetation while automatic fire continues. The cue
        // still expires normally and never extends precise contact by itself.
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
        var effectiveMemory = AiBehaviorTuning.TargetMemorySeconds *
                              Mathf.Lerp(1f, Settings.SuppressedMemoryMultiplier.Value, suppression);
        if (state.HasConfirmedTarget && now - state.LastObservedAt > effectiveMemory)
        {
            ClearConfirmedTarget(state);
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
        if (!AiState.TargetMemory.TryGetValue(soldierId, out var state) ||
            !state.HasConfirmedTarget ||
            state.RequiresTargetReacquisition)
            return false;

        if (!MatchesTarget(priorConfirmed, state.TargetToken))
            return false;

        var effectiveMemory = AiBehaviorTuning.TargetMemorySeconds *
                              Mathf.Lerp(1f, Settings.SuppressedMemoryMultiplier.Value, Suppression(soldier));
        if (now - state.LastObservedAt > effectiveMemory)
        {
            ClearConfirmedTarget(state);
            return false;
        }

        target = priorConfirmed!;
        if (!TryGetTargetSnapshot(priorConfirmed, out _, out var targetPosition))
        {
            ClearConfirmedTarget(state);
            return false;
        }
        distance = Vector3.Distance(soldier.LookPosition(), targetPosition);
        return true;
    }

    internal static bool TryRetainCloseCombatTarget(
        Soldier soldier,
        Spottable? priorConfirmed,
        float challengerDistance,
        float now,
        out Spottable target,
        out float distance)
    {
        target = null!;
        distance = float.MaxValue;

        var soldierId = soldier.GetInstanceID();
        if (!AiState.TargetMemory.TryGetValue(soldierId, out var state) ||
            !state.HasConfirmedTarget ||
            !MatchesTarget(priorConfirmed, state.TargetToken))
        {
            return false;
        }

        try
        {
            if (!TryGetTargetSnapshot(
                    priorConfirmed, out var targetToken, out var targetPosition))
            {
                return false;
            }

            var targetSoldier = priorConfirmed!.TryCast<Soldier>();
            distance = Vector3.Distance(soldier.LookPosition(), targetPosition);
            var visible = targetSoldier != null &&
                          targetSoldier.IsAlive &&
                          soldier.CanSee(priorConfirmed);
            if (!CloseTargetCommitmentCore.ShouldRetain(
                    Settings.CloseQuartersEnabled.Value,
                    state.HasConfirmedTarget,
                    targetToken == state.TargetToken,
                    targetSoldier != null && targetSoldier.IsAlive,
                    visible,
                    distance,
                    AiBehaviorTuning.ImmediateFireDistance,
                    challengerDistance))
            {
                return false;
            }

            state.LastObservedAt = now;
            state.RequiresTargetReacquisition = false;
            state.Candidates.Clear();
            RecordConfirmedNativeObservation(
                soldier, targetToken, targetPosition, now);
            target = priorConfirmed;
            return true;
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
            ClearConfirmedTarget(state);
        }
        state.Candidates.Remove(targetToken);
    }

    internal static void RecordIndependentVisualProof(
        TargetMemoryState state,
        IntPtr targetToken,
        float now)
    {
        state.IndependentVisualProofTargetToken = targetToken;
        state.IndependentVisualProofAt = now;
    }

    internal static bool TryRetainRecentIndependentVisualTarget(
        Soldier soldier,
        Spottable? priorConfirmed,
        float now,
        out Spottable target,
        out float distance)
    {
        target = null!;
        distance = float.MaxValue;
        if (!AiState.TargetMemory.TryGetValue(soldier.GetInstanceID(), out var state) ||
            !state.HasConfirmedTarget ||
            state.RequiresTargetReacquisition ||
            !MatchesTarget(priorConfirmed, state.TargetToken) ||
            !TargetConfirmationCore.HasRecentIndependentVisualProof(
                state.IndependentVisualProofTargetToken == state.TargetToken,
                state.IndependentVisualProofAt,
                now) ||
            !TryGetTargetSnapshot(priorConfirmed, out _, out var targetPosition))
        {
            return false;
        }

        target = priorConfirmed!;
        distance = Vector3.Distance(soldier.LookPosition(), targetPosition);
        return true;
    }

    internal static void RequireReacquisitionAfterNegativeObservation(Soldier soldier)
    {
        if (!AiState.TargetMemory.TryGetValue(soldier.GetInstanceID(), out var state) ||
            !state.HasConfirmedTarget ||
            state.RequiresTargetReacquisition)
        {
            return;
        }

        state.RequiresTargetReacquisition = true;
        AiState.Trace(
            $"Acquisition: soldier {soldier.GetInstanceID()} lost visual lock on " +
            $"confirmed target {state.TargetToken}; reacquisition required");
    }

    private static void ClearConfirmedTarget(TargetMemoryState state)
    {
        state.HasConfirmedTarget = false;
        state.TargetToken = IntPtr.Zero;
        state.RequiresTargetReacquisition = false;
        state.IndependentVisualProofTargetToken = IntPtr.Zero;
        state.IndependentVisualProofAt = 0f;
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
            AiBehaviorTuning.ObservationSeconds(Settings.CloseTargetAcquisitionSeconds.Value),
            AiBehaviorTuning.ObservationSeconds(Settings.DistantTargetAcquisitionSeconds.Value),
            distanceFactor);

        if (Settings.CloseQuartersEnabled.Value && distance < AiBehaviorTuning.ImmediateFireDistance)
        {
            // Point-blank threats should be identified faster than the general
            // "close" acquisition time; lerp down toward the point-blank value
            // as distance shrinks below the immediate-fire distance.
            var pointBlankFactor = Mathf.Clamp01(distance / AiBehaviorTuning.ImmediateFireDistance);
            seconds = Mathf.Lerp(
                AiBehaviorTuning.ObservationSeconds(Settings.PointBlankAcquisitionSeconds.Value),
                seconds,
                pointBlankFactor);
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
        NearbyTargetKnowledge.PublishConfirmedObservation(
            soldier, targetToken, targetPosition, now);
    }
}

/// <summary>
/// Decouples close-range discovery and confirmation from the staggered native
/// visibility scan. A targetless soldier runs a bounded nearby-creature query,
/// then uses the native <c>CanSee</c> test and the normal acquisition timer.
/// Pending candidates inside <see cref="Settings.ContactImmediateFireDistance"/>
/// continue to be checked on their own short cadence, so neither first sighting
/// nor confirmation is stretched by the shared <c>SequentialUpdate</c> queue.
/// </summary>
internal static class CloseRangeAcquisitionTick
{
    private const float PollIntervalSeconds = 0.2f;
    private const float DiscoveryIntervalSeconds = 0.35f;

    // This tick raycasts (CanSee) once per close candidate. A fixed poll interval lets
    // every soldier's next poll drift into the same frame, and the whole battle's
    // line-of-sight tests then land together — measured at 67.7ms in a single frame.
    // Two bounds fix that without dropping any check: a per-soldier offset so polls
    // cannot stay synchronized, and a per-frame ceiling that spreads a synchronized wave
    // across consecutive frames.
    private const int MaxCloseScansPerFrame = 8;
    private static int _closeScanFrame = -1;
    private static int _closeScansThisFrame;
    private static readonly Il2CppSystem.Collections.Generic.List<Creature> NearbyCreatures = new();

    // Deterministic per-soldier spread (no RNG, so multiplayer peers stay in step),
    // adding up to one extra poll interval so neighbours cannot share a due frame.
    private static float NextCloseConfirmPollDelay(int soldierId)
        => PollIntervalSeconds * (1f + (soldierId & 7) * 0.125f);

    private static float NextCloseDiscoveryPollDelay(int soldierId)
        => DiscoveryIntervalSeconds * (1f + (soldierId & 7) * 0.125f);

    private static bool TryTakeCloseScanBudget()
    {
        var frame = Time.frameCount;
        if (frame != _closeScanFrame)
        {
            _closeScanFrame = frame;
            _closeScansThisFrame = 0;
        }

        if (_closeScansThisFrame >= MaxCloseScansPerFrame)
            return false;

        _closeScansThisFrame++;
        return true;
    }

    internal static void Update(SoldierAI ai, Soldier soldier, float now)
    {
        if (!Settings.PerceptionEnabled.Value || soldier.IsOnVehicle())
            return;

        var soldierId = soldier.GetInstanceID();
        var state = AiState.GetTargetMemory(soldierId);

        // Nearby discovery is independent of the current candidate list. A soldier
        // may already be observing somebody farther away when a more immediate enemy
        // enters the room. When a due discovery scan runs, let its result settle and
        // perform the short confirmation poll on the next physics tick.
        var ranDiscoveryScan =
            TryDiscoverCloseTarget(ai, soldier, state, soldierId, now);
        if (state.Candidates.Count == 0 ||
            ranDiscoveryScan)
            return;

        if (now < state.NextCloseConfirmPollAt)
            return;

        var closeDistance = AiBehaviorTuning.ImmediateFireDistance;
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
        if (!TryTakeCloseScanBudget())
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

            PublishConfirmedCloseTarget(
                ai, soldier, target, targetToken, targetPosition, now);
            AiState.Trace(
                $"Acquisition: soldier {soldierId} fast-confirmed close target {targetToken} " +
                $"at {distance:0}m via close-range tick");
            return;
        }
    }

    private static bool TryDiscoverCloseTarget(
        SoldierAI ai,
        Soldier soldier,
        TargetMemoryState state,
        int soldierId,
        float now)
    {
        if (!Settings.CloseQuartersEnabled.Value ||
            now < state.NextCloseDiscoveryPollAt ||
            !TryTakeCloseScanBudget())
        {
            return false;
        }

        state.NextCloseDiscoveryPollAt = now + NextCloseDiscoveryPollDelay(soldierId);

        Vector3 origin;
        try
        {
            origin = soldier.LookPosition();

            var confirmedTarget = TargetAcquisition.ResolveObservedTarget(ai, soldier);
            var hasLivingVisibleConfirmedInfantry = false;
            var confirmedDistance = float.MaxValue;
            if (TargetAcquisition.MatchesTarget(confirmedTarget, state.TargetToken) &&
                TargetAcquisition.TryGetTargetSnapshot(
                    confirmedTarget, out _, out var confirmedPosition))
            {
                var confirmedSoldier = confirmedTarget!.TryCast<Soldier>();
                confirmedDistance = Vector3.Distance(origin, confirmedPosition);
                hasLivingVisibleConfirmedInfantry = confirmedSoldier != null &&
                                                    confirmedSoldier.IsAlive &&
                                                    soldier.CanSee(confirmedTarget);
            }

            if (!CloseTargetDiscoveryCore.ShouldSearch(
                    state.HasConfirmedTarget,
                    state.RequiresTargetReacquisition,
                    hasLivingVisibleConfirmedInfantry,
                    confirmedDistance,
                    AiBehaviorTuning.ImmediateFireDistance))
            {
                return true;
            }

            NearbyCreatures.Clear();
            var octree = Creature.creaturesOctatree;
            if (octree == null ||
                !octree.GetNearbyNonAlloc(
                    origin,
                    AiBehaviorTuning.ImmediateFireDistance,
                    NearbyCreatures))
            {
                return true;
            }

            var ownFaction = AiState.FactionOf(soldier);
            var suppression = TargetAcquisition.Suppression(soldier);
            Spottable? closestVisibleTarget = null;
            var closestDistance = float.MaxValue;
            var closestPosition = default(Vector3);

            for (var index = 0; index < NearbyCreatures.Count; index++)
            {
                var other = NearbyCreatures[index]?.TryCast<Soldier>();
                if (other == null ||
                    other.GetInstanceID() == soldierId ||
                    !other.IsAlive ||
                    other.IsOnVehicle() ||
                    !ResourcesManager.IsEnemyFaction(ownFaction, AiState.FactionOf(other)))
                {
                    continue;
                }

                var target = Creature.GetConnectedSpottable(other.transform);
                if (!TargetAcquisition.TryGetTargetSnapshot(
                        target, out _, out var targetPosition))
                {
                    continue;
                }

                var distance = Vector3.Distance(origin, targetPosition);
                if (distance >= closestDistance ||
                    !TargetAcquisition.IsInsideEffectiveFov(
                        soldier, targetPosition, distance, suppression) ||
                    !soldier.CanSee(target))
                {
                    continue;
                }

                closestVisibleTarget = target;
                closestDistance = distance;
                closestPosition = targetPosition;
            }

            if (closestVisibleTarget == null)
                return true;

            // First sighting deliberately starts, rather than bypasses, the configured
            // human reaction delay. The ordinary fast-confirm path owns later samples.
            TargetAcquisition.RetainOnlyNativeCandidate(
                soldier, closestVisibleTarget.Pointer);
            if (TargetAcquisition.TryConfirm(
                    soldier, closestVisibleTarget, closestDistance, suppression, now))
            {
                PublishConfirmedCloseTarget(
                    ai,
                    soldier,
                    closestVisibleTarget,
                    closestVisibleTarget.Pointer,
                    closestPosition,
                    now);
            }

            state.NextCloseConfirmPollAt = now + NextCloseConfirmPollDelay(soldierId);
            AiState.Trace(
                $"Acquisition: soldier {soldierId} discovered close target " +
                $"{closestVisibleTarget.Pointer} at {closestDistance:0}m between native scans");
        }
        catch (NullReferenceException) { }
        catch (Il2CppException) { }
        catch (ObjectCollectedException) { }
        finally
        {
            NearbyCreatures.Clear();
        }

        return true;
    }

    private static void PublishConfirmedCloseTarget(
        SoldierAI ai,
        Soldier soldier,
        Spottable target,
        IntPtr targetToken,
        Vector3 targetPosition,
        float now)
    {
        TargetAcquisition.RecordConfirmedNativeObservation(
            soldier, targetToken, targetPosition, now);
        TargetAcquisition.PublishSoldierTarget(soldier, target);
        if (!TargetAcquisition.MatchesTarget(ai.visibleTarget, targetToken))
            ai.visibleTarget = target;

        // This confirmation runs outside the shared tactical decision queue. Apply
        // the equally urgent close-contact halt here too, so a rifleman does not
        // spend the queue delay walking through an enemy with fire inhibited.
        ContactResponse.ReactToNewCloseTarget(
            ai, soldier, targetToken, targetPosition, now);
    }
}
