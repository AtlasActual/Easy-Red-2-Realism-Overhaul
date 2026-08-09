namespace ER2RealismOverhaul;

internal readonly record struct ObjectiveAttackRole(
    bool IsFlank,
    int Side,
    float AngleDegrees);

internal static class ObjectiveAttackPlanCore
{
    internal const float FlankAngleDegrees = 58f;

    internal static ObjectiveAttackRole SelectRole(
        int squadIndex,
        int squadCount,
        int objectiveId)
    {
        if (squadCount < 2 || squadIndex == 0)
            return new ObjectiveAttackRole(false, 0, 0f);

        var firstSide = (objectiveId & 1) == 0 ? -1 : 1;
        var side = (squadIndex - 1) % 2 == 0 ? firstSide : -firstSide;
        return new ObjectiveAttackRole(true, side, FlankAngleDegrees);
    }
}
