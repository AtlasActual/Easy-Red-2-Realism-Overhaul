using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class ContactResponseState
{
    internal bool Relocating;
    internal float NextDecisionAt;
    // Decision-cadence bookkeeping for ContactResponse.RunsDecisionThisFrame: when this
    // soldier last ran its decision tail, and the verdict already resolved for the
    // current frame. Several call sites ask for the same soldier within one frame
    // (movement prefix, pose write, postfix) and must all be told the same thing.
    internal float LastDecisionAt;
    internal int DecisionVerdictFrame = -1;
    internal bool DecisionVerdict;
    // When this soldier was last admitted by the per-frame service budget. Drives the
    // starvation guard so a soldier cannot be squeezed out indefinitely by arrival order.
    // Wall-clock rather than a frame index: a frame count scales with framerate and stops
    // being a backstop on a fast machine.
    internal float LastServicedAt;
    // Last native StopMove issued for this soldier. Four patched entry points can resolve
    // the same halted soldier in one frame (both movement prefixes, their postfixes, and
    // the native-hold branch of SequentialUpdate), and re-issuing an identical stop is a
    // locomotion write the game has already performed this frame.
    internal int LastStopMoveFrame = -1;
    internal SoldierPose LastStopMovePose;
    // Short-lived result of the squad covering-fire scan, which is O(all contact states)
    // and was being answered per soldier per frame.
    internal float CoveringFireCheckedUntil;
    internal bool CoveringFireEstablished;
    // Next time the write-through's re-assertion tail (pose latch, fire permission, body
    // facing) is due. The locomotion gate itself is never throttled.
    internal float NextWriteThroughMaintenanceAt;
    internal float RelocateUntil;
    internal float RelocateLastDistance;
    internal float RelocateLastProgressAt;
    internal float RelocateLastDestinationProgressAt;
    internal bool RelocatePathRetryUsed;
    internal Vector3 RelocateLastProgressPosition;
    internal IntPtr RelocateDestinationPointer;
    internal Vector3 RelocateDestinationPosition;
    internal bool RelocationPausedBySuppression;
    internal float NextRelocationAllowedAt;
    internal IntPtr ReservedCoverId;
    internal Vector3 ReservedCoverPosition;
    internal IntPtr FailedCoverId;
    internal float FailedCoverUntil;
    internal int ConsecutiveCoverSearchFailures;
    // Output of the fire arbiter (plan 017), not an input: ApplyFireDecision recomputes it
    // every frame so the debug overlay and the suppressive-fire scheduler can read why the
    // trigger is withheld. Nothing latches it.
    internal bool FireInhibitedByMovement;
    internal bool FireInhibitedByRange;
    internal bool FireInhibitedByArmoredTarget;
    // Last blocker the fire arbiter committed, so the verbose trace fires on the decision
    // FLIP instead of every frame. Replaces the deleted FireRestorePending handshake flag.
    internal FireBlocker LastFireBlocker;
    // Soldier.StopFire sends a reliable SyncStopFire RPC on every invocation, while the
    // game's LateUpdate can restore triggerState even though this mod still owns the same
    // fire hold. Keep the network-active command edge-triggered until our policy releases
    // fire permission; the native trigger enum is not a durable indication of that hold.
    internal bool StopFireIssued;
    internal bool ContactResponseActive;
    internal bool MovementInhibitedByContactResponse;
    // Outputs of the movement arbiter (plan 018), not inputs. ApplyMovementDecision is the
    // only writer: MovementHalted is "the single write site stopped this soldier on its
    // last apply" (it drives the halt-spacing rising edge, the close-contact relocation
    // resume that RelocationPausedByCloseFire used to encode, and any future reader that
    // needs the fact rather than one particular halt's flag), and LastMovementOwner exists
    // so the verbose trace fires on the ownership FLIP instead of every frame.
    internal bool MovementHalted;
    internal MovementOwner LastMovementOwner;
    // Halt spacing (plan 018 item 3): the bounded window during which the lateral
    // dispersion step outranks the fighting halt it steps out of, and the long cooldown
    // that guarantees the check runs at most once per halt episode.
    internal float HaltSpacingMoveUntil;
    internal float HaltSpacingNextCheckAt;
    internal bool HasHaltSpacingTarget;
    internal Vector3 HaltSpacingTarget;
    internal bool SuppressionMovementOwned;
    internal bool SuppressionPoseOwned;
    internal bool SuppressionFireInhibited;
    internal float SuppressionCrouchUntil;
    internal float NextOverheadProtectionCheckAt;
    internal bool HasOverheadProtection;
    internal bool ContactCrouchOwned;
    internal bool CoverClearancePoseOwned;
    internal IntPtr CoverClearanceCoverId;
    internal bool StationaryThreatFacingOwned;
    internal bool ExposedReloadProneOwned;
    internal float TacticalCrouchUntil;
    internal float EngagementHoldUntil;
    internal float ContactUntil;
    internal bool Pinned;
    internal float PinnedUntil;
    internal float PinnedFireBlockedUntil;
    internal float PinnedSince;
    internal float PinnedImmunityUntil;
    internal float HoldCoverUntil;
    internal float ManeuverCoverMinimumHoldUntil;
    internal float ManeuverCoverReleaseUntil;
    internal IntPtr ManeuverCoverReleasedId;
    internal IntPtr ManeuverCoverAnchorId;
    internal Vector3 ManeuverCoverAnchorPosition;
    internal IntPtr OccupiedCoverClaimId;
    internal float OccupiedCoverClaimRefreshAt;
    internal bool DefensiveCoverHold;
    internal bool HasDefensiveCoverAnchor;
    internal IntPtr DefensiveCoverAnchorId;
    internal Vector3 DefensiveCoverAnchorPosition;
    internal bool DefensivePositionOwned;
    internal int DefensivePositionSquadId;
    internal int DefensivePositionObjectiveRevision;
    internal Vector3 DefensivePositionEntryPoint;
    internal bool PlayerHoldPositionOwned;
    internal Vector3 PlayerHoldCenter;
    internal float PlayerHoldRadius;
    internal Vector3 LastThreatPosition;
    internal bool HasThreatPosition;
    internal int SquadId;
    internal IntPtr AttackContactToken;
    internal float AttackContactLastSeenAt;
    internal bool AttackConditionsWereFavorable;
    internal float AttackHaltStartedAt;
    internal bool AttackProgressForced;
    internal IntPtr LastOutgoingShotTargetToken;
    internal float LastOutgoingShotAt;
    internal bool LastOutgoingShotWasStationary;
    internal IntPtr EvaluatedCoverPostureId;
    internal SoldierPose EvaluatedCoverPosture;
    internal bool EvaluatedCoverIsProtective;
    internal Vector3 EvaluatedCoverThreatDirection;
    internal float EvaluatedCoverPostureUntil;
    internal Vector3 PostureThreatAxis;
    internal Vector3 PostureThreatPendingAxis;
    internal float PostureThreatPendingSince;
    internal bool HasLatchedTacticalPose;
    internal SoldierPose LatchedTacticalPose;
    // Which owner currently holds the latched pose (single pose arbiter, plan 014). The
    // owner-aware latch compares this against each frame's arbitrated owner to decide
    // whether a transition is an immediate safety handoff or an anti-flicker-held change.
    internal PoseOwner LatchedPoseOwner;
    internal float TacticalPoseHoldUntil;
    internal float CoverPostureDowngradeSince;
    // Pose arbiter stagger cache (round-robin K=3): the last decision-frame outcome of
    // the interop-heavy DECISION tail (owner ranks d-i) - the owner it resolved and the
    // pose - reused on this soldier's non-decision frames by the maintain/favourite/
    // write-through fast paths. HasArbiterCache stays false until the first full
    // resolution so a first result is never deferred. The safety owners (required action,
    // pinned/fire) are recomputed every frame above the cache.
    internal bool HasArbiterCache;
    internal PoseOwner ArbiterCachedOwner;
    internal SoldierPose ArbiterCachedPose;
    internal InfantryCoverState CoverState;
    internal float NextUrgentCoverDecisionAt;
    internal bool MovementWatchActive;
    internal Vector3 MovementWatchPosition;
    internal Vector3 MovementWatchDestination;
    internal float MovementWatchLastProgressAt;
    internal float MovementStallHoldUntil;
    internal int MovementStallFailures;
    internal bool HasMovementStallDestination;
    internal Vector3 MovementStallDestination;
    internal ProposalSource MovementWatchSource = ProposalSource.None;
    // Rejected-pose-proposal trace dedupe (diagnostic only, verbose-logging gated).
    internal SoldierPose PoseTraceLastPose;
    internal string? PoseTraceLastSource;
    internal float PoseTraceLastAt;
    internal float PoseDriftTraceLastAt;
    // IsOnUsableCover is called 6-12x per soldier per frame from the pose paths;
    // this memo makes it evaluate once per frame and reuse the result for the
    // remaining calls that frame. -1 guarantees a miss on the very first call.
    internal int LastUsableCoverFrame = -1;
    internal bool UsableCoverCached;
}

internal static class InfantryCoverPolicy
{
    // These mechanics deliberately form one policy rather than a collection of
    // user-facing tuning knobs. Keeping them together prevents incompatible timer,
    // reservation, and scoring combinations from reintroducing cover churn.
    // Dense towns can contain dozens of weak roadside nodes before the first
    // trench or building slot in CoverManager's distance-ordered list. Scan a
    // broad inventory cheaply, then run the allocating ballistic/body/route model
    // only on a representative shortlist. The old 96 x full-detail loop could
    // issue several thousand physics queries from one sequential AI update.
    internal const int RawCandidateLimit = 96;
    internal const int ConstrainedRawCandidateLimit = 192;
    internal const int DetailedCandidateLimit = 12;
    internal const int NearestDetailedCandidateCount = 6;
    internal const int DefensiveDetailedCandidateLimit = 20;
    internal const int DefensiveNearestDetailedCandidateCount = 3;
    internal static float OccupancyRadiusMeters =>
        Mathf.Clamp(Settings.InfantrySeparationDistance.Value, 1.25f, 4f);
    // SCORING-ONLY dispersion radius (plan 016): the range within which another
    // soldier's cover reservation counts as a crowding neighbour in Score(). It is
    // deliberately NOT a filter distance — every hard reservation/occupancy check
    // keeps using the small OccupancyRadiusMeters above, because a 5 m exclusion
    // would routinely leave a squad with zero eligible cover in a trench or building
    // and strand soldiers in the open (plan 016 review).
    internal const float CoverDispersionSpacingMeters = 5f;
    internal const float DecisionIntervalSeconds = 12f;
    // Long enough for an ordinary native route into a nearby trench or building.
    // Once occupied, the claim is renewed at DecisionIntervalSeconds cadence.
    internal const float CoverReservationLeaseSeconds = 30f;
    internal const float MoveProgressWindowSeconds = 6f;
    internal const float RelocationCooldownSeconds = 15f;
    // D5 (plan 015): the fighting-position minimum hold is split so an attacker
    // with a live attack route rearms it for only a short assault tempo, while a
    // soldier with no attack route (a defender) keeps the long hold.
    internal const float MinimumManeuverCoverHoldSeconds = 18f;
    internal const float MinimumAttackCoverHoldSeconds = 7f;
    internal const float StandingCoverPenalty = 225f;
    internal const float DefensiveAnchorLeashMeters = 4f;
}
