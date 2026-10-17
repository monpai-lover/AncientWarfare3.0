using System.Data.SQLite;
namespace AncientWarfare3.core.db {
    internal sealed class LineageArchiveManager {
        public static LineageArchiveManager Instance { get; } = new();
        public static long RuntimeDatabaseEpoch { get; set; }
        public static string RuntimeDbPath { get; set; }
        public SQLiteConnection OperatingDB { get; set; }
        public bool IsOperational => OperatingDB != null;
    }
    internal static class CourtOfficerTableItem { public static string GetTableName() => "CourtOfficer"; }
    internal static class ActorArchiveTableItem { public static string GetTableName() => "ActorArchive"; }
    internal static class LineageArchivePragmaService {
        public static string SnapshotReadOnlyConnectionString(string path) => new SQLiteConnectionStringBuilder {DataSource=path,ReadOnly=true,Pooling=false}.ConnectionString;
    }
}
namespace AncientWarfare3.core.lineage {
    internal sealed class EmptyScope : IDisposable { public void Dispose() { } }
    // The API's paged SQL, cursor rules, DTO mapper, event publisher and subscription
    // registry are production code. These unrelated game/UI queries are not exercised.
    internal static class HistoryQuery {
        public static IDisposable EnterBackgroundRead(SQLiteConnection db) => new EmptyScope();
        public static List<HistoryEntry> ReadPerson(long id) => throw new NotSupportedException();
        public static List<HistoryEntry> ReadCity(long id) => throw new NotSupportedException();
        public static List<HistoryEntry> ReadKingdom(long id) => throw new NotSupportedException();
        public static List<ReignPeriod> GetKingdomReigns(long id) => new();
        public static List<ReignPeriod> GetCityPeriods(long id) => new();
    }
    internal static class LineageQuery {
        public static IDisposable EnterBackgroundRead(SQLiteConnection db) => new EmptyScope();
        public static List<long> GetParentIds(long id, bool pUseReverseLiveLookup) => new();
        public static List<long> GetChildIds(long id) => new();
    }
    internal sealed class HistoryEntry {
        public long event_id, target_id, context_kingdom_id;
        public double world_time;
        public string projection_key, event_type, year_prefix, subject_name, content, category, role_snapshot;
        public int age_at_event, is_king_at_event;
    }
    internal sealed class ReignPeriod {
        public long king_actor_id;
        public string king_name, owner_name;
        public double start_time, end_time;
        public List<HistoryEntry> events = new();
    }
}
