namespace ER2RealismOverhaul;

/// <summary>
/// Stable per-squad identifiers used to key mod state and debug output.
/// Squad instances can be recreated by the game, so the native InstanceID is
/// preferred and the leader/pointer are only fallbacks.
/// </summary>
internal static class SquadIdentity
{
    internal static int GetSquadId(Soldier soldier)
        => soldier.joinedSquad != null ? GetSquadId(soldier.joinedSquad) : soldier.GetInstanceID();

    internal static int GetSquadId(Squad squad)
    {
        var stableId = squad.InstanceID.GetHashCode();
        if (stableId != 0)
            return stableId;

        var leader = squad.Leader;
        return leader != null ? leader.GetInstanceID() : squad.Pointer.GetHashCode();
    }
}
