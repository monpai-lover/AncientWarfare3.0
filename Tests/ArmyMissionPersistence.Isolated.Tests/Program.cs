using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;

var tests = new (string Name, Action Run)[]
{
    ("disposed kingdom is rejected without data-backed id access", () =>
    {
        var (army, _, _) = Setup(); army.Kingdom.data = null;
        Require(!ArmyMissionPersistence.TryRestore(army, out _, out _), "Disposed owner restored.");
        Require(!ArmyMissionPersistence.TryGetRestored(army, out _), "Invalid mission retained.");
        army.data.get(LineageKeys.AW_ARMY_RTS_WAR_ID, out long warId, -1L);
        Require(warId == -1 && KingdomWarDirectorService.Changed.Count == 0, "Invalid mission not cleared safely.");
    }),
    ("missing issue time migrates to current world time", () => AssertTime(null, 123)),
    ("malformed issue time migrates to current world time", () => AssertTime("broken", 123)),
    ("non-finite issue time migrates to current world time", () => AssertTime("NaN", 123)),
    ("valid zero issue time is preserved", () => AssertTime("0", 0)),
    ("valid fractional issue time survives invariant serialization", () => AssertTime("82.75", 82.75)),
    ("invalid saved target queues director assignment", () =>
    {
        var (army, _, target) = Setup(); target.Alive = false;
        Require(!ArmyMissionPersistence.TryRestore(army, out _, out var initial) && initial == ArmyRtsState.Rally,
            "Invalid target was restored or did not request rally.");
        Require(KingdomWarDirectorService.Changed.Count == 1, "Assignment was not queued.");
    }),
    ("ended war clears saved intent and queues reassessment", () =>
    {
        var (army, war, _) = Setup(); war.Ended = true;
        Require(!ArmyMissionPersistence.TryRestore(army, out _, out _), "Ended war restored.");
        army.data.get(LineageKeys.AW_ARMY_RTS_WAR_ID, out long id, -1L);
        Require(id < 0 && KingdomWarDirectorService.Changed.Count == 1, "Ended-war cleanup lost reassessment.");
    }),
    ("disposed army restore is ignored", () =>
    {
        var (army, _, _) = Setup(); army.data = null;
        Require(!ArmyMissionPersistence.TryRestore(army, out _, out _), "Disposed army restored.");
    })
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + error); }
}
Console.WriteLine($"Army mission persistence runtime: {tests.Length - failures}/{tests.Length} passed.");
return failures == 0 ? 0 : 1;

static (Army Army, War War, City Target) Setup()
{
    World.world = new World(); AW3MultiplayerReplicaScope.IsReplicaSession = false;
    ArmyMissionPersistence.ClearRuntime(); KingdomWarDirectorService.Changed.Clear();
    ArmyRtsControllerService.TargetAllowed = true; LineageService.Now = 123;
    var kingdom = new Kingdom { data = new EntityData { id = 1 } };
    var army = new Army { data = new EntityData { id = 2 }, Kingdom = kingdom };
    var war = new War { data = new EntityData { id = 3 } }; war.Participants.Add(kingdom);
    var target = new City { data = new EntityData { id = 4 }, kingdom = kingdom };
    World.world.armies.Add(army); World.world.wars.Add(war); World.world.cities.Add(target);
    ArmyMissionPersistence.Persist(army, new ArmyRtsMission { ArmyId = 2, KingdomId = 1, WarId = 3, TargetCityId = 4, IssuedTime = 10 });
    return (army, war, target);
}
static void AssertTime(string text, double expected)
{
    var (army, _, _) = Setup();
    if (text == null) army.data.removeString(LineageKeys.AW_ARMY_RTS_ISSUED_TIME);
    else army.data.set(LineageKeys.AW_ARMY_RTS_ISSUED_TIME, text);
    Require(ArmyMissionPersistence.TryRestore(army, out var mission, out var initial), "Valid mission did not restore.");
    Require(mission.IssuedTime == expected && initial == ArmyRtsState.Rally, "Unexpected restored time " + mission.IssuedTime);
    army.data.get(LineageKeys.AW_ARMY_RTS_ISSUED_TIME, out string stored, "");
    Require(stored == expected.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "Migration was not persisted.");
    Require(ArmyMissionPersistence.TryGetRestored(army, out var cached) && ReferenceEquals(cached, mission), "Restored cache lost mission identity.");
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
