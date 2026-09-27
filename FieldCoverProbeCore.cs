namespace ER2RealismOverhaul;

internal readonly record struct FieldCoverCandidate(
    float X,
    float Y,
    float Z,
    float DistanceMeters,
    float Quality);

/// <summary>
/// Cover selection only ever looked at AUTHORED slots: Combat Cover props, building and
/// trench nodes. A map (or a stretch of trench) without them left soldiers lying in the
/// open beside perfectly good walls and banks. This core ranks positions found by
/// probing the actual geometry instead. It deliberately proposes very few: each survivor
/// still has to pass the same ballistic protection test an authored slot does, and that
/// test is the expensive part.
/// </summary>
internal static class FieldCoverProbeCore
{
    internal const float SearchRadiusMeters = 22f;
    // A closer spot never becomes a committed route, so it could never be anchored.
    internal const float MinimumDistanceMeters = 1.5f;
    // Far enough off a wall that the body is not inside it, close enough to hug it.
    internal const float StandOffFromFaceMeters = 0.6f;
    // A face leaning back further than about 45 degrees is a slope, not a wall.
    internal const float MaximumFaceNormalY = 0.7f;
    // A crouched man needs most of a metre of bank between him and the threat.
    internal const float MinimumShelterStepMeters = 0.7f;
    // Narrower than an ordinary trench, so a radial cannot step over one.
    internal const float StepSampleSpacingMeters = 1.25f;
    internal const float MaximumHeightDeltaMeters = 3f;
    internal const float MinimumStandableNormalY = 0.6f;
    internal const int MaximumDetailedCandidates = 3;
    internal const float CandidateSeparationMeters = 2f;
    internal const float FailedProbeBackoffSeconds = 15f;
    internal const float FailedPositionSeconds = 45f;
    internal const float FailedPositionRadiusMeters = 3f;

    /// <summary>
    /// A face shelters the side the probing soldier can see when it is steep and its
    /// normal points AWAY from the threat: the obstacle then lies between that side and
    /// the enemy. A face turned toward the threat is the exposed side of something.
    /// </summary>
    internal static bool IsShelteringFace(
        float normalX, float normalY, float normalZ,
        float towardThreatX, float towardThreatZ)
    {
        if (!float.IsFinite(normalX) || !float.IsFinite(normalY) || !float.IsFinite(normalZ) ||
            !float.IsFinite(towardThreatX) || !float.IsFinite(towardThreatZ) ||
            MathF.Abs(normalY) > MaximumFaceNormalY)
        {
            return false;
        }

        var flatLength = MathF.Sqrt(normalX * normalX + normalZ * normalZ);
        var threatLength = MathF.Sqrt(towardThreatX * towardThreatX + towardThreatZ * towardThreatZ);
        if (flatLength < 0.001f || threatLength < 0.001f)
            return false;

        var dot = (normalX * towardThreatX + normalZ * towardThreatZ) / (flatLength * threatLength);
        return dot <= -0.35f;
    }

    /// <summary>
    /// Ground that stands at least a crouch higher on the threat side of a point is a
    /// bank, trench wall or crater lip in front of it.
    /// </summary>
    internal static bool IsShelteringStep(float groundNearerThreat, float groundAtCandidate)
        => float.IsFinite(groundNearerThreat) && float.IsFinite(groundAtCandidate) &&
           groundNearerThreat - groundAtCandidate >= MinimumShelterStepMeters;

    /// <summary>
    /// A step only shelters along the threat axis; a radial running across it measures
    /// banks that protect against nobody.
    /// </summary>
    internal static bool RadialRunsAlongThreatAxis(
        float directionX, float directionZ, float towardThreatX, float towardThreatZ)
        => MathF.Abs(directionX * towardThreatX + directionZ * towardThreatZ) >= 0.3f;

    internal static float StepQuality(float stepMeters)
        => float.IsFinite(stepMeters) ? Math.Clamp(stepMeters / 1.4f, 0.5f, 1f) : 0f;

    /// <summary>
    /// Nearest first, with a better barrier worth walking a few metres further for, and
    /// never two shortlisted points on the same bit of wall.
    /// </summary>
    internal static void SelectShortlist(
        List<FieldCoverCandidate> candidates,
        List<FieldCoverCandidate> shortlist)
    {
        shortlist.Clear();
        candidates.Sort(static (left, right) =>
        {
            var rank = Rank(left).CompareTo(Rank(right));
            if (rank != 0)
                return rank;
            rank = left.X.CompareTo(right.X);
            return rank != 0 ? rank : left.Z.CompareTo(right.Z);
        });

        foreach (var candidate in candidates)
        {
            if (shortlist.Count >= MaximumDetailedCandidates)
                return;
            if (!float.IsFinite(candidate.DistanceMeters) ||
                candidate.DistanceMeters < MinimumDistanceMeters ||
                candidate.DistanceMeters > SearchRadiusMeters)
            {
                continue;
            }

            var separated = true;
            foreach (var chosen in shortlist)
            {
                var dx = chosen.X - candidate.X;
                var dz = chosen.Z - candidate.Z;
                if (dx * dx + dz * dz < CandidateSeparationMeters * CandidateSeparationMeters)
                {
                    separated = false;
                    break;
                }
            }

            if (separated)
                shortlist.Add(candidate);
        }
    }

    private static float Rank(FieldCoverCandidate candidate)
        => candidate.DistanceMeters - Math.Clamp(candidate.Quality, 0f, 1f) * 6f;
}
