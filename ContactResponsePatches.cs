using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(Soldier), nameof(Soldier.CoverPosition))]
internal static class ExclusiveCoverAssignmentPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Soldier __instance, AiDestination cover)
    {
        if (!MultiplayerAuthority.CanMutateGameplay() ||
            !AiOwnership.IsAutonomous(__instance))
            return true;

        // Native AI also uses CoverPosition(null) as a generic destination clear.
        // That clear must not tear down a protected gun route or a defender's
        // selected building/trench post between director updates.
        if (cover == null)
        {
            var shouldBlockClear = ContactResponse.ShouldBlockNativeCoverClear(__instance);
            if (!shouldBlockClear)
            {
                var soldierId = __instance.GetInstanceID();
                AiState.ReleaseCoverReservation(soldierId);
                if (AiState.ContactStates.TryGetValue(soldierId, out var state))
                {
                    state.OccupiedCoverClaimId = IntPtr.Zero;
                    state.OccupiedCoverClaimRefreshAt = 0f;
                }
            }

            return !shouldBlockClear;
        }

        // Vehicles inherit AiDestination and the native boarding path calls
        // Soldier.CoverPosition(vehicle). Cover-only ownership rules must not
        // reject that call or squad vehicle orders, including static AT staffing,
        // silently succeed without sending a soldier to the requested seat.
        if (cover.IsVehicle())
            return true;

        // Inside a director-owned defensive area, only the selected movement
        // executor may change a cover destination. This closes the remaining native
        // CoverPosition writer that used to circulate defenders between updates.
        if (!ContactResponse.MayWriteCoverAssignment(__instance))
            return false;

        // Once a defender has reached useful cover inside the assigned area, that
        // cover is his position. Native cover refreshes must not send him to another
        // slot until the order moves or the position becomes unusable.
        if (ContactResponse.ShouldKeepReachedCover(__instance))
            return false;

        try
        {
            var soldierId = __instance.GetInstanceID();
            var coverId = cover.Pointer;
            if (!TryGetUsableCoverPosition(cover, out var coverPosition))
                return false;

            // A player-issued hold order defines the area in which this squad may
            // improve its position. Native or mod cover selection must not silently
            // replace that command with a cover slot outside the ordered area.
            if (!ContactResponse.CoverRespectsPlayerHoldOrder(__instance, coverPosition))
            {
                AiState.Trace(
                    $"Player hold: blocked soldier {soldierId} from cover outside the ordered area");
                return false;
            }

            if (CoverOccupancy.IsOccupiedByOther(coverPosition, __instance))
            {
                AiState.Trace($"Cover occupancy: blocked soldier {soldierId} from an occupied position");
                return false;
            }

            // Native AI assignments do not pass through Contact Response's move
            // state. Reserve their physical destination here as well so two native
            // cover objects at the same trench slot cannot attract two soldiers.
            var now = Time.time;
            if (!AiState.TryReserveCover(
                    coverId,
                    coverPosition,
                    soldierId,
                    now,
                    now + InfantryCoverPolicy.CoverReservationLeaseSeconds,
                    InfantryCoverPolicy.OccupancyRadiusMeters))
            {
                AiState.Trace(
                    $"Cover occupancy: blocked soldier {soldierId} from an occupied position");
                return false;
            }
        }
        catch (Exception ex)
        {
            // Fail open: an unexpected exception here must not block all cover
            // for this soldier. Let the native cover assignment run instead.
            Plugin.LogSource.LogWarning(
                $"Cover occupancy check failed ({ex.GetType().Name}): {ex.Message}");
            return true;
        }

        return true;
    }

    internal static void MaintainOccupiedCoverClaim(Soldier soldier, float now)
    {
        var soldierId = soldier.GetInstanceID();
        var state = AiState.GetContactState(soldierId);
        if (!soldier.IsOnCover())
        {
            state.OccupiedCoverClaimId = IntPtr.Zero;
            state.OccupiedCoverClaimRefreshAt = 0f;
            return;
        }

        try
        {
            var cover = soldier.targetDestination;
            if (cover == null || cover.WasCollected || cover.Pointer == IntPtr.Zero ||
                !TryGetUsableCoverPosition(cover, out var coverPosition))
            {
                state.OccupiedCoverClaimId = IntPtr.Zero;
                state.OccupiedCoverClaimRefreshAt = 0f;
                return;
            }

            var coverId = cover.Pointer;
            if (state.OccupiedCoverClaimId == coverId &&
                now < state.OccupiedCoverClaimRefreshAt)
            {
                return;
            }

            if (!AiState.TryReserveCover(
                    coverId,
                    coverPosition,
                    soldierId,
                    now,
                    now + InfantryCoverPolicy.CoverReservationLeaseSeconds,
                    InfantryCoverPolicy.OccupancyRadiusMeters))
            {
                ContactResponse.RejectContestedOccupiedCover(
                    soldier, state, soldierId, coverId, now);
                return;
            }

            state.OccupiedCoverClaimId = coverId;
            state.OccupiedCoverClaimRefreshAt =
                now + InfantryCoverPolicy.DecisionIntervalSeconds;
        }
        catch (NullReferenceException)
        {
            state.OccupiedCoverClaimId = IntPtr.Zero;
            state.OccupiedCoverClaimRefreshAt = 0f;
        }
        catch (Il2CppException)
        {
            state.OccupiedCoverClaimId = IntPtr.Zero;
            state.OccupiedCoverClaimRefreshAt = 0f;
        }
        catch (ObjectCollectedException)
        {
            state.OccupiedCoverClaimId = IntPtr.Zero;
            state.OccupiedCoverClaimRefreshAt = 0f;
        }
    }

    internal static bool TryGetUsableCoverPosition(AiDestination cover, out Vector3 position)
    {
        position = default;

        try
        {
            if (cover.IsCoverDestroyed())
                return false;

            var combatCover = cover.TryCast<CombatCover>();
            if (!ReferenceEquals(combatCover, null) &&
                (combatCover == null || combatCover.gameObject == null || combatCover.transform == null))
            {
                return false;
            }

            position = cover.GetCoverPosition();
            return true;
        }
        catch (NullReferenceException)
        {
            // The game can briefly retain a CombatCover after its backing
            // object has been torn down. Reject it instead of entering the
            // native CoverPosition path with an invalid destination.
            return false;
        }
        catch (Il2CppException)
        {
            // Exceptions raised by native IL2CPP methods are surfaced through
            // Il2CppException, including the observed CombatCover null access.
            return false;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
    }
}

[HarmonyPatch(typeof(CombatCover), nameof(CombatCover.IsCoverAvailable))]
internal static class BrokenCombatCoverAvailabilityPatch
{
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, ref bool __result)
    {
        if (__exception is NullReferenceException or Il2CppException or ObjectCollectedException)
        {
            // CoverManager evaluates availability from inside its iterator. One
            // torn-down CombatCover otherwise aborts the entire search (and the
            // soldier tactical update) before any healthy trench/building slot can
            // be considered. A broken cover object is simply unavailable.
            __result = false;
            return null;
        }

        return __exception;
    }
}

/// <summary>
/// Carries the identity and ownership a tactical prefix already resolved across to its own
/// postfix. Harmony runs the postfix inside the same call as the prefix — including when
/// the prefix returned false — so those facts are still valid there, and re-deriving them
/// cost a second GetSoldier plus IsAutonomous (itself IsAI + IsPlayer + GetInstanceID) plus
/// IsOnVehicle for every AI soldier every frame. That is managed->il2cpp boundary traffic,
/// not AI work: the same measurement that showed the pipeline's cost tracking the NUMBER of
/// calls rather than what any of them decides.
///
/// One slot is enough because the engine drives these calls one soldier at a time. It is
/// keyed on the controller pointer AND the frame so a postfix whose prefix did not run can
/// never consume a stale or foreign verdict — it simply misses and resolves for itself.
/// </summary>
internal static class TacticalMoveHandoff
{
    private static IntPtr _controller;
    private static int _frame = -1;
    private static Soldier? _soldier;
    private static int _soldierId;
    private static float _now;
    private static bool _owned;

    internal static void Publish(
        IntPtr controller, Soldier? soldier, int soldierId, float now, bool owned)
    {
        _controller = controller;
        _frame = Time.frameCount;
        _soldier = soldier;
        _soldierId = soldierId;
        _now = now;
        _owned = owned;
    }

    internal static bool TryConsume(
        IntPtr controller, out Soldier? soldier, out int soldierId, out float now, out bool owned)
    {
        if (controller != IntPtr.Zero && controller == _controller && _frame == Time.frameCount)
        {
            soldier = _soldier;
            soldierId = _soldierId;
            now = _now;
            owned = _owned;
            return true;
        }

        soldier = null;
        soldierId = 0;
        now = 0f;
        owned = false;
        return false;
    }
}

[HarmonyPatch(typeof(SoldierAI), nameof(SoldierAI.MoveOptimized))]
internal static class SoldierTacticalSprintPatch
{
    // NOTE: __instance must stay the wrapper type. Declaring it as IntPtr to skip the
    // per-call marshalling made Harmony emit a method the runtime rejects outright —
    // "InvalidProgramException: Common Language Runtime detected an invalid program" on
    // every invocation of the native->managed trampoline. The boundary allocation is real
    // and measurable, but this is not a way to avoid it.
    [HarmonyPrefix]
    private static bool Prefix(SoldierAI __instance, ref bool sprint, float deltaTime)
    {
        var __t = ModTimeProbe.Begin();
        var __a = ModTimeProbe.BeginAlloc();
        try
        {
            return SharedTacticalMovePrefix(__instance, deltaTime, ref sprint, updateFireInhibitionOnPass: true);
        }
        finally
        {
            ModTimeProbe.EndTacticalAlloc(__a);
            ModTimeProbe.EndTacticalMove(__t);
        }
    }

    /// <summary>
    /// Common tactical-movement gate sequence shared by the MoveOptimized (sprint) and
    /// MoveFPSOptimized prefixes. <paramref name="updateFireInhibitionOnPass"/> is true
    /// only for the sprint caller, which updates the moving-fire gate on its
    /// fall-through path; the FPS caller instead updates it from its own Postfix.
    /// </summary>
    internal static bool SharedTacticalMovePrefix(
        SoldierAI ai,
        float deltaTime,
        ref bool sprint,
        bool updateFireInhibitionOnPass)
    {
        ModTimeProbe.Stage(TacticalStage.PrefixEntry);
        // Measures only the opening block — the instance marshalling this method was
        // entered with, GetSoldier, and the ownership tests. Compared against the
        // whole-call figure it says whether the per-call garbage is at the boundary or
        // further in, which decides whether changing the patch signature is the fix.
        var __entryAlloc = ModTimeProbe.BeginAlloc();
        if (!MultiplayerAuthority.CanMutateGameplay())
        {
            ModTimeProbe.EndEntryAlloc(__entryAlloc);
            return true;
        }

        var soldier = ai.GetSoldier();
        var ownedByAi = AiOwnership.IsAutonomous(soldier) && !soldier.IsOnVehicle();
        ModTimeProbe.EndEntryAlloc(__entryAlloc);
        if (!ownedByAi)
        {
            // Publish the negative verdict too: the postfix bails on exactly this test, so
            // handing it the answer saves it from repeating the whole ownership probe.
            TacticalMoveHandoff.Publish(ai.Pointer, soldier: null, soldierId: 0, now: 0f, owned: false);
            return true;
        }

        var id = soldier.GetInstanceID();
        var now = Time.time;
        TacticalMoveHandoff.Publish(ai.Pointer, soldier, id, now, owned: true);
        ModTimeProbe.Stage(TacticalStage.SuppressionReaction);
        ContactResponse.UpdateSuppressionReaction(ai, soldier, id, now, deltaTime);
        if (Settings.DangerReactionsEnabled.Value && soldier.IsOnFire)
        {
            ModTimeProbe.Stage(TacticalStage.FireDanger);
            sprint = false;
            ContactResponse.ExecuteStopFire(soldier);
            ContactResponse.StopDangerMovement(ai, soldier, deltaTime);
            return false;
        }

        // The low-node-count movement path must honor the same committed reload
        // ownership as MoveOptimized; otherwise it can raise the soldier back to
        // a fighting crouch between reload frames.
        ModTimeProbe.Stage(TacticalStage.ReloadPosture);
        if (ExposedReloadPosture.TryMaintain(soldier, now))
        {
            sprint = false;
            return false;
        }

        var flameEvading = Settings.DangerReactionsEnabled.Value &&
                           AiState.IsFlameEvading(id, now);
        if (ContactResponse.IsPinned(id) && !flameEvading)
        {
            ModTimeProbe.Stage(TacticalStage.PinnedSuppression);
            sprint = false;
            var remainsStationary = ContactResponse.ApplyPinnedSuppression(
                ai,
                soldier,
                AiState.GetContactState(id),
                now,
                deltaTime);
            if (remainsStationary)
                return false;
        }

        ModTimeProbe.Stage(TacticalStage.MovementStall);
        if (!flameEvading &&
            ContactResponse.TryHoldMovementStall(ai, soldier, now, deltaTime))
        {
            sprint = false;
            return false;
        }

        // Everything above is a per-frame SAFETY reaction (fire, reload, pinned, flame,
        // movement stall) and stays per-frame. Everything below is the
        // per-soldier DECISION tail — selecting which stationary hold owns the soldier
        // (native ownership refresh, IsActualCharge, IsOnCover) and resolving its pose
        // (StationaryHoldPose cover geometry). On the non-decision frames of the
        // round-robin stagger, re-assert the last-decided gate instead of recomputing.
        ModTimeProbe.Stage(TacticalStage.WriteThrough);
        if (!flameEvading && !ContactResponse.RunsDecisionThisFrame(id) &&
            ContactResponse.TryWriteThroughTacticalMove(
                ai, soldier, id, now, deltaTime, ref sprint, updateFireInhibitionOnPass,
                out var stagger))
        {
            return stagger;
        }

        ModTimeProbe.Stage(TacticalStage.ChargeCheck);
        var actualCharge = ContactResponse.IsActualCharge(soldier);
        var hazardEvading = flameEvading;
        var activeThreatMovement = (ContactResponse.HasActiveContact(id, now) ||
                                    IncomingFireAwareness.HasActiveCue(id, now)) &&
                                   !hazardEvading;
        // The three stationary holds are owner DECLARATIONS (plan 018): each hands the
        // frame to the movement arbiter and only gates native locomotion when the arbiter
        // actually halted the soldier. A higher owner (flame escape) or the bounded
        // halt-spacing step falls through to the moving tail instead, which is why these
        // no longer carry their own "!hazardEvading" guards.
        if (ContactResponse.ShouldHoldDefensivePosition(soldier, now) ||
            ContactResponse.ShouldHoldEngagement(id, now) ||
            (ContactResponse.ShouldHoldCover(id, now) && soldier.IsOnCover() && !actualCharge))
        {
            ModTimeProbe.Stage(TacticalStage.StopTacticalMovement);
            sprint = false;
            var haltOwner = ContactResponse.StopTacticalMovement(
                ai,
                soldier,
                deltaTime);
            if (MovementArbiterCore.Halts(haltOwner))
                return false;
        }

        ModTimeProbe.Stage(TacticalStage.PoseApply);
        var suppression = soldier.GetSuppressionValue();
        ApplyTacticalMovementPose(ai, soldier, id, now, suppression, activeThreatMovement);

        ModTimeProbe.Stage(TacticalStage.FireDecision);
        if (updateFireInhibitionOnPass)
            ContactResponse.ApplyFireDecision(ai, soldier, now, authoritative: false);
        ModTimeProbe.Stage(TacticalStage.ThreatFacingRelease);
        ContactResponse.ReleaseStationaryThreatFacingForMovement(ai, soldier);
        ModTimeProbe.Stage(TacticalStage.SuppressiveInterrupt);
        KnownTargetSuppressiveFire.InterruptForMovement(ai, soldier);

        return true;
    }

    /// <summary>
    /// Resolves the soldier a tactical postfix should act on, reusing whatever this frame's
    /// prefix already established for the same controller and falling back to a full
    /// resolve when there is nothing to reuse (the prefix bailed before publishing, or a
    /// higher-priority patch skipped it).
    /// </summary>
    internal static bool TryResolveTacticalTarget(
        SoldierAI ai,
        [NotNullWhen(true)] out Soldier? soldier,
        out int soldierId,
        out float now)
    {
        // WasCollected is a managed-side handle check, not a boundary crossing. It restores
        // the collected-object resilience the full path gets free from IsAutonomous's
        // catch blocks, which reusing the prefix's reference would otherwise skip.
        if (TacticalMoveHandoff.TryConsume(
                ai.Pointer, out soldier, out soldierId, out now, out var owned))
            return owned && soldier != null && !soldier.WasCollected;

        soldier = null;
        soldierId = 0;
        now = 0f;
        if (!MultiplayerAuthority.CanMutateGameplay())
            return false;

        var resolved = ai.GetSoldier();
        if (!AiOwnership.IsAutonomous(resolved) || resolved.IsOnVehicle())
            return false;

        soldier = resolved;
        soldierId = resolved.GetInstanceID();
        now = Time.time;
        return true;
    }

    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            ModTimeProbe.Stage(TacticalStage.PostfixEntry);
            if (!TryResolveTacticalTarget(
                    __instance, out var soldier, out var soldierId, out var now))
                return;

            ModTimeProbe.Stage(TacticalStage.PostfixMaintainPose);
            ContactResponse.MaintainOwnedPose(
                __instance, soldier, now,
                ContactResponse.RunsDecisionThisFrame(soldierId));
            ModTimeProbe.Stage(TacticalStage.PostfixFireDecision);
            ContactResponse.ApplyFireDecision(__instance, soldier, now, authoritative: false);
        }
        finally
        {
            ModTimeProbe.EndTacticalMove(__t);
        }
    }

    internal static void ApplyTacticalMovementPose(
        SoldierAI ai,
        Soldier soldier,
        int id,
        float now,
        int suppression,
        bool activeThreatMovement)
    {
        // Moving pose write: runs on both decision frames (the prefix tail) and the
        // per-frame moving write-through, so it uses the round-robin stagger cadence for
        // the arbiter's interop-heavy decision tail.
        var resolveDecisionTail = ContactResponse.RunsDecisionThisFrame(id);
        if (ContactResponse.IsPinned(id) && !AiState.IsFlameEvading(id, now))
        {
            ContactResponse.ApplyArbitratedPose(
                ai, soldier, now, resolveDecisionTail,
                ContactResponse.SuppressionPose(soldier), "move-pinned");
        }
        else
        {
            var state = AiState.GetContactState(id);
            if (activeThreatMovement)
            {
                if (ContactResponse.HasActiveContact(id, now))
                    state.ContactCrouchOwned = true;
                else
                    state.TacticalCrouchUntil = now + ContactResponse.TacticalCrouchPersistenceSeconds;
            }

            if ((Settings.DangerReactionsEnabled.Value &&
                 suppression >= AiBehaviorTuning.CrouchSuppressionThreshold) ||
                activeThreatMovement || ContactResponse.ShouldOwnCrouch(id, now))
            {
                ContactResponse.ApplyArbitratedPose(
                    ai, soldier, now, resolveDecisionTail, SoldierPose.Crouch, "move-crouch");
            }
        }
    }

}

[HarmonyPatch(typeof(SoldierAI), nameof(SoldierAI.MoveFPSOptimized))]
internal static class SoldierTacticalFpsMovePatch
{
    [HarmonyPrefix]
    private static bool Prefix(SoldierAI __instance, float deltaTime)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            var discardSprint = false;
            return SoldierTacticalSprintPatch.SharedTacticalMovePrefix(
                __instance, deltaTime, ref discardSprint, updateFireInhibitionOnPass: false);
        }
        finally
        {
            ModTimeProbe.EndTacticalMove(__t);
        }
    }

    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            ModTimeProbe.Stage(TacticalStage.PostfixEntry);
            if (!SoldierTacticalSprintPatch.TryResolveTacticalTarget(
                    __instance, out var soldier, out var soldierId, out var now))
                return;

            ModTimeProbe.Stage(TacticalStage.PostfixFireDecision);
            ContactResponse.ApplyFireDecision(__instance, soldier, now, authoritative: false);
            ModTimeProbe.Stage(TacticalStage.PostfixMaintainPose);
            ContactResponse.MaintainOwnedPose(
                __instance, soldier, now,
                ContactResponse.RunsDecisionThisFrame(soldierId));
        }
        finally
        {
            ModTimeProbe.EndTacticalMove(__t);
        }
    }
}

[HarmonyPatch(typeof(SoldierAI), "GetFavouriteFightingPose")]
internal static class SoldierPinnedPosePatch
{
    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance, ref SoldierPose __result)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            if (!MultiplayerAuthority.CanMutateGameplay())
                return;

            var soldier = __instance.GetSoldier();
            if (soldier == null)
                return;

            var id = soldier.GetInstanceID();
            var now = Time.time;

            // The native favourite pose must agree with the single arbiter, or native
            // SetPose fights the mod's owned pose every tick (the Pose drift war). Resolve
            // the same arbiter MaintainOwnedPose uses and, whenever a mod owner is active,
            // return its pose so the two channels agree. The safety owners are recomputed
            // every frame inside the arbiter; the interop-heavy decision tail follows the
            // round-robin stagger cadence.
            var state = AiState.GetContactState(id);
            var pose = ContactResponse.ResolvePose(
                soldier, state, now, ContactResponse.RunsDecisionThisFrame(id), out var owner);
            if (owner != PoseOwner.None)
            {
                __result = ContactResponse.CommitArbitratedPose(soldier, state, owner, pose, now, null);
            }
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.FightingPose, __t);
        }
    }
}

[HarmonyPatch(typeof(SoldierAI), "OnDestroy")]
internal static class SoldierAiDestroyPatch
{
    [HarmonyPrefix]
    private static void Prefix(SoldierAI __instance)
    {
        try
        {
            var soldier = __instance.GetSoldier();
            if (soldier != null)
                AiState.RemoveSoldier(soldier);
        }
        catch (Exception ex)
        {
            // Cleanup must never interfere with the game's destruction path.
            Plugin.LogSource.LogWarning($"SoldierAiDestroyPatch.Prefix cleanup failed: {ex.Message}");
        }
    }
}
