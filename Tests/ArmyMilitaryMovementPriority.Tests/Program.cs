using AncientWarfare3.core.performance;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Require(ArmyMilitaryMovementPriorityRules.ShouldRunP0(
        largeSchedulerActive: true, ownsRtsObjective: true,
        isLandGuardFollow: false),
    "an RTS objective owner must run in a skipped batch before ordinary work");
Require(ArmyMilitaryMovementPriorityRules.ShouldRunP0(
        largeSchedulerActive: true, ownsRtsObjective: false,
        isLandGuardFollow: true),
    "a land-following royal guard must run in a skipped batch");
Require(!ArmyMilitaryMovementPriorityRules.ShouldRunP0(
        largeSchedulerActive: true, ownsRtsObjective: false,
        isLandGuardFollow: false),
    "ordinary army membership must not receive military P0");

Console.WriteLine("ArmyMilitaryMovementPriority.Tests: PASS");
