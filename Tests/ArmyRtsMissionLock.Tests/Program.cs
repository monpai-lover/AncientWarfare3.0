using System;
using AncientWarfare3.core.lineage;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Require(!ArmyRtsMissionLockRules.CanReplaceTarget(
        ArmyRtsMissionReleaseCause.PathFailed),
    "path failure must retain the strategic target");
Require(!ArmyRtsMissionLockRules.CanReplaceTarget(
        ArmyRtsMissionReleaseCause.MemberStalled),
    "member stall must retain the strategic target");
Require(!ArmyRtsMissionLockRules.CanReplaceTarget(
        ArmyRtsMissionReleaseCause.SchedulerDelayed),
    "scheduler delay must retain the strategic target");
Require(ArmyRtsMissionLockRules.CanReplaceTarget(
        ArmyRtsMissionReleaseCause.TargetInvalid),
    "invalid target may release the strategic lock");
Require(ArmyRtsMissionLockRules.CanReplaceTarget(
        ArmyRtsMissionReleaseCause.WarEnded),
    "war end may release the strategic lock");

Require(ArmyRtsMemberObjectiveRules.ResolveTargetCityId(
        missionTargetCityId: 42L, routeFailureTargetCityId: 9L) == 42L,
    "member routing must retain the mission target city");
Require(ArmyRtsMemberObjectiveRules.ShouldSubmitMemberPath(
        hasObjective: true, ownsPath: false, nativeLocalPath: false,
        pathPending: false),
    "a member with a new objective must submit its own path");
Require(!ArmyRtsMemberObjectiveRules.ShouldSubmitMemberPath(
        hasObjective: true, ownsPath: true, nativeLocalPath: false,
        pathPending: false),
    "an owning member must not submit a duplicate path");
Require(ArmyRtsMemberObjectiveRules.ShouldOwnMemberObjective(
        missionActive: true, isCaptain: false, actorEligible: true,
        immediateCombat: false, transportActive: false),
    "an eligible RTS member must own its objective without formation state");
Require(!ArmyRtsMemberObjectiveRules.ShouldOwnMemberObjective(
        missionActive: true, isCaptain: false, actorEligible: true,
        immediateCombat: false, transportActive: true),
    "transport retains movement ownership until disembarkation");
Require(!ArmyRtsMemberObjectiveRules.ShouldReplaceMemberPath(
        hasObjective: true, recordedTargetTileId: 17,
        resolvedTargetTileId: 17, ownsPath: true,
        nativeLocalPath: true),
    "an unchanged member objective must retain its existing route");
Require(ArmyRtsMemberObjectiveRules.ShouldReplaceMemberPath(
        hasObjective: true, recordedTargetTileId: 17,
        resolvedTargetTileId: 23, ownsPath: true,
        nativeLocalPath: true),
    "a changed member objective must replace its old route");
Require(ArmyRtsMemberObjectiveRules.ShouldRecoverToMissionObjective(
        hasActiveMission: true, actorEligible: true,
        combatActive: false, transportActive: false),
    "a stalled member must recover toward its active mission objective");
Require(!ArmyRtsMemberObjectiveRules.ShouldRecoverToMissionObjective(
        hasActiveMission: true, actorEligible: true,
        combatActive: false, transportActive: true),
    "transport must retain ownership during member recovery");

Console.WriteLine("ArmyRtsMissionLock.Tests: PASS");
