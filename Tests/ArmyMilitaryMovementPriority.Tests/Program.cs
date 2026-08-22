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
Require(ArmyMilitaryMovementPriorityRules.IsActiveRtsObjectiveOwner(
        controllerActive: true, ownsObjective: true),
    "a controller-owned RTS objective must enter P0");
Require(!ArmyMilitaryMovementPriorityRules.IsActiveRtsObjectiveOwner(
        controllerActive: false, ownsObjective: true),
    "pre-deployment escort ownership must not enter P0");
Require(!ArmyMilitaryMovementPriorityRules.IsActiveRtsObjectiveOwner(
        controllerActive: true, ownsObjective: false),
    "a controller in rally without a physical objective must not enter P0");
Require(ArmyMilitaryMovementPriorityRules.ResolveP0ChunkCount(
        remainingCount: 91, batchSize: 32) == 91,
    "active military movement must not be cut off by the ordinary actor budget");
Require(!ArmyMilitaryMovementPriorityRules.CanAdmitOrdinaryActorWork(
        p0SlicePending: true),
    "ordinary actor work must wait until the selected P0 slice completes");
Require(ArmyMilitaryMovementPriorityRules.CanAdmitOrdinaryActorWork(
        p0SlicePending: false),
    "ordinary actor work may resume after the P0 slice completes");

string runner = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "Code", "core", "performance",
    "AWCooperativeActorPostRunner.cs"));
Require(runner.Contains(
        "return militaryP0Cursor < militaryP0ActorIds.Count &&"),
    "a completed military P0 snapshot must not re-enter later in the same actor cycle");
Require(runner.Contains(
        "ArmyMilitaryMovementPriorityIndex.WasProcessed(actorId)"),
    "an actor already advanced by transport P0 must not run the generic P0 pipeline again");

Console.WriteLine("ArmyMilitaryMovementPriority.Tests: PASS");
