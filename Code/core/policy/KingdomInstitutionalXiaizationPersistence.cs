using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.policy
{
    internal sealed class KingdomInstitutionalXiaizationState
    {
        internal long KingdomId;
        internal int Version;
        internal string Phase = "prepared";
        internal long CursorActorId = -1L;
        internal int FailureCount;
        internal string LastError = string.Empty;
        internal double RequestedTime = -1d;
        internal double UpdatedTime = -1d;
    }

    internal static class KingdomInstitutionalXiaizationStatePersistence
    {
        internal const int CurrentVersion = 1;
        private const string Table = "KingdomInstitutionalXiaizationState";

        internal static void Request(SQLiteConnection db, long kingdomId,
            double now)
        {
            if (db == null || kingdomId < 0L) return;
            KingdomInstitutionalXiaizationState existing = Load(db,
                kingdomId);
            if (existing == null)
            {
                Execute(db, "INSERT INTO " + Table + " " +
                    "(KINGDOM_ID,VERSION,PHASE,CURSOR_ACTOR_ID," +
                    "FAILURE_COUNT,LAST_ERROR,REQUESTED_TIME,UPDATED_TIME) " +
                    "VALUES(@kingdom,@version,'prepared',-1,0,'',@time,@time)",
                    command =>
                    {
                        Add(command, "@kingdom", kingdomId);
                        Add(command, "@version", CurrentVersion);
                        Add(command, "@time", now);
                    });
                return;
            }

            long cursor = existing.Version == CurrentVersion
                ? existing.CursorActorId
                : -1L;
            Execute(db, "UPDATE " + Table + " SET VERSION=@version," +
                "PHASE='prepared',CURSOR_ACTOR_ID=@cursor," +
                "FAILURE_COUNT=0,LAST_ERROR='',REQUESTED_TIME=@time," +
                "UPDATED_TIME=@time WHERE KINGDOM_ID=@kingdom", command =>
                {
                    Add(command, "@version", CurrentVersion);
                    Add(command, "@cursor", cursor);
                    Add(command, "@time", now);
                    Add(command, "@kingdom", kingdomId);
                });
        }

        internal static KingdomInstitutionalXiaizationState Load(
            SQLiteConnection db, long kingdomId)
        {
            if (db == null || kingdomId < 0L) return null;
            using var command = new SQLiteCommand(
                "SELECT KINGDOM_ID,VERSION,PHASE,CURSOR_ACTOR_ID," +
                "FAILURE_COUNT,LAST_ERROR,REQUESTED_TIME,UPDATED_TIME " +
                "FROM " + Table + " WHERE KINGDOM_ID=@kingdom", db);
            Add(command, "@kingdom", kingdomId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new KingdomInstitutionalXiaizationState
            {
                KingdomId = reader.GetInt64(0),
                Version = reader.GetInt32(1),
                Phase = reader.IsDBNull(2) ? "prepared" : reader.GetString(2),
                CursorActorId = reader.IsDBNull(3) ? -1L : reader.GetInt64(3),
                FailureCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                LastError = reader.IsDBNull(5) ? string.Empty :
                    reader.GetString(5),
                RequestedTime = reader.IsDBNull(6) ? -1d : reader.GetDouble(6),
                UpdatedTime = reader.IsDBNull(7) ? -1d : reader.GetDouble(7)
            };
        }

        internal static void AdvanceCursor(SQLiteConnection db,
            long kingdomId, long actorId, double now)
        {
            if (db == null || kingdomId < 0L || actorId < 0L) return;
            Execute(db, "UPDATE " + Table + " SET CURSOR_ACTOR_ID=" +
                "CASE WHEN CURSOR_ACTOR_ID<@actor THEN @actor ELSE " +
                "CURSOR_ACTOR_ID END,UPDATED_TIME=@time " +
                "WHERE KINGDOM_ID=@kingdom", command =>
                {
                    Add(command, "@actor", actorId);
                    Add(command, "@time", now);
                    Add(command, "@kingdom", kingdomId);
                });
        }

        internal static void AdvancePhase(SQLiteConnection db,
            long kingdomId, string phase, double now)
        {
            if (db == null || kingdomId < 0L || string.IsNullOrWhiteSpace(phase))
                return;
            Execute(db, "UPDATE " + Table + " SET PHASE=@phase," +
                "UPDATED_TIME=@time WHERE KINGDOM_ID=@kingdom", command =>
                {
                    Add(command, "@phase", phase.Trim());
                    Add(command, "@time", now);
                    Add(command, "@kingdom", kingdomId);
                });
        }

        internal static void RecordFailure(SQLiteConnection db,
            long kingdomId, string error, double now)
        {
            if (db == null || kingdomId < 0L) return;
            Execute(db, "UPDATE " + Table + " SET PHASE='prepared'," +
                "FAILURE_COUNT=FAILURE_COUNT+1,LAST_ERROR=@error," +
                "UPDATED_TIME=@time WHERE KINGDOM_ID=@kingdom", command =>
                {
                    Add(command, "@error", (error ?? string.Empty).Trim());
                    Add(command, "@time", now);
                    Add(command, "@kingdom", kingdomId);
                });
        }

        internal static void MarkComplete(SQLiteConnection db,
            long kingdomId, double now)
        {
            if (db == null || kingdomId < 0L) return;
            Execute(db, "UPDATE " + Table + " SET PHASE='complete'," +
                "LAST_ERROR='',UPDATED_TIME=@time " +
                "WHERE KINGDOM_ID=@kingdom", command =>
                {
                    Add(command, "@time", now);
                    Add(command, "@kingdom", kingdomId);
                });
        }

        private static void Execute(SQLiteConnection db, string sql,
            Action<SQLiteCommand> bind)
        {
            using var command = new SQLiteCommand(sql, db);
            bind(command);
            command.ExecuteNonQuery();
        }

        private static void Add(SQLiteCommand command, string name,
            object value)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}
