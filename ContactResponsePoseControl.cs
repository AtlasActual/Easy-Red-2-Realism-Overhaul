using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static partial class ContactResponse
{
    private const float CoverMuzzleClearanceDistance = 2f;
    private const float OverheadProtectionCheckIntervalSeconds = 0.5f;
    private const float OverheadProtectionDistanceMeters = 4f;

    private static void ResetTacticalPoseLatch(ContactResponseState state)
    {
        state.HasLatchedTacticalPose = false;
        state.LatchedTacticalPose = default;
        state.LatchedPoseOwner = PoseOwner.None;
        state.TacticalPoseHoldUntil = 0f;
        state.CoverPostureDowngradeSince = 0f;
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
        out PoseOwner owner)
    {
        var id = soldier.GetInstanceID();

        // The safety ranks a-c all pin a soldier DOWN, and each corresponds to a halting
        // movement owner (see PoseMovementContractCore for the rank-by-rank correspondence)
        // - except while he is escaping a flame, the one movement grant that outranks the
        // halts. A running man is not prone, so all three yield to the flame escape exactly
        // as the pinned rank always did, and the movement rank below owns him instead.
        var flameEvading = AiState.IsFlameEvading(id, now);

        // a. Required-action safety (exposed reload / bandage prone).
        if (state.ExposedReloadProneOwned && !flameEvading)
        {
            owner = PoseOwner.RequiredAction;
            return SoldierPose.Prone;
        }

        // b. Pinned / on-fire / flame safety - instant.
        if (IsPinned(id) && !flameEvading)
        {
            owner = PoseOwner.Suppression;
            return SuppressionPose(soldier);
        }
        if (Settings.DangerReactionsEnabled.Value && soldier.IsOnFire)
        {
            owner = PoseOwner.Suppression;
            return SoldierPose.Prone;
        }

        // c. The movement contract (plan 019). The committed movement decision from the
        // single movement write site is an INPUT here. An ordinary mod-owned bound uses
        // Crouch, while a suppressed attacker released by the maximum combat halt keeps
        // Prone and crawls toward the objective. Native locomotion is not claimed here:
        // MovementOwner.Free leaves the game's own favourite pose untouched, including
        // native crawl movement. This rank stays above the stagger cache so a cached
        // fighting pose cannot leak into a newly committed move.
        var committedMovement = state.LastMovementOwner;
        if (PoseMovementContractCore.MovementOwnsPose(
                committedMovement,
                state.MovementHalted))
        {
            owner = PoseOwner.MovementPose;
            return FromTacticalStance(PoseMovementContractCore.MovementStance(
                committedMovement,
                state.AttackProgressForced &&
                state.SuppressionPoseOwned &&
                !state.HasOverheadProtection));
        }

        // Decision tail (ranks d-i): reuse the cached outcome on non-decision frames so
        // the interop-heavy cover geometry is only re-evaluated on this soldier's own
        // stagger cadence (or an authoritative update).
        if (!resolveDecisionTail && state.HasArbiterCache)
        {
            owner = state.ArbiterCachedOwner;
            return state.ArbiterCachedPose;
        }

        var pose = ResolveDecisionTailPose(soldier, state, now, out owner);
        state.HasArbiterCache = true;
        state.ArbiterCachedOwner = owner;
        state.ArbiterCachedPose = pose;
        return pose;
    }

    private static SoldierPose ResolveDecisionTailPose(
        Soldier soldier,
        ContactResponseState state,
        float now,
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
            if (suppressionBand &&
                proposed == SoldierPose.Prone &&
                HasOverheadProtection(soldier, state, now))
            {
                proposed = SoldierPose.Crouch;
            }
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
        // pose; off cover this keeps an already-prone soldier down and crouches a
        // standing/crouched one (SuppressionRecoveryPoseCore).
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
            return SoldierPose.Crouch;
        }

        // i. No mod owner: leave the native favourite pose untouched.
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
            PoseOwner.RequiredAction => "required-action",
            PoseOwner.Suppression => "pinned-fire",
            PoseOwner.MovementPose => "movement",
            PoseOwner.CoverClearance => "cover-clearance",
            PoseOwner.CoverEvaluation => "cover-eval",
            PoseOwner.SuppressionRecovery => "suppr-recovery",
            PoseOwner.TacticalCrouch => "tactical-crouch",
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

        return SoldierPose.Crouch;
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
            HasOverheadProtection(soldier, state, now),
            onUsableCover,
            hasEvaluation,
            evaluatedPose));
    }

    // A stationary suppression-driven crouch proposal must not raise a soldier who is
    // already prone in the open: crouch is only a sane suppression reaction on usable
    // cover or as a downward reaction from standing. On usable cover it also must not
    // fight an owned cover evaluation that already measured the slot as prone-only —
    // see SuppressionRecoveryPoseCore.
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
                   HasOverheadProtection(soldier, state, Time.time),
                   onUsableCover,
                   soldier.Pose == SoldierPose.Prone ? TacticalStance.Prone : TacticalStance.Crouched,
                   coverEvaluationOwnsProne)
               == TacticalStance.Prone ? SoldierPose.Prone : SoldierPose.Crouch;
    }

    private static bool HasOverheadProtection(
        Soldier soldier,
        ContactResponseState state,
        float now)
    {
        if (now < state.NextOverheadProtectionCheckAt)
            return state.HasOverheadProtection;

        state.NextOverheadProtectionCheckAt =
            now + OverheadProtectionCheckIntervalSeconds;
        try
        {
            // Start just above eye level so the ray cannot immediately hit the
            // soldier's own collision volume. A nearby solid ceiling is the
            // reliable geometry signal available for a roofed fighting position.
            var origin = soldier.LookPosition() + Vector3.up * 0.2f;
            state.HasOverheadProtection = Physics.Raycast(
                origin,
                Vector3.up,
                OverheadProtectionDistanceMeters,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
        }
        catch (NullReferenceException)
        {
            state.HasOverheadProtection = false;
        }
        catch (Il2CppException)
        {
            state.HasOverheadProtection = false;
        }
        catch (ObjectCollectedException)
        {
            state.HasOverheadProtection = false;
        }

        return state.HasOverheadProtection;
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
        try
        {
            var cover = soldier.targetDestination;
            if (cover == null || cover.Pointer == IntPtr.Zero)
                return;

            var newlyClaimed = !state.CoverClearancePoseOwned ||
                               state.CoverClearanceCoverId != cover.Pointer;
            state.CoverClearancePoseOwned = true;
            state.CoverClearanceCoverId = cover.Pointer;
            if (newlyClaimed)
            {
                AiState.Trace(
                    $"Cover firing clearance: soldier {soldier.GetInstanceID()} stood to clear a near muzzle obstruction");
            }
        }
        catch (NullReferenceException)
        {
            ClearCoverClearancePose(state);
        }
        catch (Il2CppException)
        {
            ClearCoverClearancePose(state);
        }
        catch (ObjectCollectedException)
        {
            ClearCoverClearancePose(state);
        }
    }

    private static bool OwnsCurrentCoverClearancePose(
        Soldier soldier,
        ContactResponseState state)
    {
        if (!Settings.ContactResponseEnabled.Value || !state.CoverClearancePoseOwned ||
            state.CoverClearanceCoverId == IntPtr.Zero || !IsOnUsableCover(soldier))
        {
            return false;
        }

        try
        {
            var cover = soldier.targetDestination;
            return cover != null && cover.Pointer == state.CoverClearanceCoverId;
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
    /// locomotion still stops at a sane stance without overriding the native favourite
    /// pose. Returns the committed pose so a caller can StopMove into the same stance.
    /// </summary>
    internal static SoldierPose ApplyArbitratedPose(
        SoldierAI ai,
        Soldier soldier,
        float now,
        bool resolveDecisionTail,
        SoldierPose fallbackPose,
        string? traceSource = null)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        var pose = ResolvePose(soldier, state, now, resolveDecisionTail, out var owner);
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
    /// The authoritative per-frame pose write. It runs the single arbiter and applies
    /// its result through the owner-aware latch - the last word on a soldier's pose each
    /// time it runs (last in the SequentialUpdate tail, and in both movement postfixes).
    /// <paramref name="resolvePose"/> is false on the non-decision frames of the
    /// round-robin stagger (the per-frame TacticalMove postfix): the safety owners are
    /// always recomputed inside the arbiter, only the interop-heavy DECISION tail is
    /// reused from the stagger cache. The authoritative SequentialUpdate call always
    /// passes true. When no owner is active the native favourite pose is left untouched.
    /// </summary>
    internal static void MaintainOwnedPose(
        SoldierAI ai,
        Soldier soldier,
        float now,
        bool resolvePose)
    {
        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);

        // Exposed reload owns posture, fire, and movement until the magazine seats; keep
        // its lifecycle (the release path lives here). When it owns, it applies its own
        // required-action halt (which routes prone through the arbiter).
        if (ExposedReloadPosture.TryMaintain(soldier, now))
            return;

        // Lifecycle: a stale contact-crouch ownership must lapse so rank g does not hold
        // a soldier crouched forever after the contact fades.
        if (state.ContactCrouchOwned && now >= state.ContactUntil)
            state.ContactCrouchOwned = false;

        var pose = ResolvePose(soldier, state, now, resolvePose, out var owner);
        if (owner == PoseOwner.None)
            return; // No mod owner: leave the native favourite pose untouched.

        var accepted = CommitArbitratedPose(soldier, state, owner, pose, now, null);
        WriteAcceptedPose(ai, soldier, accepted, owner, null);
    }

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
