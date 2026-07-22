using System.Collections.Generic;

namespace ER2RealismOverhaul;

/// <summary>
/// Chooses the next eligible artillery crew without depending on the order in
/// which Unity happens to enumerate living soldiers.
/// </summary>
internal static class ArtilleryCrewSelectionCore
{
    internal static int SelectNextCandidateIndex(IReadOnlyList<int> orderedCrewIds, int lastSelectedCrewId)
    {
        if (orderedCrewIds == null || orderedCrewIds.Count == 0)
            return -1;

        for (var index = 0; index < orderedCrewIds.Count; index++)
        {
            if (orderedCrewIds[index] == lastSelectedCrewId)
                return (index + 1) % orderedCrewIds.Count;
        }

        return 0;
    }
}
