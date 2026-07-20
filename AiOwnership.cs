using System.Diagnostics.CodeAnalysis;

namespace ER2RealismOverhaul;

/// <summary>
/// Defines the safety boundary for every patch that is allowed to control an AI
/// soldier. IsFPSPlayer only describes the active camera mode; controller-owned
/// soldiers can still have an AI controller while using another player camera.
/// </summary>
internal static class AiOwnership
{
    internal static bool IsAutonomous([NotNullWhen(true)] Soldier? soldier)
    {
        if (soldier == null)
            return false;

        try
        {
            return soldier.IsAI() && !soldier.IsPlayer();
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
}
