using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static partial class ContactResponse
{
    private const float CoverMuzzleClearanceDistance = 2f;
    private const float ConfirmedLocomotionThreshold = 0.2f;

    private static void ResetTacticalPoseLatch(ContactResponseState state)
    {
        state.HasLatchedTacticalPose = false;
        state.LatchedTacticalPose = default;
        state.LatchedPoseOwner = PoseOwner.None;
        state.TacticalPoseHoldUntil = 0f;
        state.CoverPostureDowngradeSince = 0f;
        state.MovementPoseEvidenceUntil = 0f;
        state.HasArbiterCache = false;
        state.ArbiterCachedOwner = PoseOwner.None;
        state.ArbiterCachedPose = default;
    }

    /// <summary>
    /// The single per-soldier pose arbiter (plan 014). Computes THE pose and its owner
    /// in strict priority order from the existing ownership predicates - the one place
    /// the pose is decided. Every writer applies this result instead of proposing its
    /// own pose, so two systems can no longer disagree through the shared latch (the
    /// structural generator of the prone&lt;-&gt;crouch loops and blocked-upgrade stalls of
    /// plans 004/008/012/013). The safety owners (required action, pinned/fire)
    /// are always recomputed; the interop-heavy DECISION tail (cover clearance/evaluation,
    /// suppression recovery, crouch owners) is resolved fresh when
    /// <paramref name="resolveDecisionTail"/> is true and otherwise reused from the
    /// stagger cache. Behaviour of WHEN each owner is active is unchanged - only WHO
    /// writes the pose.
    /// </summary>
    internal static SoldierPose ResolvePose(
        Soldier soldier,
        ContactResponseState state,
        float now,
        bool resolveDecisionTail,
        out PoseOwner owner,
        bool locomotionConfirmed = false,
        bool stationaryCombatActive = false)
    {
        var id = soldier.GetInstanceID();

        // A flame escape is the lethal-hazard movement grant. No stationary posture owns
        // a soldier while that escape is active.
        var flameEvading = AiState.IsFlameEvading(id, now);

        // a. A moving soldier's first visual acquisition owns one deliberate dive and
        // firing commitment. Cover evaluation below is the only other Prone author.
        if (now < state.ContactDiveProneUntil && !flameEvading && !soldier.IsOnFire)
        {
            owner = PoseOwner.ContactDive;
            return SoldierPose.Prone;
        }

        // b. Pinned / on-fire safety may halt or crouch, but cannot invent Prone.
        if (IsPinned(id) && !flameEvading)
        {
            owner = PoseOwner.Suppression;
            return SuppressionPose(soldier);
        }
        if (Settings.DangerReactionsEnabled.Value && soldier.IsOnFire)
        {
            owner = PoseOwner.Suppression;
            return SoldierPose.Crouch;
        }

        // c. The movement contract (plan 019). The committed movement decision from the
        // single movement write site and confirmed physical locomotion are INPUTS here.
        // A move/executor command by itself does not own the pose: the engine can pulse it
        // while reassessing an already-reached objective, which previously alternated
        // movement-standing with stationary-combat crouching. Active route locomotion
        // stands outside combat and crouches throughout recent contact or direct incoming
        // fire. Incidental physical correction inside a settled fighting position keeps
        // that position's latched posture instead of becoming a one-frame travel stand.
        // This rank stays above the stagger cache so a cached fighting pose cannot leak
        // into a genuinely moving soldier.
        var committedMovement = state.LastMovementOwner;
        if (PoseMovementContractCore.MovementOwnsPose(
                committedMovement,
                state.MovementHalted,
                locomotionConfirmed))
        {
            var movingUnderFire = Settings.ContactResponseEnabled.Value &&
                                  ((state.HasThreatPosition && now < state.ContactUntil) ||
                                   IncomingFireAwareness.TryGetActiveDirectCue(
                                       id, now, out _));
            movingUnderFire |= Settings.DangerReactionsEnabled.Value &&
                               state.SuppressionPoseOwned;
            var hasSettledPose = state.HasLatchedTacticalPose &&
                                 PoseMovementContractCore.CanPreserveAsSettledPose(
                                     state.LatchedPoseOwner);
            var settledPose = hasSettledPose
                ? ToTacticalStance(state.LatchedTacticalPose)
                : TacticalStance.Crouched;
            owner = PoseOwner.MovementPose;
            return FromTacticalStance(PoseMovementContractCore.MovementStance(
                committedMovement,
                movingUnderFire,
                HasStationaryTacticalHold(state, now),
                state.Relocating,
                hasSettledPose,
                settledPose));
        }

        // Decision tail (ranks d-i): reuse the cached outcome on non-decision frames so
        // the interop-heavy cover geometry is only re-evaluated on this soldier's own
        // stagger cadence (or an authoritative update).
        // A cached native/pass-through result is invalid as soon as combat ownership
        // begins, and a cached baseline-combat result is invalid as soon as it ends.
        // Re-resolve those two edges immediately; all other cached tactical owners keep
        // the normal stagger cadence.
        var cachedContextMatches = state.ArbiterCachedOwner switch
        {
            PoseOwner.None => !stationaryCombatActive,
            PoseOwner.StationaryCombat => stationaryCombatActive,
            _ => true
        };
        if (!resolveDecisionTail && state.HasArbiterCache && cachedContextMatches)
        {
            owner = state.ArbiterCachedOwner;
            return state.ArbiterCachedPose;
        }

        var pose = ResolveDecisionTailPose(
            soldier, state, now, stationaryCombatActive, out owner);
        state.HasArbiterCache = true;
        state.ArbiterCachedOwner = owner;
        state.ArbiterCachedPose = pose;
        return pose;
    }

    private static SoldierPose ResolveDecisionTailPose(
        Soldier soldier,
        ContactResponseState state,
        float now,
        bool stationaryCombatActive,
        out PoseOwner owner)
    {
        var id = soldier.GetInstanceID();
        var suppressionBand = Settings.DangerReactionsEnabled.Value &&
                              soldier.GetSuppressionValue() >= AiBehaviorTuning.CrouchSuppressionThreshold;

        // d. Cover muzzle-clearance stand.
        if (OwnsCurrentCoverClearancePose(soldier, state))
        {
            owner = PoseOwner.CoverClearance;
            return SoldierPose.Idle;
        }

        // e. Cover-geometry evaluation on an owned cover slot (with downgrade hysteresis).
        // An evaluation that measures a standing firing pose is a clearance stand the
        // owner explicitly measured, so it claims the clearance owner - the owner-aware
        // latch then grants the stand at once instead of pinning the soldier crouched
        // behind the parapet forever (W3).
        if (ShouldOwnCoverPosture(soldier) &&
            TryGetCurrentCoverEvaluation(
                soldier, state, state.LastThreatPosition, now, out var evaluation))
        {
            var proposed = ApplyCoverDowngradeHysteresis(state, evaluation.Pose, now);
            if (proposed == SoldierPose.Idle)
            {
                ClaimCoverClearancePose(soldier, state);
                owner = PoseOwner.CoverClearance;
                return SoldierPose.Idle;
            }

            ClearCoverClearancePose(state);
            owner = PoseOwner.CoverEvaluation;
            return proposed;
        }

        // f. Suppression band / recovery. On owned cover rank e above already owns the
        // pose; off cover suppression owns a crouch, never a new prone request.
        if (IsSuppressionPoseOwner(id) || suppressionBand)
        {
            owner = PoseOwner.SuppressionRecovery;
            return SuppressionRecoveryPose(soldier);
        }

        // g. Defensive / contact / tactical crouch owners. Movement crouch (moving under
        // threat) folds in here: those writers set ContactCrouchOwned / TacticalCrouchUntil
        // before applying, so ShouldOwnCrouch selects Crouch for them too.
        if (ShouldOwnCrouch(state, now))
        {
            owner = PoseOwner.TacticalCrouch;
            return FallbackStationaryPose(soldier);
        }

        // h. Baseline stationary combat ownership. The live game does not call its
        // GetFavouriteFightingPose helper; SequentialUpdate writes actualPose directly.
        // Once a soldier is fighting or holding tactically, never hand that final choice
        // back to the native stand/crouch/prone alternator. Specific cover geometry above
        // still owns any measured standing-clearance case.
        if (stationaryCombatActive)
        {
            owner = PoseOwner.StationaryCombat;
            return FallbackStationaryPose(soldier);
        }

        // i. No mod owner: leave the native pose untouched.
        owner = PoseOwner.None;
        return SoldierPose.Idle;
    }

    private static SoldierPose FromTacticalStance(TacticalStance stance)
        => stance switch
        {
            TacticalStance.Prone => SoldierPose.Prone,
            TacticalStance.Crouched => SoldierPose.Crouch,
            _ => SoldierPose.Idle
        };

    private static string OwnerTag(PoseOwner owner)
        => owner switch
        {
            PoseOwner.ContactDive => "contact-dive",
            PoseOwner.Suppression => "pinned-fire",
            PoseOwner.MovementPose => "movement",
            PoseOwner.CoverClearance => "cover-clearance",
            PoseOwner.CoverEvaluation => "cover-eval",
            PoseOwner.SuppressionRecovery => "suppr-recovery",
            PoseOwner.TacticalCrouch => "tactical-crouch",
            PoseOwner.StationaryCombat => "stationary-combat",
            PoseOwner.HaltFallback => "halt-fallback",
            _ => "native"
        };

    internal static SoldierPose StationaryHoldPose(Soldier soldier)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        if (OwnsCurrentCoverClearancePose(soldier, state))
            return SoldierPose.Idle;

        if (state.HasThreatPosition &&
            TryGetCurrentCoverEvaluation(
                soldier,
                state,
                state.LastThreatPosition,
                Time.time,
                out var evaluation))
        {
            return evaluation.Pose;
        }

        return FallbackStationaryPose(soldier);
    }

    internal static SoldierPose SuppressionPose(Soldier soldier)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        var now = Time.time;
        var onUsableCover = IsOnUsableCover(soldier);
        var hasEvaluation = false;
        var evaluatedPose = TacticalStance.Crouched;
        if (onUsableCover && state.HasThreatPosition &&
            TryGetCurrentCoverEvaluation(
                soldier, state, state.LastThreatPosition, now, out var evaluation))
        {
            hasEvaluation = true;
            evaluatedPose = ToTacticalStance(evaluation.Pose);
        }

        return FromTacticalStance(PinnedSuppressionPoseCore.Resolve(
            onUsableCover,
            hasEvaluation,
            evaluatedPose));
    }

    // Suppression itself owns Crouch. It may preserve Prone only when an active cover
    // evaluation already measured this usable position as prone-only protection.
    internal static SoldierPose SuppressionRecoveryPose(Soldier soldier)
    {
        var onUsableCover = IsOnUsableCover(soldier);
        var state = AiState.GetContactState(soldier.GetInstanceID());
        var coverEvaluationOwnsProne = false;
        if (onUsableCover)
        {
            coverEvaluationOwnsProne = CoverPostureOwnershipCore.CoverPoseOwned(
                                           state.HasThreatPosition, onUsableCover, state.DefensiveCoverHold) &&
                                       TryGetCurrentCoverEvaluation(
                                           soldier,
                                           state,
                                           state.LastThreatPosition,
                                           Time.time,
                                           out var evaluation) &&
                                       evaluation.Pose == SoldierPose.Prone;
        }

        return SuppressionRecoveryPoseCore.Resolve(
                   onUsableCover,
                   coverEvaluationOwnsProne)
               == TacticalStance.Prone ? SoldierPose.Prone : SoldierPose.Crouch;
    }

    private static SoldierPose GetStationaryEngagementPose(
        Soldier soldier,
        ContactResponseState state,
        Vector3 targetPosition)
    {
        if (!IsOnUsableCover(soldier))
            return SoldierPose.Crouch;

        if (OwnsCurrentCoverClearancePose(soldier, state))
            return SoldierPose.Idle;

        if (TryGetCurrentCoverEvaluation(
                soldier,
                state,
                targetPosition,
                Time.time,
                out var evaluation))
        {
            if (evaluation.Pose == SoldierPose.Idle)
                ClaimCoverClearancePose(soldier, state);
            else
                ClearCoverClearancePose(state);
            return evaluation.Pose;
        }

        // Native objects can disappear while a soldier changes weapon. Preserve
        // the older muzzle-clearance fallback when the full geometry evaluation
        // cannot safely complete during that frame.
        if (!OwnsCurrentCoverClearancePose(soldier, state) && soldier.Pose == SoldierPose.Crouch)
        {
            try
            {
                var gun = soldier.GetHeldGun();
                if (gun != null)
                {
                    var origin = gun.GetBulletGenerationPosition();
                    if (HasNearMuzzleObstruction(soldier, origin, targetPosition - origin))
                        ClaimCoverClearancePose(soldier, state);
                }
            }
            catch (NullReferenceException)
            {
                // Native objects can disappear while a soldier changes weapon or is
                // destroyed. The ordinary crouched posture remains the safe fallback.
            }
            catch (Il2CppException)
            {
                // Native objects can disappear while a soldier changes weapon or is
                // destroyed. The ordinary crouched posture remains the safe fallback.
            }
            catch (ObjectCollectedException)
            {
                // Native objects can disappear while a soldier changes weapon or is
                // destroyed. The ordinary crouched posture remains the safe fallback.
            }
        }

        return OwnsCurrentCoverClearancePose(soldier, state)
            ? SoldierPose.Idle
            : SoldierPose.Crouch;
    }

    private static SoldierPose FallbackStationaryPose(Soldier soldier)
        => SoldierPose.Crouch;

    private static bool HasNearMuzzleObstruction(
        Soldier soldier,
        Vector3 origin,
        Vector3 direction)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            var distance = direction.magnitude;
            if (distance <= 0.1f)
                return false;

            direction /= distance;
            origin += direction * 0.05f;
            var castDistance = Mathf.Min(CoverMuzzleClearanceDistance, distance - 0.05f);
            if (castDistance <= 0f ||
                !Physics.Raycast(
                    origin,
                    direction,
                    out var hit,
                    castDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore) ||
                hit.collider == null)
            {
                return false;
            }

            // A point-blank person is a valid target, not cover. A self-hit can occur
            // during an animation transition and likewise must not claim that the
            // parapet is obstructing the muzzle.
            var hitSoldier = hit.collider.GetComponentInParent<Soldier>();
            if (hitSoldier != null)
                return false;
            if (hit.collider.GetComponentInParent<Vehicle>() != null)
                return false;

            return true;
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
        finally
        {
            ModTimeProbe.EndSub(ModSubSite.MuzzleLane, __t);
        }
    }

    private static void ClaimCoverClearancePose(Soldier soldier, ContactResponseState state)
    {
        var coverId = CoverClearanceOwnershipCore.ResolveClaimId(
            TryGetCurrentCoverId(soldier),
            state.EvaluatedCoverPostureId,
            state.DefensiveCoverHold,
            state.HasDefensiveCoverAnchor,
            state.DefensiveCoverAnchorId,
            state.ReservedCoverId);
        if (coverId == IntPtr.Zero)
            return;

        var newlyClaimed = !state.CoverClearancePoseOwned ||
                           state.CoverClearanceCoverId != coverId;
        state.CoverClearancePoseOwned = true;
        state.CoverClearanceCoverId = coverId;
        if (newlyClaimed)
        {
            AiState.Trace(
                $"Cover firing clearance: soldier {soldier.GetInstanceID()} stood to clear a near muzzle obstruction");
        }
    }

    private static bool OwnsCurrentCoverClearancePose(
        Soldier soldier,
        ContactResponseState state)
    {
        if (!Settings.ContactResponseEnabled.Value || !state.CoverClearancePoseOwned ||
            state.CoverClearanceCoverId == IntPtr.Zero)
        {
            return false;
        }

        return CoverClearanceOwnershipCore.Owns(
            state.CoverClearanceCoverId,
            TryGetCurrentCoverId(soldier),
            state.EvaluatedCoverPostureId,
            IsOnUsableCover(soldier),
            state.DefensiveCoverHold,
            state.HasDefensiveCoverAnchor,
            state.DefensiveCoverAnchorId,
            state.ReservedCoverId);
    }

    private static IntPtr TryGetCurrentCoverId(Soldier soldier)
    {
        try
        {
            var cover = soldier.targetDestination;
            return cover == null || cover.WasCollected ? IntPtr.Zero : cover.Pointer;
        }
        catch (NullReferenceException)
        {
            return IntPtr.Zero;
        }
        catch (Il2CppException)
        {
            return IntPtr.Zero;
        }
        catch (ObjectCollectedException)
        {
            return IntPtr.Zero;
        }
    }

    private static void ClearCoverClearancePose(ContactResponseState state)
    {
        state.CoverClearancePoseOwned = false;
        state.CoverClearanceCoverId = IntPtr.Zero;
    }

    private static void ApplyContactMovementPose(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        float now)
    {
        state.ContactCrouchOwned = true;
        ApplyArbitratedPose(ai, soldier, now, resolveDecisionTail: true, SoldierPose.Crouch, "contact-move");
    }

    // Facing/perception-driven one-shot pose request (hazard escape, AT-unit crouch).
    // Records the tactical-crouch ownership window so the arbiter selects the requested
    // crouch, then applies the arbitrated pose. A non-crouch request is passed straight
    // through as the ownerless fallback.
    internal static void SetTacticalPose(
        SoldierAI ai,
        Soldier soldier,
        SoldierPose pose,
        string proposalSource = "set-tactical")
    {
        if (pose == SoldierPose.Crouch)
        {
            var state = AiState.GetContactState(soldier.GetInstanceID());
            state.TacticalCrouchUntil = Mathf.Max(
                state.TacticalCrouchUntil,
                Time.time + TacticalCrouchPersistenceSeconds);
        }

        ApplyArbitratedPose(ai, soldier, Time.time, resolveDecisionTail: true, pose, proposalSource);
    }

    /// <summary>
    /// Applies THE arbitrated pose for a soldier the mod is holding or positioning this
    /// frame: resolves the single arbiter, commits it through the owner-aware latch, and
    /// writes it to the soldier. The caller has already set the ownership flags the
    /// arbiter reads. <paramref name="fallbackPose"/> is used only when no tactical owner
    /// is active (an ownerless movement halt), committed under PoseOwner.HaltFallback so
    /// locomotion still stops at a sane stance without overriding an ownerless native
    /// pose. Returns the committed pose so a caller can StopMove into the same stance.
    /// </summary>
    internal static SoldierPose ApplyArbitratedPose(
        SoldierAI ai,
        Soldier soldier,
        float now,
        bool resolveDecisionTail,
        SoldierPose fallbackPose,
        string? traceSource = null,
        bool locomotionConfirmed = false)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        var stationaryCombatActive = OwnsStationaryCombatPose(
            ai, soldier, state, now, locomotionConfirmed);
        var pose = ResolvePose(
            soldier, state, now, resolveDecisionTail, out var owner, locomotionConfirmed,
            stationaryCombatActive);
        if (owner == PoseOwner.None)
        {
            owner = PoseOwner.HaltFallback;
            pose = fallbackPose;
        }

        var accepted = CommitArbitratedPose(soldier, state, owner, pose, now, traceSource);
        WriteAcceptedPose(ai, soldier, accepted, owner, traceSource);
        return accepted;
    }

    // Non-decision-frame write-through: re-assert the already-latched pose without
    // re-resolving the arbiter (used by the round-robin stagger fast paths).
    private static void ReassertLatchedPose(SoldierAI ai, Soldier soldier)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        if (!state.HasLatchedTacticalPose)
            return;
        WriteAcceptedPose(ai, soldier, state.LatchedTacticalPose, state.LatchedPoseOwner, "stagger");
    }

    // The owner-aware anti-flicker latch. One (owner, pose) enters per frame from the
    // arbiter, so this only shapes the transition: an unchanged pair is a no-op, a more
    // protective stance or a same-stance owner relabel is immediate, a higher-priority
    // owner takes over at once, the clearance/eval owner may raise its own measured
    // stand at once (W3), and everything else waits out the anti-flicker hold. The old
    // RenewHoldUntil starvation rule is gone.
    internal static SoldierPose CommitArbitratedPose(
        Soldier soldier,
        ContactResponseState state,
        PoseOwner owner,
        SoldierPose pose,
        float now,
        string? traceSource)
    {
        if (!state.HasLatchedTacticalPose)
        {
            state.HasLatchedTacticalPose = true;
            state.LatchedTacticalPose = pose;
            state.LatchedPoseOwner = owner;
            state.TacticalPoseHoldUntil = now + PoseArbiterCore.MinimumHoldSeconds;
            return pose;
        }

        var currentPose = state.LatchedTacticalPose;
        var currentOwner = state.LatchedPoseOwner;
        var measuredStand = (owner == PoseOwner.CoverClearance || owner == PoseOwner.CoverEvaluation) &&
                            pose == SoldierPose.Idle;

        if (PoseArbiterCore.ShouldAccept(
                currentOwner, ToTacticalStance(currentPose),
                owner, ToTacticalStance(pose),
                measuredStand, now, state.TacticalPoseHoldUntil))
        {
            state.LatchedPoseOwner = owner;
            if (currentPose != pose)
            {
                state.LatchedTacticalPose = pose;
                state.TacticalPoseHoldUntil = now + PoseArbiterCore.MinimumHoldSeconds;
                AiState.Trace(
                    $"Pose latch: soldier {soldier.GetInstanceID()} {currentPose}->{pose} " +
                    $"owner={OwnerTag(currentOwner)}->{OwnerTag(owner)} " +
                    $"src={traceSource ?? OwnerTag(owner)} hold={PoseArbiterCore.MinimumHoldSeconds:0.0}s");
            }
        }
        else if (currentPose != pose)
        {
            TraceRejectedPoseProposal(soldier, state, owner, pose, traceSource, now);
        }

        return state.LatchedTacticalPose;
    }

    private static void WriteAcceptedPose(
        SoldierAI ai,
        Soldier soldier,
        SoldierPose acceptedPose,
        PoseOwner owner,
        string? traceSource)
    {
        var soldierNeedsPose = true;
        try
        {
            soldierNeedsPose = soldier.Pose != acceptedPose;

            // Diagnostic only (verbose-logging gated): a native pose write fighting the
            // latch is invisible to the proposal traces — the latch stays stable while
            // the soldier visibly flips. Surface it when the committed pose keeps
            // diverging from the latched one.
            if (soldierNeedsPose && Settings.VerboseLogging.Value)
            {
                var state = AiState.GetContactState(soldier.GetInstanceID());
                var now = Time.time;
                if (now - state.PoseDriftTraceLastAt >= 1f)
                {
                    state.PoseDriftTraceLastAt = now;
                    AiState.Trace(
                        $"Pose drift: soldier {soldier.GetInstanceID()} " +
                        $"native={soldier.Pose} latched={acceptedPose} src={traceSource ?? OwnerTag(owner)}");
                }
            }
        }
        catch (NullReferenceException)
        {
            // If the native pose getter is unavailable during teardown, retain the
            // previous safe behavior and let SetPose perform the validation.
        }
        catch (Il2CppException)
        {
            // If the native pose getter is unavailable during teardown, retain the
            // previous safe behavior and let SetPose perform the validation.
        }
        catch (ObjectCollectedException)
        {
            // If the native pose getter is unavailable during teardown, retain the
            // previous safe behavior and let SetPose perform the validation.
        }

        ai.pose = acceptedPose;
        ai.actualPose = acceptedPose;
        if (soldierNeedsPose)
            soldier.SetPose(acceptedPose);
    }

    // Diagnostic only (verbose-logging gated): a change-proposal the latch REFUSED is
    // the half of a pose disagreement the accepted-transition trace cannot show. One
    // line per (owner, pose) per soldier per second keeps a sustained disagreement
    // readable instead of flooding the log every frame.
    private static void TraceRejectedPoseProposal(
        Soldier soldier,
        ContactResponseState state,
        PoseOwner proposedOwner,
        SoldierPose proposedPose,
        string? traceSource,
        float now)
    {
        if (!Settings.VerboseLogging.Value)
            return;

        var source = traceSource ?? OwnerTag(proposedOwner);
        if (proposedPose == state.PoseTraceLastPose &&
            source == state.PoseTraceLastSource &&
            now - state.PoseTraceLastAt < 1f)
        {
            return;
        }

        state.PoseTraceLastPose = proposedPose;
        state.PoseTraceLastSource = source;
        state.PoseTraceLastAt = now;
        AiState.Trace(
            $"Pose reject: soldier {soldier.GetInstanceID()} " +
            $"{state.LatchedTacticalPose}-x->{proposedPose} " +
            $"owner={OwnerTag(state.LatchedPoseOwner)}-x->{OwnerTag(proposedOwner)} src={source} " +
            $"holdRemain={Mathf.Max(0f, state.TacticalPoseHoldUntil - now):0.0}s " +
            $"pinned={(state.Pinned ? 1 : 0)} " +
            $"supprOwned={(state.SuppressionPoseOwned ? 1 : 0)} " +
            $"onCover={(IsOnUsableCover(soldier) ? 1 : 0)} " +
            $"evalProt={(state.EvaluatedCoverIsProtective ? 1 : 0)} " +
            $"evalPose={state.EvaluatedCoverPosture} " +
            $"contactRemain={Mathf.Max(0f, state.ContactUntil - now):0.0}s " +
            $"holdCoverRemain={Mathf.Max(0f, state.HoldCoverUntil - now):0.0}s");
    }

    private static TacticalStance ToTacticalStance(SoldierPose pose)
        => pose switch
        {
            SoldierPose.Prone => TacticalStance.Prone,
            SoldierPose.Crouch => TacticalStance.Crouched,
            _ => TacticalStance.Standing
        };

    internal static bool ShouldOwnCrouch(int soldierId, float now)
    {
        if (!AiState.ContactStates.TryGetValue(soldierId, out var state))
            return false;
        return ShouldOwnCrouch(state, now);
    }

    // Lets a caller ask whether a stationary crouch owner is the suppression reason
    // specifically (as opposed to defensive hold or contact/tactical crouch), without
    // duplicating the ContactResponseState reads.
    internal static bool IsSuppressionPoseOwner(int soldierId)
    {
        if (!AiState.ContactStates.TryGetValue(soldierId, out var state))
            return false;
        return Settings.DangerReactionsEnabled.Value && state.SuppressionPoseOwned && !state.Pinned;
    }

    internal static bool ShouldOwnCoverPosture(Soldier soldier)
    {
        if (!Settings.ContactResponseEnabled.Value ||
            !AiState.ContactStates.TryGetValue(soldier.GetInstanceID(), out var state) ||
            !state.HasThreatPosition)
        {
            return false;
        }

        // Ownership deliberately no longer lapses with the contact timer: keying it on
        // the short ContactUntil window let the generic crouch fallback take the pose
        // over between sightings, and the pose latch turned that into a prone<->crouch
        // loop. While the soldier holds cover against a known threat the cover
        // evaluation owns the pose.
        return CoverPostureOwnershipCore.CoverPoseOwned(
            state.HasThreatPosition, IsOnUsableCover(soldier), state.DefensiveCoverHold);
    }

    private static bool ShouldOwnCrouch(ContactResponseState state, float now)
    {
        if (Settings.DangerReactionsEnabled.Value && state.SuppressionPoseOwned && !state.Pinned)
            return true;
        if (Settings.ContactResponseEnabled.Value && state.DefensiveCoverHold)
            return true;
        if (Settings.ContactResponseEnabled.Value && state.ContactCrouchOwned && now < state.ContactUntil)
            return true;
        return now < state.TacticalCrouchUntil;
    }

    internal static void MaintainOwnedPose(SoldierAI ai, Soldier soldier, float now)
        => MaintainOwnedPose(ai, soldier, now, resolvePose: true);

    /// <summary>
    /// The authoritative scheduled pose write. It runs the single arbiter and applies
    /// its result through the owner-aware latch after SequentialUpdate and both movement
    /// paths. The Soldier.StopMove boundary separately enforces the committed result
    /// against later native stationary-pose writes.
    /// <paramref name="resolvePose"/> is false on the non-decision frames of the
    /// round-robin stagger (the per-frame TacticalMove postfix): the safety owners are
    /// always recomputed inside the arbiter, only the interop-heavy DECISION tail is
    /// reused from the stagger cache. The authoritative SequentialUpdate call always
    /// passes true. When no owner is active the native pose is left untouched.
    /// </summary>
    internal static void MaintainOwnedPose(
        SoldierAI ai,
        Soldier soldier,
        float now,
        bool resolvePose)
    {
        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);

        // Exposed reload owns its safety halt and fire inhibition until the magazine
        // seats; keep its lifecycle (the release path lives here). It does not author Prone.
        if (ExposedReloadPosture.TryMaintain(soldier, now))
            return;

        // Lifecycle: a stale contact-crouch ownership must lapse so rank g does not hold
        // a soldier crouched forever after the contact fades.
        if (state.ContactCrouchOwned && now >= state.ContactUntil)
            state.ContactCrouchOwned = false;

        var movementActive = RefreshConfirmedLocomotion(soldier, state, now);
        var stationaryCombatActive = OwnsStationaryCombatPose(
            ai, soldier, state, now, movementActive);
        var pose = ResolvePose(
            soldier, state, now, resolvePose, out var owner,
            movementActive,
            stationaryCombatActive);
        if (owner == PoseOwner.None)
            return; // No mod owner: leave the native pose untouched.

        var accepted = CommitArbitratedPose(soldier, state, owner, pose, now, null);
        WriteAcceptedPose(ai, soldier, accepted, owner, null);
    }

    /// <summary>
    /// Samples translation rather than SoldierAI.moveCharacter. The latter is reset and
    /// reasserted by the base decision/executor loops and can pulse at a reached objective.
    /// A short lease keeps locomotion continuous across path-node and fixed-update sampling
    /// gaps, but only real movement can start or refresh that lease.
    /// </summary>
    internal static bool RefreshConfirmedLocomotion(
        Soldier soldier,
        ContactResponseState state,
        float now)
    {
        if (state.MovementHalted || MovementArbiterCore.Halts(state.LastMovementOwner))
        {
            state.MovementPoseEvidenceUntil = 0f;
            return false;
        }

        var physicallyMoving = soldier.IsMoving(ConfirmedLocomotionThreshold);
        state.MovementPoseEvidenceUntil = PoseMovementContractCore.RefreshEvidenceUntil(
            physicallyMoving,
            now,
            state.MovementPoseEvidenceUntil);
        return PoseMovementContractCore.HasConfirmedLocomotion(
            physicallyMoving,
            now,
            state.MovementPoseEvidenceUntil);
    }

    /// <summary>
    /// Rewrites the pose argument at Soldier.StopMove, the final native stationary
    /// locomotion boundary. Native SequentialUpdate may have just replaced actualPose;
    /// this method restores the one arbitrated result before Soldier.SetPose sees it.
    /// It deliberately does not call SetPose itself because StopMove will do so with the
    /// rewritten argument immediately after the prefix returns.
    /// </summary>
    internal static SoldierPose RewriteNativeStopPose(
        SoldierAI ai,
        Soldier soldier,
        SoldierPose nativeRequest,
        float now)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        var stationaryCombatActive = OwnsStationaryCombatPose(
            ai, soldier, state, now, movementActive: false);
        var pose = ResolvePose(
            soldier,
            state,
            now,
            resolveDecisionTail: false,
            out var owner,
            locomotionConfirmed: false,
            stationaryCombatActive: stationaryCombatActive);
        if (owner == PoseOwner.None)
            return nativeRequest;

        var accepted = CommitArbitratedPose(
            soldier, state, owner, pose, now, "stop-boundary");
        ai.pose = accepted;
        ai.actualPose = accepted;
        return FromTacticalStance(FinalPoseAuthorityCore.RewriteNativeRequest(
            modOwnsPose: true,
            nativeRequest: ToTacticalStance(nativeRequest),
            committedPose: ToTacticalStance(accepted)));
    }

    private static bool OwnsStationaryCombatPose(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        float now,
        bool movementActive)
    {
        var hasVisibleTarget = false;
        try
        {
            hasVisibleTarget = ai.visibleTarget != null || soldier.CurrentVisibleTarget != null;
        }
        catch (NullReferenceException)
        {
            // A disappearing target during teardown is not evidence of active combat.
        }
        catch (Il2CppException)
        {
            // A disappearing target during teardown is not evidence of active combat.
        }
        catch (ObjectCollectedException)
        {
            // A disappearing target during teardown is not evidence of active combat.
        }

        var hasRecentContact = state.HasThreatPosition && now < state.ContactUntil;
        var hasStationaryTacticalHold = HasStationaryTacticalHold(state, now);
        return FinalPoseAuthorityCore.OwnsStationaryCombatPose(
            Settings.ContactResponseEnabled.Value,
            movementActive,
            hasVisibleTarget,
            hasRecentContact,
            hasStationaryTacticalHold);
    }

    private static bool HasStationaryTacticalHold(
        ContactResponseState state,
        float now)
        =>
            state.DefensiveCoverHold ||
            state.DefensivePositionOwned ||
            state.PlayerHoldPositionOwned ||
            state.MovementInhibitedByContactResponse ||
            now < state.EngagementHoldUntil ||
            now < state.HoldCoverUntil;

    private static SoldierPose ApplyCoverDowngradeHysteresis(
        ContactResponseState state,
        SoldierPose proposedPose,
        float now)
    {
        // Without an owned pose there is nothing to downgrade from; the latch below
        // will establish the first pose normally.
        if (!state.HasLatchedTacticalPose)
        {
            state.CoverPostureDowngradeSince = 0f;
            return proposedPose;
        }

        var current = ToTacticalStance(state.LatchedTacticalPose);
        var proposed = ToTacticalStance(proposedPose);
        if (!CoverPostureDowngradeCore.IsDowngrade(current, proposed))
        {
            state.CoverPostureDowngradeSince = 0f;
            return proposedPose;
        }

        if (state.CoverPostureDowngradeSince <= 0f)
            state.CoverPostureDowngradeSince = now;

        if (CoverPostureDowngradeCore.ShouldAccept(
                current, proposed, state.CoverPostureDowngradeSince, now))
        {
            state.CoverPostureDowngradeSince = 0f;
            return proposedPose;
        }

        // Keep the currently owned, still-firing pose until the flip persists.
        return state.LatchedTacticalPose;
    }

    private static void FaceThreatWhenStationary(SoldierAI ai, Soldier soldier, Vector3 targetPosition)
    {
        var direction = targetPosition - soldier.transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f)
        {
            direction.Normalize();
            AiState.GetContactState(soldier.GetInstanceID()).StationaryThreatFacingOwned = true;
            ai.moveLookingTarget = true;
            ai.fireDir = direction;
            soldier.RotateToward(direction, Time.deltaTime);
        }
    }

    internal static void ReleaseStationaryThreatFacingForMovement(SoldierAI ai, Soldier soldier)
        => ReleaseStationaryThreatFacing(
            ai,
            AiState.GetContactState(soldier.GetInstanceID()));

    private static void ReleaseStationaryThreatFacing(
        SoldierAI ai,
        ContactResponseState state)
    {
        if (!state.StationaryThreatFacingOwned)
            return;

        state.StationaryThreatFacingOwned = false;
        ai.moveLookingTarget = false;
    }
}
