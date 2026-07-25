using System.Text;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class AiDebugOverlayController : MonoBehaviour
{
    private const float SampleIntervalSeconds = 0.20f;
    private const int MaximumLeaseMarkers = 20;
    private const int MaximumWorldLabels = 12;

    private static readonly Color IdentityColor = new(0.90f, 0.94f, 0.98f);
    private static readonly Color PerceptionColor = new(0.45f, 1.00f, 0.38f);
    private static readonly Color NavigationColor = new(0.18f, 0.78f, 1.00f);
    private static readonly Color CombatColor = new(1.00f, 0.58f, 0.20f);
    private static readonly Color DangerColor = new(1.00f, 0.22f, 0.35f);
    private static readonly Color CommandColor = new(0.74f, 0.45f, 1.00f);
    private static readonly Color VehicleColor = new(0.18f, 0.88f, 0.72f);
    private static readonly Color SupportAccentColor = new(0.38f, 0.62f, 1.00f);
    private static readonly Color EventColor = new(1.00f, 0.76f, 0.24f);

    private static readonly LayerDescriptor[] LayerPresets =
    {
        new(1, AiDebugCategory.Identity, "ACTORS", IdentityColor,
            "cross=AI  stem=facing  label=stable soldier/squad ID"),
        new(2, AiDebugCategory.Perception, "PERCEPTION", PerceptionColor,
            "green=confirmed  amber=memory/acquiring  focus=FOV + candidates"),
        new(3, AiDebugCategory.Navigation, "MOVEMENT", NavigationColor,
            "cyan=actual engine route  pale=cover/hold  red=stalled"),
        new(4, AiDebugCategory.Combat, "FIRE SAFETY", CombatColor,
            "magenta=suppress aim  green=clear lane  red=blocked/veto"),
        new(5, AiDebugCategory.Danger, "DANGER", DangerColor,
            "bar=suppression  red ray=incoming fire  tags=pin/tank/flame"),
        new(6, AiDebugCategory.Command, "COMMAND", CommandColor,
            "violet=lease  line=squad order source to destination"),
        new(7, AiDebugCategory.Vehicle, "VEHICLES", VehicleColor,
            "teal=clear  orange=engaged/retro  stem=heading"),
        new(8, AiDebugCategory.Support | AiDebugCategory.Events, "SUPPORT", SupportAccentColor,
            "support decisions appear in feed"),
        new(9, AiDebugCategory.Events | AiDebugCategory.Performance, "EVENTS + CPU", EventColor,
            "left=recent decisions  right=one-second update cost")
    };

    private readonly List<SoldierDebugSnapshot> _soldiers = new();
    private readonly List<VehicleDebugSnapshot> _vehicles = new();
    private readonly List<CommandLease> _leases = new();
    private readonly List<CoverReservationDebugSnapshot> _coverReservations = new();
    private readonly List<AiDebugEvent> _events = new();
    private readonly List<AiDebugProfileSnapshot> _profiles = new();
    private readonly Dictionary<int, Vector3> _entityPositions = new();
    private readonly Dictionary<int, Vector3> _squadPositions = new();
    private readonly Dictionary<int, string> _entityFactions = new();
    private readonly Dictionary<int, string> _squadFactions = new();
    private readonly Dictionary<string, bool> _factionHostility =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly StringBuilder _builder = new(1024);

    private bool _visible;
    private bool _frozen;
    private bool _initialized;
    private float _nextSampleAt;
    private float _maximumDistance;
    private int _focusedId;
    private AiDebugScope _scope = AiDebugScope.All;
    private AiDebugCategory _categories = AiDebugCategory.Identity;
    private GUIStyle? _headerStyle;
    private GUIStyle? _labelStyle;
    private GUIStyle? _smallStyle;
    private string _lastError = string.Empty;
    private string _referenceFaction = string.Empty;
    private bool _referenceFactionIsCached;

    private void Update()
    {
        try
        {
            if (!_initialized)
            {
                _initialized = true;
                _visible = Settings.AiDebugOverlayStartEnabled.Value;
                _maximumDistance = Settings.AiDebugOverlayMaximumDistance.Value;
                AiDebugTelemetry.CaptureEnabled = _visible;
            }

            if (Input.GetKeyDown(Settings.AiDebugOverlayToggleKey.Value))
            {
                _visible = !_visible;
                _frozen = false;
                _nextSampleAt = 0f;
                AiDebugTelemetry.CaptureEnabled = _visible;
                Plugin.LogSource.LogInfo($"AI visual debug layer {(_visible ? "enabled" : "disabled")}");
            }

            if (!_visible)
                return;

            HandleHotkeys();
            AiDebugTelemetry.CaptureEnabled = !_frozen;
            if (!_frozen && Time.unscaledTime >= _nextSampleAt)
                Sample();
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void OnGUI()
    {
        if (!_visible || Event.current.type != EventType.Repaint)
            return;

        try
        {
            EnsureStyles();
            var priorDepth = GUI.depth;
            GUI.depth = -1000;
            DrawHeader();

            var camera = Camera.main;
            if (camera != null)
            {
                DrawBattlefield(camera);
                DrawFocusedInspector(camera);
            }

            DrawEventFeed();
            DrawPerformance();
            GUI.depth = priorDepth;
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    [HideFromIl2Cpp]
    private void HandleHotkeys()
    {
        if (Input.GetKeyDown(KeyCode.F7))
            _frozen = !_frozen;
        if (Input.GetKeyDown(KeyCode.F6))
        {
            _scope = (AiDebugScope)(((int)_scope + 1) % 3);
            if (_focusedId != 0 && !EntityOrSquadInScope(_focusedId))
                _focusedId = 0;
            Plugin.LogSource.LogInfo(
                $"AI visual debug allegiance set to {_scope} ({ReferenceFactionStatus()})");
        }
        if (Input.GetKeyDown(KeyCode.Backslash))
            FocusNearestToReticle();
        if (Input.GetKeyDown(KeyCode.Backspace))
            _focusedId = 0;
        if (Input.GetKeyDown(KeyCode.LeftBracket))
            CycleFocus(-1);
        if (Input.GetKeyDown(KeyCode.RightBracket))
            CycleFocus(1);
        if (Input.GetKeyDown(KeyCode.Delete))
            AiDebugTelemetry.Clear();
        if (Input.GetKeyDown(KeyCode.Minus))
            _maximumDistance = Mathf.Max(25f, _maximumDistance - 50f);
        if (Input.GetKeyDown(KeyCode.Equals))
            _maximumDistance = Mathf.Min(1500f, _maximumDistance + 50f);

        SelectLayer(KeyCode.Alpha1, LayerPresets[0]);
        SelectLayer(KeyCode.Alpha2, LayerPresets[1]);
        SelectLayer(KeyCode.Alpha3, LayerPresets[2]);
        SelectLayer(KeyCode.Alpha4, LayerPresets[3]);
        SelectLayer(KeyCode.Alpha5, LayerPresets[4]);
        SelectLayer(KeyCode.Alpha6, LayerPresets[5]);
        SelectLayer(KeyCode.Alpha7, LayerPresets[6]);
        SelectLayer(KeyCode.Alpha8, LayerPresets[7]);
        SelectLayer(KeyCode.Alpha9, LayerPresets[8]);
        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            _categories = ShiftHeld()
                ? AiDebugCategory.All
                : AiDebugCategory.Identity;
        }
    }

    [HideFromIl2Cpp]
    private void SelectLayer(KeyCode key, LayerDescriptor layer)
    {
        if (!Input.GetKeyDown(key))
            return;
        if (ShiftHeld())
        {
            if ((_categories & layer.Categories) == layer.Categories)
                _categories &= ~layer.Categories;
            else
                _categories |= layer.Categories;
        }
        else
            _categories = layer.Categories;
    }

    [HideFromIl2Cpp]
    private static bool ShiftHeld()
        => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    [HideFromIl2Cpp]
    private void Sample()
    {
        _nextSampleAt = Time.unscaledTime + SampleIntervalSeconds;
        var camera = Camera.main;
        if (camera == null)
            return;

        _soldiers.Clear();
        _vehicles.Clear();
        _entityPositions.Clear();
        _squadPositions.Clear();
        _entityFactions.Clear();
        _squadFactions.Clear();
        _coverReservations.Clear();
        var origin = camera.transform.position;
        var controlled = Soldier.CurrentControlledSoldierOrNull();
        UpdateReferenceFaction(controlled?.faction);
        var now = Time.time;

        foreach (var ai in UnityEngine.Object.FindObjectsOfType<SoldierAI>())
        {
            try
            {
                if (ai == null)
                    continue;
                var soldier = ai.GetSoldier();
                if (soldier == null || !soldier.IsAlive)
                    continue;
                var position = soldier.GetCenterOfUnit();
                var distance = Vector3.Distance(origin, position);
                if (distance > _maximumDistance)
                    continue;
                _soldiers.Add(CaptureSoldier(ai, soldier, position, distance, now));
            }
            catch
            {
                // Native objects may despawn between FindObjectsOfType and capture.
            }
        }

        _soldiers.Sort((left, right) => left.Distance.CompareTo(right.Distance));
        var actorLimit = Mathf.Max(1, Settings.AiDebugOverlayMaximumActors.Value);
        if (_soldiers.Count > actorLimit)
            _soldiers.RemoveRange(actorLimit, _soldiers.Count - actorLimit);
        foreach (var soldier in _soldiers)
        {
            _entityPositions[soldier.Id] = soldier.Position;
            _entityFactions[soldier.Id] = soldier.Faction;
            if (soldier.SquadId != 0 && !_squadPositions.ContainsKey(soldier.SquadId))
            {
                _squadPositions[soldier.SquadId] = soldier.Position;
                _squadFactions[soldier.SquadId] = soldier.Faction;
            }
        }

        foreach (var reservation in AiState.CoverReservations.Values)
        {
            if (reservation.ExpiresAt > now)
            {
                _coverReservations.Add(new CoverReservationDebugSnapshot(
                    reservation.SoldierId,
                    reservation.Position,
                    reservation.ExpiresAt - now));
            }
        }

        foreach (var ai in UnityEngine.Object.FindObjectsOfType<AIVehicle>())
        {
            try
            {
                if (ai == null)
                    continue;
                var vehicle = ai.veh;
                // AIPlane is an AIVehicle, so planes turn up in this enumeration. The
                // ground-vehicle layer has always excluded them and still should.
                if (vehicle == null || vehicle is VehiclePlane)
                    continue;
                var position = vehicle.GetCenterOfUnit();
                var distance = Vector3.Distance(origin, position);
                var faction = vehicle.GetVehicleFaction() ?? string.Empty;
                if (distance > _maximumDistance)
                    continue;
                var id = vehicle.GetInstanceID();
                var life = vehicle.Maxlife > 0
                    ? Mathf.Clamp01((float)vehicle.life / vehicle.Maxlife)
                    : 1f;
                _vehicles.Add(new VehicleDebugSnapshot(
                    id, faction, position, vehicle.transform.forward, distance,
                    ai.destinationActive, ai.DestinationReached, ai.going_in_retro,
                    ai.retroBehaviour.ToString(), ai.hasEnemy, vehicle.movingDir, life));
                _entityPositions[id] = position;
                _entityFactions[id] = faction;
            }
            catch
            {
                // Despawn race; omit this sample.
            }
        }

        GroundAiDirector.CollectDebugLeases(now, _leases);
        AiDebugTelemetry.CopyEvents(
            Time.unscaledTime,
            Settings.AiDebugOverlayEventHistorySeconds.Value,
            _events);
        AiDebugTelemetry.CopyProfiles(Time.unscaledTime, _profiles);
    }

    [HideFromIl2Cpp]
    private SoldierDebugSnapshot CaptureSoldier(
        SoldierAI ai,
        Soldier soldier,
        Vector3 position,
        float distance,
        float now)
    {
        var id = soldier.GetInstanceID();
        var squadId = soldier.joinedSquad != null
            ? SquadIdentity.GetSquadId(soldier.joinedSquad)
            : 0;
        var order = soldier.joinedSquad != null
            ? soldier.joinedSquad.order.ToString()
            : "none";
        var suppression = Mathf.Clamp01(soldier.GetSuppressionValue() / 255f);
        var memory = AiState.TargetMemory.TryGetValue(id, out var targetMemory)
            ? targetMemory
            : null;
        var contact = AiState.ContactStates.TryGetValue(id, out var contactState)
            ? contactState
            : null;
        var suppressiveFire = AiState.KnownTargetSuppressionStates.TryGetValue(id, out var fireState)
            ? fireState
            : null;
        var resolution = GroundAiDirector.DebugResolution(id);
        var candidates = new List<TargetCandidateDebugSnapshot>();
        if (memory != null)
        {
            foreach (var candidate in memory.Candidates.Values
                         .OrderByDescending(candidate => candidate.LastSeenAt)
                         .Take(8))
            {
                candidates.Add(new TargetCandidateDebugSnapshot(
                    candidate.LastKnownPosition,
                    candidate.ObservedSeconds,
                    Mathf.Max(0f, now - candidate.LastSeenAt)));
            }
        }
        var winners = DescribeResolution(resolution);
        var hasExecutorDestination = false;
        var executorDestination = default(MapPoint);
        var executorDistance = 0f;
        var moveCharacter = false;
        var hasPathRequest = false;
        try
        {
            moveCharacter = ai.moveCharacter;
            hasPathRequest = ai.HasPathRequest;
            hasExecutorDestination = soldier.HasDestinationAssigned &&
                                     !soldier.DestinationReached &&
                                     soldier.DestinationDistance > 1.25f;
            if (hasExecutorDestination)
            {
                var liveDestination = ai.MoveDestination;
                executorDestination = new MapPoint(liveDestination.x, liveDestination.z);
                executorDistance = soldier.DestinationDistance;
            }
        }
        catch
        {
            hasExecutorDestination = false;
            executorDestination = default;
            executorDistance = 0f;
        }
        var movement = MovementDebugProjectionCore.Project(
            resolution, hasExecutorDestination, executorDestination);
        var movementDestination = movement.HasExecutorDestination
            ? new Vector3(movement.ExecutorDestination.X, position.y, movement.ExecutorDestination.Z)
            : Vector3.zero;

        var hasLane = CombatSafety.TryGetDebugFiringLane(
            id, out var laneOrigin, out var laneDirection, out var laneRadius,
            out var laneDistance, out var laneBlocked);
        return new SoldierDebugSnapshot(
            id,
            soldier.faction ?? string.Empty,
            squadId,
            order,
            position,
            soldier.transform.forward,
            distance,
            suppression,
            soldier.m_pose.ToString(),
            soldier.IsOnVehicle(),
            memory?.HasConfirmedTarget ?? false,
            memory?.HasConfirmedLastKnownPosition ?? false,
            memory?.ConfirmedLastKnownPosition ?? Vector3.zero,
            memory?.Candidates.Count ?? 0,
            memory != null && now < memory.IncomingFireUntil,
            memory?.IncomingFirePosition ?? Vector3.zero,
            contact?.ContactResponseActive ?? false,
            contact?.Relocating ?? false,
            contact?.RelocateDestinationPosition ?? Vector3.zero,
            contact?.Pinned ?? false,
            contact?.FireInhibitedByMovement ?? false,
            contact?.FireInhibitedByRange ?? false,
            contact?.FireInhibitedByArmoredTarget ?? false,
            contact?.HasThreatPosition ?? false,
            contact?.LastThreatPosition ?? Vector3.zero,
            contact?.HasDefensiveCoverAnchor ?? false,
            contact?.DefensiveCoverAnchorPosition ?? Vector3.zero,
            contact?.PlayerHoldPositionOwned ?? false,
            contact?.PlayerHoldCenter ?? Vector3.zero,
            contact?.PlayerHoldRadius ?? 0f,
            contact?.MovementWatchActive ?? false,
            contact?.MovementWatchDestination ?? Vector3.zero,
            contact?.MovementStallFailures ?? 0,
            suppressiveFire?.Active ?? false,
            suppressiveFire?.AimPoint ?? Vector3.zero,
            AiState.IsHidingFromTank(id, now),
            AiState.IsFlameEvading(id, now),
            winners,
            movement.Source.ToString(),
            movement.Action,
            movement.Authority,
            movement.Constraint,
            movement.HasExecutorDestination,
            movementDestination,
            executorDistance,
            moveCharacter,
            hasPathRequest,
            hasLane,
            laneOrigin,
            laneDirection,
            laneRadius,
            laneDistance,
            laneBlocked,
            candidates,
            contact?.CoverState.ToString() ?? "none",
            Mathf.Max(0f, (contact?.HoldCoverUntil ?? 0f) - now),
            Mathf.Max(0f, (contact?.EngagementHoldUntil ?? 0f) - now),
            AiState.TankCoverHideUntil.TryGetValue(id, out var tankUntil)
                ? Mathf.Max(0f, tankUntil - now)
                : 0f,
            AiState.FlameEvasionUntil.TryGetValue(id, out var flameUntil)
                ? Mathf.Max(0f, flameUntil - now)
                : 0f,
            contact?.LastMovementOwner ?? MovementOwner.Free,
            contact?.LatchedPoseOwner ?? PoseOwner.None);
    }

    [HideFromIl2Cpp]
    private string DescribeResolution(SoldierTacticalResolution? resolution)
    {
        if (resolution == null || resolution.Winners.Count == 0)
            return "native/no resolution";
        _builder.Clear();
        foreach (var channel in Enum.GetValues<TacticalChannel>())
        {
            if (!resolution.Winners.TryGetValue(channel, out var winner))
                continue;
            if (_builder.Length > 0)
                _builder.Append(" | ");
            _builder.Append(channel).Append(':').Append(winner.Action)
                .Append(" <- ").Append(winner.Source.ToString())
                .Append(" [").Append(winner.Priority).Append(']');
        }
        return _builder.ToString();
    }

    [HideFromIl2Cpp]
    private void UpdateReferenceFaction(string? currentFaction)
    {
        if (IsKnownFaction(currentFaction))
        {
            if (!string.Equals(_referenceFaction, currentFaction,
                    StringComparison.OrdinalIgnoreCase))
                _factionHostility.Clear();
            _referenceFaction = currentFaction!;
            _referenceFactionIsCached = false;
            return;
        }

        _referenceFactionIsCached = IsKnownFaction(_referenceFaction);
    }

    [HideFromIl2Cpp]
    private bool InScope(string candidateFaction)
    {
        var hasReference = IsKnownFaction(_referenceFaction);
        var candidateKnown = IsKnownFaction(candidateFaction);
        var isEnemy = false;
        if (_scope != AiDebugScope.All && hasReference && candidateKnown)
        {
            if (!_factionHostility.TryGetValue(candidateFaction, out isEnemy))
            {
                try
                {
                    isEnemy = ResourcesManager.IsEnemyFaction(
                        _referenceFaction, candidateFaction);
                    _factionHostility[candidateFaction] = isEnemy;
                }
                catch
                {
                    // Do not guess an allegiance when the game's hostility table
                    // is unavailable. A scoped diagnostic must never show both sides.
                    candidateKnown = false;
                }
            }
        }
        return AiDebugAllegianceCore.Includes(
            _scope, hasReference, candidateKnown, isEnemy);
    }

    [HideFromIl2Cpp]
    private static bool IsKnownFaction(string? faction)
        => !string.IsNullOrWhiteSpace(faction) &&
           !string.Equals(faction, Soldier.UnknownFaction,
               StringComparison.OrdinalIgnoreCase);

    [HideFromIl2Cpp]
    private bool EntityOrSquadInScope(int id)
    {
        if (_scope == AiDebugScope.All)
            return true;
        if (_entityFactions.TryGetValue(id, out var entityFaction))
            return InScope(entityFaction);
        return _squadFactions.TryGetValue(id, out var squadFaction) &&
               InScope(squadFaction);
    }

    [HideFromIl2Cpp]
    private string ReferenceFactionStatus()
    {
        if (_scope == AiDebugScope.All)
            return "reference not required";
        if (!IsKnownFaction(_referenceFaction))
            return "NO PLAYER FACTION";
        return _referenceFactionIsCached
            ? $"{_referenceFaction}, cached"
            : _referenceFaction;
    }

    [HideFromIl2Cpp]
    private void DrawHeader()
    {
        var state = _frozen ? "FROZEN" : "LIVE";
        var width = Mathf.Min(Screen.width - 24f, 1240f);
        var rect = new Rect(12f, 10f, width, 98f);
        var accent = PrimaryLayerColor();
        DrawOutlinedPanel(rect, new Color(0.015f, 0.022f, 0.03f, 0.94f), accent);

        SetTextColor(_headerStyle!, accent);
        var visibleActors = _soldiers.Count(soldier => InScope(soldier.Faction));
        GUI.Label(new Rect(rect.x + 9f, rect.y + 5f, rect.width - 18f, 19f),
            $"AI DEBUG  {state}  |  {_scope.ToString().ToUpperInvariant()} [{ReferenceFactionStatus()}]  |  {_maximumDistance:0}m  |  {visibleActors}/{_soldiers.Count} visible actors  |  {DescribeCategories()}",
            _headerStyle);
        SetTextColor(_smallStyle!, IdentityColor);
        GUI.Label(new Rect(rect.x + 9f, rect.y + 24f, rect.width - 18f, 18f),
            $"{Settings.AiDebugOverlayToggleKey.Value} hide   F7 freeze   F6 allegiance   \\ focus nearest   Backspace clear focus   [ ] cycle   -/= range   Del clear",
            _smallStyle);

        var gap = 3f;
        var chipY = rect.y + 45f;
        var chipWidth = (rect.width - 18f - gap * (LayerPresets.Length - 1)) / LayerPresets.Length;
        for (var index = 0; index < LayerPresets.Length; index++)
        {
            var layer = LayerPresets[index];
            var active = (_categories & layer.Categories) == layer.Categories;
            var chip = new Rect(rect.x + 9f + index * (chipWidth + gap), chipY, chipWidth, 21f);
            DrawOutlinedPanel(
                chip,
                active
                    ? new Color(layer.Color.r, layer.Color.g, layer.Color.b, 0.24f)
                    : new Color(0.055f, 0.065f, 0.075f, 0.92f),
                active ? layer.Color : new Color(0.22f, 0.25f, 0.28f));
            SetTextColor(_smallStyle!, active ? layer.Color : new Color(0.50f, 0.54f, 0.58f));
            _smallStyle!.alignment = TextAnchor.MiddleCenter;
            GUI.Label(chip, $"{layer.Key} {layer.Name}", _smallStyle);
        }
        _smallStyle!.alignment = TextAnchor.UpperLeft;

        var activeLayer = SingleActiveLayer();
        SetTextColor(_smallStyle, activeLayer?.Color ?? IdentityColor);
        var legend = activeLayer.HasValue
            ? $"SEEING: {activeLayer.Value.Name}  |  {activeLayer.Value.Legend}"
            : "SEEING: MULTI-LAYER  |  press 1-9 for one clear view; hold Shift + 1-9 to combine; 0=Actors; Shift+0=everything";
        GUI.Label(new Rect(rect.x + 9f, rect.y + 70f, rect.width - 18f, 20f), legend, _smallStyle);
    }

    [HideFromIl2Cpp]
    private string DescribeCategories()
    {
        if (_categories == AiDebugCategory.All)
            return "ALL";
        if (_categories == AiDebugCategory.None)
            return "NONE";
        _builder.Clear();
        foreach (var pair in new[]
                 {
                     (AiDebugCategory.Identity, "ID"), (AiDebugCategory.Perception, "PER"),
                     (AiDebugCategory.Navigation, "NAV"), (AiDebugCategory.Combat, "CBT"),
                     (AiDebugCategory.Danger, "DNG"), (AiDebugCategory.Command, "CMD"),
                     (AiDebugCategory.Vehicle, "VEH"),
                     (AiDebugCategory.Support, "SUP"), (AiDebugCategory.Events, "EVT"),
                     (AiDebugCategory.Performance, "CPU")
                 })
        {
            if ((_categories & pair.Item1) == 0)
                continue;
            if (_builder.Length > 0)
                _builder.Append(',');
            _builder.Append(pair.Item2);
        }
        return _builder.ToString();
    }

    [HideFromIl2Cpp]
    private LayerDescriptor? SingleActiveLayer()
    {
        LayerDescriptor? selected = null;
        foreach (var layer in LayerPresets)
        {
            if ((_categories & layer.Categories) != layer.Categories)
                continue;
            if (selected.HasValue)
                return null;
            selected = layer;
        }
        return selected;
    }

    [HideFromIl2Cpp]
    private Color PrimaryLayerColor()
        => SingleActiveLayer()?.Color ?? IdentityColor;

    [HideFromIl2Cpp]
    private void DrawBattlefield(Camera camera)
    {
        const AiDebugCategory infantryLayers = AiDebugCategory.Identity |
                                                AiDebugCategory.Perception |
                                                AiDebugCategory.Navigation |
                                                AiDebugCategory.Combat |
                                                AiDebugCategory.Danger;
        if ((_categories & infantryLayers) != 0)
        {
            var visibleIndex = 0;
            for (var index = 0; index < _soldiers.Count; index++)
            {
                var soldier = _soldiers[index];
                if (!InScope(soldier.Faction))
                    continue;
                DrawSoldier(camera, soldier, visibleIndex++);
            }
        }

        if (Enabled(AiDebugCategory.Vehicle))
        {
            for (var index = 0; index < _vehicles.Count; index++)
            {
                var vehicle = _vehicles[index];
                if (!InScope(vehicle.Faction))
                    continue;
                var color = vehicle.HasEnemy || vehicle.Retro ? CombatColor : VehicleColor;
                DrawWorldCross(camera, vehicle.Position + Vector3.up * 2.2f, 7f, color);
                DrawWorldLine(camera, vehicle.Position, vehicle.Position + vehicle.Forward * 12f, color, 2f);
                if (index < 8)
                {
                    DrawWorldLabel(camera, vehicle.Position + Vector3.up * 3.2f,
                        $"VEH {vehicle.Id} {vehicle.Distance:0}m  HP {vehicle.Life:P0}\n" +
                        $"drive {vehicle.Drive.x:+0.00;-0.00;0.00}/{vehicle.Drive.y:+0.00;-0.00;0.00}  " +
                        $"path {(vehicle.DestinationActive ? (vehicle.DestinationReached ? "reached" : "active") : "none")}" +
                        (vehicle.Retro ? $"  RETRO:{vehicle.RetroMode}" : string.Empty), color);
                }
            }
        }

        if (Enabled(AiDebugCategory.Command))
        {
            var focused = _soldiers.FirstOrDefault(candidate =>
                candidate.Id == _focusedId && InScope(candidate.Faction));
            var leaseCount = 0;
            foreach (var lease in _leases)
            {
                if (!EntityOrSquadInScope(lease.Key.EntityId))
                    continue;
                var relevant = focused == null || lease.Key.EntityId == focused.Id ||
                               lease.Key.EntityId == focused.SquadId;
                if (!relevant || leaseCount++ >= MaximumLeaseMarkers)
                    continue;
                var hasSource = _entityPositions.TryGetValue(lease.Key.EntityId, out var source) ||
                                lease.Key.Channel == CommandChannel.SquadOrders &&
                                _squadPositions.TryGetValue(lease.Key.EntityId, out source);
                var destination = new Vector3(
                    lease.Destination.X,
                    hasSource ? source.y : 0f,
                    lease.Destination.Z);
                if (hasSource)
                    DrawWorldLine(camera, source, destination, CommandColor, 2f);
                DrawWorldCross(camera, destination + Vector3.up * 0.5f, 6f, CommandColor);
                if (focused != null || leaseCount <= 8)
                {
                    DrawWorldLabel(camera, destination + Vector3.up * 1.2f,
                        $"{lease.Key.Channel}:{lease.Key.EntityId}\n{lease.Owner}/{lease.Role} r{lease.ObjectiveRevision} g{lease.Generation}",
                        CommandColor);
                }
            }

        }

        if (Enabled(AiDebugCategory.Navigation))
        {
            var shown = 0;
            foreach (var reservation in _coverReservations)
            {
                if (!EntityOrSquadInScope(reservation.SoldierId))
                    continue;
                if (_focusedId != 0 && reservation.SoldierId != _focusedId)
                    continue;
                if (_focusedId == 0 && shown++ >= 16)
                    break;
                var color = reservation.SoldierId == _focusedId
                    ? Color.white
                    : new Color(NavigationColor.r, NavigationColor.g, NavigationColor.b, 0.7f);
                if (_focusedId != 0)
                    DrawWorldCircle(camera, reservation.Position,
                        InfantryCoverPolicy.OccupancyRadiusMeters, color);
                DrawWorldCross(camera, reservation.Position + Vector3.up * 0.25f, 4f, color);
                if (reservation.SoldierId == _focusedId)
                    DrawWorldLabel(camera, reservation.Position + Vector3.up,
                        $"reserved S{reservation.SoldierId} {reservation.SecondsRemaining:0.0}s", color);
            }
        }
    }

    [HideFromIl2Cpp]
    private void DrawSoldier(Camera camera, SoldierDebugSnapshot soldier, int distanceIndex)
    {
        var focused = soldier.Id == _focusedId;
        var near = distanceIndex < MaximumWorldLabels;
        if (Enabled(AiDebugCategory.Identity))
        {
            var color = focused ? Color.white : IdentityColor;
            DrawWorldCross(camera, soldier.Position + Vector3.up * 1.9f, focused ? 8f : 4f, color);
            if (focused || near)
                DrawWorldLine(camera, soldier.Position,
                    soldier.Position + soldier.Forward * (focused ? 5f : 2f), color, focused ? 2f : 1f);
            var tags = soldier.Mounted ? " MOUNT" : string.Empty;
            if (soldier.Pinned) tags += " PINNED";
            if (soldier.Relocating) tags += " MOVE";
            if (soldier.FireBlocked) tags += " NO-FIRE";
            if (focused || near)
            {
                var label = focused
                    ? $"S{soldier.Id} Q{soldier.SquadId} {soldier.Distance:0}m {soldier.Pose}{tags}"
                    : $"S{soldier.Id} Q{soldier.SquadId}";
                DrawWorldLabel(camera, soldier.Position + Vector3.up * 2.35f, label, color);
            }
        }

        if (Enabled(AiDebugCategory.Danger))
        {
            DrawWorldCross(camera, soldier.Position + Vector3.up * 1.9f, focused ? 8f : 4f,
                soldier.Pinned ? DangerColor : new Color(DangerColor.r, DangerColor.g, DangerColor.b, 0.68f));
            DrawSuppressionBar(camera, soldier, DangerColor);
            if (soldier.HasIncomingFire && (focused || soldier.Pinned))
                DrawWorldLine(camera, soldier.IncomingFirePosition, soldier.Position, DangerColor, 2f);
        }

        if (Enabled(AiDebugCategory.Perception))
        {
            if (soldier.HasTarget || soldier.HasLastKnown)
                DrawWorldDiamond(camera, soldier.Position + Vector3.up * 2f, focused ? 7f : 4f,
                    soldier.HasTarget ? PerceptionColor : EventColor);
            if (focused)
            {
                if (soldier.HasLastKnown)
                    DrawWorldLine(camera, soldier.Position, soldier.LastKnownPosition,
                        soldier.HasTarget ? PerceptionColor : EventColor, 1.5f);
                DrawFov(camera, soldier);
                foreach (var candidate in soldier.Candidates)
                {
                    var candidateColor = candidate.AgeSeconds <= 0.5f
                        ? PerceptionColor
                        : EventColor;
                    DrawWorldLine(camera, soldier.Position, candidate.Position, candidateColor, 1f);
                    DrawWorldDiamond(camera, candidate.Position + Vector3.up * 0.5f, 4f, candidateColor);
                    DrawWorldLabel(camera, candidate.Position + Vector3.up * 1.2f,
                        $"candidate observe={candidate.ObservedSeconds:0.00}s age={candidate.AgeSeconds:0.0}s",
                        candidateColor);
                }
            }
        }

        if (Enabled(AiDebugCategory.Navigation))
        {
            if (soldier.HasMovementDestination && (focused || near))
            {
                DrawWorldLine(camera, soldier.Position, soldier.MovementDestination, NavigationColor,
                    focused ? 3f : 2f);
                DrawWorldDiamond(camera, soldier.MovementDestination + Vector3.up * 0.35f,
                    focused ? 7f : 4f, NavigationColor);
                if (focused)
                    DrawWorldLabel(camera, soldier.MovementDestination + Vector3.up * 1.25f,
                        $"EXECUTOR TARGET {soldier.MovementDestinationDistance:0}m\n" +
                        $"{soldier.MovementOwner} / {soldier.MovementAction}", NavigationColor);
            }
            else if (focused && soldier.Relocating)
            {
                DrawWorldDiamond(camera, soldier.RelocateDestination + Vector3.up * 0.35f, 7f, EventColor);
                DrawWorldLabel(camera, soldier.RelocateDestination + Vector3.up * 1.25f,
                    "PLANNED COVER\nno active engine path", EventColor);
            }
            if (focused && soldier.HasDefensiveAnchor)
            {
                DrawWorldLine(camera, soldier.Position, soldier.DefensiveAnchor, NavigationColor, 1f);
                DrawWorldCircle(camera, soldier.DefensiveAnchor, InfantryCoverPolicy.DefensiveAnchorLeashMeters,
                    new Color(NavigationColor.r, NavigationColor.g, NavigationColor.b, 0.55f));
            }
            if (focused && soldier.HasPlayerHold)
                DrawWorldCircle(camera, soldier.PlayerHoldCenter, soldier.PlayerHoldRadius, Color.white);
            if (focused && soldier.MovementWatchActive &&
                (!soldier.HasMovementDestination ||
                 Vector3.Distance(soldier.MovementWatchDestination, soldier.MovementDestination) > 1f))
                DrawWorldLine(camera, soldier.Position, soldier.MovementWatchDestination,
                    soldier.MovementStallFailures > 0 ? DangerColor : SupportAccentColor, 1f);
        }

        if (Enabled(AiDebugCategory.Combat))
        {
            if (focused && soldier.SuppressingKnownTarget)
                DrawWorldLine(camera, soldier.Position, soldier.SuppressiveAimPoint,
                    new Color(1f, 0.38f, 0.85f), 2f);
            if (soldier.HasFiringLane && (focused || soldier.FiringLaneBlocked))
            {
                var laneColor = soldier.FiringLaneBlocked ? DangerColor : PerceptionColor;
                DrawWorldLine(camera, soldier.FiringLaneOrigin,
                    soldier.FiringLaneOrigin + soldier.FiringLaneDirection * soldier.FiringLaneDistance,
                    laneColor, Mathf.Max(1f, soldier.FiringLaneRadius * 2f));
            }
        }
    }

    [HideFromIl2Cpp]
    private void DrawFocusedInspector(Camera camera)
    {
        var soldier = _soldiers.FirstOrDefault(candidate =>
            candidate.Id == _focusedId && InScope(candidate.Faction));
        if (soldier == null)
            return;

        _builder.Clear();
        _builder.AppendLine($"FOCUS S{soldier.Id} / Q{soldier.SquadId}  |  {DescribeCategories()}");
        if (Enabled(AiDebugCategory.Identity))
            // The pose OWNER next to the pose itself: with LOCOMOTION below, the pose/movement
            // contract (plan 019) is readable at a glance - a moving locomotion owner beside a
            // prone pose is the contradiction this line exists to make visible.
            _builder.AppendLine($"ACTOR  faction={soldier.Faction}  order={soldier.Order}  pose={soldier.Pose}/{soldier.PoseOwner}  mounted={soldier.Mounted}");
        if (Enabled(AiDebugCategory.Perception))
            _builder.AppendLine($"PERCEPTION  confirmed={soldier.HasTarget}  last-known={soldier.HasLastKnown}  candidates={soldier.CandidateCount}  contact={soldier.ContactActive}");
        if (Enabled(AiDebugCategory.Navigation))
        {
            _builder.AppendLine($"MOVEMENT OWNER  {soldier.MovementOwner} / {soldier.MovementAction} / {soldier.MovementAuthority}");
            // LOCOMOTION is the movement arbiter's committed owner (plan 018) - the single
            // answer to "who stopped this soldier?", separate from the proposal source above.
            _builder.AppendLine($"LOCOMOTION  {soldier.LocomotionOwner}");
            _builder.AppendLine($"ENGINE EXECUTOR  target={soldier.HasMovementDestination}  distance={soldier.MovementDestinationDistance:0.0}m  moveCharacter={soldier.MoveCharacter}  pathRequest={soldier.HasPathRequest}");
            _builder.AppendLine($"CONSTRAINT  {(string.IsNullOrWhiteSpace(soldier.MovementConstraint) ? "none" : soldier.MovementConstraint)}");
            _builder.AppendLine($"STATE  relocating={soldier.Relocating}  watch={soldier.MovementWatchActive}  stalls={soldier.MovementStallFailures}  anchor={soldier.HasDefensiveAnchor}  hold={soldier.HasPlayerHold}");
        }
        if (Enabled(AiDebugCategory.Combat))
            _builder.AppendLine($"FIRE SAFETY  movement={soldier.FireInhibitedMovement}  range={soldier.FireInhibitedRange}  armor={soldier.FireInhibitedArmor}  friendly-lane={soldier.FiringLaneBlocked}");
        if (Enabled(AiDebugCategory.Danger))
            _builder.AppendLine($"DANGER  suppression={soldier.Suppression:P0}  pinned={soldier.Pinned}  incoming={soldier.HasIncomingFire}  tank={soldier.TankHiding}  flame={soldier.FlameEvading}");
        if ((_categories & (AiDebugCategory.Command | AiDebugCategory.Events)) != 0)
        {
            _builder.AppendLine("ARBITRATION WINNERS");
            _builder.AppendLine(soldier.Winners);
        }

        var width = Mathf.Min(610f, Screen.width * 0.44f);
        var content = new GUIContent(_builder.ToString());
        var height = Mathf.Clamp(_smallStyle!.CalcHeight(content, width - 16f) + 12f, 54f, 240f);
        var rect = new Rect(Screen.width - width - 12f, 118f, width, height);
        DrawOutlinedPanel(rect, new Color(0.02f, 0.03f, 0.05f, 0.92f), PrimaryLayerColor());
        SetTextColor(_smallStyle, PrimaryLayerColor());
        GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f),
            _builder.ToString(), _smallStyle!);
    }

    [HideFromIl2Cpp]
    private void DrawEventFeed()
    {
        if (!Enabled(AiDebugCategory.Events))
            return;
        var visible = new List<AiDebugEvent>(12);
        for (var index = _events.Count - 1; index >= 0 && visible.Count < 12; index--)
        {
            var item = _events[index];
            if (!Enabled(item.Category) && item.Category != AiDebugCategory.Events)
                continue;
            if (item.EntityId != 0 && !EntityOrSquadInScope(item.EntityId))
                continue;
            if (_focusedId != 0 && item.EntityId != 0 && item.EntityId != _focusedId)
                continue;
            visible.Add(item);
        }
        if (visible.Count == 0)
            return;

        _builder.Clear();
        _builder.AppendLine(_focusedId == 0 ? "TACTICAL EVENTS" : $"EVENTS FOR {_focusedId} (+ global)");
        foreach (var item in visible)
        {
            var age = Mathf.Max(0f, Time.unscaledTime - item.At);
            _builder.Append('[').Append(age.ToString("0.0")).Append("s] ")
                .Append(item.Category).Append("  ").AppendLine(item.Message);
        }
        var height = 24f + visible.Count * 18f;
        var rect = new Rect(12f, Screen.height - height - 12f, Mathf.Min(760f, Screen.width * 0.58f), height);
        DrawOutlinedPanel(rect, new Color(0.02f, 0.03f, 0.04f, 0.90f), EventColor);
        SetTextColor(_smallStyle!, EventColor);
        GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, rect.width - 16f, rect.height - 10f),
            _builder.ToString(), _smallStyle!);
    }

    [HideFromIl2Cpp]
    private void DrawPerformance()
    {
        if (!Enabled(AiDebugCategory.Performance) || _profiles.Count == 0)
            return;
        _builder.Clear();
        _builder.AppendLine("AI UPDATE COST (1s window)");
        foreach (var profile in _profiles)
        {
            _builder.Append(profile.Kind).Append("  calls ").Append(profile.Calls)
                .Append("  total ").Append(profile.TotalMilliseconds.ToString("0.00"))
                .Append("ms  avg ").Append(profile.AverageMilliseconds.ToString("0.000"))
                .Append("ms  max ").Append(profile.MaximumMilliseconds.ToString("0.00"))
                .AppendLine("ms");
        }
        var width = Mathf.Min(430f, Screen.width * 0.38f);
        var rect = new Rect(Screen.width - width - 12f, Screen.height - 110f, width, 98f);
        DrawOutlinedPanel(rect, new Color(0.02f, 0.03f, 0.04f, 0.90f), SupportAccentColor);
        SetTextColor(_smallStyle!, SupportAccentColor);
        GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, rect.width - 16f, rect.height - 10f),
            _builder.ToString(), _smallStyle!);
    }

    [HideFromIl2Cpp]
    private void FocusNearestToReticle()
    {
        var camera = Camera.main;
        if (camera == null || _soldiers.Count == 0)
            return;
        var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var bestDistance = float.MaxValue;
        var bestId = 0;
        foreach (var soldier in _soldiers)
        {
            if (!InScope(soldier.Faction))
                continue;
            var screen = camera.WorldToScreenPoint(soldier.Position + Vector3.up * 1.2f);
            if (screen.z <= 0f)
                continue;
            var point = new Vector2(screen.x, Screen.height - screen.y);
            var distance = (point - center).sqrMagnitude;
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            bestId = soldier.Id;
        }
        _focusedId = bestId;
    }

    [HideFromIl2Cpp]
    private void CycleFocus(int direction)
    {
        var visible = _soldiers.Where(soldier => InScope(soldier.Faction)).ToList();
        if (visible.Count == 0)
        {
            _focusedId = 0;
            return;
        }
        var index = visible.FindIndex(candidate => candidate.Id == _focusedId);
        index = index < 0 ? 0 : (index + direction + visible.Count) % visible.Count;
        _focusedId = visible[index].Id;
    }

    [HideFromIl2Cpp]
    private bool Enabled(AiDebugCategory category) => (_categories & category) != 0;

    [HideFromIl2Cpp]
    private void DrawFov(Camera camera, SoldierDebugSnapshot soldier)
    {
        var halfAngle = Settings.HorizontalFov.Value * 0.5f;
        var forward = soldier.Forward;
        forward.y = 0f;
        forward.Normalize();
        var left = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
        var right = Quaternion.AngleAxis(halfAngle, Vector3.up) * forward;
        DrawWorldLine(camera, soldier.Position, soldier.Position + left * 18f,
            new Color(PerceptionColor.r, PerceptionColor.g, PerceptionColor.b, 0.7f), 1f);
        DrawWorldLine(camera, soldier.Position, soldier.Position + right * 18f,
            new Color(PerceptionColor.r, PerceptionColor.g, PerceptionColor.b, 0.7f), 1f);
    }

    [HideFromIl2Cpp]
    private void DrawSuppressionBar(Camera camera, SoldierDebugSnapshot soldier, Color color)
    {
        var screen = camera.WorldToScreenPoint(soldier.Position + Vector3.up * 2.15f);
        if (screen.z <= 0f)
            return;
        var x = screen.x - 18f;
        var y = Screen.height - screen.y;
        DrawSolidRect(new Rect(x, y, 36f, 3f), new Color(0f, 0f, 0f, 0.8f));
        DrawSolidRect(new Rect(x, y, 36f * soldier.Suppression, 3f),
            soldier.Suppression > 0.65f ? Color.red : color);
    }

    [HideFromIl2Cpp]
    private void DrawWorldCircle(Camera camera, Vector3 center, float radius, Color color)
    {
        if (radius <= 0f)
            return;
        var previous = center + new Vector3(radius, 0.15f, 0f);
        for (var index = 1; index <= 20; index++)
        {
            var angle = index * Mathf.PI * 2f / 20f;
            var next = center + new Vector3(Mathf.Cos(angle) * radius, 0.15f, Mathf.Sin(angle) * radius);
            DrawWorldLine(camera, previous, next, color, 1f);
            previous = next;
        }
    }

    [HideFromIl2Cpp]
    private void DrawWorldLine(Camera camera, Vector3 from, Vector3 to, Color color, float width)
    {
        var start = camera.WorldToScreenPoint(from);
        var end = camera.WorldToScreenPoint(to);
        if (start.z <= 0f || end.z <= 0f)
            return;
        DrawScreenLine(
            new Vector2(start.x, Screen.height - start.y),
            new Vector2(end.x, Screen.height - end.y),
            color, width);
    }

    [HideFromIl2Cpp]
    private void DrawWorldCross(Camera camera, Vector3 position, float radius, Color color)
    {
        var screen = camera.WorldToScreenPoint(position);
        if (screen.z <= 0f)
            return;
        var point = new Vector2(screen.x, Screen.height - screen.y);
        DrawScreenLine(point + Vector2.left * radius, point + Vector2.right * radius, color, 2f);
        DrawScreenLine(point + Vector2.up * radius, point + Vector2.down * radius, color, 2f);
    }

    [HideFromIl2Cpp]
    private void DrawWorldDiamond(Camera camera, Vector3 position, float radius, Color color)
    {
        var screen = camera.WorldToScreenPoint(position);
        if (screen.z <= 0f)
            return;
        var point = new Vector2(screen.x, Screen.height - screen.y);
        var top = point + Vector2.up * radius;
        var right = point + Vector2.right * radius;
        var bottom = point + Vector2.down * radius;
        var left = point + Vector2.left * radius;
        DrawScreenLine(top, right, color, 1.5f);
        DrawScreenLine(right, bottom, color, 1.5f);
        DrawScreenLine(bottom, left, color, 1.5f);
        DrawScreenLine(left, top, color, 1.5f);
    }

    [HideFromIl2Cpp]
    private void DrawWorldLabel(Camera camera, Vector3 position, string text, Color color)
    {
        var screen = camera.WorldToScreenPoint(position);
        if (screen.z <= 0f)
            return;
        var content = new GUIContent(text);
        var size = _labelStyle!.CalcSize(content);
        var rect = new Rect(screen.x - size.x * 0.5f - 3f, Screen.height - screen.y - size.y * 0.5f,
            size.x + 6f, size.y + 2f);
        DrawPanel(rect, new Color(0f, 0f, 0f, 0.62f));
        SetTextColor(_labelStyle, color);
        GUI.Label(rect, text, _labelStyle);
    }

    [HideFromIl2Cpp]
    private static void DrawScreenLine(Vector2 from, Vector2 to, Color color, float width)
    {
        var delta = to - from;
        var length = delta.magnitude;
        if (length < 0.1f)
            return;
        var oldMatrix = GUI.matrix;
        var oldColor = GUI.color;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
        GUI.DrawTexture(new Rect(from.x, from.y - width * 0.5f, length, width), Texture2D.whiteTexture);
        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
    }

    [HideFromIl2Cpp]
    private static void DrawPanel(Rect rect, Color color) => DrawSolidRect(rect, color);

    [HideFromIl2Cpp]
    private static void DrawOutlinedPanel(Rect rect, Color fill, Color border)
    {
        DrawSolidRect(rect, border);
        DrawSolidRect(new Rect(rect.x + 2f, rect.y + 2f,
            Mathf.Max(0f, rect.width - 4f), Mathf.Max(0f, rect.height - 4f)), fill);
    }

    [HideFromIl2Cpp]
    private static void DrawSolidRect(Rect rect, Color color)
    {
        var oldColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = oldColor;
    }

    [HideFromIl2Cpp]
    private void EnsureStyles()
    {
        if (_headerStyle != null)
            return;
        _headerStyle = CloneStyle(GUI.skin.label);
        _headerStyle.fontSize = 13;
        _headerStyle.fontStyle = FontStyle.Bold;
        _headerStyle.alignment = TextAnchor.UpperLeft;
        SetTextColor(_headerStyle, Color.white);

        _labelStyle = CloneStyle(GUI.skin.label);
        _labelStyle.fontSize = 11;
        _labelStyle.fontStyle = FontStyle.Bold;
        _labelStyle.alignment = TextAnchor.MiddleCenter;
        _labelStyle.wordWrap = false;
        SetTextColor(_labelStyle, Color.white);

        _smallStyle = CloneStyle(GUI.skin.label);
        _smallStyle.fontSize = 11;
        _smallStyle.alignment = TextAnchor.UpperLeft;
        _smallStyle.wordWrap = true;
        SetTextColor(_smallStyle, Color.white);
    }

    [HideFromIl2Cpp]
    private static GUIStyle CloneStyle(GUIStyle source)
    {
        var style = new GUIStyle();
        GUIStyle.Internal_Copy(style, source);
        return style;
    }

    [HideFromIl2Cpp]
    private static void SetTextColor(GUIStyle style, Color color)
    {
        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        style.focused.textColor = color;
    }

    [HideFromIl2Cpp]
    private void ReportError(Exception ex)
    {
        var signature = ex.GetType().Name + ":" + ex.Message;
        if (signature == _lastError)
            return;
        _lastError = signature;
        Plugin.LogSource.LogWarning($"AI visual debug layer failed: {signature}");
    }

    private sealed record SoldierDebugSnapshot(
        int Id,
        string Faction,
        int SquadId,
        string Order,
        Vector3 Position,
        Vector3 Forward,
        float Distance,
        float Suppression,
        string Pose,
        bool Mounted,
        bool HasTarget,
        bool HasLastKnown,
        Vector3 LastKnownPosition,
        int CandidateCount,
        bool HasIncomingFire,
        Vector3 IncomingFirePosition,
        bool ContactActive,
        bool Relocating,
        Vector3 RelocateDestination,
        bool Pinned,
        bool FireInhibitedMovement,
        bool FireInhibitedRange,
        bool FireInhibitedArmor,
        bool HasThreat,
        Vector3 ThreatPosition,
        bool HasDefensiveAnchor,
        Vector3 DefensiveAnchor,
        bool HasPlayerHold,
        Vector3 PlayerHoldCenter,
        float PlayerHoldRadius,
        bool MovementWatchActive,
        Vector3 MovementWatchDestination,
        int MovementStallFailures,
        bool SuppressingKnownTarget,
        Vector3 SuppressiveAimPoint,
        bool TankHiding,
        bool FlameEvading,
        string Winners,
        string MovementOwner,
        TacticalAction MovementAction,
        CommandAuthority MovementAuthority,
        string MovementConstraint,
        bool HasMovementDestination,
        Vector3 MovementDestination,
        float MovementDestinationDistance,
        bool MoveCharacter,
        bool HasPathRequest,
        bool HasFiringLane,
        Vector3 FiringLaneOrigin,
        Vector3 FiringLaneDirection,
        float FiringLaneRadius,
        float FiringLaneDistance,
        bool FiringLaneBlocked,
        List<TargetCandidateDebugSnapshot> Candidates,
        string CoverState,
        float CoverHoldSeconds,
        float EngagementHoldSeconds,
        float TankHideSeconds,
        float FlameEvadeSeconds,
        MovementOwner LocomotionOwner,
        PoseOwner PoseOwner)
    {
        internal bool FireBlocked => FireInhibitedMovement || FireInhibitedRange ||
                                     FireInhibitedArmor || FiringLaneBlocked;
    }

    private readonly record struct TargetCandidateDebugSnapshot(
        Vector3 Position,
        float ObservedSeconds,
        float AgeSeconds);

    private readonly record struct CoverReservationDebugSnapshot(
        int SoldierId,
        Vector3 Position,
        float SecondsRemaining);

    private readonly record struct LayerDescriptor(
        int Key,
        AiDebugCategory Categories,
        string Name,
        Color Color,
        string Legend);

    private sealed record VehicleDebugSnapshot(
        int Id,
        string Faction,
        Vector3 Position,
        Vector3 Forward,
        float Distance,
        bool DestinationActive,
        bool DestinationReached,
        bool Retro,
        string RetroMode,
        bool HasEnemy,
        Vector2 Drive,
        float Life);
}
