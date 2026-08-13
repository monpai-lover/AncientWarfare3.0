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

Console.WriteLine("ArmyRtsMissionLock.Tests: PASS");
