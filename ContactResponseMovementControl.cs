using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static partial class ContactResponse
{
    private const float HaltSpacingProbeHeight = 0.9f;

    /// <summary>
    /// The single per-soldier MOVEMENT arbiter (plan 018), the third and last channel to
    /// get the treatment plans 014 (pose) and 017 (fire) gave theirs. It computes THE
    /// locomotion owner from the existing timers and flags in strict priority order; the
    /// timers stay exactly as they were (this changes WHO writes, not WHEN each system
    /// wants to halt). <paramref name="declared"/> is the owner the calling site claims
    /// for itself, competing at its own rank rather than bypassing the ladder.
    /// </summary>
    internal static MovementOwner ResolveMovementOwner(
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now,
        MovementOwner declared)
    {
        var dangerReactions = Settings.DangerReactionsEnabled.Value;
        var onFire = dangerReactions && soldier.IsOnFire;
        var flameEvading = dangerReactions && !onFire && AiState.IsFlameEvading(soldierId, now);

        return MovementArbiterCore.Resolve(
            declared,
            // Burning, a required action, or the stall watchdog's recovery hold. Flame
            // evasion outranks the last two - a reload/stall hold releases itself while
            // the soldier leaves the flame, which is exactly what the scattered
            // "!flameEvading &&" guards on the old halt sites encoded.
            onFire ||
            (!flameEvading &&
             (state.ExposedReloadProneOwned || now < state.MovementStallHoldUntil)),
            flameEvading,
            state.SuppressionMovementOwned,
            AiState.IsHidingFromTank(soldierId, now),
            now < state.HaltSpacingMoveUntil,
            // Deliberately NOT gated on ContactResponseEnabled: defensive-position control
            // (UpdateDefensivePosition / ShouldHoldDefensivePosition) runs its halts with
            // contact response switched off, and gating here would silently stop halting
            // those defenders. A live toggle-off releases the channel by zeroing these
            // timers in Disable/SuspendForVehicle, which is where that belongs.
            state.MovementInhibitedByContactResponse || now < state.EngagementHoldUntil,
            // IsOnCover is interop, so the timer is tested first and short-circuits it.
            now < state.HoldCoverUntil && soldier.IsOnCover(),
            state.Relocating);
    }

    /// <summary>
    /// THE single write site for <c>ai.moveCharacter</c> / <c>StopMove</c> on the
    /// foot-soldier path. Resolves the arbiter and applies it: a halting owner stops
    /// locomotion into the arbitrated pose, a granting owner releases it (and clears the
    /// contact-halt flag, so the flag can no longer disagree with the actual locomotion
    /// state), and <see cref="MovementOwner.Free"/> writes nothing at all so native
    /// locomotion is untouched on frames this mod has nothing to say about.
    /// <paramref name="resolvePose"/> is false on the round-robin stagger's write-through
    /// frames, which re-assert the already latched pose instead of re-resolving it.
    /// Returns the committed owner.
    /// </summary>
    internal static MovementOwner ApplyMovementDecision(
        SoldierAI ai,
        Soldier soldier,
        float deltaTime,
        float now,
        MovementOwner declared,
        string? traceSource = null,
        bool resolvePose = true)
    {
        var soldierId = soldier.GetInstanceID();
        var state = AiState.GetContactState(soldierId);

        // Declaring a MOVE releases the declaring site's own contact halt - the manual
        // "MovementInhibitedByContactResponse = false" that every grant path used to carry
        // immediately before writing moveCharacter. It happens before the resolve so the
        // flag cannot outrank the declaration it was just released by. Timed holds
        // (EngagementHoldUntil, HoldCoverUntil) are deliberately NOT cleared here: a live
        // timer is a real hold and must still be able to outrank an ordered move.
        if (MovementArbiterCore.Grants(declared))
            state.MovementInhibitedByContactResponse = false;

        return ApplyResolvedMovementDecision(
            ai,
            soldier,
            state,
            soldierId,
            ResolveMovementOwner(soldier, state, soldierId, now, declared),
            deltaTime,
            now,
            traceSource,
            resolvePose);
    }

    /// <summary>
    /// THE pose for a soldier this mod is halting when the pose arbiter finds no tactical
    /// owner (plan 020 D1). It used to be whatever <c>fallbackPose</c> the halting call
    /// site passed in, and that was the last multi-writer disagreement left after plan 014:
    /// two sites halting the same defender on alternate decisions - one passing
    /// <c>StationaryHoldPose</c> (Crouch), one a hardcoded Prone - flip-flopped him
    /// forever, and the owner-aware latch could not arbitrate it because both commit under
    /// the same <see cref="PoseOwner.HaltFallback"/>. That is the prone-crouch loop.
    ///
    /// Crouch is the correct answer here by construction, not by tuning: every reason a
    /// halted soldier should be prone - pinned, suppressed, on fire, hiding from armour,
    /// exposed reload, a measured cover-geometry pose - is an owner ABOVE HaltFallback in
    /// the ladder, so reaching this line means none of them applies. A man halted with no
    /// tactical reason to be down takes a fighting crouch, not a nap in the open.
    /// </summary>
    private const SoldierPose OwnerlessHaltPose = SoldierPose.Crouch;

    /// <summary>
    /// The write half of <see cref="ApplyMovementDecision"/>, split out only so the
    /// round-robin stagger's write-through can act on an owner it already resolved
    /// instead of paying for a second resolve. All locomotion writes are here.
    /// </summary>
    private static MovementOwner ApplyResolvedMovementDecision(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        MovementOwner owner,
        float deltaTime,
        float now,
        string? traceSource,
        bool resolvePose)
    {
        // resolvePose false marks the stagger's write-through REPLAY. A replay must not
        // start a new dispersion step: the step is a decision, and granting one here would
        // hand movement back while the caller is still gating native locomotion off.
        if (resolvePose && MovementArbiterCore.Halts(owner) &&
            TryStepOutOfStackedHalt(ai, soldier, state, soldierId, owner, now))
        {
            owner = MovementOwner.HaltSpacing;
        }

        TraceMovementDecision(soldier, state, owner, now, traceSource);

        // No mod owner: native locomotion is left exactly as it is. The halt output is
        // still cleared - "this mod is not holding him" is the fact other readers need,
        // and a stale true would make a moving soldier look like a halted squadmate to
        // the spacing check.
        if (owner == MovementOwner.Free)
        {
            state.MovementHalted = false;
            return owner;
        }

        if (MovementArbiterCore.Grants(owner))
        {
            state.MovementHalted = false;
            state.MovementInhibitedByContactResponse = false;
            ai.moveCharacter = true;
            return owner;
        }

        state.MovementHalted = true;
        ai.moveCharacter = false;
        soldier.isSprinting = false;
        SoldierPose pose;
        if (resolvePose)
        {
            pose = ApplyArbitratedPose(
                ai, soldier, now, resolveDecisionTail: true, OwnerlessHaltPose, traceSource);
        }
        else
        {
            ReassertLatchedPose(ai, soldier);
            pose = state.HasLatchedTacticalPose ? state.LatchedTacticalPose : OwnerlessHaltPose;
        }

        soldier.StopMove(pose, deltaTime);
        return owner;
    }

    /// <summary>
    /// Plan 018 item 3. On the RISING EDGE of a fighting halt only, and at most once per
    /// long cooldown, a soldier about to freeze on top of an already-halted friendly
    /// takes one bounded lateral step off the threat axis first. An unreachable step
    /// means he halts anyway: this must never become a loop, and it must never become a
    /// formation manager. Safety halts (burning, pinned, tank-hide) are deliberately
    /// excluded - a man under armour or a burst does not walk sideways - and a soldier
    /// already committed to a cover route is excluded so the step cannot replace his
    /// destination.
    /// </summary>
    private static bool TryStepOutOfStackedHalt(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        MovementOwner owner,
        float now)
    {
        // Off by default (playtest 2026-07-24: "some AI walk in place"). The step grants
        // locomotion for a FIXED window rather than until arrival, so a soldier who
        // completes - or cannot complete - the 2.5 m offset keeps moveCharacter set for the
        // remainder of it with nothing left to walk toward, and re-arms every cooldown.
        // Cover-slot dispersion (plan 016's crowding penalty) is independent of this and
        // stays on.
        if (!Settings.HaltSpacingEnabled.Value ||
            owner is not (MovementOwner.EngagementHold or MovementOwner.CoverHold) ||
            state.MovementHalted || state.Relocating ||
            now < state.HaltSpacingNextCheckAt)
        {
            return false;
        }

        state.HaltSpacingNextCheckAt = now + HaltSpacingCore.RecheckCooldownSeconds;
        var position = soldier.transform.position;
        if (!CoverOccupancy.TryFindHaltedNeighbour(
                position, soldier, HaltSpacingCore.MinimumSpacingMeters, out var neighbour) ||
            !HaltSpacingCore.TryResolveStep(
                new MapPoint(position.x, position.z),
                new MapPoint(neighbour.x, neighbour.z),
                new MapPoint(state.LastThreatPosition.x, state.LastThreatPosition.z),
                state.HasThreatPosition,
                out var step))
        {
            return false;
        }

        var target = new Vector3(position.x + step.X, position.y, position.z + step.Z);
        if (!IsShortStepReachable(soldier, position, target))
            return false;

        state.HaltSpacingMoveUntil = now + HaltSpacingCore.StepWindowSeconds;
        ai.MoveDirectlyToward(target, 1.5f);
        AiState.Trace(
            $"Halt spacing: soldier {soldierId} stepped clear of a halted squadmate " +
            "before taking his own fighting halt");
        return true;
    }

    private static bool IsShortStepReachable(Soldier soldier, Vector3 from, Vector3 to)
    {
        try
        {
            var direction = to - from;
            direction.y = 0f;
            var distance = direction.magnitude;
            if (distance <= 0.1f)
                return false;

            direction /= distance;
            if (Physics.Raycast(
                    from + Vector3.up * HaltSpacingProbeHeight,
                    direction,
                    out var hit,
                    distance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore) &&
                hit.collider != null)
            {
                // A wall, and a body is no better: stepping into an occupied slot solves
                // nothing. Either way the soldier halts where he stands.
                return false;
            }

            return !CoverOccupancy.IsOccupiedByOther(to, soldier);
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
    }

    private static void TraceMovementDecision(
        Soldier soldier,
        ContactResponseState state,
        MovementOwner owner,
        float now,
        string? traceSource)
    {
        if (owner == state.LastMovementOwner)
            return;

        var previous = state.LastMovementOwner;
        state.LastMovementOwner = owner;
        if (!Settings.VerboseLogging.Value)
            return;

        AiState.Trace(
            $"Movement decision: soldier {soldier.GetInstanceID()} " +
            $"{MovementOwnerTag(previous)}->{MovementOwnerTag(owner)} " +
            $"move={(MovementArbiterCore.Halts(owner) ? 0 : 1)} " +
            $"src={traceSource ?? MovementOwnerTag(owner)} " +
            $"relocating={(state.Relocating ? 1 : 0)} " +
            $"engageRemain={Mathf.Max(0f, state.EngagementHoldUntil - now):0.0}s " +
            $"coverRemain={Mathf.Max(0f, state.HoldCoverUntil - now):0.0}s");
    }

    private static string MovementOwnerTag(MovementOwner owner)
        => owner switch
        {
            MovementOwner.SafetyHalt => "safety-halt",
            MovementOwner.HazardEscape => "hazard-escape",
            MovementOwner.PinnedHold => "pinned-hold",
            MovementOwner.TankHide => "tank-hide",
            MovementOwner.HaltSpacing => "halt-spacing",
            MovementOwner.EngagementHold => "engagement-hold",
            MovementOwner.CoverHold => "cover-hold",
            MovementOwner.CommittedMove => "committed-move",
            MovementOwner.OrderedMove => "ordered-move",
            _ => "native"
        };
}
