using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal sealed class IntegratedCultureNamingMigrationState
    {
        internal long CultureId;
        internal int Version;
        internal string Phase = "pending";
        internal long CursorActorId = -1L;
        internal int FailureCount;
        internal string LastError = string.Empty;
        internal double RequestedTime = -1d;
        internal double UpdatedTime = -1d;
    }

    internal static class IntegratedCultureNamingMigrationStatePersistence
    {
        internal const int CurrentVersion = 1;
        private const string Table = "CultureNamingMigrationState";

        internal static void Request(SQLiteConnection pDb, long cultureId,
            double now)
        {
            if (pDb == null || cultureId < 0L) return;
            IntegratedCultureNamingMigrationState existing = Load(pDb,
                cultureId);
            if (existing == null)
            {
                Execute(pDb, "INSERT INTO " + Table + " " +
                    "(CULTURE_ID,VERSION,PHASE,CURSOR_ACTOR_ID," +
                    "FAILURE_COUNT,LAST_ERROR,REQUESTED_TIME,UPDATED_TIME) " +
                    "VALUES(@culture,@version,'pending',-1,0,'',@time,@time)",
                    command =>
                    {
                        Add(command, "@culture", cultureId);
                        Add(command, "@version", CurrentVersion);
                        Add(command, "@time", now);
                    });
                return;
            }

            long cursor = existing.Version == CurrentVersion
                ? existing.CursorActorId
                : -1L;
            Execute(pDb, "UPDATE " + Table + " SET VERSION=@version," +
                "PHASE='pending',CURSOR_ACTOR_ID=@cursor," +
                "FAILURE_COUNT=0,LAST_ERROR='',REQUESTED_TIME=@time," +
                "UPDATED_TIME=@time WHERE CULTURE_ID=@culture", command =>
                {
                    Add(command, "@version", CurrentVersion);
                    Add(command, "@cursor", cursor);
                    Add(command, "@time", now);
                    Add(command, "@culture", cultureId);
                });
        }

        internal static IntegratedCultureNamingMigrationState Load(
            SQLiteConnection pDb, long cultureId)
        {
            if (pDb == null || cultureId < 0L) return null;
            using var command = new SQLiteCommand(
                "SELECT CULTURE_ID,VERSION,PHASE,CURSOR_ACTOR_ID," +
                "FAILURE_COUNT,LAST_ERROR,REQUESTED_TIME,UPDATED_TIME " +
                "FROM " + Table + " WHERE CULTURE_ID=@culture", pDb);
            Add(command, "@culture", cultureId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new IntegratedCultureNamingMigrationState
            {
                CultureId = reader.GetInt64(0),
                Version = reader.GetInt32(1),
                Phase = reader.IsDBNull(2) ? "pending" : reader.GetString(2),
                CursorActorId = reader.IsDBNull(3) ? -1L : reader.GetInt64(3),
                FailureCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                LastError = reader.IsDBNull(5) ? string.Empty :
                    reader.GetString(5),
                RequestedTime = reader.IsDBNull(6) ? -1d : reader.GetDouble(6),
                UpdatedTime = reader.IsDBNull(7) ? -1d : reader.GetDouble(7)
            };
        }

        internal static void AdvanceCursor(SQLiteConnection pDb,
            long cultureId, long actorId, double now)
        {
            if (pDb == null || cultureId < 0L || actorId < 0L) return;
            Execute(pDb, "UPDATE " + Table + " SET CURSOR_ACTOR_ID=" +
                "CASE WHEN CURSOR_ACTOR_ID<@actor THEN @actor ELSE " +
                "CURSOR_ACTOR_ID END,UPDATED_TIME=@time " +
                "WHERE CULTURE_ID=@culture", command =>
                {
                    Add(command, "@actor", actorId);
                    Add(command, "@time", now);
                    Add(command, "@culture", cultureId);
                });
        }

        internal static void RecordFailure(SQLiteConnection pDb,
            long cultureId, string error, double now)
        {
            if (pDb == null || cultureId < 0L) return;
            Execute(pDb, "UPDATE " + Table + " SET PHASE='pending'," +
                "FAILURE_COUNT=FAILURE_COUNT+1,LAST_ERROR=@error," +
                "UPDATED_TIME=@time WHERE CULTURE_ID=@culture", command =>
                {
                    Add(command, "@error", (error ?? string.Empty).Trim());
                    Add(command, "@time", now);
                    Add(command, "@culture", cultureId);
                });
        }

        internal static void MarkComplete(SQLiteConnection pDb,
            long cultureId, double now)
        {
            if (pDb == null || cultureId < 0L) return;
            Execute(pDb, "UPDATE " + Table + " SET PHASE='complete'," +
                "LAST_ERROR='',UPDATED_TIME=@time WHERE CULTURE_ID=@culture",
                command =>
                {
                    Add(command, "@time", now);
                    Add(command, "@culture", cultureId);
                });
        }

        private static void Execute(SQLiteConnection pDb, string sql,
            Action<SQLiteCommand> bind)
        {
            using var command = new SQLiteCommand(sql, pDb);
            bind(command);
            command.ExecuteNonQuery();
        }

        private static void Add(SQLiteCommand pCommand, string pName,
            object pValue)
        {
            pCommand.Parameters.AddWithValue(pName, pValue ?? DBNull.Value);
        }
    }
}
