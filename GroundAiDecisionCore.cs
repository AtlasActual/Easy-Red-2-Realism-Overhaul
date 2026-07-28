using System;
using System.Collections.Generic;
using System.Linq;

namespace ER2RealismOverhaul;

internal enum StrategicPosture
{
    Attack,
    Defend
}

internal enum CommandChannel
{
    SquadOrders,
    InfantryAssignment
}

internal readonly record struct MapPoint(float X, float Z)
{
    internal bool IsFinite => float.IsFinite(X) && float.IsFinite(Z);
}

internal enum CommandAuthority
{
    NativeFallback = 0,
    ImmediateCombat = 200,
    ProtectedFortification = 300,
    CriticalSuppression = 400,
    RequiredSafety = 500,
    LethalEmergency = 600,
    PlayerOrScript = 700
}

internal readonly record struct CommandLeaseKey(CommandChannel Channel, int EntityId);

internal readonly record struct CommandLeaseRequest(
    CommandChannel Channel,
    int EntityId,
    string Owner,
    CommandAuthority Authority,
    int ObjectiveRevision,
    string Role,
    MapPoint Destination,
    string Constraints,
    float ValidUntil);

internal readonly record struct CommandLease(
    CommandLeaseKey Key,
    string Owner,
    CommandAuthority Authority,
    int ObjectiveRevision,
    string Role,
    MapPoint Destination,
    string Constraints,
    float ValidUntil,
    long Generation)
{
    internal bool IsValid(float now)
        => !float.IsNaN(now) && !float.IsInfinity(now) &&
           (float.IsPositiveInfinity(ValidUntil) || ValidUntil >= now);
}

/// <summary>
/// Pure, deterministic command ownership registry. Runtime adapters store only
/// stable integer entity IDs here, keeping Unity objects out of the planner.
/// </summary>
internal sealed class CommandLeaseRegistryCore
{
    private readonly Dictionary<CommandLeaseKey, CommandLease> _leases = new();
    private long _generation;

    internal int Count => _leases.Count;

    internal bool TryAcquire(CommandLeaseRequest request, float now, out CommandLease lease)
    {
        lease = default;
        if (!Valid(request, now))
            return false;

        var key = new CommandLeaseKey(request.Channel, request.EntityId);
        if (_leases.TryGetValue(key, out var current) && !current.IsValid(now))
            _leases.Remove(key);

        if (_leases.TryGetValue(key, out current))
        {
            // Objective revisions are monotonic. This rejects delayed commander
            // work after a capture or battle-phase transition.
            if (request.ObjectiveRevision < current.ObjectiveRevision)
                return false;

            // External ownership is a latch, not a short priority boost. It has to
            // be explicitly ended before an AI system can reacquire the channel.
            if (current.Authority == CommandAuthority.PlayerOrScript &&
                !string.Equals(current.Owner, request.Owner, StringComparison.Ordinal))
            {
                return false;
            }

            if (request.ObjectiveRevision == current.ObjectiveRevision &&
                request.Authority < current.Authority)
            {
                return false;
            }

            // Equal-priority ownership is sticky. This prevents two feature loops
            // from alternating ownership every update.
            if (request.ObjectiveRevision == current.ObjectiveRevision &&
                request.Authority == current.Authority &&
                !string.Equals(request.Owner, current.Owner, StringComparison.Ordinal))
            {
                return false;
            }

            // A planner heartbeat renews the same ownership decision without
            // creating a new command generation. New generations are reserved for
            // actual changes of objective, role, destination, constraints, owner,
            // or authority so downstream executors do not observe false churn.
            if (SameDecision(current, request))
            {
                var validUntil = Math.Max(current.ValidUntil, request.ValidUntil);
                lease = current with { ValidUntil = validUntil };
                _leases[key] = lease;
                return true;
            }
        }

        lease = new CommandLease(
            key,
            request.Owner,
            request.Authority,
            request.ObjectiveRevision,
            request.Role ?? string.Empty,
            request.Destination,
            request.Constraints ?? string.Empty,
            request.ValidUntil,
            ++_generation);
        _leases[key] = lease;
        return true;
    }

    internal bool TryGet(CommandChannel channel, int entityId, float now, out CommandLease lease)
    {
        var key = new CommandLeaseKey(channel, entityId);
        if (_leases.TryGetValue(key, out lease) && lease.IsValid(now))
            return true;

        _leases.Remove(key);
        lease = default;
        return false;
    }

    internal bool IsCurrent(CommandLease lease, float now)
        => TryGet(lease.Key.Channel, lease.Key.EntityId, now, out var current) &&
           current.Generation == lease.Generation &&
           current.ObjectiveRevision == lease.ObjectiveRevision &&
           string.Equals(current.Owner, lease.Owner, StringComparison.Ordinal);

    internal bool Release(CommandChannel channel, int entityId, string? owner = null)
    {
        var key = new CommandLeaseKey(channel, entityId);
        if (!_leases.TryGetValue(key, out var current) ||
            owner != null && !string.Equals(owner, current.Owner, StringComparison.Ordinal))
        {
            return false;
        }

        return _leases.Remove(key);
    }

    internal void ReleaseEntity(int entityId)
    {
        foreach (var key in _leases.Keys.Where(key => key.EntityId == entityId).ToArray())
            _leases.Remove(key);
    }

    internal void ReleaseOlderThanRevision(int objectiveRevision)
    {
        foreach (var key in _leases.Where(pair => pair.Value.ObjectiveRevision < objectiveRevision)
                     .Select(pair => pair.Key).ToArray())
        {
            _leases.Remove(key);
        }
    }

    internal int ReleaseOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
            return 0;

        var keys = _leases.Where(pair =>
                string.Equals(pair.Value.Owner, owner, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in keys)
            _leases.Remove(key);
        return keys.Length;
    }

    internal void Clear() => _leases.Clear();

    internal void CopyActive(float now, List<CommandLease> destination)
    {
        destination.Clear();
        List<CommandLeaseKey>? expired = null;
        foreach (var pair in _leases)
        {
            if (pair.Value.IsValid(now))
                destination.Add(pair.Value);
            else
                (expired ??= new List<CommandLeaseKey>()).Add(pair.Key);
        }

        if (expired != null)
        {
            foreach (var key in expired)
                _leases.Remove(key);
        }

        destination.Sort((left, right) =>
        {
            var channel = left.Key.Channel.CompareTo(right.Key.Channel);
            return channel != 0 ? channel : left.Key.EntityId.CompareTo(right.Key.EntityId);
        });
    }

    private static bool SameDecision(CommandLease current, CommandLeaseRequest request)
        => current.ObjectiveRevision == request.ObjectiveRevision &&
           current.Authority == request.Authority &&
           string.Equals(current.Owner, request.Owner, StringComparison.Ordinal) &&
           string.Equals(current.Role, request.Role ?? string.Empty, StringComparison.Ordinal) &&
           current.Destination.Equals(request.Destination) &&
           string.Equals(current.Constraints, request.Constraints ?? string.Empty,
               StringComparison.Ordinal);

    private static bool Valid(CommandLeaseRequest request, float now)
        => request.EntityId != 0 &&
           !string.IsNullOrWhiteSpace(request.Owner) &&
           request.ObjectiveRevision >= 0 &&
           !float.IsNaN(now) && !float.IsInfinity(now) &&
           (float.IsPositiveInfinity(request.ValidUntil) ||
            !float.IsNaN(request.ValidUntil) && !float.IsInfinity(request.ValidUntil) &&
            request.ValidUntil >= now);
}


internal enum TacticalStance
{
    Standing,
    Crouched,
    Prone
}

/// <summary>
/// Who owns a soldier's pose this frame. Exactly one owner writes the pose channel,
/// chosen by the arbiter in strict priority order, so two systems can no longer
/// propose conflicting poses through a shared latch (the structural generator of the
/// prone&lt;-&gt;crouch loops and blocked-upgrade stalls of plans 004/008/012/013).
/// A numerically higher owner outranks a lower one.
/// </summary>
internal enum PoseOwner
{
    // The native favourite pose - no mod override.
    None = 0,

    // A movement halt with no tactical owner (stall, grenade-safety). Latch-only floor:
    // it stops locomotion at a sane stance but never overrides the native favourite
    // pose, so ownerless halts behave exactly as they did before the arbiter.
    HaltFallback = 1,

    // g: defensive / contact / tactical crouch owners (ShouldOwnCrouch) -> Crouch.
    TacticalCrouch = 2,

    // f: suppression band / recovery (SuppressionRecoveryPoseCore) off owned cover.
    SuppressionRecovery = 3,

    // e: cover-geometry evaluation on an owned cover slot (with downgrade hysteresis).
    CoverEvaluation = 4,

    // d: muzzle-clearance stand (OwnsCurrentCoverClearancePose) -> Idle.
    CoverClearance = 5,

    // c2: the MOVEMENT contract (plan 019). A mod-committed movement decision owns the
    // locomotion pose. Ordinary bounds use Crouch, while a suppressed attacker forced
    // onward by the maximum combat halt may deliberately crawl. It outranks every
    // FIGHTING pose below it and yields to every SAFETY pose above it, which is exactly
    // where the two ladders meet: see PoseMovementContractCore.
    MovementPose = 6,

    // b: pinned / on-fire / flame safety (SuppressionPose) - instant.
    Suppression = 7,

    // a: required-action safety (exposed reload / bandage prone).
    RequiredAction = 8
}

/// <summary>
/// The contract between the MOVEMENT ladder (<see cref="MovementOwner"/>, plan 018) and the
/// POSE ladder (<see cref="PoseOwner"/>, plan 014). Each was internally consistent but they
/// did not talk to each other, so a soldier could be granted a bound (CommittedMove /
/// OrderedMove) while the pose ladder independently supplied an unrelated Prone cover
/// posture. Movement now owns the stance only for mod-committed moves: ordinary bounds
/// crouch, while an explicitly authorized suppressed crawl remains prone. Native movement
/// keeps the game's native favourite pose instead of being raised out of a crawl.
///
/// The invariant that keeps the two ladders consistent, verified rank by rank:
/// every pose owner ABOVE <see cref="PoseOwner.MovementPose"/> that can demand Prone has a
/// movement owner at or above the halting ranks, so whenever pose insists on Prone for
/// SAFETY the movement ladder is already halting -
///   RequiredAction (ExposedReloadProneOwned) -> MovementOwner.SafetyHalt,
///   Suppression    (pinned / on fire)        -> MovementOwner.PinnedHold / SafetyHalt,
/// each of which <see cref="MovementArbiterCore.Halts"/> reports as halting. The single
/// exception is <see cref="MovementOwner.HazardEscape"/>, the one GRANT above the halts: a
/// man leaving a flame keeps running, so the pose ranks above this one carry the same
/// "not while evading flame" carve-out the pinned rank always had, and the escape resolves
/// here to the movement pose instead of to a prone safety pose.
/// </summary>
internal static class PoseMovementContractCore
{
    /// <summary>
    /// True when the committed movement decision is actually MOVING this soldier, so the
    /// movement channel owns his pose. A halting owner never owns it (that is the halt
    /// case: a stationary soldier keeps his evaluated fighting pose, prone included).
    /// <see cref="MovementOwner.Free"/> means this mod wrote nothing this frame, so native
    /// locomotion retains the game's own favourite pose, including a native crawl.
    /// </summary>
    internal static bool MovementOwnsPose(
        MovementOwner committed,
        bool halted)
    {
        if (halted || MovementArbiterCore.Halts(committed))
            return false;
        return MovementArbiterCore.Grants(committed);
    }

    /// <summary>
    /// The pose a mod-owned moving soldier takes. Hazard escapes and spacing steps must
    /// remain mobile crouch moves. An ordered or committed attacker whose combat-halt
    /// deadline explicitly forced progress may instead crawl while suppressed.
    /// </summary>
    internal static TacticalStance MovementStance(
        MovementOwner committed,
        bool suppressedForcedAdvance)
        => suppressedForcedAdvance &&
           committed is MovementOwner.OrderedMove or MovementOwner.CommittedMove
            ? TacticalStance.Prone
            : TacticalStance.Crouched;
}

/// <summary>
/// Owner-aware anti-flicker latch for the single pose arbiter. Because the arbiter
/// hands the latch exactly one (owner, pose) per frame, persistent disagreement is
/// impossible; this core only shapes the transitions so independent native/tactical
/// updates cannot animate a soldier between stances every frame. It replaces the
/// pair-latch's <c>RenewHoldUntil</c> starvation rule (deleted): that rule renewed the
/// hold forever while a lower-pose owner was active, which permanently blocked a
/// legitimate stand (W3). MinimumHoldSeconds keeps its meaning and value.
/// </summary>
internal static class PoseArbiterCore
{
    // A downward safety reaction may happen immediately. A more exposed stance is
    // deliberately slower so independent native/tactical updates cannot animate a
    // soldier between prone and crouched every frame.
    internal const float MinimumHoldSeconds = 3.5f;

    internal static int ProtectionRank(TacticalStance stance)
        => stance switch
        {
            TacticalStance.Prone => 2,
            TacticalStance.Crouched => 1,
            _ => 0
        };

    /// <param name="proposedMeasuredStand">The proposing owner is the cover
    /// clearance/evaluation owner raising its OWN soldier to a stand (Idle) it just
    /// measured - accepted immediately so the clearance system can actually clear the
    /// muzzle it believes it owns (fixes W3).</param>
    internal static bool ShouldAccept(
        PoseOwner currentOwner,
        TacticalStance currentStance,
        PoseOwner proposedOwner,
        TacticalStance proposedStance,
        bool proposedMeasuredStand,
        float now,
        float holdUntil)
    {
        if (!float.IsFinite(now) || !float.IsFinite(holdUntil))
            return false;

        // The committed (owner, pose) is unchanged.
        if (currentOwner == proposedOwner && currentStance == proposedStance)
            return false;

        // Same committed stance, only the owner label changes: no visible motion, so
        // relabel at once (keeps the owner comparison current for the next proposal).
        if (currentStance == proposedStance)
            return true;

        // A more protective (more covered) stance is always safe to adopt immediately;
        // any cover-re-evaluation hysteresis has already been applied upstream.
        if (ProtectionRank(proposedStance) > ProtectionRank(currentStance))
            return true;

        // A higher-priority owner takes over immediately (safety handoff), even to a
        // more exposed stance (e.g. cover clearance standing to clear a muzzle).
        if ((int)proposedOwner > (int)currentOwner)
            return true;

        // The same owner explicitly measured a stand it needs (W3).
        if (proposedOwner == currentOwner && proposedMeasuredStand)
            return true;

        // Otherwise raising to a more exposed stance, or a lower-priority owner taking
        // over from a released higher one: wait out the anti-flicker hold.
        return now >= holdUntil;
    }
}

/// <summary>
/// The single priority ladder for the FIRE channel (plan 017), the exact mirror of
/// <see cref="PoseOwner"/> for poses. Numerically higher wins. One resolver produces one
/// blocker per soldier per frame and one write site applies it, so the old
/// set-a-flag/consume-it-later restore handshake (<c>FireRestorePending</c>) - which
/// could leave a soldier permanently mute after a transition nobody re-ran - cannot be
/// reconstructed. <see cref="None"/> is the DEFAULT: absent a reason to withhold the
/// trigger, the soldier may fire.
/// </summary>
internal enum FireBlocker
{
    // g: no blocker - the soldier may fire.
    None = 0,

    // f: moving without a weapon/range combination that permits moving fire.
    Moving = 1,

    // e: beyond the weapon's engagement range, or small arms against armor.
    Range = 2,

    // d: the brief shock reaction to being pinned (bounded by PinnedFireBlockedUntil).
    PinnedShock = 3,

    // c: lethal hazard - on fire or evading flame.
    Hazard = 4,

    // b: a required action owns the soldier (exposed reload / bandage).
    RequiredAction = 5,

    // a: not the mod's flag to write (dead, mounted, or not autonomous) - passthrough.
    NativeControl = 6
}

/// <summary>
/// Pure ordering rule for the fire channel. Every input is an existing predicate; this
/// core only decides which one wins so the ordering is testable and lives in one place.
/// </summary>
internal static class FireArbiterCore
{
    internal static bool ShouldIssueStopFire(bool stopAlreadyIssued)
        => !stopAlreadyIssued;

    internal static FireBlocker Resolve(
        bool nativeControlled,
        bool requiredAction,
        bool hazard,
        bool pinnedShock,
        bool rangeInhibited,
        bool movingWithoutMovingFire)
    {
        if (nativeControlled)
            return FireBlocker.NativeControl;
        if (requiredAction)
            return FireBlocker.RequiredAction;
        if (hazard)
            return FireBlocker.Hazard;
        if (pinnedShock)
            return FireBlocker.PinnedShock;
        if (rangeInhibited)
            return FireBlocker.Range;
        if (movingWithoutMovingFire)
            return FireBlocker.Moving;
        return FireBlocker.None;
    }

    internal static bool MayFire(FireBlocker blocker) => blocker == FireBlocker.None;
}

/// <summary>
/// Who owns a soldier's LOCOMOTION this frame (plan 018) - the third and last channel to
/// get the treatment plans 014 (pose) and 017 (fire) gave theirs. Exactly one owner writes
/// <c>moveCharacter</c>/<c>StopMove</c> per frame, chosen in strict priority order from the
/// existing timers and flags, so "who stopped this soldier?" is answerable from one
/// function instead of from seven independent halt sites coordinating through four
/// overlapping channels. A numerically higher owner outranks a lower one. Every owner
/// either HALTS or GRANTS; <see cref="Free"/> means the mod has nothing to say this frame
/// and native locomotion is left completely untouched.
/// </summary>
internal enum MovementOwner
{
    // Native locomotion - no mod override, nothing is written.
    Free = 0,

    // A mod-granted move along an existing order / attack route. It has no persistent
    // flag: the site that just released its own hold declares it.
    OrderedMove = 1,

    // A committed relocation to a chosen cover slot (ContactResponseState.Relocating).
    // Above OrderedMove so a transient contact cannot interrupt a dash already underway.
    CommittedMove = 2,

    // A reached fighting position held for its minimum hold (HoldCoverUntil, on cover).
    CoverHold = 3,

    // A contact fighting halt (MovementInhibitedByContactResponse / EngagementHoldUntil).
    EngagementHold = 4,

    // The bounded lateral dispersion step out of a stacked halt (plan 018 item 3). It
    // outranks exactly the two fighting halts it steps out of and nothing else, and it is
    // bounded by HaltSpacingMoveUntil so it can never become a movement mode.
    HaltSpacing = 5,

    // Pinned: suppression owns locomotion (SuppressionMovementOwned).
    PinnedHold = 6,

    // Evading an active flame - the ONE owner above the halts that GRANTS movement. A
    // soldier inside the beaten zone of a flamethrower leaves it even while pinned, which
    // is why the halt sites used to be individually guarded by !flameEvading.
    HazardEscape = 7,

    // Burning, a required action (reload/bandage), a grenade-safety halt, or the movement
    // watchdog's stall recovery hold. Nothing moves through this.
    SafetyHalt = 8
}

/// <summary>
/// Pure ordering rule for the movement channel - the exact mirror of
/// <see cref="FireArbiterCore"/> for fire and the <see cref="PoseOwner"/> ladder for pose.
/// Every input is an existing predicate; this core only decides which one wins, so the
/// ordering is testable and lives in one place. <paramref name="declared"/> is the owner a
/// caller claims for itself (an ordered move it just released, a grenade-safety halt with
/// no state of its own); it competes at its own rank like any other owner rather than
/// bypassing the ladder.
/// </summary>
internal static class MovementArbiterCore
{
    internal static MovementOwner Resolve(
        MovementOwner declared,
        bool safetyHalt,
        bool hazardEscape,
        bool pinnedHold,
        bool haltSpacing,
        bool engagementHold,
        bool coverHold,
        bool committedMove)
    {
        var resolved = MovementOwner.Free;
        if (committedMove)
            resolved = MovementOwner.CommittedMove;
        if (coverHold)
            resolved = MovementOwner.CoverHold;
        if (engagementHold)
            resolved = MovementOwner.EngagementHold;
        if (haltSpacing)
            resolved = MovementOwner.HaltSpacing;
        if (pinnedHold)
            resolved = MovementOwner.PinnedHold;
        if (hazardEscape)
            resolved = MovementOwner.HazardEscape;
        if (safetyHalt)
            resolved = MovementOwner.SafetyHalt;
        return (int)declared > (int)resolved ? declared : resolved;
    }

    internal static bool Halts(MovementOwner owner)
        => owner is MovementOwner.SafetyHalt or MovementOwner.PinnedHold or
                    MovementOwner.EngagementHold or MovementOwner.CoverHold;

    internal static bool Grants(MovementOwner owner)
        => owner is MovementOwner.HazardEscape or MovementOwner.HaltSpacing or
                    MovementOwner.CommittedMove or MovementOwner.OrderedMove;
}

/// <summary>
/// Halt spacing (plan 018 item 3, deferred here by plan 016). No halt path used to check
/// the distance to an already-halted squadmate, so a squad walking one path stacked at the
/// first LOS-opening doorway or crest. Consolidating locomotion into one write site makes
/// ONE check possible: before a fighting halt freezes a soldier on top of a halted
/// neighbour, he takes one short lateral step off the threat axis first. Sideways, because
/// stepping across the line of fire is what actually clears the doorway without walking
/// him toward the enemy. If the step is unreachable the soldier halts anyway - this is one
/// bounded correction on a throttled halt check, never a loop and never a formation manager.
/// </summary>
internal static class HaltSpacingCore
{
    // Roughly two body widths plus a rifle: closer than this and two men share a doorway.
    internal const float MinimumSpacingMeters = 2.5f;
    internal const float LateralStepMeters = 2.5f;

    // The step is granted for a bounded window and re-checked only after a long cooldown,
    // so a soldier can never oscillate between stepping and halting.
    internal const float StepWindowSeconds = 1.25f;
    internal const float RecheckCooldownSeconds = 6f;

    /// <summary>
    /// Returns the horizontal step that opens the gap, or false when the pair is already
    /// adequately spaced (the caller then halts normally).
    /// </summary>
    internal static bool TryResolveStep(
        MapPoint self,
        MapPoint neighbour,
        MapPoint threat,
        bool hasThreat,
        out MapPoint step)
        => TryResolveStep(
            self,
            neighbour,
            threat,
            hasThreat,
            MinimumSpacingMeters,
            out step);

    internal static bool TryResolveStep(
        MapPoint self,
        MapPoint neighbour,
        MapPoint threat,
        bool hasThreat,
        float minimumSpacingMeters,
        out MapPoint step)
    {
        step = default;
        if (!self.IsFinite || !neighbour.IsFinite ||
            !float.IsFinite(minimumSpacingMeters) || minimumSpacingMeters <= 0f)
            return false;

        var awayX = self.X - neighbour.X;
        var awayZ = self.Z - neighbour.Z;
        var awaySqr = awayX * awayX + awayZ * awayZ;
        if (awaySqr > minimumSpacingMeters * minimumSpacingMeters)
            return false;

        var dirX = 0f;
        var dirZ = 0f;
        if (hasThreat && threat.IsFinite)
        {
            var threatX = threat.X - self.X;
            var threatZ = threat.Z - self.Z;
            var threatLength = MathF.Sqrt(threatX * threatX + threatZ * threatZ);
            if (threatLength > 0.01f)
            {
                dirX = -threatZ / threatLength;
                dirZ = threatX / threatLength;
            }
        }

        if (dirX == 0f && dirZ == 0f)
        {
            // No usable threat axis: step straight away from the neighbour instead.
            var awayLength = MathF.Sqrt(awaySqr);
            if (awayLength > 0.01f)
            {
                dirX = awayX / awayLength;
                dirZ = awayZ / awayLength;
            }
            else
            {
                dirX = 1f;
            }
        }
        else if (dirX * awayX + dirZ * awayZ < 0f)
        {
            // Take the side of the lateral axis that increases the gap.
            dirX = -dirX;
            dirZ = -dirZ;
        }

        step = new MapPoint(
            dirX * minimumSpacingMeters,
            dirZ * minimumSpacingMeters);
        return true;
    }

    internal static bool StepDoesNotCloseGap(
        MapPoint self,
        MapPoint neighbour,
        MapPoint step)
    {
        if (!self.IsFinite || !neighbour.IsFinite || !step.IsFinite)
            return false;

        var awayX = self.X - neighbour.X;
        var awayZ = self.Z - neighbour.Z;
        return step.X * awayX + step.Z * awayZ >= -0.001f;
    }
}

/// <summary>
/// Weapon roles for the moving-fire rule. vision.md is explicit: ordinary rifles and
/// machine guns do not fire while moving; only appropriate submachine guns do, at close
/// range. The rifle band therefore exists only as an opt-in configuration (default 0 =
/// off) so the shipped default keeps the vision rule.
/// </summary>
internal enum MovingFireWeapon
{
    Rifle,
    SubmachineGun,
    MachineGun,
    Launcher
}

internal static class MovingFireCore
{
    internal static bool Allows(
        bool restrictionEnabled,
        MovingFireWeapon weapon,
        bool hasVisibleTarget,
        float targetDistance,
        float submachineGunMaxDistance,
        float rifleMaxDistance)
    {
        if (!restrictionEnabled)
            return true;

        // Machine guns and launchers halt and brace; they have no moving-fire band.
        var maximumDistance = weapon switch
        {
            MovingFireWeapon.SubmachineGun => submachineGunMaxDistance,
            MovingFireWeapon.Rifle => rifleMaxDistance,
            _ => 0f
        };

        if (!(maximumDistance > 0f) || !hasVisibleTarget || !float.IsFinite(targetDistance))
            return false;

        return targetDistance <= maximumDistance;
    }
}

/// <summary>
/// Pure rule for who owns a soldier's pose while he holds a cover position. The
/// cover-evaluation posture (for example prone on a prone-protective wall) must own
/// the pose for as long as he is holding cover against a known threat. It must NOT be
/// tied to the short contact-halt timer: when it was, the pose source alternated
/// between the evaluation pose and the generic crouch fallback every time the enemy
/// briefly left line of sight, and the asymmetric pose latch (instant drop to prone,
/// slow rise to crouch) turned that alternation into a sustained prone&lt;-&gt;crouch loop.
/// A defensive anchor keeps ownership even when the native cover flag is not reported;
/// an unanchored soldier keeps it while he remains on usable cover.
/// </summary>
internal static class CoverPostureOwnershipCore
{
    internal static bool CoverPoseOwned(
        bool hasThreatMemory,
        bool onUsableCover,
        bool defensiveHold)
        => hasThreatMemory && (defensiveHold || onUsableCover);
}

/// <summary>
/// Chooses the immediate pinned posture from protection that the soldier could
/// physically recognize. A roofed position keeps a soldier crouched so the
/// suppression reaction does not put him flat on an indoor floor and remove his
/// firing lane.
/// </summary>
internal static class PinnedSuppressionPoseCore
{
    internal static TacticalStance Resolve(
        bool hasOverheadProtection,
        bool onUsableCover,
        bool hasCoverEvaluation,
        TacticalStance evaluatedCoverPose)
    {
        if (hasOverheadProtection)
            return TacticalStance.Crouched;
        if (!onUsableCover)
            return TacticalStance.Prone;
        if (!hasCoverEvaluation || evaluatedCoverPose == TacticalStance.Standing)
            return TacticalStance.Crouched;
        return evaluatedCoverPose;
    }
}

/// <summary>
/// A soldier already prone in the open under suppression must not be raised to a
/// crouch while the suppression band stays active — rising in the open while still
/// under fire is never the correct reaction, and it is what produced the visible
/// prone&lt;-&gt;crouch rhythm (instant drop to prone on the next pin, forced crouch on
/// release, repeat). Crouch remains correct on usable cover (a parapet protects while
/// still allowing him to fire) and as the downward reaction from standing.
/// </summary>
internal static class SuppressionRecoveryPoseCore
{
    // coverEvaluationOwnsProne: the cover-posture ownership is active AND the cover
    // evaluation measured this slot as only protecting a prone soldier. Raising him
    // to crouch under suppression would then expose him above cover the evaluation
    // already ruled prone-only, so the suppression band must defer to it instead of
    // fighting it every frame (plan 012).
    internal static TacticalStance Resolve(
        bool hasOverheadProtection,
        bool onUsableCover,
        TacticalStance current,
        bool coverEvaluationOwnsProne)
        => !hasOverheadProtection &&
           ((!onUsableCover && current == TacticalStance.Prone) ||
            (onUsableCover && coverEvaluationOwnsProne))
            ? TacticalStance.Prone
            : TacticalStance.Crouched;
}

internal enum TacticalChannel
{
    Movement,
    Pose,
    FirePermission,
    Aim
}

internal enum TacticalAction
{
    Native,
    Hold,
    Move,
    Stand,
    Crouch,
    Prone,
    AllowFire,
    InhibitFire,
    AimAt
}

internal readonly record struct ContactMovementSensor(
    bool HasActionableContact,
    bool HasRecentContact,
    bool HasCommittedCoverMove,
    bool HasStableCoverHold,
    bool HasTimedCoverHold,
    bool CanClaimReachedCover,
    bool HasEngagementHold,
    bool NeedsDefensivePositionControl = false);

internal readonly record struct SoldierTacticalSnapshot(
    int SoldierId,
    int SquadId,
    int ObjectiveRevision,
    StrategicPosture Posture,
    bool PlayerLed,
    bool ScriptOwned,
    bool Alive,
    bool Mounted,
    bool Suppressed,
    bool NeedsReloadSafety,
    bool LethalHazard,
    MapPoint Position,
    MapPoint ThreatPosition,
    MapPoint HazardPosition = default,
    ContactMovementSensor ContactMovement = default,
    bool Autonomous = false,
    bool HasPlayerHoldOrder = false,
    bool HasProtectedAssignment = false);

/// <summary>
/// Identifies which system submitted a tactical proposal. Member order IS the
/// same-authority tie-break rank (lower value wins when two proposals for the
/// same channel share the same CommandAuthority).
/// </summary>
internal enum ProposalSource
{
    None = 0,
    External,
    PlayerHold,
    Hazard,
    ActionSafety,
    Suppression,
    ProtectedAssignment,
    DefensivePosition,
    CoverHold,
    Contact,
    Native
}

internal readonly record struct TacticalProposal(
    TacticalChannel Channel,
    TacticalAction Action,
    CommandAuthority Priority,
    ProposalSource Source,
    MapPoint Destination,
    string Constraint);

internal sealed class SoldierTacticalResolution
{
    internal SoldierTacticalSnapshot Snapshot { get; set; }
    internal Dictionary<TacticalChannel, TacticalProposal> Winners { get; }

    internal SoldierTacticalResolution(
        SoldierTacticalSnapshot snapshot,
        Dictionary<TacticalChannel, TacticalProposal>? winners = null)
    {
        Snapshot = snapshot;
        Winners = winners ?? new Dictionary<TacticalChannel, TacticalProposal>();
    }
}

/// <summary>
/// Read-only projection used by diagnostics. Tactical proposal destinations are
/// semantic inputs (for example, a threat position or an empty commander marker),
/// not guaranteed locomotion targets. Only the live executor destination may be
/// presented as the route the soldier is actually following.
/// </summary>
internal readonly record struct MovementDebugProjection(
    ProposalSource Source,
    TacticalAction Action,
    CommandAuthority Authority,
    string Constraint,
    bool HasExecutorDestination,
    MapPoint ExecutorDestination);

internal static class MovementDebugProjectionCore
{
    internal static MovementDebugProjection Project(
        SoldierTacticalResolution? resolution,
        bool hasExecutorDestination,
        MapPoint executorDestination)
    {
        var source = ProposalSource.Native;
        var action = TacticalAction.Native;
        var authority = CommandAuthority.NativeFallback;
        var constraint = string.Empty;
        if (resolution != null &&
            resolution.Winners.TryGetValue(TacticalChannel.Movement, out var winner))
        {
            source = winner.Source;
            action = winner.Action;
            authority = winner.Priority;
            constraint = winner.Constraint;
        }
        var destinationIsValid = hasExecutorDestination && executorDestination.IsFinite;
        return new MovementDebugProjection(
            source,
            action,
            authority,
            constraint,
            destinationIsValid,
            destinationIsValid ? executorDestination : default);
    }
}

internal enum AiDebugScope
{
    All,
    Allies,
    Enemies
}

/// <summary>
/// Keeps the overlay's allegiance policy deterministic and fail-closed. Runtime
/// code supplies the game's hostility result; this core only decides whether a
/// classified actor belongs in the selected view.
/// </summary>
internal static class AiDebugAllegianceCore
{
    internal static bool Includes(
        AiDebugScope scope,
        bool hasReferenceFaction,
        bool candidateFactionKnown,
        bool isEnemy)
    {
        if (scope == AiDebugScope.All)
            return true;
        if (!hasReferenceFaction || !candidateFactionKnown)
            return false;
        return scope == AiDebugScope.Enemies ? isEnemy : !isEnemy;
    }
}

internal static class TacticalArbitrationCore
{
    internal static SoldierTacticalResolution Resolve(
        SoldierTacticalSnapshot snapshot,
        IEnumerable<TacticalProposal>? proposals)
    {
        var resolution = new SoldierTacticalResolution(snapshot);
        ResolveInto(resolution, proposals);
        return resolution;
    }

    // Runtime AI updates reuse this object for the same soldier.  Keeping the
    // allocation-free form separate preserves the simple enumerable API used by
    // deterministic planner tests and other one-shot callers.
    internal static SoldierTacticalResolution ResolveInto(
        SoldierTacticalResolution resolution,
        SoldierTacticalSnapshot snapshot,
        IReadOnlyList<TacticalProposal> proposals)
    {
        resolution.Snapshot = snapshot;
        resolution.Winners.Clear();
        if (!snapshot.Alive)
            return resolution;

        for (var index = 0; index < proposals.Count; index++)
            Consider(resolution.Winners, proposals[index]);

        return resolution;
    }

    private static void ResolveInto(
        SoldierTacticalResolution resolution,
        IEnumerable<TacticalProposal>? proposals)
    {
        resolution.Winners.Clear();
        if (!resolution.Snapshot.Alive)
            return;

        foreach (var proposal in proposals ?? Array.Empty<TacticalProposal>())
            Consider(resolution.Winners, proposal);
    }

    private static void Consider(
        Dictionary<TacticalChannel, TacticalProposal> winners,
        TacticalProposal proposal)
    {
        if (proposal.Source == ProposalSource.None)
            return;

        if (!winners.TryGetValue(proposal.Channel, out var current) ||
            Compare(proposal, current) < 0)
        {
            winners[proposal.Channel] = proposal;
        }
    }

    private static int Compare(TacticalProposal left, TacticalProposal right)
    {
        var priority = right.Priority.CompareTo(left.Priority);
        if (priority != 0)
            return priority;

        var source = left.Source.CompareTo(right.Source);
        if (source != 0)
            return source;

        var action = left.Action.CompareTo(right.Action);
        if (action != 0)
            return action;

        var x = left.Destination.X.CompareTo(right.Destination.X);
        return x != 0 ? x : left.Destination.Z.CompareTo(right.Destination.Z);
    }
}

internal static class CombatMovementPolicyCore
{
    internal static bool NeedsLocalCombatControl(ContactMovementSensor sensor)
        => sensor.HasActionableContact ||
           sensor.HasRecentContact ||
           sensor.HasCommittedCoverMove ||
           sensor.HasTimedCoverHold ||
           sensor.CanClaimReachedCover ||
           sensor.HasEngagementHold;

    internal static bool NeedsProtectedCoverControl(ContactMovementSensor sensor)
        => sensor.HasStableCoverHold;

    internal static bool NeedsDefensivePositionControl(ContactMovementSensor sensor)
        => sensor.NeedsDefensivePositionControl;

    internal static TacticalAction SelectLocalAction(ContactMovementSensor sensor)
        // Contact by itself is a reason to halt, observe, and return fire. Movement
        // is only the selected local action after this system has committed the
        // soldier to a specific cover destination.
        => sensor.HasCommittedCoverMove
            ? TacticalAction.Move
            : TacticalAction.Hold;

    internal static bool ShouldAuthorizeAttackBound(
        bool hasAttackRoute,
        bool coveringFireEstablished,
        bool maximumHaltReached,
        bool maximumOnCoverHaltReached,
        bool underDirectFire,
        bool pinned,
        bool onUsableCover,
        float coverHoldUntil,
        float now)
    {
        if (!hasAttackRoute || pinned ||
            float.IsNaN(now) || float.IsInfinity(now) ||
            float.IsNaN(coverHoldUntil))
        {
            return false;
        }

        if (onUsableCover)
        {
            if (now < coverHoldUntil)
                return false;

            // Direct fire is a strong reason to stay, not a veto with no escape
            // (plan 015 / D1): once the longer on-cover halt cap expires it
            // authorizes the bound even under sustained fire, so an enemy firing
            // more often than the direct-fire cue's lifetime can no longer freeze
            // a covered squad forever.
            if (underDirectFire)
                return maximumOnCoverHaltReached;

            return coveringFireEstablished || maximumOnCoverHaltReached;
        }

        // In the open, direct fire no longer vetoes the bound: lying in a beaten
        // zone with no cover is worse than bounding to the next position, so the
        // maximum-halt escape must still be able to fire here.
        return coveringFireEstablished || maximumHaltReached;
    }

    /// <summary>
    /// D3 (plan 015): the mover's own preconditions for a coordinated attack
    /// advance no longer require the mover to have fired himself — a soldier
    /// whose cover slot has no firing lane was otherwise permanently ineligible.
    /// </summary>
    internal static bool MoverQualifiesForAttackAdvance(
        IntPtr targetToken,
        int moverSquadId,
        IntPtr moverAttackContactToken)
        => targetToken != IntPtr.Zero && moverSquadId != 0 &&
           moverAttackContactToken == targetToken;

    /// <summary>
    /// D2 (plan 015): a squadmate's fresh stationary shot at ANY confirmed enemy
    /// counts as covering fire — it no longer has to match the mover's own target
    /// token, which broke down whenever squadmates engaged different visible
    /// enemies.
    /// </summary>
    internal static bool IsCoveringFireEstablished(
        int moverSquadId,
        int candidateSquadId,
        IntPtr candidateLastShotTargetToken,
        bool candidateShotWasStationary,
        bool candidateRelocating,
        bool candidatePinned,
        bool candidateSuppressionMovementOwned,
        float candidateLastShotAt,
        float now,
        float freshnessSeconds)
        => candidateSquadId == moverSquadId &&
           candidateLastShotTargetToken != IntPtr.Zero &&
           candidateShotWasStationary && !candidateRelocating &&
           !candidatePinned && !candidateSuppressionMovementOwned &&
           now - candidateLastShotAt <= freshnessSeconds;
}

internal static class SquadOrderMovementCore
{
    /// <summary>
    /// A defend order is stationary only after the soldier reaches its assigned
    /// area. Until then it is a real reinforcement route and needs the same bounded
    /// combat-halt liveness as any other squad movement order.
    /// </summary>
    internal static bool ShouldTreatAsMoving(bool isDefendOrder, bool isInsideDefendArea)
        => !isDefendOrder || !isInsideDefendArea;
}

internal readonly record struct PinnedReleaseDecision(bool Released, bool GrantsImmunity);

internal static class PinnedReleaseCore
{
    /// <summary>
    /// Decides when a suppression-based pin releases. The normal rule (today's
    /// behavior) requires the minimum-hold timer to expire and suppression to
    /// fall to the release threshold. A bounded time cap additionally forces a
    /// release regardless of suppression so an attacker under sustained fire in
    /// the open is not pinned forever; that release path alone grants a short
    /// re-pin immunity window so the same incoming fire cannot instantly re-pin
    /// the soldier before it can act on the release.
    /// </summary>
    internal static PinnedReleaseDecision EvaluatePinnedRelease(
        float pinnedSince,
        float pinnedUntil,
        int suppression,
        int releaseSuppressionThreshold,
        float maximumPinnedSeconds,
        float now)
    {
        if (float.IsNaN(now) || float.IsInfinity(now) || float.IsNaN(pinnedSince))
            return new PinnedReleaseDecision(false, false);

        if (now - pinnedSince >= maximumPinnedSeconds)
            return new PinnedReleaseDecision(true, true);

        if (now >= pinnedUntil && suppression <= releaseSuppressionThreshold)
            return new PinnedReleaseDecision(true, false);

        return new PinnedReleaseDecision(false, false);
    }

    /// <summary>
    /// Decides whether fresh suppression should (re-)engage a pin. A soldier still
    /// inside its post-time-cap-release immunity window is not re-pinned regardless
    /// of suppression; once immunity lapses, ordinary suppression pins normally.
    /// </summary>
    internal static bool ShouldEngagePin(
        int suppression,
        int proneSuppressionThreshold,
        float immunityUntil,
        float now)
        => suppression >= proneSuppressionThreshold && now >= immunityUntil;
}

internal static class ExternalMovementPolicyCore
{
    internal static bool AllowsPlayerHoldCover(
        bool playerLed,
        bool scriptOwned,
        bool autonomousSoldier,
        bool validPlayerHold)
        => playerLed && !scriptOwned && autonomousSoldier && validPlayerHold;
}

/// <summary>
/// Settings values consulted while generating tactical proposals. Every
/// Settings.*.Value read inside the old GroundAiDirector.CollectProposals body
/// is represented here so ProposalGenerationCore stays pure.
/// </summary>
internal readonly record struct TacticalPolicyOptions(
    bool ContactResponseEnabled);

/// <summary>
/// Pure per-soldier movement/pose/fire-permission proposal generator. A
/// line-for-line translation of the former GroundAiDirector.CollectProposals:
/// same branch order, same constraint strings, same authorities. Reads only
/// its snapshot and options arguments — no Settings, no Unity, no Soldier.
/// </summary>
internal static class ProposalGenerationCore
{
    internal static void Collect(
        SoldierTacticalSnapshot snapshot,
        TacticalPolicyOptions options,
        List<TacticalProposal> destination)
    {
        destination.Clear();
        destination.Add(new TacticalProposal(
            TacticalChannel.Movement, TacticalAction.Native, CommandAuthority.NativeFallback,
            ProposalSource.Native, default, string.Empty));

        var playerHoldCover = ExternalMovementPolicyCore.AllowsPlayerHoldCover(
            snapshot.PlayerLed,
            snapshot.ScriptOwned,
            snapshot.Autonomous,
            snapshot.HasPlayerHoldOrder);
        if (playerHoldCover)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement,
                snapshot.ContactMovement.HasCommittedCoverMove
                    ? TacticalAction.Move
                    : TacticalAction.Hold,
                CommandAuthority.PlayerOrScript,
                ProposalSource.PlayerHold,
                snapshot.Position,
                "occupy protected positions inside player hold area"));
        }
        else if (snapshot.PlayerLed || snapshot.ScriptOwned)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Native, CommandAuthority.PlayerOrScript,
                ProposalSource.External, default, "preserve player/script squad order"));
        }
        else if (snapshot.LethalHazard)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Move, CommandAuthority.LethalEmergency,
                ProposalSource.Hazard, snapshot.HazardPosition, "temporary emergency override"));
        }
        else if (snapshot.NeedsReloadSafety)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Hold, CommandAuthority.RequiredSafety,
                ProposalSource.ActionSafety, snapshot.Position, "complete medical or reload action safely"));
        }
        else if (snapshot.Suppressed)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Hold, CommandAuthority.CriticalSuppression,
                ProposalSource.Suppression, snapshot.Position, "pin in protection"));
        }

        if (snapshot.HasProtectedAssignment)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Move, CommandAuthority.ProtectedFortification,
                ProposalSource.ProtectedAssignment, default, "retain fortification or weapon lease"));
        }

        if (!snapshot.HasProtectedAssignment &&
            CombatMovementPolicyCore.NeedsDefensivePositionControl(
                snapshot.ContactMovement))
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Hold,
                CommandAuthority.ProtectedFortification,
                ProposalSource.DefensivePosition, snapshot.Position,
                "take one useful defensive position and remain there"));
        }

        if (options.ContactResponseEnabled &&
            CombatMovementPolicyCore.NeedsProtectedCoverControl(snapshot.ContactMovement))
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Hold,
                CommandAuthority.ProtectedFortification,
                ProposalSource.CoverHold, snapshot.Position, "retain reached fortified cover"));
        }

        if (options.ContactResponseEnabled &&
            CombatMovementPolicyCore.NeedsLocalCombatControl(snapshot.ContactMovement))
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement,
                CombatMovementPolicyCore.SelectLocalAction(snapshot.ContactMovement),
                CommandAuthority.ImmediateCombat,
                ProposalSource.Contact, snapshot.ThreatPosition, "contact response"));
        }

        if (snapshot.Suppressed)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Pose, TacticalAction.Crouch, CommandAuthority.CriticalSuppression,
                ProposalSource.Suppression, default, "reduce exposure"));
        }

        if (snapshot.NeedsReloadSafety)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Pose, TacticalAction.Prone, CommandAuthority.RequiredSafety,
                ProposalSource.ActionSafety, default, "protect required action"));
            destination.Add(new TacticalProposal(
                TacticalChannel.FirePermission, TacticalAction.InhibitFire, CommandAuthority.RequiredSafety,
                ProposalSource.ActionSafety, default, "do not interrupt required action"));
        }
    }
}

internal enum MovementProgressDecision
{
    Reset,
    Observe,
    Progressed,
    Halt
}

internal readonly record struct MovementProgressInput(
    bool ShouldMonitor,
    bool WaitingForPath,
    bool DestinationChanged,
    float PhysicalTravelMeters,
    float SecondsWithoutProgress);

/// <summary>
/// Package-free locomotion stall policy. Reported path distance is deliberately
/// excluded: a repath can alter it while the character remains physically stuck.
/// </summary>
internal static class MovementProgressWatchdogCore
{
    internal const float ProgressEpsilonMeters = 0.35f;
    internal const float DestinationChangeMeters = 1.5f;
    internal const float StallSeconds = 2.75f;
    internal const float PathRequestStallSeconds = 5f;
    internal const float RecoveryHoldSeconds = 4f;

    internal static float RecoverySeconds(int consecutiveFailures)
        => RecoveryHoldSeconds * Math.Clamp(consecutiveFailures, 1, 3);

    internal static MovementProgressDecision Evaluate(MovementProgressInput input)
    {
        if (!input.ShouldMonitor ||
            !float.IsFinite(input.PhysicalTravelMeters) ||
            !float.IsFinite(input.SecondsWithoutProgress))
        {
            return MovementProgressDecision.Reset;
        }

        if (input.DestinationChanged ||
            input.PhysicalTravelMeters >= ProgressEpsilonMeters)
        {
            return MovementProgressDecision.Progressed;
        }

        var limit = input.WaitingForPath ? PathRequestStallSeconds : StallSeconds;
        return input.SecondsWithoutProgress >= limit
            ? MovementProgressDecision.Halt
            : MovementProgressDecision.Observe;
    }
}

internal enum CoverRouteRecoveryDecision
{
    Continue,
    DestinationProgressed,
    RefreshPath,
    Abandon
}

internal static class CoverRouteRecoveryCore
{
    internal const float DestinationProgressEpsilonMeters = 0.35f;
    internal const float NoDestinationProgressSeconds = 10f;

    internal static CoverRouteRecoveryDecision Evaluate(
        float currentDestinationDistance,
        float bestDestinationDistance,
        float secondsWithoutDestinationProgress,
        bool pathRetryUsed,
        bool waitingForPath)
    {
        if (waitingForPath)
            return CoverRouteRecoveryDecision.Continue;

        if (IsFinitePositive(currentDestinationDistance) &&
            (!IsFinitePositive(bestDestinationDistance) ||
             currentDestinationDistance + DestinationProgressEpsilonMeters <
             bestDestinationDistance))
        {
            return CoverRouteRecoveryDecision.DestinationProgressed;
        }

        if (!float.IsNaN(secondsWithoutDestinationProgress) &&
            !float.IsInfinity(secondsWithoutDestinationProgress) &&
            secondsWithoutDestinationProgress >= NoDestinationProgressSeconds)
        {
            return pathRetryUsed
                ? CoverRouteRecoveryDecision.Abandon
                : CoverRouteRecoveryDecision.RefreshPath;
        }

        return CoverRouteRecoveryDecision.Continue;
    }

    private static bool IsFinitePositive(float value)
        => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}

internal readonly record struct DefensivePositionOwnershipInput(
    bool AlreadyOwned,
    bool EligibleDefender,
    bool InsideAssignedArea,
    bool SameSquad,
    bool SameObjectiveRevision);

internal static class DefensivePositionOwnershipCore
{
    internal static bool ShouldOwn(DefensivePositionOwnershipInput input)
    {
        if (!input.EligibleDefender)
            return false;

        // Crossing into the assigned area is the one acquisition gate. Once the
        // position has been acquired, native destination/order flicker and a few
        // metres of incidental displacement cannot give locomotion back. Only an
        // actual squad/objective/ownership transition releases it.
        return input.AlreadyOwned
            ? input.SameSquad && input.SameObjectiveRevision
            : input.InsideAssignedArea;
    }
}

internal static class GroundAuthorityCore
{
    internal static bool CanMutate(bool multiplayerIntent, bool inRoom, bool isHost)
        => (!multiplayerIntent && !inRoom) || (inRoom && isHost);
}

internal static class StaticWeaponAssignmentCore
{
    internal static bool SeatPreventsTransit(
        bool seatExists,
        int occupantSoldierId,
        int assignedSoldierId)
        => !seatExists ||
           occupantSoldierId != 0 && occupantSoldierId != assignedSoldierId;

    internal static bool ShouldReassertDestination(
        bool mountedOnAssignedWeapon,
        bool destinationTargetsAssignedWeapon,
        bool emergencyInterrupted)
        => !mountedOnAssignedWeapon && !destinationTargetsAssignedWeapon &&
           !emergencyInterrupted;
}

internal readonly record struct DefenderSquadCandidate(
    int SquadId,
    float EffectiveStrength,
    int CombatReadyOnFoot,
    bool PlannedReserve,
    bool ExternallyOwned);

internal readonly record struct DefenderCrewCandidate(
    int SoldierId,
    int SquadId,
    bool IsLeader,
    bool IsMedic,
    bool IsRadioman,
    bool HasHandheldAntiTank,
    bool Reachable);

internal readonly record struct DefensiveWeaponCandidate(
    int WeaponId,
    bool Viable,
    bool ArmorPiercing,
    float Caliber,
    float AmmunitionScore,
    float ThreatCoverage,
    float InfantryApproachCoverage);

internal readonly record struct DefensiveWeaponAssignment(int WeaponId, int SoldierId, int SquadId);

internal sealed record DefenderAllocationPlan(
    IReadOnlyList<int> ReserveSquadIds,
    IReadOnlyList<DefensiveWeaponAssignment> WeaponAssignments,
    IReadOnlyList<int> UnstaffedWeaponIds);

internal static class DefenderAllocationCore
{
    // Was CommanderPlannerCore.ReserveFraction; inlined once the commander planner
    // that owned that constant was removed.
    internal const float ReserveFraction = 0.20f;

    internal static DefenderAllocationPlan Allocate(
        IReadOnlyList<DefenderSquadCandidate>? squads,
        IReadOnlyList<DefenderCrewCandidate>? crews,
        IReadOnlyList<DefensiveWeaponCandidate>? weapons,
        bool armorReported)
    {
        var availableSquads = (squads ?? Array.Empty<DefenderSquadCandidate>())
            .Where(squad => squad.SquadId != 0 && !squad.ExternallyOwned &&
                            squad.CombatReadyOnFoot > 0 && float.IsFinite(squad.EffectiveStrength))
            .GroupBy(squad => squad.SquadId)
            .Select(group => group.OrderByDescending(squad => squad.EffectiveStrength).First())
            .ToArray();
        var reserveCount = availableSquads.Length >= 2
            ? Math.Max(1, (int)Math.Ceiling(availableSquads.Length * ReserveFraction))
            : 0;
        var reserve = availableSquads
            .OrderByDescending(squad => squad.PlannedReserve)
            .ThenByDescending(squad => squad.EffectiveStrength)
            .ThenBy(squad => squad.SquadId)
            .Take(reserveCount)
            .Select(squad => squad.SquadId)
            .ToHashSet();

        var squadsById = availableSquads.ToDictionary(squad => squad.SquadId);
        var onFootRemaining = availableSquads.ToDictionary(
            squad => squad.SquadId, squad => squad.CombatReadyOnFoot);
        var specialistRemaining = new Dictionary<(int SquadId, int Kind), int>();
        var crewPool = (crews ?? Array.Empty<DefenderCrewCandidate>())
            .Where(crew => crew.SoldierId != 0 && crew.SquadId != 0 && crew.Reachable &&
                           !crew.IsLeader && squadsById.ContainsKey(crew.SquadId) &&
                           !reserve.Contains(crew.SquadId))
            .GroupBy(crew => crew.SoldierId)
            .Select(group => group.OrderBy(crew => crew.SquadId).First())
            .ToArray();

        foreach (var squadId in squadsById.Keys)
        {
            specialistRemaining[(squadId, 0)] = crewPool.Count(crew => crew.SquadId == squadId && crew.IsMedic);
            specialistRemaining[(squadId, 1)] = crewPool.Count(crew => crew.SquadId == squadId && crew.IsRadioman);
            specialistRemaining[(squadId, 2)] = crewPool.Count(crew => crew.SquadId == squadId && crew.HasHandheldAntiTank);
        }

        var orderedWeapons = (weapons ?? Array.Empty<DefensiveWeaponCandidate>())
            .Where(weapon => weapon.WeaponId != 0 && weapon.Viable &&
                             float.IsFinite(weapon.Caliber) &&
                             float.IsFinite(weapon.AmmunitionScore) &&
                             float.IsFinite(weapon.ThreatCoverage) &&
                             float.IsFinite(weapon.InfantryApproachCoverage))
            .GroupBy(weapon => weapon.WeaponId)
            .Select(group => group.First())
            .OrderByDescending(weapon => armorReported && weapon.ArmorPiercing)
            .ThenByDescending(weapon => armorReported && weapon.ArmorPiercing ? weapon.Caliber : 0f)
            .ThenByDescending(weapon => armorReported ? weapon.ThreatCoverage : weapon.InfantryApproachCoverage)
            .ThenByDescending(weapon => weapon.AmmunitionScore)
            .ThenBy(weapon => weapon.WeaponId)
            .ToArray();

        var assignments = new List<DefensiveWeaponAssignment>();
        var assignedSoldiers = new HashSet<int>();
        var unstaffed = new List<int>();
        foreach (var weapon in orderedWeapons)
        {
            DefenderCrewCandidate? selected = null;
            foreach (var crew in crewPool
                         .Where(crew => !assignedSoldiers.Contains(crew.SoldierId) &&
                                        onFootRemaining[crew.SquadId] > 3 &&
                                        SpecialistCanLeave(crew, specialistRemaining))
                         .OrderBy(crew => SpecialistCount(crew))
                         .ThenByDescending(crew => onFootRemaining[crew.SquadId])
                         .ThenBy(crew => crew.SquadId)
                         .ThenBy(crew => crew.SoldierId))
            {
                selected = crew;
                break;
            }

            if (selected is not { } gunner)
            {
                unstaffed.Add(weapon.WeaponId);
                continue;
            }

            assignments.Add(new DefensiveWeaponAssignment(
                weapon.WeaponId, gunner.SoldierId, gunner.SquadId));
            assignedSoldiers.Add(gunner.SoldierId);
            onFootRemaining[gunner.SquadId]--;
            DecrementSpecialists(gunner, specialistRemaining);
        }

        return new DefenderAllocationPlan(
            reserve.OrderBy(id => id).ToArray(), assignments, unstaffed);
    }

    private static int SpecialistCount(DefenderCrewCandidate crew)
        => (crew.IsMedic ? 1 : 0) + (crew.IsRadioman ? 1 : 0) +
           (crew.HasHandheldAntiTank ? 1 : 0);

    private static bool SpecialistCanLeave(
        DefenderCrewCandidate crew,
        IReadOnlyDictionary<(int SquadId, int Kind), int> remaining)
        => (!crew.IsMedic || remaining[(crew.SquadId, 0)] > 1) &&
           (!crew.IsRadioman || remaining[(crew.SquadId, 1)] > 1) &&
           (!crew.HasHandheldAntiTank || remaining[(crew.SquadId, 2)] > 1);

    private static void DecrementSpecialists(
        DefenderCrewCandidate crew,
        IDictionary<(int SquadId, int Kind), int> remaining)
    {
        if (crew.IsMedic)
            remaining[(crew.SquadId, 0)]--;
        if (crew.IsRadioman)
            remaining[(crew.SquadId, 1)]--;
        if (crew.HasHandheldAntiTank)
            remaining[(crew.SquadId, 2)]--;
    }
}

internal readonly record struct FortifiedCoverSlot(
    int SlotId,
    MapPoint Position,
    float Protection,
    float FiringLaneQuality,
    float RouteExposure,
    float DistanceMeters,
    bool Viable);

internal sealed record FortifiedPosition(
    int PositionId,
    IReadOnlyList<int> SlotIds,
    MapPoint Center,
    float Score);

internal static class FortifiedPositionCore
{
    internal const float MinimumSlotSeparationMeters = 1.75f;
    internal const float ReplacementDelaySeconds = 15f;
    internal const float ReplacementImprovementRatio = 1.25f;

    internal static float Score(FortifiedCoverSlot slot, float searchRadius, int capacity = 1)
    {
        if (!slot.Viable || !slot.Position.IsFinite || !float.IsFinite(searchRadius) || searchRadius <= 0f ||
            !float.IsFinite(slot.Protection) || !float.IsFinite(slot.FiringLaneQuality) ||
            !float.IsFinite(slot.RouteExposure) || !float.IsFinite(slot.DistanceMeters))
        {
            return float.NegativeInfinity;
        }

        var protection = Math.Clamp(slot.Protection, 0f, 1f);
        var firing = Math.Clamp(slot.FiringLaneQuality, 0f, 1f);
        var exposure = Math.Clamp(slot.RouteExposure, 0f, 1f);
        var distance = Math.Clamp(slot.DistanceMeters / searchRadius, 0f, 1f);
        var capacityScore = Math.Clamp((Math.Max(1, capacity) - 1f) / 5f, 0f, 1f);
        return protection * 0.42f + firing * 0.32f + capacityScore * 0.10f +
               (1f - exposure) * 0.10f + (1f - distance) * 0.06f;
    }

    internal static bool ShouldReplace(
        float currentScore,
        float alternativeScore,
        float secondsWithoutRelevantFiringLane,
        bool currentDestroyedOrUnsafe)
    {
        if (currentDestroyedOrUnsafe)
            return float.IsFinite(alternativeScore);
        if (!float.IsFinite(currentScore) || !float.IsFinite(alternativeScore) ||
            !float.IsFinite(secondsWithoutRelevantFiringLane))
        {
            return false;
        }

        return secondsWithoutRelevantFiringLane >= ReplacementDelaySeconds &&
               alternativeScore >= Math.Max(0f, currentScore) * ReplacementImprovementRatio;
    }

    internal static IReadOnlyList<FortifiedPosition> Group(
        IReadOnlyList<FortifiedCoverSlot>? slots,
        float groupingDistance,
        float searchRadius)
    {
        if (slots == null || !float.IsFinite(groupingDistance) || groupingDistance <= 0f)
            return Array.Empty<FortifiedPosition>();

        var viable = slots.Where(slot => slot.Viable && slot.Position.IsFinite)
            .GroupBy(slot => slot.SlotId).Select(group => group.First())
            .OrderBy(slot => slot.SlotId).ToArray();
        var unvisited = viable.Select(slot => slot.SlotId).ToHashSet();
        var byId = viable.ToDictionary(slot => slot.SlotId);
        var result = new List<FortifiedPosition>();
        var rangeSqr = groupingDistance * groupingDistance;

        while (unvisited.Count > 0)
        {
            var root = unvisited.Min();
            unvisited.Remove(root);
            var queue = new Queue<int>();
            queue.Enqueue(root);
            var members = new List<FortifiedCoverSlot>();
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                var slot = byId[id];
                members.Add(slot);
                foreach (var otherId in unvisited.ToArray())
                {
                    var other = byId[otherId];
                    var dx = slot.Position.X - other.Position.X;
                    var dz = slot.Position.Z - other.Position.Z;
                    if (dx * dx + dz * dz > rangeSqr)
                        continue;
                    unvisited.Remove(otherId);
                    queue.Enqueue(otherId);
                }
            }

            var center = new MapPoint(
                members.Average(slot => slot.Position.X),
                members.Average(slot => slot.Position.Z));
            var score = members.Average(slot => Score(slot, searchRadius, members.Count));
            result.Add(new FortifiedPosition(
                members.Min(slot => slot.SlotId),
                members.Select(slot => slot.SlotId).OrderBy(id => id).ToArray(),
                center,
                score));
        }

        return result.OrderByDescending(position => position.Score)
            .ThenBy(position => position.PositionId).ToArray();
    }
}

/// <summary>
/// Was defined alongside the commander planner; relocated here (the pure-core
/// home for the surviving tactical/defensive logic) because
/// <see cref="ContactResponseCoverSearch"/>, <see cref="ContactResponseDefensiveOccupation"/>,
/// and <c>EmplacementPatches</c> still consult it for native defend-order areas.
/// </summary>
internal static class DefensivePositioningCore
{
    internal static bool IsInsideArea(
        MapPoint position,
        MapPoint center,
        float radius,
        float tolerance = 0f)
    {
        if (!position.IsFinite || !center.IsFinite || !float.IsFinite(radius) ||
            !float.IsFinite(tolerance))
        {
            return false;
        }

        var allowed = Math.Max(0f, radius) + Math.Max(0f, tolerance);
        var dx = (double)position.X - center.X;
        var dz = (double)position.Z - center.Z;
        return dx * dx + dz * dz <= (double)allowed * allowed;
    }
}
