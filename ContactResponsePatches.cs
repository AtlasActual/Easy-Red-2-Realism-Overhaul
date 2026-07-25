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
            return !ContactResponse.ShouldBlockNativeCoverClear(__instance);

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

            // Same small HARD radius as the mod's own search (plan 016 review): this
            // prefix silently rejects a native cover assignment, so a wide exclusion
            // here would strand soldiers wherever they stand.
            if (AiState.CoverReservedByOther(
                    coverId,
                    coverPosition,
                    soldierId,
                    Time.time,
                    InfantryCoverPolicy.OccupancyRadiusMeters))
            {
                AiState.Trace($"Cover occupancy: blocked soldier {soldierId} from an occupied position");
                return false;
            }

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
            var reservationSeconds = InfantryCoverPolicy.DecisionIntervalSeconds;
            AiState.ReserveCover(
                coverId, coverPosition, soldierId, Time.time + reservationSeconds);
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

[HarmonyPatch(typeof(SoldierAI), nameof(SoldierAI.MoveOptimized))]
internal static class SoldierTacticalSprintPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SoldierAI __instance, ref bool sprint, float deltaTime)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            return SharedTacticalMovePrefix(__instance, deltaTime, ref sprint, updateFireInhibitionOnPass: true);
        }
        finally
        {
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
        if (!MultiplayerAuthority.CanMutateGameplay())
            return true;

        var soldier = ai.GetSoldier();
        if (!AiOwnership.IsAutonomous(soldier) || soldier.IsOnVehicle())
            return true;

        var id = soldier.GetInstanceID();
        var now = Time.time;
        ContactResponse.UpdateSuppressionReaction(ai, soldier, now, deltaTime);
        if (Settings.DangerReactionsEnabled.Value && soldier.IsOnFire)
        {
            sprint = false;
            soldier.StopFire();
            ContactResponse.StopDangerMovement(ai, soldier, SoldierPose.Prone, deltaTime);
            return false;
        }

        // The low-node-count movement path must honor the same committed reload
        // ownership as MoveOptimized; otherwise it can raise the soldier back to
        // a fighting crouch between reload frames.
        if (ExposedReloadPosture.TryMaintain(soldier, now))
        {
            sprint = false;
            return false;
        }

        var flameEvading = Settings.DangerReactionsEnabled.Value &&
                           AiState.IsFlameEvading(id, now);
        if (ContactResponse.IsPinned(id) && !flameEvading)
        {
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

        if (!flameEvading &&
            ContactResponse.TryHoldMovementStall(ai, soldier, now, deltaTime))
        {
            sprint = false;
            return false;
        }

        if (!flameEvading &&
            ContactResponse.TryHoldTankCover(ai, soldier, now, deltaTime))
        {
            sprint = false;
            return false;
        }

        // Everything above is a per-frame SAFETY reaction (fire, reload, pinned, flame,
        // movement stall, tank-hide) and stays per-frame. Everything below is the
        // per-soldier DECISION tail — selecting which stationary hold owns the soldier
        // (native ownership refresh, IsActualCharge, IsOnCover) and resolving its pose
        // (StationaryHoldPose cover geometry). On the non-decision frames of the
        // round-robin stagger, re-assert the last-decided gate instead of recomputing.
        if (!flameEvading && !ContactResponse.RunsDecisionThisFrame(id) &&
            ContactResponse.TryWriteThroughTacticalMove(
                ai, soldier, deltaTime, ref sprint, updateFireInhibitionOnPass,
                out var stagger))
        {
            return stagger;
        }

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
            sprint = false;
            var haltOwner = ContactResponse.StopTacticalMovement(
                ai,
                soldier,
                ContactResponse.StationaryHoldPose(soldier),
                deltaTime);
            if (MovementArbiterCore.Halts(haltOwner))
                return false;
        }

        var suppression = soldier.GetSuppressionValue();
        ApplyTacticalMovementPose(ai, soldier, now, suppression, activeThreatMovement);

        if (updateFireInhibitionOnPass)
            ContactResponse.ApplyFireDecision(ai, soldier, now, authoritative: false);
        ContactResponse.ReleaseStationaryThreatFacingForMovement(ai, soldier);
        KnownTargetSuppressiveFire.InterruptForMovement(ai, soldier);

        return true;
    }

    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            if (!MultiplayerAuthority.CanMutateGameplay())
                return;

            var soldier = __instance.GetSoldier();
            if (!AiOwnership.IsAutonomous(soldier) || soldier.IsOnVehicle())
                return;

            var now = Time.time;
            ContactResponse.MaintainOwnedPose(
                __instance, soldier, now,
                ContactResponse.RunsDecisionThisFrame(soldier.GetInstanceID()));
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
        float now,
        int suppression,
        bool activeThreatMovement)
    {
        var id = soldier.GetInstanceID();
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
                 suppression >= Settings.CrouchSuppression.Value) ||
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
            if (!MultiplayerAuthority.CanMutateGameplay())
                return;

            var soldier = __instance.GetSoldier();
            if (!AiOwnership.IsAutonomous(soldier) || soldier.IsOnVehicle())
                return;

            var now = Time.time;
            ContactResponse.ApplyFireDecision(__instance, soldier, now, authoritative: false);
            ContactResponse.MaintainOwnedPose(
                __instance, soldier, now,
                ContactResponse.RunsDecisionThisFrame(soldier.GetInstanceID()));
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
