using System;
using AncientWarfare3.core.lineage;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var state = new ArmyStallWatchdogState();
Require(ArmyStallWatchdogRules.RecordRouteFailure(state) ==
        ArmyStallRecoveryAction.RebuildRoute,
    "first route failure should rebuild the same target route");
Require(ArmyStallWatchdogRules.RecordRouteFailure(state) ==
        ArmyStallRecoveryAction.AlternateEndpoint,
    "second route failure should choose a same-target endpoint");
Require(ArmyStallWatchdogRules.RecordRouteFailure(state) ==
        ArmyStallRecoveryAction.AlternateEndpoint,
    "repeated route failure must not replace an open strategic target");

var replanState = new ArmyStallWatchdogState();
ArmyStallWatchdogRules.RecordRouteFailure(replanState);
ArmyStallWatchdogRules.RecordRouteFailure(replanState);
Require(ArmyStallWatchdogRules.RecordReplanResult(replanState,
        succeeded: false) == ArmyStallRecoveryAction.AlternateEndpoint,
    "failed same-target replans must not change target");

Require(!ArmyRtsMissionLockRules.CanHandoffAfterRecovery(
        ArmyRtsMissionReleaseCause.PathFailed, objectiveOpen: true),
    "a failed transport recovery must retain an open strategic target");
Require(!ArmyRtsMissionLockRules.CanHandoffAfterRecovery(
        ArmyRtsMissionReleaseCause.TargetInvalid, objectiveOpen: true),
    "an open target cannot be handed off by watchdog recovery");
Require(ArmyRtsMissionLockRules.CanHandoffAfterRecovery(
        ArmyRtsMissionReleaseCause.TargetInvalid, objectiveOpen: false),
    "a closed target may be handed off");

Console.WriteLine("ArmyRtsWatchdogLock.Tests: PASS");
