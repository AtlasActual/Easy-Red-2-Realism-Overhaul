using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class ContactResponseState
{
    internal bool Relocating;
    internal float NextDecisionAt;
    internal float RelocateUntil;
    internal float RelocateLastDistance;
    internal float RelocateLastProgressAt;
    internal Vector3 RelocateLastProgressPosition;
    internal IntPtr RelocateDestinationPointer;
    internal Vector3 RelocateDestinationPosition;
    internal bool RelocationPausedBySuppression;
    internal bool RelocationPausedByCloseFire;
    internal float NextRelocationAllowedAt;
    internal IntPtr ReservedCoverId;
    internal Vector3 ReservedCoverPosition;
    internal IntPtr FailedCoverId;
    internal float FailedCoverUntil;
    internal int ConsecutiveCoverSearchFailures;
    internal bool FireInhibitedByMovement;
    internal bool FireInhibitedByRange;
    internal bool FireInhibitedByArmoredTarget;
    internal bool FireRestorePending;
    internal bool ContactResponseActive;
    internal bool MovementInhibitedByContactResponse;
    internal bool SuppressionMovementOwned;
    internal bool SuppressionPoseOwned;
    internal bool SuppressionFireInhibited;
    internal float SuppressionCrouchUntil;
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
    internal float HoldCoverUntil;
    internal float ManeuverCoverMinimumHoldUntil;
    internal float ManeuverCoverReleaseUntil;
    internal IntPtr ManeuverCoverReleasedId;
    internal IntPtr ManeuverCoverAnchorId;
    internal Vector3 ManeuverCoverAnchorPosition;
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
    internal bool HasFiredAtAttackContact;
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
    internal float TacticalPoseHoldUntil;
    internal float CoverPostureDowngradeSince;
    // GetFavouriteFightingPose stagger cache (round-robin K=3): the last decision-frame
    // outcome of the non-safety pose resolution — whether the mod overrode the native
    // favourite pose and with which pose — reused on this soldier's non-decision frames.
    // HasFightingPoseCache stays false until the first full resolution so a first result
    // is never deferred. The pinned/flame safety pose is recomputed every frame, above.
    internal bool HasFightingPoseCache;
    internal bool FightingPoseOverrode;
    internal SoldierPose FightingPoseCached;
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
    internal const float OccupancyRadiusMeters = 1.75f;
    internal const float DecisionIntervalSeconds = 12f;
    internal const float MoveProgressWindowSeconds = 6f;
    internal const float RelocationCooldownSeconds = 15f;
    internal const float MinimumManeuverCoverHoldSeconds = 18f;
    internal const float StandingCoverPenalty = 225f;
    internal const float DefensiveAnchorLeashMeters = 4f;
}
