using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal sealed class NameIntegrationMaterializationState
    {
        internal long KingdomId;
        internal int Version;
        internal string Phase = "pending";
        internal long CursorActorId = -1L;
        internal int FailureCount;
        internal string LastError = string.Empty;
        internal double RequestedTime = -1d;
        internal double UpdatedTime = -1d;
    }

    internal static class NameIntegrationMaterializationStatePersistence
    {
        internal const int CurrentVersion = 1;
        private const string Table =
            "KingdomNameIntegrationMigrationState";

        internal static void Request(SQLiteConnection pDb, long kingdomId,
            double now)
        {
            if (pDb == null || kingdomId < 0L) return;
            NameIntegrationMaterializationState existing = Load(pDb,
                kingdomId);
            if (existing == null)
            {
                Execute(pDb, "INSERT INTO " + Table + " " +
                    "(KINGDOM_ID,VERSION,PHASE,CURSOR_ACTOR_ID," +
                    "FAILURE_COUNT,LAST_ERROR,REQUESTED_TIME,UPDATED_TIME) " +
                    "VALUES(@kingdom,@version,'pending',-1,0,'',@time,@time)",
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
            Execute(pDb, "UPDATE " + Table + " SET VERSION=@version," +
                "PHASE='pending',CURSOR_ACTOR_ID=@cursor," +
                "FAILURE_COUNT=0,LAST_ERROR='',REQUESTED_TIME=@time," +
                "UPDATED_TIME=@time WHERE KINGDOM_ID=@kingdom", command =>
                {
                    Add(command, "@version", CurrentVersion);
                    Add(command, "@cursor", cursor);
                    Add(command, "@time", now);
                    Add(command, "@kingdom", kingdomId);
                });
        }

        internal static NameIntegrationMaterializationState Load(
            SQLiteConnection pDb, long kingdomId)
        {
            if (pDb == null || kingdomId < 0L) return null;
            using var command = new SQLiteCommand(
                "SELECT KINGDOM_ID,VERSION,PHASE,CURSOR_ACTOR_ID," +
                "FAILURE_COUNT,LAST_ERROR,REQUESTED_TIME,UPDATED_TIME " +
                "FROM " + Table + " WHERE KINGDOM_ID=@kingdom", pDb);
            Add(command, "@kingdom", kingdomId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new NameIntegrationMaterializationState
            {
                KingdomId = reader.GetInt64(0),
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
            long kingdomId, long actorId, double now)
        {
            if (pDb == null || kingdomId < 0L || actorId < 0L) return;
            Execute(pDb, "UPDATE " + Table + " SET CURSOR_ACTOR_ID=" +
                "CASE WHEN CURSOR_ACTOR_ID<@actor THEN @actor ELSE " +
                "CURSOR_ACTOR_ID END,UPDATED_TIME=@time " +
                "WHERE KINGDOM_ID=@kingdom", command =>
                {
                    Add(command, "@actor", actorId);
                    Add(command, "@time", now);
                    Add(command, "@kingdom", kingdomId);
                });
        }

        internal static void RecordFailure(SQLiteConnection pDb,
            long kingdomId, string error, double now)
        {
            if (pDb == null || kingdomId < 0L) return;
            Execute(pDb, "UPDATE " + Table + " SET PHASE='pending'," +
                "FAILURE_COUNT=FAILURE_COUNT+1,LAST_ERROR=@error," +
                "UPDATED_TIME=@time WHERE KINGDOM_ID=@kingdom", command =>
                {
                    Add(command, "@error", (error ?? string.Empty).Trim());
                    Add(command, "@time", now);
                    Add(command, "@kingdom", kingdomId);
                });
        }

        internal static void MarkComplete(SQLiteConnection pDb,
            long kingdomId, double now)
        {
            if (pDb == null || kingdomId < 0L) return;
            Execute(pDb, "UPDATE " + Table + " SET PHASE='complete'," +
                "LAST_ERROR='',UPDATED_TIME=@time " +
                "WHERE KINGDOM_ID=@kingdom", command =>
                {
                    Add(command, "@time", now);
                    Add(command, "@kingdom", kingdomId);
                });
        }

        private static void Execute(SQLiteConnection pDb, string pSql,
            Action<SQLiteCommand> pBind)
        {
            using var command = new SQLiteCommand(pSql, pDb);
            pBind(command);
            command.ExecuteNonQuery();
        }

        private static void Add(SQLiteCommand pCommand, string pName,
            object pValue)
        {
            pCommand.Parameters.AddWithValue(pName, pValue ?? DBNull.Value);
        }
    }
}
