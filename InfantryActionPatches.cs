using HarmonyLib;

namespace ER2RealismOverhaul;

internal readonly record struct ReloadCoverCandidate(
    bool HasUsableCover,
    SoldierPose Pose);

internal static class ReloadPosture
{
    internal static ReloadCoverCandidate CaptureCoverCandidate(Soldier soldier)
    {
        if (!Settings.DangerReactionsEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return default;
        }

        try
        {
            if (!soldier.IsAlive || !AiOwnership.IsAutonomous(soldier) ||
                soldier.IsOnVehicle() || soldier.IsOnFire ||
                soldier.LuaSoldier?.HasScriptAssigned() == true)
            {
                return default;
            }

            var soldierId = soldier.GetInstanceID();
            var now = UnityEngine.Time.time;
            if (AiState.IsFlameEvading(soldierId, now) ||
                !ContactResponse.IsOnUsableCover(soldier))
            {
                return default;
            }

            var state = AiState.GetContactState(soldierId);
            return new ReloadCoverCandidate(
                true,
                ContactResponse.SelectReloadCoverPose(soldier, state, now));
        }
        catch
        {
            return default;
        }
    }

    internal static void Prepare(
        Soldier soldier,
        ReloadCoverCandidate capturedCover)
    {
        if (!Settings.DangerReactionsEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        try
        {
            if (!soldier.IsAlive || !AiOwnership.IsAutonomous(soldier) ||
                !soldier.IsReloading ||
                soldier.IsOnVehicle() ||
                soldier.LuaSoldier?.HasScriptAssigned() == true)
            {
                return;
            }

            var ai = soldier.aiController;
            if (ai == null)
                return;

            var soldierId = soldier.GetInstanceID();
            var now = UnityEngine.Time.time;
            var state = AiState.GetContactState(soldierId);
            if (state.CoverReloadPoseOwned)
            {
                state.CoverReloadPosePending = false;
                return;
            }

            // Keep a bounded retry alive only for this accepted reload. This avoids
            // polling every ordinary soldier while still recovering if Reload() hid
            // the cover bit before the postfix or a lethal hazard temporarily won.
            state.CoverReloadPosePending = true;
            if (soldier.IsOnFire || AiState.IsFlameEvading(soldierId, now))
                return;

            // Soldier.Reload can clear the native on-cover bit as it begins the action.
            // Prefer the validated prefix snapshot, but still accept a live cover claim
            // when the soldier reached cover during the call.
            if (capturedCover.HasUsableCover)
            {
                ClaimCoveredReloadPose(state, capturedCover.Pose);
                return;
            }

            if (ContactResponse.IsOnUsableCover(soldier))
            {
                // A reload does not need the cover's firing-height clearance. Choose
                // once so target changes cannot make the soldier alternate posture
                // while the magazine is being replaced. This is posture-only: normal
                // movement and fire ownership remain untouched for covered soldiers.
                ClaimCoveredReloadPose(soldier, state, now);
                return;
            }

            if (soldier.m_pose != SoldierPose.Crouch)
                return;

            // Only claim the safety halt after the game has accepted and started the
            // reload. The required action stays crouched and cannot create a third
            // prone intent.
            state.ExposedReloadSafetyOwned = true;
            GroundAiDirector.ExecuteRequiredActionHalt(soldier);
        }
        catch
        {
            // Preserve native reload behavior if a mission-owned or modded soldier
            // cannot expose the state needed for this optional survival posture.
        }
    }

    internal static bool TryMaintain(Soldier soldier, float now)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        if (!state.ExposedReloadSafetyOwned && !state.CoverReloadPosePending)
            return false;

        try
        {
            var flameEvading = Settings.DangerReactionsEnabled.Value &&
                               AiState.IsFlameEvading(soldier.GetInstanceID(), now);
            if (!Settings.DangerReactionsEnabled.Value || !soldier.IsAlive ||
                !soldier.IsReloading || soldier.IsOnVehicle() ||
                soldier.LuaSoldier?.HasScriptAssigned() == true)
            {
                state.ExposedReloadSafetyOwned = false;
                state.CoverReloadPosePending = false;
                state.CoverReloadPoseOwned = false;
                return false;
            }

            if (soldier.IsOnFire || flameEvading)
            {
                state.ExposedReloadSafetyOwned = false;
                return false;
            }

            // The native cover bit can be unavailable in the exact frame Reload()
            // accepts the action. Retry only until this active reload acquires a cover
            // pose; after that the selected crouch/prone posture remains latched and no
            // cover geometry is re-evaluated for the rest of the reload.
            var onUsableCover = !state.CoverReloadPoseOwned &&
                                ContactResponse.IsOnUsableCover(soldier);
            if (InfantryCoverDecisionCore.ShouldAcquireReloadPosture(
                    soldier.IsReloading,
                    state.CoverReloadPoseOwned,
                    onUsableCover))
            {
                ClaimCoveredReloadPose(soldier, state, now);
                return false;
            }

            if (state.CoverReloadPoseOwned)
            {
                state.ExposedReloadSafetyOwned = false;
                return false;
            }

            if (!state.ExposedReloadSafetyOwned)
                return false;

            // Reload owns both posture and fire permission until the magazine is
            // seated (the arbiter's rank b). Other tactical systems must not raise the
            // soldier to engage an enemy in the middle of this vulnerable action.
            GroundAiDirector.ExecuteRequiredActionHalt(soldier);
            return true;
        }
        catch
        {
            state.ExposedReloadSafetyOwned = false;
            state.CoverReloadPosePending = false;
            return false;
        }
    }

    private static void ClaimCoveredReloadPose(
        Soldier soldier,
        ContactResponseState state,
        float now)
        => ClaimCoveredReloadPose(
            state,
            ContactResponse.SelectReloadCoverPose(soldier, state, now));

    private static void ClaimCoveredReloadPose(
        ContactResponseState state,
        SoldierPose pose)
    {
        state.ExposedReloadSafetyOwned = false;
        state.CoverReloadPosePending = false;
        state.CoverReloadPose = pose;
        state.CoverReloadPoseOwned = true;
    }
}

internal static class ProneMovingActionRestriction
{
    internal static bool Prepare(Soldier soldier)
    {
        if (!Settings.PreventReloadingAndBandagingWhileCrawling.Value)
            return true;

        try
        {
            // This mod never gates a player's own actions; an autonomous soldier who
            // is already moving prone is halted so the urgent action starts from a
            // stable position. This restriction never requests the prone pose itself.
            if (!AiOwnership.IsAutonomous(soldier) ||
                !MultiplayerAuthority.CanMutateGameplay())
            {
                return true;
            }

            if (soldier.m_pose != SoldierPose.Prone || !soldier.IsMoving())
                return true;

            GroundAiDirector.ExecuteRequiredActionHalt(soldier);
            return true;
        }
        catch
        {
            // If a modded soldier implementation cannot report its movement state,
            // preserve the base game's action behavior.
            return true;
        }
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.Reload))]
internal static class SoldierProneMovingReloadPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        Soldier __instance,
        out ReloadCoverCandidate __state)
    {
        __state = ReloadPosture.CaptureCoverCandidate(__instance);
        return ProneMovingActionRestriction.Prepare(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(
        Soldier __instance,
        ReloadCoverCandidate __state) =>
        ReloadPosture.Prepare(__instance, __state);
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.UseBandages))]
internal static class SoldierProneMovingBandagePatch
{
    [HarmonyPrefix]
    private static bool Prefix(Soldier __instance) =>
        ProneMovingActionRestriction.Prepare(__instance);
}
