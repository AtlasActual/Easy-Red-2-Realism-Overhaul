using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Unity adapter for <see cref="FieldCoverProbeCore"/>: finds protective ground where no
/// authored cover slot exists. Two cheap probes propose positions - a horizontal fan
/// for wall and bank faces turned away from the threat, and ground-height radials for
/// trench floors, ditches and crater lips - and the ordinary ballistic cover evaluation
/// then accepts or rejects the best few exactly as it would an authored slot.
/// </summary>
internal static partial class ContactResponse
{
    private const float FieldFaceLowHeight = 0.45f;
    private const float FieldFaceHighHeight = 1.15f;
    // Below an ordinary ceiling: a probe started above it lands on the floor upstairs.
    private const float FieldGroundProbeRise = 2.2f;
    private const float FieldGroundProbeDepth = 6f;
    private const int FieldStepRadials = 12;
    private const int FieldStepSamples = 16;

    // Relative to the threat bearing: a sheltering face can only be seen by looking
    // roughly toward the enemy it shelters from.
    private static readonly float[] FieldFaceAngles = { 0f, -25f, 25f, -50f, 50f, -75f, 75f, -100f, 100f };
    private static readonly List<FieldCoverCandidate> FieldCandidates = new();
    private static readonly List<FieldCoverCandidate> FieldShortlist = new();
    private static readonly RaycastHit[] FieldRayHits = new RaycastHit[12];
    private static readonly float[] FieldRadialGround = new float[FieldStepSamples + 1];
    private static readonly Vector3[] FieldRadialPoints = new Vector3[FieldStepSamples + 1];

    private static AiDestination? FindFieldCover(
        Soldier soldier,
        Vector3 position,
        Vector3 targetPosition,
        Vector3 towardThreat,
        ContactResponseState state,
        float now,
        CoverSelectionMode selectionMode,
        IReadOnlyList<Vector3> knownThreats,
        bool evaluateFiringQuality,
        bool enforceDefensiveArea,
        Vector3 defensiveCenter,
        float defensiveRadius,
        bool enforceAttackProgress,
        Vector3 attackWaypoint,
        out CoverPositionQuality quality)
    {
        quality = default;
        if (!Settings.ImprovisedCoverEnabled.Value || now < state.NextFieldCoverProbeAt)
            return null;

        var soldierId = soldier.GetInstanceID();
        FieldCandidates.Clear();
        try
        {
            CollectShelteringFaces(position, towardThreat);
            CollectShelteringSteps(position, towardThreat);
        }
        catch (NullReferenceException) { }
        catch (Il2CppException) { }
        catch (ObjectCollectedException) { }

        for (var index = FieldCandidates.Count - 1; index >= 0; index--)
        {
            var candidate = FieldCandidates[index];
            var point = new Vector3(candidate.X, candidate.Y, candidate.Z);
            var failedBefore = now < state.FailedFieldCoverUntil &&
                               HorizontalDistanceSqr(point, state.FailedFieldCoverPosition) <=
                               FieldCoverProbeCore.FailedPositionRadiusMeters *
                               FieldCoverProbeCore.FailedPositionRadiusMeters;
            if (failedBefore ||
                enforceDefensiveArea && !DefensivePositioningCore.IsInsideArea(
                    new MapPoint(point.x, point.z),
                    new MapPoint(defensiveCenter.x, defensiveCenter.z),
                    defensiveRadius) ||
                enforceAttackProgress && !AttackCoverCorridorCore.Accepts(
                    new MapPoint(position.x, position.z),
                    new MapPoint(point.x, point.z),
                    new MapPoint(attackWaypoint.x, attackWaypoint.z)) ||
                !CoverRespectsDefensiveOrder(soldier, point) ||
                AiState.CoverReservedByOther(
                    IntPtr.Zero, point, soldierId, now, InfantryCoverPolicy.OccupancyRadiusMeters))
            {
                FieldCandidates.RemoveAt(index);
            }
        }

        FieldCoverProbeCore.SelectShortlist(FieldCandidates, FieldShortlist);

        var bestScore = float.MaxValue;
        var bestPosition = default(Vector3);
        var bestChoice = CoverPostureChoice.Crouched;
        var found = false;
        var evaluated = 0;
        foreach (var candidate in FieldShortlist)
        {
            // The authored pass may already have spent this frame's geometry allowance.
            // One evaluation is always taken so a map without cover props cannot starve.
            if (evaluated > 0 && SearchGeometryBudgetSpent())
                break;

            var coverPosition = new Vector3(candidate.X, candidate.Y, candidate.Z);
            if (CoverOccupancy.IsOccupiedByOther(coverPosition, soldier))
                continue;

            var geometry = EvaluateCoverGeometry(coverPosition, targetPosition, evaluateFiringQuality);
            evaluated++;
            // Nobody vouched for this spot, so only a measured barrier counts.
            if (!geometry.IsProtective || !geometry.ClassificationSucceeded)
                continue;

            var unprotectedSecondaryThreats = 0;
            for (var threatIndex = 1; threatIndex < knownThreats.Count; threatIndex++)
            {
                if (!InfantryCoverDecisionCore.HasMeaningfulProtection(
                        EvaluateCoverProtection(coverPosition, knownThreats[threatIndex], geometry.Choice)))
                {
                    unprotectedSecondaryThreats++;
                }
            }

            var selectedPosture = geometry.Selected;
            var standingCanFire = !evaluateFiringQuality ||
                                  geometry.Standing.CanFire &&
                                  (InfantryCoverDecisionCore.HasMeaningfulProtection(geometry.Standing) ||
                                   InfantryCoverDecisionCore.HasMeaningfulProtection(geometry.Crouched));
            var protectionPosture = geometry.Choice == CoverPostureChoice.Standing &&
                                    InfantryCoverDecisionCore.HasMeaningfulProtection(geometry.Crouched)
                ? geometry.Crouched
                : selectedPosture;
            var protectionFraction = InfantryCoverDecisionCore.ProtectionFraction(protectionPosture);
            var exposedRouteFraction = MeasureExposedRouteFraction(position, coverPosition, knownThreats);
            var distanceSqr = (coverPosition - position).sqrMagnitude;
            var score = InfantryCoverDecisionCore.Score(
                selectionMode,
                new CoverScoreInput(
                    distanceSqr,
                    geometry.Choice == CoverPostureChoice.Standing
                        ? InfantryCoverPolicy.StandingCoverPenalty
                        : 0f,
                    PrimaryThreatProtected: true,
                    unprotectedSecondaryThreats,
                    selectedPosture.CanFire,
                    standingCanFire,
                    Mathf.Sqrt(distanceSqr) * exposedRouteFraction,
                    exposedRouteFraction,
                    protectionFraction,
                    PreferProtectionOverFiringLine: selectionMode == CoverSelectionMode.DefensiveOccupation,
                    NearbyReservationCount: AiState.CountNearbyReservations(
                        coverPosition, soldierId, now, InfantryCoverPolicy.CoverDispersionSpacingMeters)));
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestPosition = coverPosition;
            bestChoice = geometry.Choice;
            quality = new CoverPositionQuality(
                true, true, protectionFraction, selectedPosture.CanFire || standingCanFire);
            found = true;
        }

        AiState.Trace(
            $"Improvised cover: soldier {soldierId} probed={FieldCandidates.Count} " +
            $"shortlisted={FieldShortlist.Count} evaluated={evaluated} selected={found}");
        if (!found)
        {
            state.NextFieldCoverProbeAt = now + FieldCoverProbeCore.FailedProbeBackoffSeconds;
            quality = default;
            return null;
        }

        try
        {
            // The same destination type the game builds for a squad member it cannot
            // give a cover slot to; only the winner is ever allocated.
            return new DestinationWithoutCover(bestPosition, ToSoldierPose(bestChoice))
                .TryCast<AiDestination>();
        }
        catch (Il2CppException)
        {
            quality = default;
            return null;
        }
    }

    private static void CollectShelteringFaces(Vector3 position, Vector3 towardThreat)
    {
        foreach (var angle in FieldFaceAngles)
        {
            var direction = Quaternion.AngleAxis(angle, Vector3.up) * towardThreat;
            var low = TryProbeShelteringFace(position, direction, towardThreat, FieldFaceLowHeight, out var lowPoint);
            var high = TryProbeShelteringFace(position, direction, towardThreat, FieldFaceHighHeight, out var highPoint);
            if (!low && !high)
                continue;

            // Both heights on one obstacle is a wall a man can crouch or stand behind;
            // the low ray alone is a kerb, log or sandbag course.
            var sameObstacle = low && high && HorizontalDistanceSqr(lowPoint, highPoint) <= 1f;
            var point = low ? lowPoint : highPoint;
            AddFieldCandidate(position, point, sameObstacle ? 1f : low ? 0.6f : 0.8f);
            if (low && high && !sameObstacle)
                AddFieldCandidate(position, highPoint, 0.8f);
        }
    }

    private static bool TryProbeShelteringFace(
        Vector3 position, Vector3 direction, Vector3 towardThreat, float height, out Vector3 standPoint)
    {
        standPoint = default;
        if (!TryRaycastPastMen(
                position + Vector3.up * height, direction,
                FieldCoverProbeCore.SearchRadiusMeters, out var hit) ||
            !FieldCoverProbeCore.IsShelteringFace(
                hit.normal.x, hit.normal.y, hit.normal.z, towardThreat.x, towardThreat.z))
        {
            return false;
        }

        var outward = hit.normal;
        outward.y = 0f;
        outward.Normalize();
        return TryGroundFieldPoint(
            hit.point + outward * FieldCoverProbeCore.StandOffFromFaceMeters, position.y, out standPoint);
    }

    private static void CollectShelteringSteps(Vector3 position, Vector3 towardThreat)
    {
        for (var radial = 0; radial < FieldStepRadials; radial++)
        {
            var direction = Quaternion.AngleAxis(radial * 360f / FieldStepRadials, Vector3.up) * Vector3.forward;
            if (!FieldCoverProbeCore.RadialRunsAlongThreatAxis(
                    direction.x, direction.z, towardThreat.x, towardThreat.z))
            {
                continue;
            }

            // Heights only while marching: the per-point validity checks are paid for
            // the handful of samples that turn out to sit below a step.
            FieldRadialPoints[0] = position;
            FieldRadialGround[0] = position.y;
            for (var sample = 1; sample <= FieldStepSamples; sample++)
            {
                var probe = position + direction * (sample * FieldCoverProbeCore.StepSampleSpacingMeters);
                FieldRadialGround[sample] = TrySampleGround(probe, position.y, out var ground, out _)
                    ? ground.y
                    : float.NaN;
                FieldRadialPoints[sample] = ground;
            }

            // Along this radial the neighbour nearer the threat is the next sample when
            // walking toward it and the previous one when walking away.
            var nearerThreatOffset = Vector3.Dot(direction, towardThreat) > 0f ? 1 : -1;
            for (var sample = 1; sample <= FieldStepSamples; sample++)
            {
                var neighbour = sample + nearerThreatOffset;
                if (neighbour < 0 || neighbour > FieldStepSamples ||
                    !FieldCoverProbeCore.IsShelteringStep(
                        FieldRadialGround[neighbour], FieldRadialGround[sample]))
                {
                    continue;
                }

                var step = FieldRadialGround[neighbour] - FieldRadialGround[sample];
                if (TryPlaceBelowStep(
                        FieldRadialPoints[sample], FieldRadialPoints[neighbour],
                        position.y, out var standPoint))
                {
                    AddFieldCandidate(position, standPoint, FieldCoverProbeCore.StepQuality(step));
                }
            }
        }
    }

    /// <summary>
    /// The samples bracket the lip somewhere inside one spacing. Two halvings find it to
    /// about a foot, and the man is then placed just clear of it on the low side - close
    /// under a bank is where it actually covers him.
    /// </summary>
    private static bool TryPlaceBelowStep(
        Vector3 low, Vector3 high, float referenceHeight, out Vector3 standPoint)
    {
        standPoint = default;
        // The marching samples skip this check; a "bank" that is the top of a
        // squadmate's helmet or a parked vehicle is ruled out here instead.
        if (!TrySampleGround(high, referenceHeight, out _, out var rim) ||
            BulletPenetration.IsCharacterOrVehicle(rim.collider))
        {
            return false;
        }

        for (var halving = 0; halving < 2; halving++)
        {
            var middle = (low + high) * 0.5f;
            if (TrySampleGround(middle, referenceHeight, out var ground, out _) &&
                FieldCoverProbeCore.IsShelteringStep(high.y, ground.y))
            {
                low = ground;
            }
            else
            {
                high = new Vector3(middle.x, high.y, middle.z);
            }
        }

        var awayFromLip = low - high;
        awayFromLip.y = 0f;
        if (awayFromLip.sqrMagnitude > 0.0001f)
            low += awayFromLip.normalized * 0.35f;
        return TryGroundFieldPoint(low, referenceHeight, out standPoint);
    }

    /// <summary>
    /// Nearest hit that is not a soldier or a vehicle. The probing soldier's own limbs
    /// and rifle, and any squadmate standing in the way, would otherwise end every ray
    /// a step from where it started.
    /// </summary>
    private static bool TryRaycastPastMen(
        Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest = default;
        var hitCount = Physics.RaycastNonAlloc(
            origin, direction, FieldRayHits, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        var found = false;
        for (var attempt = 0; attempt < hitCount && !found; attempt++)
        {
            var nearestIndex = -1;
            for (var index = 0; index < hitCount; index++)
            {
                if (FieldRayHits[index].collider != null &&
                    (nearestIndex < 0 || FieldRayHits[index].distance < FieldRayHits[nearestIndex].distance))
                {
                    nearestIndex = index;
                }
            }

            if (nearestIndex < 0)
                break;

            // Pay for the component walk only on the hit that would actually be used.
            if (BulletPenetration.IsCharacterOrVehicle(FieldRayHits[nearestIndex].collider))
            {
                FieldRayHits[nearestIndex] = default;
                continue;
            }

            nearest = FieldRayHits[nearestIndex];
            found = true;
        }

        return found;
    }

    private static void AddFieldCandidate(Vector3 from, Vector3 point, float candidateQuality)
        => FieldCandidates.Add(new FieldCoverCandidate(
            point.x, point.y, point.z, HorizontalDistance(from, point), candidateQuality));

    /// <summary>
    /// Drops a probe point onto standable ground near the soldier's own level and
    /// rejects one that lands on a man, a vehicle, a steep face or inside a solid.
    /// </summary>
    private static bool TryGroundFieldPoint(Vector3 point, float referenceHeight, out Vector3 grounded)
        => TrySampleGround(point, referenceHeight, out grounded, out var hit) &&
           hit.normal.y >= FieldCoverProbeCore.MinimumStandableNormalY &&
           !BulletPenetration.IsCharacterOrVehicle(hit.collider) &&
           !Physics.CheckSphere(
               grounded + Vector3.up * 0.9f,
               0.25f,
               Physics.DefaultRaycastLayers,
               QueryTriggerInteraction.Ignore);

    // Height only: cheap enough to run along every radial.
    private static bool TrySampleGround(
        Vector3 point, float referenceHeight, out Vector3 ground, out RaycastHit hit)
    {
        ground = default;
        var origin = new Vector3(point.x, referenceHeight + FieldGroundProbeRise, point.z);
        if (!Physics.Raycast(
                origin,
                Vector3.down,
                out hit,
                FieldGroundProbeRise + FieldGroundProbeDepth,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) ||
            hit.collider == null ||
            Mathf.Abs(hit.point.y - referenceHeight) > FieldCoverProbeCore.MaximumHeightDeltaMeters)
        {
            return false;
        }

        ground = hit.point;
        return true;
    }
}
