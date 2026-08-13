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

Console.WriteLine("ArmyRtsWatchdogLock.Tests: PASS");
