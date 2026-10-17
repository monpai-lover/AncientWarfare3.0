using AncientWarfare3.core.lineage;

var tests = new (string Name, Action Test)[]
{
    ("unrecoverable lower army id cannot starve other captains", CaptainFairness),
    ("captain retry respects cooldown and resumes in Large entry point", CooldownAdvances),
    ("missing source city leaves field-army recovery retryable", MissingSourceCanRetry),
    ("succession queue processes each kingdom once in its budget", KingdomFairness),
    ("invalid armies are removed without blocking healthy work", StaleArmyDoesNotBlock),
    ("disposed kingdoms are ignored before reading their data-backed id", DisposedKingdomIsIgnored),
    ("a failed establishment request remains retryable", FailedRequestCanRetry),
    ("a single-slot recovery budget still advances to other armies", SingleSlotFairness),
    ("replicas do not mutate recovery state", ReplicaDoesNotRecover),
    ("zero recovery budget preserves pending captain work", ZeroBudgetPreservesWork),
    ("an existing eligible captain retains command", ExistingCaptainRetained),
    ("a temporarily rejected establishment request remains retryable", RejectedRequestCanRetry),
    ("production levy requests tolerate disposed entities with diagnostics disabled", () => TemporaryLevyRequestRuntimeTests.Run(false)),
    ("production levy requests tolerate disposed entities with diagnostics enabled", () => TemporaryLevyRequestRuntimeTests.Run(true)),
    ("route rejection tolerates an already disposed army", () => ArmyServiceBoundaryRuntimeTests.Run("DisposedArmyRoute")),
    ("route requests reject a disposed captain retaining a tile", () => ArmyServiceBoundaryRuntimeTests.Run("DisposedCaptainRoute")),
    ("route rejection preserves valid army identity", () => ArmyServiceBoundaryRuntimeTests.Run("InvalidRouteTarget")),
    ("valid route replacement preserves provider behavior", () => ArmyServiceBoundaryRuntimeTests.Run("ValidRouteReplacement")),
    ("retreat observation tolerates disposed cities", () => ArmyServiceBoundaryRuntimeTests.Run("DisposedCityObservation")),
    ("retreat observation removes a city whose kingdom was disposed", () => ArmyServiceBoundaryRuntimeTests.Run("DisposedKingdomObservation")),
    ("retreat control-change handles a disposed current owner", () => ArmyServiceBoundaryRuntimeTests.Run("DisposedOwnerControlChange")),
    ("retreat city destruction handles a disposed previous owner", () => ArmyServiceBoundaryRuntimeTests.Run("DisposedOwnerCityDestruction")),
    ("retreat events preserve valid transfer and removal", () => ArmyServiceBoundaryRuntimeTests.Run("ValidRetreatEvents")),
    ("war-ended synthetic actors clean up without army or return arrival", () => SyntheticDemobilizationRuntimeTests.Run("NoArmyOrArrivalRequired")),
    ("synthetic cleanup preserves its bounded continuation", () => SyntheticDemobilizationRuntimeTests.Run("BoundedBatches")),
    ("synthetic cleanup preserves permanent and unrelated actors", () => SyntheticDemobilizationRuntimeTests.Run("PermanentAndUnrelatedActorsSurvive"))
};
int failed = 0;
foreach (var test in tests)
{
    try { test.Test(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message); }
}
Console.WriteLine($"Succession recovery runtime: {tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static void CaptainFairness()
{
    Reset();
    Kingdom kingdom = AddKingdom(1);
    Army blocked = AddArmy(10, kingdom, eligible: false);
    Army healthy = AddArmy(20, kingdom, eligible: true);
    ArmyRtsSuccessionRecoveryService.OnCaptainVacated(blocked, 99);
    ArmyRtsSuccessionRecoveryService.OnCaptainVacated(healthy, 98);
    int processed = ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(2);
    Require(processed == 2 && blocked.CaptainChecks == 1,
        "the same blocked army consumed more than one recovery opportunity");
    Require(healthy.Captain == healthy.Members[0], "the later army never received its eligible captain");
    Require(ArmyRtsSuccessionRecoveryService.PendingCaptainCount == 1,
        "the blocked army must remain queued without retaining the repaired army");
}

static void CooldownAdvances()
{
    Reset();
    Army army = AddArmy(10, AddKingdom(1), eligible: false);
    ArmyRtsSuccessionRecoveryService.OnCaptainVacated(army, 99);
    ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1);
    Require(army.CaptainChecks == 1, "initial captain recovery was not attempted");
    army.Members[0].Adult = true;
    for (int cycle = 1; cycle < ArmyRtsSuccessionRecoveryRules.CaptainRecoveryRetryCooldownCycles; cycle++)
        ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1);
    Require(army.CaptainChecks == 1 && army.Captain == null,
        "captain retries ignored their bounded cooldown");
    ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1);
    Require(army.Captain == army.Members[0],
        "Large-mode recovery did not advance its cooldown clock");
}

static void MissingSourceCanRetry()
{
    Reset();
    Kingdom kingdom = AddKingdom(1);
    ArmyRtsControllerService.Missions.Add(new ArmyRtsMission { ArmyId = 100, KingdomId = kingdom.id });
    ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1);
    Require(TemporaryLevyService.Requests.Count == 0, "recovery ran without a source city");
    kingdom.capital = new City { data = new Data { id = 50 } };
    ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1);
    ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1);
    Require(TemporaryLevyService.Requests.Count == 1,
        "a temporary missing capital permanently suppressed field-army reestablishment");
}

static void KingdomFairness()
{
    Reset();
    Kingdom first = AddKingdom(1);
    Kingdom second = AddKingdom(2);
    Army firstArmy = AddArmy(10, first, eligible: true);
    Army secondArmy = AddArmy(20, second, eligible: true);
    firstArmy.Captain = firstArmy.Members[0];
    secondArmy.Captain = secondArmy.Members[0];
    ArmyRtsSuccessionRecoveryService.OnKingInstalled(first, first.king);
    ArmyRtsSuccessionRecoveryService.OnKingInstalled(second, second.king);
    int processed = ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(2);
    Require(processed == 2 && ArmyRtsControllerService.Rehydrated.SequenceEqual(new[] { 10L, 20L }),
        "a completed first kingdom was revisited instead of the next queued kingdom");
}

static void StaleArmyDoesNotBlock()
{
    Reset();
    Kingdom kingdom = AddKingdom(1);
    Army stale = AddArmy(10, kingdom, eligible: false);
    Army healthy = AddArmy(20, kingdom, eligible: true);
    ArmyRtsSuccessionRecoveryService.OnCaptainVacated(stale, 99);
    ArmyRtsSuccessionRecoveryService.OnCaptainVacated(healthy, 98);
    World.world.armies.Items.Remove(stale.id);
    ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(2);
    Require(healthy.Captain == healthy.Members[0], "stale queued army blocked healthy recovery");
}

static void DisposedKingdomIsIgnored()
{
    Reset();
    Kingdom kingdom = AddKingdom(1);
    Actor king = kingdom.king;
    // CoreSystemObject.Dispose clears data, while NanoObject.id still calls
    // CoreSystemObject.getID, which dereferences data.id in the game DLL.
    kingdom.data = null;
    ArmyRtsSuccessionRecoveryService.OnKingInstalled(kingdom, king);
    Require(ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1) == 0,
        "a disposed kingdom enqueued succession work");
}

static void FailedRequestCanRetry()
{
    Reset();
    Kingdom kingdom = AddKingdom(1);
    kingdom.capital = new City();
    ArmyRtsControllerService.Missions.Add(new ArmyRtsMission { ArmyId = 100, KingdomId = kingdom.id });
    TemporaryLevyService.ThrowRequest = true;
    bool failureObserved = false;
    try { ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1); }
    catch (InvalidOperationException) { failureObserved = true; }
    Require(failureObserved, "the injected failure must remain visible to the caller");
    TemporaryLevyService.ThrowRequest = false;
    ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1);
    Require(TemporaryLevyService.Requests.Count == 1,
        "a failed request was incorrectly coalesced as successful");
}

static void RejectedRequestCanRetry()
{
    Reset();
    Kingdom kingdom = AddKingdom(1);
    kingdom.capital = new City();
    ArmyRtsControllerService.Missions.Add(new ArmyRtsMission { ArmyId = 100, KingdomId = kingdom.id });
    // The real levy service rejects without throwing while emergency state
    // is absent or the kingdom is in a restoration campaign.
    TemporaryLevyService.AcceptRequest = false;
    ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1);
    Require(TemporaryLevyService.Requests.Count == 0, "a rejected request scheduled recruitment");
    TemporaryLevyService.AcceptRequest = true;
    ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1);
    ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1);
    Require(TemporaryLevyService.Requests.Count == 1,
        "silent request rejection permanently suppressed reestablishment");
}

static void SingleSlotFairness()
{
    Reset();
    Kingdom kingdom = AddKingdom(1);
    Army blocked = AddArmy(10, kingdom, eligible: false);
    Army healthy = AddArmy(20, kingdom, eligible: true);
    ArmyRtsSuccessionRecoveryService.OnCaptainVacated(blocked, 99);
    ArmyRtsSuccessionRecoveryService.OnCaptainVacated(healthy, 98);
    Require(ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1) == 1,
        "the first recovery exceeded its single-slot budget");
    Require(ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1) == 1,
        "cooling work consumed the second pass budget");
    Require(healthy.Captain == healthy.Members[0] && blocked.CaptainChecks == 1,
        "a cooling lower-id army prevented later recovery");
}

static void ReplicaDoesNotRecover()
{
    Reset();
    Army army = AddArmy(10, AddKingdom(1), eligible: true);
    ArmyRtsSuccessionRecoveryService.OnCaptainVacated(army, 99);
    ArmyRtsRuntimeMode.ShouldCommit = false;
    Require(ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1) == 0 &&
        army.Captain == null && army.CaptainChecks == 0,
        "a replica executed authority recovery");
    Require(ArmyRtsSuccessionRecoveryService.PendingCaptainCount == 1,
        "skipping replica recovery consumed pending work");
}

static void ZeroBudgetPreservesWork()
{
    Reset();
    Army army = AddArmy(10, AddKingdom(1), eligible: true);
    ArmyRtsSuccessionRecoveryService.OnCaptainVacated(army, 99);
    Require(ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(0) == 0 &&
        army.CaptainChecks == 0 && ArmyRtsSuccessionRecoveryService.PendingCaptainCount == 1,
        "zero-budget recovery consumed pending captain work");
}

static void ExistingCaptainRetained()
{
    Reset();
    Army army = AddArmy(10, AddKingdom(1), eligible: true);
    Actor captain = army.Members[1];
    army.Captain = captain;
    ArmyRtsSuccessionRecoveryService.OnCaptainVacated(army, 99);
    ArmyRtsSuccessionRecoveryService.ProcessPendingRecoveries(1);
    Require(army.Captain == captain && ArmyRtsSuccessionRecoveryService.PendingCaptainCount == 0,
        "a stale vacancy notification replaced the recovered live captain");
}

static void Reset()
{
    World.world = new World();
    ArmyRtsSuccessionRecoveryService.Reset();
    ArmyRtsRuntimeMode.ShouldCommit = true;
    ArmyRtsControllerService.Missions.Clear();
    ArmyRtsControllerService.Rehydrated.Clear();
    ArmyRtsAssignmentReconciliationService.Enqueued.Clear();
    KingdomWarDirectorService.Changed.Clear();
    TemporaryLevyService.Requests.Clear();
    TemporaryLevyService.ThrowRequest = false;
    TemporaryLevyService.AcceptRequest = true;
}
static Kingdom AddKingdom(long id)
{
    var kingdom = new Kingdom { data = new Data { id = id } };
    kingdom.king = new Actor { data = new Data { id = id * 1000 }, kingdom = kingdom };
    World.world.kingdoms.Add(kingdom);
    return kingdom;
}
static Army AddArmy(long id, Kingdom kingdom, bool eligible)
{
    var army = new Army { data = new Data { id = id }, Kingdom = kingdom };
    for (int i = 0; i < ArmyLogisticsRules.MinimumOperationalForce; i++)
        army.Members.Add(new Actor { data = new Data { id = id * 100 + i },
            kingdom = kingdom, army = army, Adult = eligible });
    World.world.armies.Add(army);
    ArmyRtsControllerService.Missions.Add(new ArmyRtsMission { ArmyId = id, KingdomId = kingdom.id });
    return army;
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
