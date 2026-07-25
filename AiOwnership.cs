using System.Diagnostics.CodeAnalysis;

namespace ER2RealismOverhaul;

/// <summary>
/// Defines the safety boundary for every patch that is allowed to control an AI
/// soldier. IsFPSPlayer only describes the active camera mode; controller-owned
/// soldiers can still have an AI controller while using another player camera.
/// </summary>
internal static class AiOwnership
{
    private static int _localSoldierFrame = -1;
    private static int _localSoldierId;
    private static bool _loggedWedgedControlFlags;

    internal static bool IsAutonomous([NotNullWhen(true)] Soldier? soldier)
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
