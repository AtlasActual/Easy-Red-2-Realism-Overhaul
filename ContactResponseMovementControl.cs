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
            (state.ExposedReloadSafetyOwned || now < state.MovementStallHoldUntil)),
            flameEvading,
            state.SuppressionMovementOwned,
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
    /// Filters SoldierAI's path-intent result through the same owner that controls the
    /// actual locomotion write. This is deliberately read-only: preserving the native path
    /// lets a lapsed hold or a real movement grant resume on the very next native update.
    /// </summary>
    internal static bool FilterNativeMovementIntent(
        Soldier soldier,
        bool nativeMoving,
        float now)
    {
        if (!nativeMoving)
            return false;

        var soldierId = soldier.GetInstanceID();
        var state = AiState.GetContactState(soldierId);
        var owner = ResolveMovementOwner(
            soldier, state, soldierId, now, MovementOwner.Free);
        return NativeMovementIntentCore.ShouldReportMoving(nativeMoving, owner);
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
    /// <c>StationaryHoldPose</c>, one a hardcoded Prone - flip-flopped him
    /// forever, and the owner-aware latch could not arbitrate it because both commit under
    /// the same <see cref="PoseOwner.HaltFallback"/>. That is the prone-crouch loop.
    ///
    /// The fallback follows the same stationary fighting rule as the pose arbiter:
    /// crouch on usable cover and prone in the open. A measured clearance stand remains
    /// an owner above HaltFallback.
    /// </summary>

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
        if (HaltSpacingCore.ShouldRearm(
                state.HaltSpacingAttemptedThisEpisode,
                owner,
                HorizontalDistance(
                    soldier.transform.position,
                    state.HaltSpacingAttemptPosition)))
        {
            state.HaltSpacingAttemptedThisEpisode = false;
            state.HaltSpacingAttemptPosition = default;
        }

        // End a dispersion grant as soon as its destination is reached instead of
        // leaving moveCharacter enabled for the remainder of a fixed timer. A higher
        // priority halt also cancels the step rather than allowing it to resume later.
        if (state.HasHaltSpacingTarget)
        {
            var reachedSpacingTarget =
                HorizontalDistanceSqr(soldier.transform.position, state.HaltSpacingTarget) <=
                MovementProgressWatchdogCore.ProgressEpsilonMeters *
                MovementProgressWatchdogCore.ProgressEpsilonMeters;
            if (owner != MovementOwner.HaltSpacing ||
                reachedSpacingTarget ||
                now >= state.HaltSpacingMoveUntil)
            {
                state.HasHaltSpacingTarget = false;
                state.HaltSpacingTarget = default;
                if (owner != MovementOwner.HaltSpacing)
                {
                    state.HaltSpacingMoveUntil = 0f;
                }
                else if (reachedSpacingTarget)
                {
                    state.HaltSpacingMoveUntil = 0f;
                    owner = ResolveMovementOwner(
                        soldier, state, soldierId, now, MovementOwner.Free);
                }
            }
        }

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
        var ownerlessHaltPose = StationaryHoldPose(soldier);
        SoldierPose pose;
        if (resolvePose)
        {
            pose = ApplyArbitratedPose(
                ai, soldier, now, resolveDecisionTail: true, ownerlessHaltPose, traceSource);
        }
        else
        {
            ReassertLatchedPose(ai, soldier);
            pose = state.HasLatchedTacticalPose ? state.LatchedTacticalPose : ownerlessHaltPose;
        }

        // Idempotent within a frame: StopMove applies a deltaTime-scaled stop, so issuing
        // it several times for one soldier in one frame is not just wasted native work,
        // it applies that stop repeatedly. Re-write only when the frame or the pose
        // actually changes — every distinct decision still reaches the game.
        var frame = Time.frameCount;
        if (state.LastStopMoveFrame != frame || state.LastStopMovePose != pose)
        {
            state.LastStopMoveFrame = frame;
            state.LastStopMovePose = pose;
            var __t = ModTimeProbe.Begin();
            try
            {
                soldier.StopMove(pose, deltaTime);
            }
            finally
            {
                ModTimeProbe.EndSub(ModSubSite.StopMove, __t);
            }
        }

        return owner;
    }

    /// <summary>
    /// Plan 018 item 3. Once per fighting-halt episode, a soldier who overlaps a nearby
    /// friendly takes one bounded lateral step off the threat axis. Looking at all nearby
    /// friendlies, rather than only soldiers whose halt write happened earlier that frame,
    /// lets a whole squad entering the halt together spread instead of stacking. An
    /// unreachable step still ends in a halt: this never becomes a formation manager.
    /// Safety halts (burning, pinned) are deliberately excluded - a man under a burst does
    /// not walk sideways - and a soldier already committed to a cover route is excluded so
    /// the step cannot replace his destination.
    /// </summary>
    private static bool TryStepOutOfStackedHalt(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        MovementOwner owner,
        float now)
    {
        if (!Settings.HaltSpacingEnabled.Value ||
            !HaltSpacingCore.ShouldAttempt(state.HaltSpacingAttemptedThisEpisode, owner) ||
            state.Relocating)
        {
            return false;
        }

        // Mark the episode even when no reachable step exists. Repeatedly probing and
        // granting movement is the crouch/run/stop loop this correction is designed to end.
        state.HaltSpacingAttemptedThisEpisode = true;
        state.HaltSpacingAttemptPosition = soldier.transform.position;
        if (!TryStartSpacingStep(ai, soldier, state, now))
            return false;

        AiState.Trace(
            $"Halt spacing: soldier {soldierId} stepped clear of a halted squadmate " +
            "before taking his own fighting halt");
        return true;
    }

    internal static bool TryStartCoverConflictSeparation(
        Soldier soldier,
        ContactResponseState state,
        int soldierId,
        float now)
    {
        var ai = soldier.aiController;
        if (ai == null)
            return false;

        // Losing an exclusive cover claim is positive evidence of a contested slot,
        // not an ordinary optional halt-dispersion event. Vacate it even when the
        // cosmetic halt-spacing option is disabled; a new cover move may supersede
        // this short step on the same decision if another valid slot is available.
        state.HaltSpacingAttemptedThisEpisode = true;
        state.HaltSpacingAttemptPosition = soldier.transform.position;
        if (!TryStartSpacingStep(ai, soldier, state, now))
            return false;

        AiState.Trace(
            $"Cover occupancy: soldier {soldierId} stepped away from the retained slot owner");
        return true;
    }

    private static bool TryStartSpacingStep(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        float now)
    {
        var separation = InfantryCoverPolicy.OccupancyRadiusMeters;
        var position = soldier.transform.position;
        if (!CoverOccupancy.TryFindCrowdedFriendly(
                position, soldier, separation, out var neighbour) ||
            !HaltSpacingCore.TryResolveStep(
                new MapPoint(position.x, position.z),
                new MapPoint(neighbour.x, neighbour.z),
                new MapPoint(state.LastThreatPosition.x, state.LastThreatPosition.z),
                state.HasThreatPosition,
                separation,
                out var step))
        {
            return false;
        }

        var target = new Vector3(position.x + step.X, position.y, position.z + step.Z);
        if (!IsShortStepReachable(soldier, position, target))
        {
            // With co-located soldiers either side of the threat-lateral axis opens the
            // same gap. Try the other side when it does not move toward an offset
            // neighbour; this matters in narrow trenches where one wall can block the
            // otherwise arbitrary first side.
            var alternate = new MapPoint(-step.X, -step.Z);
            if (!HaltSpacingCore.StepDoesNotCloseGap(
                    new MapPoint(position.x, position.z),
                    new MapPoint(neighbour.x, neighbour.z),
                    alternate))
            {
                return false;
            }

            target = new Vector3(
                position.x + alternate.X,
                position.y,
                position.z + alternate.Z);
            if (!IsShortStepReachable(soldier, position, target))
                return false;
        }

        var stepWindow = Mathf.Clamp(
            separation / 1.5f + 0.35f,
            HaltSpacingCore.StepWindowSeconds,
            3.5f);
        state.HaltSpacingMoveUntil = now + stepWindow;
        state.HasHaltSpacingTarget = true;
        state.HaltSpacingTarget = target;
        ai.MoveDirectlyToward(target, stepWindow);
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
            MovementOwner.HaltSpacing => "halt-spacing",
            MovementOwner.EngagementHold => "engagement-hold",
            MovementOwner.CoverHold => "cover-hold",
            MovementOwner.CommittedMove => "committed-move",
            MovementOwner.OrderedMove => "ordered-move",
            _ => "native"
        };
}
