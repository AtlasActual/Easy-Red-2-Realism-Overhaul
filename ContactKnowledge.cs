using UnityEngine;

namespace ER2RealismOverhaul;

internal enum ContactKind
{
    Infantry,
    GroundVehicle,
    Aircraft
}

internal enum ContactDeliveryKind
{
    Direct,
    Voice,
    Radio
}

internal sealed class ContactReport
{
    internal Vector3 LastKnownPosition;
    internal ContactKind Kind;
    internal ContactDeliveryKind DeliveryKind;
    internal float ObservedAt;
    internal float ReceivedAt;
    internal float Confidence;
    internal float InitialConfidence;
    internal float CommunicationDistance;
    internal int SourceSoldierId;
    internal int SourceSquadId;
    internal int TargetId;
    internal int TargetSquadId;
    internal string TargetFaction = string.Empty;

    internal ContactReport Copy()
    {
        return new ContactReport
        {
            LastKnownPosition = LastKnownPosition,
            Kind = Kind,
            DeliveryKind = DeliveryKind,
            ObservedAt = ObservedAt,
            ReceivedAt = ReceivedAt,
            Confidence = Confidence,
            InitialConfidence = InitialConfidence,
            CommunicationDistance = CommunicationDistance,
            SourceSoldierId = SourceSoldierId,
            SourceSquadId = SourceSquadId,
            TargetId = TargetId,
            TargetSquadId = TargetSquadId,
            TargetFaction = TargetFaction
        };
    }
}

internal static class ContactKnowledge
{
    private const float ObservationCadenceSeconds = 0.35f;
    private const float DeliveryUpdateCadenceSeconds = 0.1f;
    private static readonly Dictionary<int, Dictionary<int, ContactReport>> ReportsBySquad = new();
    private static readonly Dictionary<int, SquadEndpoint> KnownSquads = new();
    private static readonly Dictionary<PendingDeliveryKey, PendingDelivery> PendingDeliveries = new();
    private static readonly Dictionary<ObservationKey, float> LastObservationAt = new();
    private static float _nextMaintenanceAt;
    private static float _nextDeliveryUpdateAt;

    internal static void ResetBattle()
    {
        ReportsBySquad.Clear();
        KnownSquads.Clear();
        PendingDeliveries.Clear();
        LastObservationAt.Clear();
        _nextMaintenanceAt = 0f;
        _nextDeliveryUpdateAt = 0f;
    }

    internal static void RegisterSquad(Soldier soldier, float now)
    {
        var squad = soldier.joinedSquad;
        if (squad == null)
            return;

        var squadId = GetSquadId(squad);
        var isNew = !KnownSquads.TryGetValue(squadId, out var endpoint);
        if (isNew)
        {
            endpoint = new SquadEndpoint();
            KnownSquads[squadId] = endpoint;
        }

        endpoint!.Squad = squad;
        endpoint.Faction = soldier.faction ?? string.Empty;
        endpoint.LastSeenAt = now;

        if (isNew && Settings.InterSquadContactSharingEnabled.Value)
            ScheduleExistingDirectReportsFor(endpoint, squadId, now);
    }

    internal static void Observe(Soldier observer, Spottable target, float now)
    {
        if (!Settings.ContactReportingEnabled.Value && !Settings.CommanderEnabled.Value)
            return;

        RegisterSquad(observer, now);

        var targetSoldier = target.TryCast<Soldier>();
        var targetVehicle = targetSoldier == null ? target.TryCast<Vehicle>() : null;
        var targetSquad = targetSoldier?.joinedSquad;
        var targetSquadId = targetSquad != null ? GetSquadId(targetSquad) : 0;
        var targetFaction = targetSoldier?.faction ?? targetVehicle?.GetVehicleFaction() ?? string.Empty;
        var targetId = targetSoldier != null
            ? targetSoldier.GetInstanceID()
            : targetVehicle != null
                ? targetVehicle.GetInstanceID()
                : 0;
        if (targetId == 0)
            return;

        if (targetSoldier != null)
            RegisterSquad(targetSoldier, now);

        var kind = target.IsAirVehicle()
            ? ContactKind.Aircraft
            : target.IsWheeledVehicleOrTank()
                ? ContactKind.GroundVehicle
                : ContactKind.Infantry;

        Observe(observer, targetId, targetSquadId, targetFaction,
            target.GetCenterOfUnit(), kind, now);
    }

    private static void Observe(
        Soldier observer,
        int targetId,
        int targetSquadId,
        string targetFaction,
        Vector3 position,
        ContactKind kind,
        float now)
    {
        var soldierId = observer.GetInstanceID();
        var sourceSquadId = GetSquadId(observer);
        var reportTargetId = targetSquadId != 0 ? targetSquadId : targetId;
        var observationKey = new ObservationKey(sourceSquadId, reportTargetId);
        if (LastObservationAt.TryGetValue(observationKey, out var lastObservedAt) &&
            now - lastObservedAt < ObservationCadenceSeconds)
        {
            return;
        }

        LastObservationAt[observationKey] = now;
        var suppression = Settings.SuppressionAwarenessEnabled.Value
            ? Mathf.Clamp01(observer.GetSuppressionValue() / 255f)
            : 0f;
        var confidence = Mathf.Lerp(1f, Settings.SuppressedReportConfidence.Value, suppression);
        var report = new ContactReport
        {
            LastKnownPosition = position,
            Kind = kind,
            DeliveryKind = ContactDeliveryKind.Direct,
            ObservedAt = now,
            ReceivedAt = now,
            InitialConfidence = confidence,
            Confidence = confidence,
            SourceSoldierId = soldierId,
            SourceSquadId = sourceSquadId,
            TargetId = targetId,
            TargetSquadId = targetSquadId,
            TargetFaction = targetFaction
        };

        StoreReport(sourceSquadId, report);
        if (Settings.InterSquadContactSharingEnabled.Value)
            ScheduleFromSource(report, now);
    }

    internal static void Update(float now)
    {
        if (!Settings.ContactReportingEnabled.Value && !Settings.CommanderEnabled.Value)
            return;

        if (now < _nextDeliveryUpdateAt)
            return;
        _nextDeliveryUpdateAt = now + DeliveryUpdateCadenceSeconds;

        if (PendingDeliveries.Count > 0)
        {
            List<PendingDeliveryKey>? delivered = null;
            foreach (var pair in PendingDeliveries)
            {
                if (now < pair.Value.DeliverAt)
                    continue;

                var pending = pair.Value;
                if (now - pending.Report.ObservedAt <= Settings.ContactReportLifetimeSeconds.Value)
                {
                    pending.Report.ReceivedAt = now;
                    StoreReport(pending.RecipientSquadId, pending.Report);
                    AiState.Trace($"Contact {pending.Report.DeliveryKind.ToString().ToLowerInvariant()}: " +
                                  $"squad {pending.Report.SourceSquadId} -> {pending.RecipientSquadId} " +
                                  $"after {now - pending.Report.ObservedAt:0.0}s");
                }

                (delivered ??= new List<PendingDeliveryKey>()).Add(pair.Key);
            }

            if (delivered != null)
            {
                foreach (var key in delivered)
                    PendingDeliveries.Remove(key);
            }
        }

        if (now < _nextMaintenanceAt)
            return;

        _nextMaintenanceAt = now + 2f;
        RemoveExpiredReports(now);
        RemoveStaleSquads(now);
    }

    internal static void CollectKnownSquads(float now, List<Squad> destination)
    {
        destination.Clear();
        foreach (var endpoint in KnownSquads.Values)
        {
            if (endpoint.Squad == null || now - endpoint.LastSeenAt > 30f)
                continue;

            destination.Add(endpoint.Squad);
        }
    }

    internal static void AppendRecentGroundThreatPositions(
        Soldier soldier,
        float now,
        List<Vector3> destination,
        int maximumTotal)
    {
        if (destination.Count >= maximumTotal ||
            !ReportsBySquad.TryGetValue(GetSquadId(soldier), out var reports))
        {
            return;
        }

        var origin = soldier.transform.position;
        var freshness = Mathf.Min(15f, Settings.ContactReportLifetimeSeconds.Value);
        foreach (var report in reports.Values
                     .Where(report => report.Kind != ContactKind.Aircraft &&
                                      now - report.ObservedAt >= 0f &&
                                      now - report.ObservedAt <= freshness &&
                                      report.Confidence >= 0.2f &&
                                      IsKnownEnemyFaction(soldier.faction, report.TargetFaction))
                     .OrderBy(report =>
                         (report.LastKnownPosition - origin).sqrMagnitude))
        {
            var duplicate = false;
            for (var i = 0; i < destination.Count; i++)
            {
                if ((destination[i] - report.LastKnownPosition).sqrMagnitude <= 16f)
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
                destination.Add(report.LastKnownPosition);
            if (destination.Count >= maximumTotal)
                break;
        }
    }

    internal static bool TryGetImminentTransportThreat(
        Soldier soldier,
        Vector3 transportPosition,
        float now,
        out ContactKind kind,
        out float distance)
    {
        kind = ContactKind.Infantry;
        distance = float.MaxValue;
        if (!ReportsBySquad.TryGetValue(GetSquadId(soldier), out var reports))
            return false;

        var found = false;
        foreach (var report in reports.Values)
        {
            if (report.Kind == ContactKind.Aircraft ||
                !RefreshConfidence(report, now) ||
                !IsKnownEnemyFaction(soldier.faction, report.TargetFaction))
            {
                continue;
            }

            var candidateDistance = Vector3.Distance(
                transportPosition, report.LastKnownPosition);
            var threatType = report.Kind == ContactKind.GroundVehicle
                ? TransportThreatType.GroundVehicle
                : TransportThreatType.Infantry;
            if (!TransportDismountDecisionCore.IsImminent(new TransportThreatInput(
                    threatType,
                    candidateDistance,
                    now - report.ObservedAt,
                    report.Confidence,
                    DirectlyVisible: false)) ||
                candidateDistance >= distance)
            {
                continue;
            }

            found = true;
            kind = report.Kind;
            distance = candidateDistance;
        }

        return found;
    }

    private static bool IsKnownEnemyFaction(string ownFaction, string targetFaction)
    {
        if (string.IsNullOrEmpty(ownFaction) || string.IsNullOrEmpty(targetFaction))
            return false;

        try
        {
            return ResourcesManager.IsEnemyFaction(ownFaction, targetFaction);
        }
        catch
        {
            return false;
        }
    }

    internal static void CollectCommanderReports(
        BattleData battle,
        bool invaderSide,
        float now,
        List<ContactReport> destination)
    {
        destination.Clear();
        var newestByTarget = new Dictionary<int, ContactReport>();

        foreach (var squadPair in ReportsBySquad)
        {
            if (!KnownSquads.TryGetValue(squadPair.Key, out var endpoint) ||
                endpoint.Squad == null ||
                now - endpoint.LastSeenAt > 30f)
            {
                continue;
            }

            var belongsToSide = invaderSide
                ? battle.IsInvaderFaction(endpoint.Faction)
                : battle.IsDefenderFaction(endpoint.Faction);
            if (!belongsToSide)
                continue;

            foreach (var report in squadPair.Value.Values)
            {
                if (!RefreshConfidence(report, now))
                    continue;

                var targetBelongsToOpposingSide = invaderSide
                    ? battle.IsDefenderFaction(report.TargetFaction)
                    : battle.IsInvaderFaction(report.TargetFaction);
                if (!targetBelongsToOpposingSide)
                    continue;

                var targetKey = report.TargetSquadId != 0 ? report.TargetSquadId : report.TargetId;
                if (!newestByTarget.TryGetValue(targetKey, out var existing) ||
                    report.ObservedAt > existing.ObservedAt ||
                    report.ObservedAt == existing.ObservedAt && report.Confidence > existing.Confidence)
                {
                    newestByTarget[targetKey] = report.Copy();
                }
            }
        }

        destination.AddRange(newestByTarget.Values.OrderBy(report =>
            report.TargetSquadId != 0 ? report.TargetSquadId : report.TargetId));
    }

    internal static void RemoveSoldier(Soldier soldier)
    {
        var soldierId = soldier.GetInstanceID();
        List<(int SquadId, int TargetId)>? removals = null;
        foreach (var squadPair in ReportsBySquad)
        {
            foreach (var reportPair in squadPair.Value)
            {
                if (reportPair.Value.SourceSoldierId == soldierId &&
                    reportPair.Value.DeliveryKind == ContactDeliveryKind.Direct)
                {
                    (removals ??= new List<(int, int)>()).Add((squadPair.Key, reportPair.Key));
                }
            }
        }

        if (removals == null)
            return;

        foreach (var removal in removals)
            ReportsBySquad[removal.SquadId].Remove(removal.TargetId);
    }

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

    private static void StoreReport(int recipientSquadId, ContactReport report)
    {
        if (!ReportsBySquad.TryGetValue(recipientSquadId, out var reports))
        {
            reports = new Dictionary<int, ContactReport>();
            ReportsBySquad[recipientSquadId] = reports;
        }

        var key = report.TargetSquadId != 0 ? report.TargetSquadId : report.TargetId;
        if (!reports.TryGetValue(key, out var existing) ||
            report.ObservedAt >= existing.ObservedAt ||
            report.DeliveryKind < existing.DeliveryKind)
        {
            reports[key] = report;
        }
    }

    private static void ScheduleFromSource(ContactReport report, float now)
    {
        if (!KnownSquads.TryGetValue(report.SourceSquadId, out var source))
            return;

        foreach (var pair in KnownSquads)
        {
            if (pair.Key == report.SourceSquadId)
                continue;
            ScheduleDelivery(source, pair.Key, pair.Value, report, now);
        }
    }

    private static void ScheduleExistingDirectReportsFor(SquadEndpoint recipient, int recipientId, float now)
    {
        foreach (var sourcePair in ReportsBySquad)
        {
            if (sourcePair.Key == recipientId || !KnownSquads.TryGetValue(sourcePair.Key, out var source))
                continue;

            foreach (var report in sourcePair.Value.Values)
            {
                if (report.DeliveryKind == ContactDeliveryKind.Direct &&
                    report.SourceSquadId == sourcePair.Key)
                {
                    ScheduleDelivery(source, recipientId, recipient, report, now);
                }
            }
        }
    }

    private static void ScheduleDelivery(
        SquadEndpoint source,
        int recipientId,
        SquadEndpoint recipient,
        ContactReport sourceReport,
        float now)
    {
        if (!string.Equals(source.Faction, recipient.Faction, StringComparison.OrdinalIgnoreCase) ||
            source.Squad == null || recipient.Squad == null)
        {
            return;
        }

        var sourceLeader = source.Squad.Leader;
        var recipientLeader = recipient.Squad.Leader;
        if (sourceLeader == null || recipientLeader == null)
            return;

        var distance = Vector3.Distance(sourceLeader.transform.position, recipientLeader.transform.position);
        ContactDeliveryKind deliveryKind;
        float deliverAt;
        float confidenceMultiplier;
        var deliveredPosition = sourceReport.LastKnownPosition;

        if (distance <= Settings.NearbyVoiceRangeMeters.Value)
        {
            deliveryKind = ContactDeliveryKind.Voice;
            deliverAt = now + Settings.NearbyVoiceDelaySeconds.Value + distance / 100f;
            confidenceMultiplier = Settings.NearbyVoiceConfidenceMultiplier.Value;
        }
        else
        {
            if (distance > Settings.DistantRadioMaximumRangeMeters.Value ||
                !CanUseRadio(source.Squad) || !CanUseRadio(recipient.Squad))
            {
                return;
            }

            deliveryKind = ContactDeliveryKind.Radio;
            deliverAt = now + Settings.DistantRadioDelaySeconds.Value + distance / 300f;
            confidenceMultiplier = Settings.DistantRadioConfidenceMultiplier.Value;
            deliveredPosition = AddRadioPositionError(
                deliveredPosition,
                sourceReport.SourceSquadId,
                recipientId,
                sourceReport.TargetId,
                sourceReport.ObservedAt);
        }

        var deliveredReport = sourceReport.Copy();
        deliveredReport.LastKnownPosition = deliveredPosition;
        deliveredReport.DeliveryKind = deliveryKind;
        deliveredReport.CommunicationDistance = distance;
        deliveredReport.InitialConfidence *= confidenceMultiplier;
        deliveredReport.Confidence = deliveredReport.InitialConfidence;

        var key = new PendingDeliveryKey(sourceReport.SourceSquadId, recipientId,
            sourceReport.TargetSquadId != 0 ? sourceReport.TargetSquadId : sourceReport.TargetId);
        if (PendingDeliveries.TryGetValue(key, out var existing))
        {
            existing.Report = deliveredReport;
            existing.DeliverAt = Mathf.Min(existing.DeliverAt, deliverAt);
            return;
        }

        PendingDeliveries[key] = new PendingDelivery
        {
            RecipientSquadId = recipientId,
            DeliverAt = deliverAt,
            Report = deliveredReport
        };
    }

    private static bool CanUseRadio(Squad squad)
    {
        try
        {
            if (squad.HasRadioman())
                return true;
            var leader = squad.Leader;
            return leader != null && leader.HasRadio();
        }
        catch
        {
            return false;
        }
    }

    private static Vector3 AddRadioPositionError(
        Vector3 position,
        int sourceSquadId,
        int recipientSquadId,
        int targetId,
        float observedAt)
    {
        unchecked
        {
            var hash = sourceSquadId;
            hash = hash * 397 ^ recipientSquadId;
            hash = hash * 397 ^ targetId;
            hash = hash * 397 ^ Mathf.RoundToInt(observedAt * 2f);
            var positive = hash & 0x7fffffff;
            var angle = positive % 360 * Mathf.Deg2Rad;
            var radiusFraction = 0.35f + ((positive / 360) % 650) / 1000f;
            var radius = Settings.DistantRadioPositionErrorMeters.Value * radiusFraction;
            position.x += Mathf.Cos(angle) * radius;
            position.z += Mathf.Sin(angle) * radius;
            return position;
        }
    }

    private static bool RefreshConfidence(ContactReport report, float now)
    {
        var age = now - report.ObservedAt;
        var lifetime = Settings.ContactReportLifetimeSeconds.Value;
        if (age < 0f || age > lifetime)
            return false;

        report.Confidence = report.InitialConfidence *
                            Mathf.Clamp01(1f - age / Mathf.Max(0.1f, lifetime));
        return report.Confidence > 0f;
    }

    private static void RemoveExpiredReports(float now)
    {
        List<int>? emptySquads = null;
        foreach (var squadPair in ReportsBySquad)
        {
            List<int>? expired = null;
            foreach (var reportPair in squadPair.Value)
            {
                if (!RefreshConfidence(reportPair.Value, now))
                    (expired ??= new List<int>()).Add(reportPair.Key);
            }

            if (expired != null)
            {
                foreach (var targetId in expired)
                    squadPair.Value.Remove(targetId);
            }

            if (squadPair.Value.Count == 0)
                (emptySquads ??= new List<int>()).Add(squadPair.Key);
        }

        if (emptySquads != null)
        {
            foreach (var squadId in emptySquads)
                ReportsBySquad.Remove(squadId);
        }
    }

    private static void RemoveStaleSquads(float now)
    {
        List<int>? stale = null;
        foreach (var pair in KnownSquads)
        {
            if (now - pair.Value.LastSeenAt > 60f)
                (stale ??= new List<int>()).Add(pair.Key);
        }

        if (stale == null)
            return;

        foreach (var squadId in stale)
            KnownSquads.Remove(squadId);
    }

    private sealed class SquadEndpoint
    {
        internal Squad? Squad;
        internal string Faction = string.Empty;
        internal float LastSeenAt;
    }

    private sealed class PendingDelivery
    {
        internal int RecipientSquadId;
        internal float DeliverAt;
        internal ContactReport Report = null!;
    }

    private readonly record struct PendingDeliveryKey(int SourceSquadId, int RecipientSquadId, int TargetId);
    private readonly record struct ObservationKey(int SourceSquadId, int TargetId);
}
