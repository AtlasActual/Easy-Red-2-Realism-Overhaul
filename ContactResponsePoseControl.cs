using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static partial class ContactResponse
{
    private const float CoverMuzzleClearanceDistance = 2f;

    private static void ResetTacticalPoseLatch(ContactResponseState state)
    {
        state.HasLatchedTacticalPose = false;
        state.LatchedTacticalPose = default;
        state.TacticalPoseHoldUntil = 0f;
        state.CoverPostureDowngradeSince = 0f;
        state.HasFightingPoseCache = false;
        state.FightingPoseOverrode = false;
        state.FightingPoseCached = default;
    }

    /// <summary>
    /// The non-safety GetFavouriteFightingPose resolution, factored out of the postfix so
    /// the round-robin stagger can reuse its cached outcome between a soldier's decision
    /// frames. Records whether the mod overrode the native favourite pose (and with which
    /// pose) for <see cref="TryReuseFightingPose"/>. The caller handles the pinned/flame
    /// safety pose per-frame before ever reaching this.
    /// </summary>
    internal static void ResolveFightingPose(
        Soldier soldier,
        int id,
        float now,
        ref SoldierPose result)
    {
        var state = AiState.GetContactState(id);
        var stationaryHoldPose = StationaryHoldPose(soldier);
        if (ShouldOwnCoverPosture(soldier))
        {
            // The cover geometry owner must win as one unit (standing and prone alike).
            result = ResolveTacticalPoseProposal(soldier, stationaryHoldPose, now, "fav-covereval");
            CacheFightingPose(state, overrode: true, result);
            return;
        }

        if (stationaryHoldPose == SoldierPose.Idle)
        {
            result = ResolveTacticalPoseProposal(soldier, SoldierPose.Idle, now, "fav-idle");
            CacheFightingPose(state, overrode: true, result);
            return;
        }

        var suppressionCrouchBand = Settings.DangerReactionsEnabled.Value &&
                                     soldier.GetSuppressionValue() >= Settings.CrouchSuppression.Value;
        if (ShouldOwnCrouch(id, now) || suppressionCrouchBand)
        {
            var suppressionOwnsCrouch = IsSuppressionPoseOwner(id) || suppressionCrouchBand;
            result = ResolveTacticalPoseProposal(
                soldier,
                suppressionOwnsCrouch ? SuppressionRecoveryPose(soldier) : SoldierPose.Crouch,
                now,
                suppressionOwnsCrouch ? "fav-suppr-crouch" : "fav-crouch");
            CacheFightingPose(state, overrode: true, result);
            return;
        }

        // No branch fired: the native favourite pose stands unchanged. Cache the
        // no-override outcome so off-frames also leave the native result alone.
        CacheFightingPose(state, overrode: false, default);
    }

    private static void CacheFightingPose(
        ContactResponseState state,
        bool overrode,
        SoldierPose pose)
    {
        state.HasFightingPoseCache = true;
        state.FightingPoseOverrode = overrode;
        state.FightingPoseCached = pose;
    }

    /// <summary>
    /// Non-decision-frame reuse of the cached GetFavouriteFightingPose outcome. Returns
    /// false when no outcome has been cached yet, so a first resolution is never deferred.
    /// When the last resolution did not override, the native result is left untouched.
    /// </summary>
    internal static bool TryReuseFightingPose(int soldierId, ref SoldierPose result)
    {
        var state = AiState.GetContactState(soldierId);
        if (!state.HasFightingPoseCache)
            return false;

        if (state.FightingPoseOverrode)
            result = state.FightingPoseCached;

        return true;
    }

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
        if (!IsOnUsableCover(soldier))
            return SoldierPose.Prone;

        var state = AiState.GetContactState(soldier.GetInstanceID());
        if (state.HasThreatPosition &&
            TryGetCurrentCoverEvaluation(
                soldier,
                state,
                state.LastThreatPosition,
                Time.time,
                out var evaluation))
        {
            // Pinning never owns a standing firing exposure. Keep the protection
            // decision, but drop behind low cover if its firing pose was standing.
            return evaluation.Pose == SoldierPose.Idle
                ? SoldierPose.Crouch
                : evaluation.Pose;
        }

        return SoldierPose.Crouch;
    }

    // A stationary suppression-driven crouch proposal must not raise a soldier who is
    // already prone in the open: crouch is only a sane suppression reaction on usable
    // cover or as a downward reaction from standing. On usable cover it also must not
    // fight an owned cover evaluation that already measured the slot as prone-only —
    // see SuppressionRecoveryPoseCore.
    internal static SoldierPose SuppressionRecoveryPose(Soldier soldier)
    {
        var onUsableCover = IsOnUsableCover(soldier);
        var coverEvaluationOwnsProne = false;
        if (onUsableCover)
        {
            var state = AiState.GetContactState(soldier.GetInstanceID());
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
                   soldier.Pose == SoldierPose.Prone ? TacticalStance.Prone : TacticalStance.Crouched,
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
        EnsureTacticalPose(ai, soldier, SoldierPose.Crouch, "contact-move");
    }

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

        EnsureTacticalPose(ai, soldier, pose, proposalSource);
    }

    internal static void EnsureTacticalPose(
        SoldierAI ai,
        Soldier soldier,
        SoldierPose pose,
        string proposalSource = "untagged")
    {
        var acceptedPose = ResolveTacticalPoseProposal(soldier, pose, Time.time, proposalSource);
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
                        $"native={soldier.Pose} latched={acceptedPose} src={proposalSource}");
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

    internal static SoldierPose ResolveTacticalPoseProposal(
        Soldier soldier,
        SoldierPose pose,
        float now,
        string proposalSource = "untagged")
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        if (!state.HasLatchedTacticalPose)
        {
            state.HasLatchedTacticalPose = true;
            state.LatchedTacticalPose = pose;
            state.TacticalPoseHoldUntil = now + TacticalPoseStabilityCore.MinimumHoldSeconds;
        }
        else
        {
            var lowerPoseStillOwned = state.Pinned ||
                                      state.EvaluatedCoverIsProtective &&
                                      now < state.EvaluatedCoverPostureUntil &&
                                      (state.DefensiveCoverHold ||
                                       state.ContactCrouchOwned && now < state.ContactUntil);
            state.TacticalPoseHoldUntil = TacticalPoseStabilityCore.RenewHoldUntil(
                now,
                state.TacticalPoseHoldUntil,
                lowerPoseStillOwned);

            if (state.LatchedTacticalPose != pose &&
                TacticalPoseStabilityCore.ShouldAccept(
                    ToTacticalStance(state.LatchedTacticalPose),
                    ToTacticalStance(pose),
                    now,
                    state.TacticalPoseHoldUntil,
                    lowerPoseStillOwned))
            {
                var previous = state.LatchedTacticalPose;
                state.LatchedTacticalPose = pose;
                state.TacticalPoseHoldUntil = now + TacticalPoseStabilityCore.MinimumHoldSeconds;
                AiState.Trace(
                    $"Pose latch: soldier {soldier.GetInstanceID()} {previous}->{pose} " +
                    $"src={proposalSource} hold={TacticalPoseStabilityCore.MinimumHoldSeconds:0.0}s");
            }
            else if (state.LatchedTacticalPose != pose)
            {
                TraceRejectedPoseProposal(
                    soldier, state, pose, proposalSource, lowerPoseStillOwned, now);
            }
        }

        return state.LatchedTacticalPose;
    }

    // Diagnostic only (verbose-logging gated): a change-proposal the latch REFUSED is
    // the half of a pose disagreement the accepted-transition trace cannot show. One
    // line per (source, pose) per soldier per second keeps a sustained disagreement
    // readable instead of flooding the log every frame.
    private static void TraceRejectedPoseProposal(
        Soldier soldier,
        ContactResponseState state,
        SoldierPose proposedPose,
        string proposalSource,
        bool lowerPoseStillOwned,
        float now)
    {
        if (!Settings.VerboseLogging.Value)
            return;

        if (proposedPose == state.PoseTraceLastPose &&
            proposalSource == state.PoseTraceLastSource &&
            now - state.PoseTraceLastAt < 1f)
        {
            return;
        }

        state.PoseTraceLastPose = proposedPose;
        state.PoseTraceLastSource = proposalSource;
        state.PoseTraceLastAt = now;
        AiState.Trace(
            $"Pose reject: soldier {soldier.GetInstanceID()} " +
            $"{state.LatchedTacticalPose}-x->{proposedPose} src={proposalSource} " +
            $"holdRemain={Mathf.Max(0f, state.TacticalPoseHoldUntil - now):0.0}s " +
            $"lowerOwned={(lowerPoseStillOwned ? 1 : 0)} " +
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
    /// <paramref name="resolvePose"/> is false on the non-decision frames of the
    /// round-robin stagger (the per-frame TacticalMove postfix). The reload/pinned/
    /// tank-hide SAFETY poses above the resolution block always run per-frame; only the
    /// interop-heavy cover-posture / crouch-ownership RESOLUTION is deferred, replaced
    /// by a cheap re-assertion of the already-latched pose. The authoritative
    /// (game-staggered) SequentialUpdate call always passes true.
    /// </summary>
    internal static void MaintainOwnedPose(
        SoldierAI ai,
        Soldier soldier,
        float now,
        bool resolvePose)
    {
        var id = soldier.GetInstanceID();
        var state = AiState.GetContactState(id);
        if (ExposedReloadPosture.TryMaintain(ai, soldier, now, Time.deltaTime))
            return;

        if (IsPinned(id) && !AiState.IsFlameEvading(id, now))
        {
            EnsureTacticalPose(ai, soldier, SuppressionPose(soldier), "maintain-pinned");
            return;
        }

        if (AiState.IsHidingFromTank(id, now))
        {
            EnsureTacticalPose(
                ai,
                soldier,
                IsOnUsableCover(soldier) ? StationaryHoldPose(soldier) : SoldierPose.Prone,
                "maintain-tankhide");
            return;
        }

        if (state.ContactCrouchOwned && now >= state.ContactUntil)
            state.ContactCrouchOwned = false;

        // Non-decision frame: re-assert the latched pose rather than re-resolving cover
        // posture and crouch ownership below (an as-yet-unlatched soldier falls through
        // to the full resolution so a first pose is never blocked).
        if (!resolvePose && state.HasLatchedTacticalPose)
        {
            EnsureTacticalPose(ai, soldier, state.LatchedTacticalPose, "maintain-stagger");
            return;
        }

        if (OwnsCurrentCoverClearancePose(soldier, state))
        {
            EnsureTacticalPose(ai, soldier, SoldierPose.Idle, "maintain-clearance");
            return;
        }
        // The cover evaluation owns the pose while holding cover against a known
        // threat, independent of the short contact-halt timer. Tying it to
        // ContactUntil let this pose source alternate with the generic crouch fallback
        // as the enemy flicked in and out of sight, which the latch amplified into a
        // sustained prone<->crouch loop.
        var coverPostureOwned = CoverPostureOwnershipCore.CoverPoseOwned(
            state.HasThreatPosition, IsOnUsableCover(soldier), state.DefensiveCoverHold);
        if (coverPostureOwned &&
            TryGetCurrentCoverEvaluation(
                soldier,
                state,
                state.LastThreatPosition,
                now,
                out var coverEvaluation))
        {
            // A cover re-evaluation that flips crouch->prone must persist before it
            // drops an owned crouch below the parapet. Suppression and pinning were
            // already handled above and reach this only through their instant paths.
            var proposedPose = ApplyCoverDowngradeHysteresis(
                state, coverEvaluation.Pose, now);
            if (proposedPose == SoldierPose.Idle)
                ClaimCoverClearancePose(soldier, state);
            else
                ClearCoverClearancePose(state);
            EnsureTacticalPose(ai, soldier, proposedPose, "maintain-covereval");
            return;
        }
        if (ShouldOwnCrouch(state, now))
        {
            // Only the suppression reason must be softened into the recovery pose.
            // Defensive-hold, contact-crouch, and tactical-crouch owners keep
            // proposing plain Crouch — their pose ownership is untouched by this fix.
            var suppressionOwnsCrouch = Settings.DangerReactionsEnabled.Value &&
                                         state.SuppressionPoseOwned && !state.Pinned;
            EnsureTacticalPose(
                ai,
                soldier,
                suppressionOwnsCrouch ? SuppressionRecoveryPose(soldier) : SoldierPose.Crouch,
                suppressionOwnsCrouch ? "maintain-suppr-crouch" : "maintain-crouch");
        }
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
