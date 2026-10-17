using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.pathfinding;

var tests = new (string Name, Action Run)[]
{
    ("snapshot and strategic release precede transfer", PersistBeforeTransfer),
    ("transfer can detach defeated army before cleanup", DetachedDefenders),
    ("prepared transaction resumes without mission records", () => Resume(ArmyAbstractBattlePhase.Prepared)),
    ("transferred transaction resumes without mission records", () => Resume(ArmyAbstractBattlePhase.Transferred)),
    ("demobilization resumes at persisted actor cursor", () => Resume(ArmyAbstractBattlePhase.Demobilizing)),
    ("special garrison contributes to defense", SpecialDefenders),
    ("defeated special garrisons are demobilized", DefeatedSpecialDefenders),
    ("civil authorities and synthetic troops use correct disposal", ProtectedAndSynthetic),
    ("failed transfer retries persisted operation", RetryTransfer),
    ("completed transaction is idempotent", CompleteIsIdempotent),
    ("replica cannot execute abstract transactions", ReplicaDoesNotExecute),
    ("ended war cancels a prepared transfer without casualties", EndedWarCancelsPrepared),
    ("transfer completed before save still cleans losers after war ends", TransferBeforeSave),
    ("pending transaction recovery rotates within its frame budget", RecoveryBudgetRotates),
    ("completed history discovery is bounded and becomes idle", BoundedCompletedDiscovery),
    ("failed recovery cannot starve fresh battles", () => RecoveryDoesNotStarveNewBattle(4)),
    ("single-slot recovery leaves turns for fresh battles", () => RecoveryDoesNotStarveNewBattle(1))
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message); }
}
Console.WriteLine($"Abstract battle runtime: {tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static void PersistBeforeTransfer()
{
    Fixture f = NewFixture();
    bool preparedBeforeTransfer = false;
    bool releasedBeforeTransfer = false;
    f.Target.BeforeTransfer = () =>
    {
        ArmyAbstractBattleTransactionSnapshot snapshot = Read(f.Target);
        preparedBeforeTransfer = snapshot?.Phase == ArmyAbstractBattlePhase.Prepared &&
            snapshot.LoserArmyIds.Contains(f.Defender.id) &&
            f.Defender.units.All(actor => snapshot.LoserActorIds.Contains(actor.id));
        releasedBeforeTransfer = ArmyRouteProviderService.Cancelled.Contains(f.Attacker.id);
    };
    ArmyAbstractBattleService.ProcessFrame(4);
    Require(preparedBeforeTransfer, "full snapshot was absent when the city transfer began");
    Require(releasedBeforeTransfer, "the old strategic route remained owned during transfer");
    Require(Read(f.Target)?.Phase == ArmyAbstractBattlePhase.Complete, "transaction did not finish");
}

static void DetachedDefenders()
{
    Fixture f = NewFixture();
    Actor[] defeated = f.Defender.units.ToArray();
    f.Target.AfterTransfer = () => Detach(f.Defender);
    ArmyAbstractBattleService.ProcessFrame(4);
    Require(defeated.All(actor => !actor.Alive && actor.DeathCalls == 1),
        "city transfer discarded the only path to defeated actors");
    Require(Read(f.Target)?.Phase == ArmyAbstractBattlePhase.Complete, "detached cleanup did not finish");
}

static void Resume(ArmyAbstractBattlePhase phase)
{
    Fixture f = NewFixture();
    Actor[] defeated = f.Defender.units.ToArray();
    var snapshot = ArmyAbstractBattleTransactionRules.Prepare(f.War.id,
        f.Target.id, f.Target.id, 7, ArmyAbstractBattleOutcome.AttackVictory,
        f.AttackKingdom.id, f.Attacker.id, 123,
        new[] { f.Attacker.id, f.Defender.id },
        f.Attacker.units.Concat(defeated).Select(actor => actor.id),
        new[] { f.Defender.id }, defeated.Select(actor => actor.id));
    snapshot = ArmyAbstractBattleTransactionRules.Advance(snapshot, phase);
    if (phase >= ArmyAbstractBattlePhase.Transferred) f.Target.kingdom = f.AttackKingdom;
    if (phase == ArmyAbstractBattlePhase.Demobilizing)
    {
        defeated[0].die(false, AttackType.Other, false, false);
        snapshot.DemobilizationCursor = 1;
    }
    f.Target.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_TRANSACTION,
        ArmyAbstractBattleTransactionRules.Encode(snapshot));
    ArmyRtsControllerService.Missions.Clear();
    Detach(f.Defender);
    ArmyAbstractBattleService.ClearRuntime();
    ArmyAbstractBattleService.ProcessFrame(4);
    Require(f.Target.kingdom == f.AttackKingdom, "reload failed to finish the saved transfer");
    Require(defeated.All(actor => !actor.Alive && actor.DeathCalls == 1),
        "reload skipped or repeated defeated actor disposal");
    ArmyAbstractBattleTransactionSnapshot completed = Read(f.Target);
    Require(completed?.Phase == ArmyAbstractBattlePhase.Complete && completed.Sequence == 7,
        "reload restarted or lost the saved transaction");
}

static void SpecialDefenders()
{
    Fixture f = NewFixture(attackerCount: 5, defenderCount: 1);
    Army special = AddArmy(30, f.DefendKingdom, f.Target, 20, AWArmyRole.FeudatoryGarrison);
    ArmyAbstractBattleService.ProcessFrame(4);
    Require(f.Source.kingdom == f.DefendKingdom && f.Target.kingdom == f.DefendKingdom,
        "the anchored garrison was omitted from the defensive battle value");
    Require(special.units.All(actor => actor.Alive), "winning garrison was demobilized");
    Require(Read(f.Target)?.Outcome == ArmyAbstractBattleOutcome.DefenseVictory,
        "defense result was not saved");
}

static void DefeatedSpecialDefenders()
{
    Fixture f = NewFixture(attackerCount: 100, defenderCount: 2);
    var roles = new[] { AWArmyRole.RoyalGuard, AWArmyRole.SlaveArmy,
        AWArmyRole.BorderArmy, AWArmyRole.FeudatoryGarrison };
    var extra = roles.Select((role, index) => AddArmy(30 + index,
        f.DefendKingdom, f.Target, 2, role)).ToArray();
    Actor[] defeated = extra.SelectMany(army => army.units).ToArray();
    ArmyAbstractBattleService.ProcessFrame(4);
    Require(defeated.All(actor => !actor.Alive), "an anchored role garrison escaped defeated-army cleanup");
    Require(extra.All(army => Read(f.Target).LoserArmyIds.Contains(army.id)),
        "the persisted loser roster omitted a role garrison");
}

static void ProtectedAndSynthetic()
{
    Fixture f = NewFixture(defenderCount: 3);
    Actor king = f.Defender.units[0];
    king.King = true;
    Actor synthetic = f.Defender.units[1];
    synthetic.Synthetic = true;
    Actor ordinary = f.Defender.units[2];
    ArmyAbstractBattleService.ProcessFrame(4);
    Require(king.Alive && king.army == null && !king.Warrior,
        "protected authority was killed or retained in the defeated army");
    Require(synthetic.SyntheticRemoved && synthetic.DeathCalls == 0,
        "synthetic troop used personal-history death instead of synthetic removal");
    Require(!ordinary.Alive && ordinary.DeathCalls == 1, "ordinary loser was not disposed exactly once");
}

static void RetryTransfer()
{
    Fixture f = NewFixture();
    Actor[] defeated = f.Defender.units.ToArray();
    f.Target.FailTransfer = true;
    ArmyAbstractBattleService.ProcessFrame(1);
    ArmyAbstractBattleTransactionSnapshot prepared = Read(f.Target);
    Require(prepared?.Phase == ArmyAbstractBattlePhase.Prepared,
        "failed transfer did not retain its prepared snapshot");
    Require(defeated.All(actor => actor.Alive), "losers were demobilized before transfer succeeded");
    ArmyRtsControllerService.Missions.Clear();
    f.Target.FailTransfer = false;
    ArmyAbstractBattleService.ClearRuntime();
    ArmyAbstractBattleService.ProcessFrame(4);
    Require(Read(f.Target)?.Phase == ArmyAbstractBattlePhase.Complete &&
            Read(f.Target).Sequence == prepared.Sequence && defeated.All(actor => !actor.Alive),
        "saved failed transfer could not resume after missions were lost");
}

static void CompleteIsIdempotent()
{
    Fixture f = NewFixture();
    Actor[] defeated = f.Defender.units.ToArray();
    ArmyAbstractBattleService.ProcessFrame(4);
    string saved = Encode(f.Target);
    ArmyAbstractBattleService.ClearRuntime();
    for (int i = 0; i < 3; i++) ArmyAbstractBattleService.ProcessFrame(4);
    Require(f.Target.TransferCalls == 1 && defeated.All(actor => actor.DeathCalls == 1),
        "recovery repeated a completed transaction's side effects");
    Require(Encode(f.Target) == saved && Read(f.Target)?.Phase == ArmyAbstractBattlePhase.Complete,
        "completed transaction was changed or never persisted");
}

static void ReplicaDoesNotExecute()
{
    Fixture f = NewFixture();
    AW3MultiplayerReplicaScope.IsReplicaSession = true;
    ArmyAbstractBattleService.ProcessFrame(4);
    Require(f.Target.TransferCalls == 0 && Read(f.Target) == null,
        "replica executed authoritative battle effects");
}

static void EndedWarCancelsPrepared()
{
    Fixture f = NewFixture();
    f.Target.FailTransfer = true;
    ArmyAbstractBattleService.ProcessFrame(1);
    Require(Read(f.Target)?.Phase == ArmyAbstractBattlePhase.Prepared,
        "fixture must have a saved pending transfer");
    f.War.Ended = true;
    f.Target.FailTransfer = false;
    ArmyRtsControllerService.Missions.Clear();
    ArmyAbstractBattleService.ClearRuntime();

    ArmyAbstractBattleService.ProcessFrame(1);

    Require(f.Target.kingdom == f.DefendKingdom && f.Target.TransferCalls == 1,
        "recovery captured a city after peace");
    Require(f.Defender.units.All(actor => actor.Alive) &&
            Read(f.Target)?.Phase == ArmyAbstractBattlePhase.Complete,
        "cancelled stale transfer left casualties or an indefinitely pending transaction");
}

static void TransferBeforeSave()
{
    Fixture f = NewFixture();
    Actor[] defeated = f.Defender.units.ToArray();
    f.Target.AfterTransfer = () => throw new InvalidOperationException("interrupted after transfer");
    ArmyAbstractBattleService.ProcessFrame(1);
    Require(Read(f.Target)?.Phase == ArmyAbstractBattlePhase.Prepared &&
            f.Target.kingdom == f.AttackKingdom,
        "fixture must interrupt after the engine transfer but before phase persistence");
    f.War.Ended = true;
    ArmyRtsControllerService.Missions.Clear();
    ArmyAbstractBattleService.ClearRuntime();

    ArmyAbstractBattleService.ProcessFrame(1);

    Require(f.Target.TransferCalls == 1 && defeated.All(actor => actor.DeathCalls == 1),
        "recovery repeated the transfer or lost defeated actors after the war ended");
    Require(Read(f.Target)?.Phase == ArmyAbstractBattlePhase.Complete,
        "already transferred transaction did not finish");
}

static void RecoveryBudgetRotates()
{
    Fixture f = NewFixture();
    f.Target.FailTransfer = true;
    ArmyAbstractBattleService.ProcessFrame(1);
    var other = new City { data = new Data { id = 103 }, kingdom = f.DefendKingdom,
        FailTransfer = true };
    World.world.cities.Add(other);
    ArmyAbstractBattleTransactionSnapshot second = Read(f.Target).Clone();
    second.TargetCityId = other.id;
    second.TransferredCityId = other.id;
    other.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_TRANSACTION,
        ArmyAbstractBattleTransactionRules.Encode(second));
    ArmyRtsControllerService.Missions.Clear();
    ArmyAbstractBattleService.ClearRuntime();

    ArmyAbstractBattleService.ProcessFrame(0);
    Require(f.Target.TransferCalls == 1 && other.TransferCalls == 0,
        "zero recovery budget performed work");
    ArmyAbstractBattleService.ProcessFrame(1);
    Require(f.Target.TransferCalls == 2 && other.TransferCalls == 0,
        "first frame exceeded its single-transaction recovery budget");
    ArmyAbstractBattleService.ProcessFrame(1);
    Require(f.Target.TransferCalls == 2 && other.TransferCalls == 1,
        "permanently blocked transaction starved the next pending city");
}

static void BoundedCompletedDiscovery()
{
    Fixture f = NewFixture();
    ArmyRtsControllerService.Missions.Clear();
    for (int i = 0; i < 100; i++)
    {
        var city = new City { data = new Data { id = 1000 + i }, kingdom = f.AttackKingdom };
        World.world.cities.Add(city);
        var complete = ArmyAbstractBattleTransactionRules.Prepare(f.War.id,
            city.id, city.id, 1, ArmyAbstractBattleOutcome.AttackVictory,
            f.AttackKingdom.id, f.Attacker.id, 1, new[] { f.Attacker.id },
            Enumerable.Range(0, 1000).Select(id => (long)id),
            Array.Empty<long>(), Enumerable.Range(1000, 1000).Select(id => (long)id));
        complete = ArmyAbstractBattleTransactionRules.Advance(complete,
            ArmyAbstractBattlePhase.Complete);
        city.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PHASE, (int)complete.Phase);
        city.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_TRANSACTION,
            ArmyAbstractBattleTransactionRules.Encode(complete));
    }
    ArmyAbstractBattleService.ClearRuntime();
    int PhaseReads() => World.world.cities.Sum(city =>
        city.data.ReadCount(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PHASE));
    int HistoricalSnapshotReads() => World.world.cities.Where(city => city.id >= 1000)
        .Sum(city => city.data.ReadCount(LineageKeys.AW_RTS_ABSTRACT_BATTLE_TRANSACTION));
    int before = PhaseReads();
    ArmyAbstractBattleService.ProcessFrame(4);
    Require(PhaseReads() - before > 0 && PhaseReads() - before <= 32,
        "reload discovery scanned more than one bounded city slice");
    for (int frame = 0; frame < 10 && PhaseReads() < World.world.cities.Count; frame++)
    {
        before = PhaseReads();
        ArmyAbstractBattleService.ProcessFrame(4);
        Require(PhaseReads() - before <= 32, "later discovery exceeded the city slice budget");
    }
    Require(PhaseReads() >= World.world.cities.Count, "bounded discovery never reached every city");
    Require(HistoricalSnapshotReads() == 0,
        "completed battles decoded historical soldier rosters during discovery");
    before = PhaseReads();
    for (int frame = 0; frame < 3; frame++) ArmyAbstractBattleService.ProcessFrame(4);
    Require(PhaseReads() == before, "idle frames repeatedly rescanned completed cities");
    Require(ArmyAbstractBattleService.PendingWorkUpperBound == 0,
        "completed discovery kept scheduling idle historical battle work");
}

static void RecoveryDoesNotStarveNewBattle(int budget)
{
    Fixture f = NewFixture();
    var blocked = new List<City>();
    for (int i = 0; i < 4; i++)
    {
        var city = new City { data = new Data { id = 200 + i },
            kingdom = f.DefendKingdom, FailTransfer = true };
        World.world.cities.Add(city);
        var pending = ArmyAbstractBattleTransactionRules.Prepare(f.War.id,
            city.id, city.id, 1, ArmyAbstractBattleOutcome.AttackVictory,
            f.AttackKingdom.id, 10000 + i, 1, new[] { 10000L + i },
            Array.Empty<long>(), Array.Empty<long>(), Array.Empty<long>());
        city.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PHASE, (int)pending.Phase);
        city.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_TRANSACTION,
            ArmyAbstractBattleTransactionRules.Encode(pending));
        blocked.Add(city);
    }
    ArmyAbstractBattleService.ClearRuntime();
    int Attempts() => blocked.Sum(city => city.TransferCalls) + f.Target.TransferCalls;
    for (int frame = 0; frame < 4 && f.Target.kingdom != f.AttackKingdom; frame++)
    {
        int before = Attempts();
        ArmyAbstractBattleService.ProcessFrame(budget);
        Require(Attempts() - before <= budget,
            "recovery fairness expanded the total battle attempt budget");
    }
    Require(f.Target.kingdom == f.AttackKingdom,
        "permanently failing transfers starved an independent new battle");
    Require(blocked.All(city => city.kingdom == f.DefendKingdom &&
            Read(city)?.Phase == ArmyAbstractBattlePhase.Prepared),
        "fairness discarded failed prepared operations instead of preserving them");
    Require(blocked.Sum(city => city.TransferCalls) > 0,
        "fresh battle execution completely displaced pending recovery");
}

static Fixture NewFixture(int attackerCount = 30, int defenderCount = 3)
{
    World.world = new World();
    ArmyAbstractBattleService.ClearRuntime();
    ArmyRtsControllerService.Missions.Clear();
    ArmyRtsControllerService.Invalidated.Clear();
    ArmyRtsWarLifecycleService.Cleared.Clear();
    ArmyRouteProviderService.Cancelled.Clear();
    AW3MultiplayerReplicaScope.IsReplicaSession = false;
    ArmyRtsWarDoctrine.IsAbstractDecisive = true;
    var attack = new Kingdom { data = new Data { id = 1 } };
    var defend = new Kingdom { data = new Data { id = 2 } };
    World.world.kingdoms.Add(attack);
    World.world.kingdoms.Add(defend);
    var source = new City { data = new Data { id = 101 }, kingdom = attack };
    var target = new City { data = new Data { id = 102 }, kingdom = defend };
    World.world.cities.Add(source);
    World.world.cities.Add(target);
    var war = new War { data = new Data { id = 3 }, Participants = new() { attack, defend } };
    World.world.wars.Add(war);
    Army attacker = AddArmy(10, attack, source, attackerCount);
    Army defender = AddArmy(20, defend, target, defenderCount);
    source.Army = attacker;
    target.Army = defender;
    ArmyRtsControllerService.Missions.Add(new ArmyRtsMission
    {
        ArmyId = attacker.id, WarId = war.id, TargetCityId = target.id,
        ProposalKind = ArmyRtsProposalKind.Attack
    });
    return new(attack, defend, source, target, war, attacker, defender);
}

static Army AddArmy(long id, Kingdom kingdom, City anchor, int count, string role = null)
{
    var army = new Army { data = new Data { id = id }, Kingdom = kingdom, Anchor = anchor, Role = role };
    World.world.armies.Add(army);
    for (int i = 0; i < count; i++)
    {
        var actor = new Actor { data = new Data { id = id * 1000 + i }, army = army, kingdom = kingdom };
        army.units.Add(actor);
        World.world.units.Add(actor);
    }
    army.Captain = army.units.FirstOrDefault();
    return army;
}

static void Detach(Army army)
{
    foreach (Actor actor in army.units) actor.army = null;
    army.units.Clear();
    army.Captain = null;
    World.world.armies.Remove(army.id);
}
static string Encode(City city)
{
    city.data.get(LineageKeys.AW_RTS_ABSTRACT_BATTLE_TRANSACTION, out string encoded, "");
    return encoded;
}
static ArmyAbstractBattleTransactionSnapshot Read(City city) =>
    ArmyAbstractBattleTransactionRules.Decode(Encode(city));
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
internal sealed record Fixture(Kingdom AttackKingdom, Kingdom DefendKingdom,
    City Source, City Target, War War, Army Attacker, Army Defender);
