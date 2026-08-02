using HarmonyLib;

namespace ER2RealismOverhaul;

internal static class ExposedReloadPosture
{
    internal static void Prepare(Soldier soldier)
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
                soldier.IsOnVehicle() || soldier.IsOnFire ||
                soldier.m_pose != SoldierPose.Crouch ||
                ContactResponse.IsOnUsableCover(soldier) ||
                soldier.LuaSoldier?.HasScriptAssigned() == true)
            {
                return;
            }

            var ai = soldier.aiController;
            if (ai == null)
                return;

            var soldierId = soldier.GetInstanceID();
            if (AiState.IsFlameEvading(soldierId, UnityEngine.Time.time))
                return;

            // Only claim the safety halt after the game has accepted and started the
            // reload. The required action stays crouched and cannot create a third
            // prone intent.
            var state = AiState.GetContactState(soldierId);
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
        if (!state.ExposedReloadSafetyOwned)
            return false;

        try
        {
            var flameEvading = Settings.DangerReactionsEnabled.Value &&
                               AiState.IsFlameEvading(soldier.GetInstanceID(), now);
            if (!Settings.DangerReactionsEnabled.Value || !soldier.IsAlive ||
                !soldier.IsReloading || soldier.IsOnVehicle() || soldier.IsOnFire ||
                flameEvading || ContactResponse.IsOnUsableCover(soldier))
            {
                state.ExposedReloadSafetyOwned = false;
                return false;
            }

            // Reload owns both posture and fire permission until the magazine is
            // seated (the arbiter's rank b). Other tactical systems must not raise the
            // soldier to engage an enemy in the middle of this vulnerable action.
            GroundAiDirector.ExecuteRequiredActionHalt(soldier);
            return true;
        }
        catch
        {
            state.ExposedReloadSafetyOwned = false;
            return false;
        }
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
    private static bool Prefix(Soldier __instance) =>
        ProneMovingActionRestriction.Prepare(__instance);

    [HarmonyPostfix]
    private static void Postfix(Soldier __instance) =>
        ExposedReloadPosture.Prepare(__instance);
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.UseBandages))]
internal static class SoldierProneMovingBandagePatch
{
    [HarmonyPrefix]
    private static bool Prefix(Soldier __instance) =>
        ProneMovingActionRestriction.Prepare(__instance);
}
