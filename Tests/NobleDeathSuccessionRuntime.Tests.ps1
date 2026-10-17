$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = Get-Content -Raw -LiteralPath (Join-Path $repo 'Code/core/lineage/NobleRankService.cs')
$queue = Get-Content -Raw -LiteralPath (Join-Path $repo 'Code/core/lineage/NobleDeathSuccessionRetryQueue.cs')
$designation = Get-Content -Raw -LiteralPath (Join-Path $repo 'Code/core/lineage/TitleSuccessionDesignation.cs')
$designation = [regex]::Replace($designation, '(?m)^using [^;]+;\r?\n', '')
function Get-Section([string]$start, [string]$end) {
    $first = $source.IndexOf($start, [StringComparison]::Ordinal)
    if ($first -lt 0) { throw "Missing production section: $start" }
    $last = $source.IndexOf($end, $first, [StringComparison]::Ordinal)
    if ($last -le $first) { throw "Missing production section end: $end" }
    return $source.Substring($first, $last - $first)
}
# Compile the production death callback, retry and insert methods. Only the native
# Actor and database boundaries are simulated; the existing SQL suites exercise
# transaction rollback and exactly-once replay against SQLite itself.
$pending = Get-Section 'private sealed class PendingDeathSuccession' 'private static readonly NobleDeathSuccessionRetryQueue'
$death = Get-Section 'public static void OnActorDying(Actor pHolder)' 'private static bool TryProcessPendingDeathSuccession('
$process = Get-Section 'private static bool TryProcessPendingDeathSuccession(' 'private static bool ValidTitleForActor('
$insert = Get-Section 'private static void InsertActiveGrant(SQLiteTransaction pTransaction,' 'private static long NextGrantId('
$harness = @'
namespace AncientWarfare3.core.lineage {
#pragma warning disable CS0649
    public enum NobleTitleStyle { Male }
    public struct NobleTitleSnapshot {
        public long GrantId, KingdomId;
        public int Rank;
        public NobleTitleStyle Style;
        public string TitleName;
        public bool IsActive => Rank > 0;
    }
    public sealed class ActorData { public long id; }
    public sealed class Actor {
        public ActorData data;
        public Kingdom kingdom;
        public string Name;
        public NobleTitleSnapshot Title;
        public bool Cleared, Projected;
        public bool Alive = true, Rekt;
        public long DesignatedSuccessorId = -1L;
        public bool Madness, Slave;
        public string getName() => data == null ? throw new Exception("released actor") : Name;
        public bool isSexMale() => true;
        public bool isAlive() => Alive;
        public bool isRekt() => Rekt;
        public bool hasTrait(string trait) => trait == "madness" && Madness;
    }
    public sealed class KingdomData { public long id = 7; }
    public sealed class Kingdom {
        public KingdomData data = new KingdomData();
        public long id => data.id;
        public string name = "realm";
        public bool isRekt() => false;
    }
    public sealed class ActorManager : Dictionary<long, Actor> {
        public Actor get(long id) => TryGetValue(id, out Actor actor) ? actor : null;
    }
    public sealed class FakeWorld { public ActorManager units = new ActorManager(); }
    public static class World { public static FakeWorld world = new FakeWorld(); }
    public static class SlaveService { public static bool IsSlave(Actor actor) => actor.Slave; }
    public static class DynasticMaleLineContinuityService {
        public static long ReadDesignatedSuccessorId(Actor holder) => holder.DesignatedSuccessorId;
    }
    public sealed class SQLiteTransaction : IDisposable {
        public void Commit() { }
        public void Rollback() { }
        public void Dispose() { }
    }
    public sealed class FakeDatabase {
        public bool FailNext;
        public readonly List<Dictionary<string, object>> Inserts = new List<Dictionary<string, object>>();
        public SQLiteTransaction BeginTransaction() {
            if (FailNext) { FailNext = false; throw new Exception("injected transient database failure"); }
            return new SQLiteTransaction();
        }
    }
    public sealed class FakeParameters : Dictionary<string, object> {
        public void AddWithValue(string key, object value) { Add(key, value); }
    }
    public sealed class SQLiteCommand : IDisposable {
        private readonly FakeDatabase db;
        public SQLiteTransaction Transaction;
        public string CommandText;
        public FakeParameters Parameters = new FakeParameters();
        public SQLiteCommand(FakeDatabase value) { db = value; }
        public int ExecuteNonQuery() { db.Inserts.Add(new Dictionary<string, object>(Parameters)); return 1; }
        public void Dispose() { }
    }
    public sealed class NobleDeathSuccessionCommittedGrant {
        public long GrantId, KingdomId;
        public int Rank;
        public NobleTitleStyle Style;
        public string TitleName;
        public bool Active;
    }
    public static class NobleDeathSuccessionPersistence {
        public static NobleDeathSuccessionCommittedGrant Committed;
        public static bool TryReadCommittedInheritance(FakeDatabase db, string table,
            long grant, long successor, long holder, long kingdom, int rank, NobleTitleStyle style,
            out NobleDeathSuccessionCommittedGrant existing, SQLiteTransaction transaction) {
            existing = Committed != null && Committed.Rank == rank ? Committed : null;
            return existing != null;
        }
    }
    public static class EnfeoffmentTableItem { public static string GetTableName() => "Enfeoffment"; }
    public static class NobleRankRules {
        public const int RankNone = 0;
        public static int ResultingInheritedRank(int a, int b) => Math.Max(a, b);
    }
    public static class HereditaryTitleSuccessionRules {
        public static bool CanTransfer(bool a, bool b, bool c) => a && b && c;
    }
    public static class HereditaryTitleSuccessionService {
        public static Actor Successor;
        public static int SearchCount;
        public static Actor FindSuccessor(Actor holder, Kingdom kingdom) { SearchCount++; return Successor; }
    }
    public static class NobleTitleNameRules {
        public static bool ShouldKeepSuccessorTitle(int a, int b) => a > b;
    }
    public static class LineageService {
        public static int AliveArchives;
        public static double CurTime() => 12;
        public static void ArchiveActor(Actor actor, bool pAlive) { if (pAlive) AliveArchives++; }
    }
    public static class ChronicleEvents {
        public static void OnNobleRankInherited(Kingdom context, Actor holder, Actor successor, string title) { }
        public static void OnNobleRankExtinct(Kingdom context, Actor holder, string title) { }
    }
    public static class ModClass { public static void LogWarning(string warning) { } }
    public static class NobleRankService {
        private static readonly NobleDeathSuccessionRetryQueue<PendingDeathSuccession> PendingDeathSuccessions =
            new NobleDeathSuccessionRetryQueue<PendingDeathSuccession>();
        private static readonly FakeDatabase DB = new FakeDatabase();
        private static readonly Kingdom Realm = new Kingdom();
        private static readonly List<long> ClosedActors = new List<long>();
        private static bool FailProjection;
        private static bool Ready => true;
        private static NobleTitleSnapshot ReadHot(Actor actor) => actor.Title;
        private static string GetDisplayTitle(Actor actor) => actor.Title.TitleName;
        private static Kingdom FindKingdom(long id) => Realm;
        private static int SafeYear() => 1;
        private static HashSet<string> ReadActiveTitleNames(SQLiteTransaction transaction) => new HashSet<string>();
        private static long CloseActiveGrant(SQLiteTransaction transaction, long id, int year, double time, string reason) {
            ClosedActors.Add(id); return 1;
        }
        private static long NextGrantId(SQLiteTransaction transaction) => 2;
        private static string AllocateTitleName(Actor actor, Kingdom kingdom, int rank, NobleTitleStyle style,
            long grant, string title, HashSet<string> used) => title;
        private static string StyleId(NobleTitleStyle style) => "male";
        private static void ClearProjection(Actor actor) { if (actor?.data != null) actor.Cleared = true; }
        private static void Project(Actor actor, long grant, long kingdom, int rank, NobleTitleStyle style, string title) {
            if (FailProjection) { FailProjection = false; throw new Exception("post-commit projection failure"); }
            actor.Projected = true;
        }
        public static void Run() {
            CheckReleasedHolder(false, false);
            CheckReleasedHolder(true, false);
            CheckReleasedHolder(true, true);
            CheckInvalidRecipientContext("released kingdom");
            CheckInvalidRecipientContext("reused kingdom");
            CheckInvalidRecipientContext("reused successor");
            CheckInvalidRecipientContext("dead successor");
            CheckSuccessorGrantedWhilePending(1);
            CheckSuccessorGrantedWhilePending(5);
            CheckCommittedReplayBeforeRefreshingTitle();
            CheckDesignatedSuccessor("valid");
            CheckDesignatedSuccessor("dead");
            CheckDesignatedSuccessor("foreign");
            CheckDesignatedSuccessor("self");
        }
        private static void CheckReleasedHolder(bool inherits, bool reused) {
            ClearPendingDeathSuccessions();
            ClosedActors.Clear();
            DB.Inserts.Clear();
            Realm.data = new KingdomData();
            NobleDeathSuccessionPersistence.Committed = null;
            var holder = new Actor { data = new ActorData { id = 10 }, kingdom = Realm, Name = "original holder",
                Title = new NobleTitleSnapshot { GrantId = 1, KingdomId = 7, Rank = 3, TitleName = "old title" } };
            var successor = new Actor { data = new ActorData { id = 11 }, kingdom = Realm, Name = "heir" };
            HereditaryTitleSuccessionService.Successor = inherits ? successor : null;
            DB.FailNext = true;
            OnActorDying(holder);
            Require(PendingDeathSuccessions.Count == 1 && ClosedActors.Count == 0,
                "transient failure must retain original death work");
            holder.data = reused ? new ActorData { id = 99 } : null;
            holder.Name = "recycled actor";
            Require(FlushPendingDeathSuccessionsForSave(),
                "native holder cleanup must not permanently block save");
            Require(PendingDeathSuccessions.Count == 0 && ClosedActors.Count == 1 && ClosedActors[0] == 10,
                "retry must close the deceased actor's grant using captured identity");
            Require(!holder.Cleared, "retry must not clear a reused actor's title projection");
            if (inherits) {
                Require(successor.Projected && DB.Inserts.Count == 1, "living successor must inherit once");
                var values = DB.Inserts[0];
                Require((long)values["@grantor"] == 10 && (string)values["@grantorName"] == "original holder" &&
                    (long)values["@inherited"] == 10 && (long)values["@previous"] == 1,
                    "grant provenance must preserve death-time identity after native cleanup or reuse");
            } else Require(DB.Inserts.Count == 0, "extinction must not create an inherited grant");
            Require(FlushPendingDeathSuccessionsForSave() && ClosedActors.Count == 1,
                "successful death retry must not repeat settlement on later saves");
        }
        private static void CheckInvalidRecipientContext(string invalidated) {
            ClearPendingDeathSuccessions();
            ClosedActors.Clear();
            DB.Inserts.Clear();
            Realm.data = new KingdomData();
            NobleDeathSuccessionPersistence.Committed = null;
            LineageService.AliveArchives = 0;
            var holder = new Actor { data = new ActorData { id = 10 }, kingdom = Realm, Name = "original holder",
                Title = new NobleTitleSnapshot { GrantId = 1, KingdomId = 7, Rank = 3, TitleName = "old title" } };
            var successor = new Actor { data = new ActorData { id = 11 }, kingdom = Realm, Name = "heir" };
            HereditaryTitleSuccessionService.Successor = successor;
            DB.FailNext = true;
            OnActorDying(holder);
            Require(PendingDeathSuccessions.Count == 1, "transient failure must queue death");
            if (invalidated == "released kingdom") Realm.data = null;
            else if (invalidated == "reused kingdom") Realm.data = new KingdomData { id = 99 };
            else if (invalidated == "dead successor") successor.Alive = false;
            else successor.data = new ActorData { id = 99 };
            Require(FlushPendingDeathSuccessionsForSave(), invalidated + " must not permanently block save");
            Require(ClosedActors.Count == 1 && ClosedActors[0] == 10 && PendingDeathSuccessions.Count == 0,
                invalidated + " must still settle the deceased holder");
            Require(DB.Inserts.Count == 0 && !successor.Projected,
                invalidated + " must not transfer a title into a released or unrelated actor/realm");
            Require(LineageService.AliveArchives == 0, invalidated + " must not archive an ineligible heir as alive");
        }
        private static void CheckSuccessorGrantedWhilePending(int newRank) {
            ClearPendingDeathSuccessions();
            ClosedActors.Clear();
            DB.Inserts.Clear();
            Realm.data = new KingdomData();
            NobleDeathSuccessionPersistence.Committed = null;
            var holder = new Actor { data = new ActorData { id = 10 }, kingdom = Realm, Name = "original holder",
                Title = new NobleTitleSnapshot { GrantId = 1, KingdomId = 7, Rank = 3, TitleName = "old title" } };
            var successor = new Actor { data = new ActorData { id = 11 }, kingdom = Realm, Name = "heir" };
            HereditaryTitleSuccessionService.Successor = successor;
            DB.FailNext = true;
            OnActorDying(holder);
            successor.Title = new NobleTitleSnapshot { GrantId = 4, KingdomId = 7, Rank = newRank, TitleName = "new grant" };
            Require(FlushPendingDeathSuccessionsForSave(), "new grant while pending must not block save");
            if (newRank > 3) {
                Require(DB.Inserts.Count == 0 && ClosedActors.Count == 1 && ClosedActors[0] == 10,
                    "a newer higher title must be retained while the deceased holder's title is merged");
            } else {
                Require(DB.Inserts.Count == 1 && ClosedActors.Count == 2 && ClosedActors[1] == 11,
                    "a newer lower grant must be closed before inserting inheritance to preserve one active grant");
                Require((int)DB.Inserts[0]["@rank"] == 3, "inheritance retains the higher inherited rank");
            }
        }
        private static void CheckCommittedReplayBeforeRefreshingTitle() {
            ClearPendingDeathSuccessions();
            ClosedActors.Clear();
            DB.Inserts.Clear();
            Realm.data = new KingdomData();
            NobleDeathSuccessionPersistence.Committed = null;
            var holder = new Actor { data = new ActorData { id = 10 }, kingdom = Realm, Name = "original holder",
                Title = new NobleTitleSnapshot { GrantId = 1, KingdomId = 7, Rank = 3, TitleName = "old title" } };
            var successor = new Actor { data = new ActorData { id = 11 }, kingdom = Realm, Name = "heir" };
            HereditaryTitleSuccessionService.Successor = successor;
            FailProjection = true;
            OnActorDying(holder);
            Require(DB.Inserts.Count == 1 && PendingDeathSuccessions.Count == 1,
                "projection failure after committing inheritance must remain replayable");
            successor.Title = new NobleTitleSnapshot { GrantId = 4, KingdomId = 7, Rank = 5, TitleName = "promotion" };
            NobleDeathSuccessionPersistence.Committed = new NobleDeathSuccessionCommittedGrant {
                GrantId = 2, KingdomId = 7, Rank = 3, TitleName = "old title", Active = false };
            Require(FlushPendingDeathSuccessionsForSave() && DB.Inserts.Count == 1 && ClosedActors.Count == 1,
                "replay must recognize the committed rank before reading the heir's newer title");
            Require(!successor.Projected, "inactive committed inheritance must not overwrite a newer title projection");
            NobleDeathSuccessionPersistence.Committed = null;
        }
        private static void CheckDesignatedSuccessor(string condition) {
            ClearPendingDeathSuccessions();
            ClosedActors.Clear();
            DB.Inserts.Clear();
            Realm.data = new KingdomData();
            NobleDeathSuccessionPersistence.Committed = null;
            World.world.units.Clear();
            var holder = new Actor { data = new ActorData { id = 10 }, kingdom = Realm, Name = "original holder",
                Title = new NobleTitleSnapshot { GrantId = 1, KingdomId = 7, Rank = 3, TitleName = "old title" },
                DesignatedSuccessorId = condition == "self" ? 10 : 12 };
            var fallback = new Actor { data = new ActorData { id = 11 }, kingdom = Realm, Name = "fallback" };
            var designated = new Actor { data = new ActorData { id = 12 }, kingdom = Realm, Name = "designated" };
            if (condition == "dead") designated.Alive = false;
            if (condition == "foreign") designated.kingdom = new Kingdom();
            World.world.units[10] = holder;
            World.world.units[12] = designated;
            HereditaryTitleSuccessionService.Successor = fallback;
            HereditaryTitleSuccessionService.SearchCount = 0;
            OnActorDying(holder);
            bool valid = condition == "valid";
            Require(PendingDeathSuccessions.Count == 0 && DB.Inserts.Count == 1 &&
                (long)DB.Inserts[0]["@actor"] == (valid ? 12 : 11),
                "b branch designated successor selection must retain the valid candidate or use fallback");
            Require(HereditaryTitleSuccessionService.SearchCount == (valid ? 0 : 1),
                "valid designation must bypass lineage search and invalid designation must search exactly once");
        }
        private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
// PRODUCTION_METHODS
    }
}
namespace AncientWarfare3.core.performance {
    public static class AWDiagnosticsGate { public static bool Enabled => false; }
}
'@
$methods = $pending + "`n" + $death + "`n" + $process + "`n" + $insert
$harness = $harness.Replace('// PRODUCTION_METHODS', $methods)
Add-Type -TypeDefinition ("using System.Globalization;`n" + $queue + "`n" + $designation + "`n" + $harness) -Language CSharp
[AncientWarfare3.core.lineage.NobleRankService]::Run()
Write-Output 'Noble death runtime regression passed: released/reused actors and kingdoms, deceased heir, concurrent grants, committed replay and b branch designated succession (14 cases).'
