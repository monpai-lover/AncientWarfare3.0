using System.Data.SQLite;
using System.Reflection;
using AncientWarfare3.attributes;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

var tests = new (string Name, Action Body)[]
{
    ("candidate SQL failure rolls back the session and permits same-cycle retry", CandidateWriteFailure),
    ("candidate enumeration failure does not consume the cycle", CandidateReadFailure),
    ("transaction failure leaves record identities reusable", RecordIdentityPublication),
    ("retired soldiers cannot displace serving-age candidates", RetiredCandidates),
    ("normal candidate ranking, snapshots and cycle idempotency survive", NormalCycle),
    ("a valid empty candidate pool can still create its session", EmptyPool)
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Body(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
}
return failures == 0 ? 0 : 1;

static void CandidateWriteFailure()
{
    using var db = Open();
    Kingdom kingdom = Realm();
    kingdom.Units = new[] { Candidate(kingdom, 1, 90), Candidate(kingdom, 2, 80) };
    Execute(db, "CREATE TRIGGER reject_second_candidate BEFORE INSERT ON MilitaryExamCandidate " +
        "WHEN NEW.ACTOR_ID=2 BEGIN SELECT RAISE(ABORT,'injected candidate write failure'); END");
    MilitaryExamService.OnKingdomYear(kingdom);
    Equal(0L, Count(db, "MilitaryExamSession"), "no committed session after a candidate write fails");
    Equal(0L, Count(db, "MilitaryExamCandidate"), "no partial candidate batch");
    Execute(db, "DROP TRIGGER reject_second_candidate");
    MilitaryExamService.OnKingdomYear(kingdom);
    Equal(1L, Count(db, "MilitaryExamSession"), "same cycle can be retried");
    Equal(2L, Count(db, "MilitaryExamCandidate"), "retry saves the complete candidate pool");
    MilitaryExamService.OnKingdomYear(kingdom);
    Equal(1L, Count(db, "MilitaryExamSession"), "repeated annual call remains idempotent");
    Equal(2L, Count(db, "MilitaryExamCandidate"), "repeated annual call does not append candidates");
}

static void CandidateReadFailure()
{
    using var db = Open();
    Kingdom kingdom = Realm();
    Actor actor = Candidate(kingdom, 1, 90);
    kingdom.Units = Interrupted(actor);
    MilitaryExamService.OnKingdomYear(kingdom);
    Equal(0L, Count(db, "MilitaryExamSession"), "incomplete native roster cannot consume the cycle");
    kingdom.Units = new[] { actor };
    MilitaryExamService.OnKingdomYear(kingdom);
    Equal(1L, Count(db, "MilitaryExamCandidate"), "recovered roster can be retried in the same cycle");
}

static IEnumerable<Actor> Interrupted(Actor actor)
{
    yield return actor;
    throw new InvalidOperationException("native roster changed during enumeration");
}

static void RecordIdentityPublication()
{
    using var db = Open();
    var session = new MilitaryExamSessionRecord { KingdomId = 1, CycleYear = 100 };
    var candidates = new[]
    {
        new MilitaryExamCandidateRecord { KingdomId = 1, ActorId = 1 },
        new MilitaryExamCandidateRecord { KingdomId = 1, ActorId = 2 }
    };
    Execute(db, "CREATE TRIGGER reject_second_candidate BEFORE INSERT ON MilitaryExamCandidate " +
        "WHEN NEW.ACTOR_ID=2 BEGIN SELECT RAISE(ABORT,'injected candidate write failure'); END");
    Equal(false, MilitaryExamPersistence.TryCreateSession(db, session, candidates), "failed batch reports failure");
    Equal(-1L, session.Id, "rolled-back session ID is not published");
    Equal(true, candidates.All(candidate => candidate.Id == -1 && candidate.SessionId == -1),
        "rolled-back candidate IDs and session links are not published");
    Execute(db, "DROP TRIGGER reject_second_candidate");
    Equal(true, MilitaryExamPersistence.TryCreateSession(db, session, candidates), "same records remain retryable");
    Equal(true, session.Id > 0 && candidates.All(candidate => candidate.Id > 0 && candidate.SessionId == session.Id),
        "committed records publish their generated identities");
    Equal(2L, Count(db, "MilitaryExamCandidate"), "retry commits the complete batch");

    var duplicate = new MilitaryExamSessionRecord { KingdomId = 1, CycleYear = 100 };
    var duplicateCandidates = new[] { new MilitaryExamCandidateRecord { KingdomId = 1, ActorId = 3 } };
    Equal(false, MilitaryExamPersistence.TryCreateSession(db, duplicate, duplicateCandidates), "duplicate cycle is rejected");
    Equal(-1L, duplicate.Id, "duplicate cycle does not publish a phantom ID");
    Equal(-1L, duplicateCandidates[0].Id, "duplicate cycle does not insert unrelated candidates");
    Equal(2L, Count(db, "MilitaryExamCandidate"), "duplicate cycle preserves existing candidates");
}

static void RetiredCandidates()
{
    using var db = Open();
    Kingdom kingdom = Realm();
    Actor retired = Candidate(kingdom, 1, 100);
    retired.Age = 70;
    retired.Retired = true;
    kingdom.Units = new[] { retired, Candidate(kingdom, 2, 90), Candidate(kingdom, 3, 80),
        Candidate(kingdom, 4, 70), Candidate(kingdom, 5, 60) };
    MilitaryExamService.OnKingdomYear(kingdom);
    Equal("2,3,4,5", ActorIds(db), "retirement below 75 remains final despite a high exam score");
}

static void NormalCycle()
{
    using var db = Open();
    Kingdom kingdom = Realm();
    Actor winner = Candidate(kingdom, 5, 100);
    winner.city = new City { data = new Data { id = 99, name = "home snapshot" }, kingdom = kingdom };
    Actor elderly = Candidate(kingdom, 6, 100); elderly.Age = 75;
    Actor captain = Candidate(kingdom, 7, 100); captain.Captain = true;
    Actor dead = Candidate(kingdom, 8, 100); dead.Alive = false;
    Actor foreign = Candidate(Realm(2), 9, 100);
    kingdom.Units = new[] { Candidate(kingdom, 4, 70), Candidate(kingdom, 3, 80),
        Candidate(kingdom, 2, 80), Candidate(kingdom, 1, 30), winner, elderly, captain, dead, foreign,
        null, new Actor { data = null } };
    MilitaryExamService.OnKingdomYear(kingdom);
    Equal("2,3,4,5", ActorIds(db), "top four eligible candidates respect bounded reserve");
    Equal("5,2,3,4", Convert.ToString(Scalar(db, "SELECT group_concat(ACTOR_ID, ',') FROM " +
        "(SELECT ACTOR_ID FROM MilitaryExamCandidate ORDER BY ID)")), "score order uses actor ID to resolve ties");
    Equal(0L, Convert.ToInt64(Scalar(db, "SELECT COUNT(*) FROM MilitaryExamCandidate c " +
        "LEFT JOIN MilitaryExamSession s ON s.ID=c.SESSION_ID WHERE s.ID IS NULL OR s.KINGDOM_ID<>c.KINGDOM_ID")),
        "every candidate belongs to the committed session");
    Equal("home snapshot", Convert.ToString(Scalar(db,
        "SELECT HOME_CITY_NAME FROM MilitaryExamCandidate WHERE ACTOR_ID=5")), "home name snapshot survives");
    MilitaryExamService.OnKingdomYear(kingdom);
    Equal(1L, Count(db, "MilitaryExamSession"), "one session per kingdom and cycle");
    Date.Year = 103;
    MilitaryExamService.OnKingdomYear(kingdom);
    Equal(2L, Count(db, "MilitaryExamSession"), "later cycle creates an independent session");
    Equal(8L, Count(db, "MilitaryExamCandidate"), "later-cycle candidates retain their own records");
}

static void EmptyPool()
{
    using var db = Open();
    MilitaryExamService.OnKingdomYear(Realm());
    Equal(1L, Count(db, "MilitaryExamSession"), "empty successful scan is distinct from a failed scan");
    Equal(0L, Count(db, "MilitaryExamCandidate"), "empty pool has no candidates");
}

static Kingdom Realm(long id = 1)
{
    var kingdom = new Kingdom { data = new Data { id = id } };
    kingdom.king = new Actor { kingdom = kingdom, King = true };
    kingdom.data.set(LineageKeys.CIVIL_SERVICE_EXAM_ANCHOR_YEAR, 100);
    return kingdom;
}
static Actor Candidate(Kingdom kingdom, long id, int score)
{
    var actor = new Actor { kingdom = kingdom, data = new Data { id = id, name = "actor " + id }, Merit = score };
    foreach (string key in new[] { "warfare", "damage", "strength", "speed", "diplomacy" }) actor.stats[key] = score;
    return actor;
}
static SQLiteConnection Open()
{
    var db = new SQLiteConnection("Data Source=:memory:;Version=3;New=True;");
    db.Open();
    CreateTable<MilitaryExamSessionTableItem>(db);
    CreateTable<MilitaryExamCandidateTableItem>(db);
    CreateTable<MilitaryOfficerRecordTableItem>(db);
    Execute(db, "CREATE UNIQUE INDEX session_cycle ON MilitaryExamSession(KINGDOM_ID,CYCLE_YEAR)");
    Execute(db, "CREATE UNIQUE INDEX session_actor ON MilitaryExamCandidate(SESSION_ID,ACTOR_ID)");
    LineageArchiveManager.Instance.OperatingDB = db;
    Date.Year = 100;
    return db;
}
static void CreateTable<T>(SQLiteConnection db)
{
    var columns = typeof(T).GetFields().Select(field =>
    {
        var definition = field.GetCustomAttribute<TableItemDefAttribute>();
        string name = string.IsNullOrEmpty(definition?.Name) ? field.Name.ToUpperInvariant() : definition.Name;
        string type = field.FieldType == typeof(string) ? "TEXT" : field.FieldType == typeof(double) ? "REAL" : "INTEGER";
        string sql = name + " " + type + (definition?.IsPrimary == true ? " PRIMARY KEY" : "");
        if (!string.IsNullOrEmpty(definition?.DefaultValue)) sql += " DEFAULT " + definition.DefaultValue;
        return sql;
    });
    Execute(db, "CREATE TABLE " + typeof(T).GetCustomAttribute<TableDefAttribute>().Name + " (" + string.Join(",", columns) + ")");
}
static string ActorIds(SQLiteConnection db) => Convert.ToString(Scalar(db,
    "SELECT group_concat(ACTOR_ID, ',') FROM (SELECT ACTOR_ID FROM MilitaryExamCandidate ORDER BY ACTOR_ID)"));
static long Count(SQLiteConnection db, string table) => Convert.ToInt64(Scalar(db, "SELECT COUNT(*) FROM " + table));
static object Scalar(SQLiteConnection db, string sql) { using var command = new SQLiteCommand(sql, db); return command.ExecuteScalar(); }
static void Execute(SQLiteConnection db, string sql) { using var command = new SQLiteCommand(sql, db); command.ExecuteNonQuery(); }
static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
}
