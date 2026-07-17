using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public sealed class EraAtomicCommitRequest
    {
        public long EraId = -1;
        public long KingdomId = -1;
        public string KingdomColor = "";
        public long ShiId = -1;
        public long ActorId = -1;
        public long ReignId = -1;
        public string EraName = "";
        public string ChangeKind = "";
        public string ChangeReason = "";
        public string SourceEventId = "";
        public double DecidedTime;
        public int StartYear;
        public string YearPrefix = "";
        public string YearPrefixRich = "";
        public string StateName = "";
        public string ActorName = "";
        public string HistoryContent = "";
        public string HistoryContentRich = "";
        public string BiographyCategory = "";
        public string BiographyRole = "";
        public string BiographyRoleLabel = "";
        public int AgeAtEvent = -1;
    }

    public readonly struct EraAtomicCommitResult
    {
        public readonly bool Success;
        public readonly bool AlreadyCommitted;
        public readonly long EraId;
        public readonly string EraName;
        public readonly string Error;

        public EraAtomicCommitResult(bool pSuccess, bool pAlreadyCommitted,
            long pEraId, string pEraName, string pError)
        {
            Success = pSuccess;
            AlreadyCommitted = pAlreadyCommitted;
            EraId = pEraId;
            EraName = pEraName ?? "";
            Error = pError ?? "";
        }

        public static EraAtomicCommitResult Failed(string pError = "") =>
            new EraAtomicCommitResult(false, false, -1, "", pError);
    }

    public static class EraAtomicPersistence
    {
        private const string EraTable = "EraPeriod";
        private const string RegistryTable = "DynastyTitleRegistry";
        private const string KingdomHistoryTable = "KingdomHistory";
        private const string PersonBiographyTable = "PersonBiography";

        public static EraAtomicCommitResult TryCommit(SQLiteConnection pDb,
            EraAtomicCommitRequest pRequest)
        {
            if (!IsValid(pDb, pRequest))
                return EraAtomicCommitResult.Failed("invalid era commit request");
            if (TryReadExisting(pDb, null, pRequest, out EraAtomicCommitResult existing))
                return existing;

            try
            {
                using SQLiteTransaction transaction = pDb.BeginTransaction();
                if (TryReadExisting(pDb, transaction, pRequest, out existing))
                {
                    transaction.Rollback();
                    return existing;
                }

                long eraId = pRequest.EraId >= 0
                    ? pRequest.EraId
                    : NextId(pDb, transaction, EraTable, "ERA_ID");
                ClosePreviousEra(pDb, transaction, pRequest);
                InsertEra(pDb, transaction, eraId, pRequest);
                InsertRegistry(pDb, transaction, pRequest);
                InsertKingdomHistory(pDb, transaction, pRequest);
                if (pRequest.ActorId >= 0)
                    InsertPersonBiography(pDb, transaction, pRequest);
                transaction.Commit();
                return new EraAtomicCommitResult(true, false, eraId,
                    pRequest.EraName, "");
            }
            catch (Exception error)
            {
                if (TryReadExisting(pDb, null, pRequest, out existing))
                    return existing;
                return EraAtomicCommitResult.Failed(error.Message);
            }
        }

        private static bool IsValid(SQLiteConnection pDb,
            EraAtomicCommitRequest pRequest)
        {
            return pDb != null && pDb.State == System.Data.ConnectionState.Open &&
                   pRequest != null && pRequest.KingdomId >= 0 &&
                   pRequest.ShiId >= 0 && pRequest.ActorId >= 0 &&
                   pRequest.ReignId >= 0 &&
                   !string.IsNullOrWhiteSpace(pRequest.EraName) &&
                   !string.IsNullOrWhiteSpace(pRequest.ChangeKind) &&
                   !string.IsNullOrWhiteSpace(pRequest.SourceEventId);
        }

        private static void ClosePreviousEra(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, EraAtomicCommitRequest pRequest)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "UPDATE " + EraTable +
                                  " SET END_TIME=@time WHERE KINGDOM_ID=@kingdom AND END_TIME=-1";
            command.Parameters.AddWithValue("@time", pRequest.DecidedTime);
            command.Parameters.AddWithValue("@kingdom", pRequest.KingdomId);
            command.ExecuteNonQuery();
        }

        private static void InsertEra(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pEraId,
            EraAtomicCommitRequest pRequest)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + EraTable +
                " (ERA_ID,KINGDOM_ID,KINGDOM_COLOR,SHI_ID,ACTOR_ID,REIGN_ID," +
                "ERA_STEM,ERA_COLOR,CHANGE_KIND,CHANGE_REASON,SOURCE_EVENT_ID," +
                "DECIDED_TIME,START_TIME,END_TIME,START_YEAR) VALUES " +
                "(@era,@kingdom,@color,@shi,@actor,@reign,@name,@color,@kind," +
                "@reason,@source,@time,@time,-1,@year)";
            command.Parameters.AddWithValue("@era", pEraId);
            command.Parameters.AddWithValue("@kingdom", pRequest.KingdomId);
            command.Parameters.AddWithValue("@color", pRequest.KingdomColor ?? "");
            command.Parameters.AddWithValue("@shi", pRequest.ShiId);
            command.Parameters.AddWithValue("@actor", pRequest.ActorId);
            command.Parameters.AddWithValue("@reign", pRequest.ReignId);
            command.Parameters.AddWithValue("@name", pRequest.EraName.Trim());
            command.Parameters.AddWithValue("@kind", pRequest.ChangeKind.Trim());
            command.Parameters.AddWithValue("@reason", pRequest.ChangeReason ?? "");
            command.Parameters.AddWithValue("@source", pRequest.SourceEventId.Trim());
            command.Parameters.AddWithValue("@time", pRequest.DecidedTime);
            command.Parameters.AddWithValue("@year", pRequest.StartYear);
            command.ExecuteNonQuery();
        }

        private static void InsertRegistry(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, EraAtomicCommitRequest pRequest)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + RegistryTable +
                " (REGISTRY_ID,SHI_ID,TITLE_TYPE,TITLE_VALUE,CYCLE_NO,ACTOR_ID," +
                "REIGN_ID,USED_TIME) VALUES ((SELECT IFNULL(MAX(REGISTRY_ID),-1)+1 " +
                "FROM " + RegistryTable + "),@shi,'era',@name,0,@actor,@reign,@time)";
            command.Parameters.AddWithValue("@shi", pRequest.ShiId);
            command.Parameters.AddWithValue("@name", pRequest.EraName.Trim());
            command.Parameters.AddWithValue("@actor", pRequest.ActorId);
            command.Parameters.AddWithValue("@reign", pRequest.ReignId);
            command.Parameters.AddWithValue("@time", pRequest.DecidedTime);
            command.ExecuteNonQuery();
        }

        private static void InsertKingdomHistory(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, EraAtomicCommitRequest pRequest)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + KingdomHistoryTable +
                " (EVENT_ID,KINGDOM_ID,WORLD_TIME,YEAR_PREFIX,YEAR_PREFIX_RICH," +
                "SUBJECT_NAME,SUBJECT_COLOR,CONTENT,CONTENT_RICH,EVENT_TYPE," +
                "CONTEXT_KINGDOM_ID,CONTEXT_KINGDOM_NAME,CONTEXT_KINGDOM_COLOR," +
                "TARGET_TYPE,TARGET_ID) VALUES (@id,@kingdom,@time,@year,@yearRich," +
                "@state,@color,@content,@rich,'era_change',@kingdom,@state,@color," +
                "'actor',@actor)";
            command.Parameters.AddWithValue("@id",
                NextId(pDb, pTransaction, KingdomHistoryTable, "EVENT_ID"));
            AddHistoryParameters(command, pRequest);
            command.ExecuteNonQuery();
        }

        private static void InsertPersonBiography(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, EraAtomicCommitRequest pRequest)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + PersonBiographyTable +
                " (EVENT_ID,ACTOR_ID,WORLD_TIME,YEAR_PREFIX,YEAR_PREFIX_RICH," +
                "SUBJECT_NAME,SUBJECT_COLOR,CONTENT,CONTENT_RICH,EVENT_TYPE,CATEGORY," +
                "AGE_AT_EVENT,IS_KING_AT_EVENT,ROLE_SNAPSHOT,ROLE_LABEL," +
                "CONTEXT_KINGDOM_ID,CONTEXT_KINGDOM_NAME,CONTEXT_KINGDOM_COLOR," +
                "TARGET_TYPE,TARGET_ID) VALUES (@id,@actor,@time,@year,@yearRich," +
                "@actorName,@color,@content,@rich,'era_change',@category,@age,1," +
                "@role,@roleLabel,@kingdom,@state,@color,'kingdom',@kingdom)";
            command.Parameters.AddWithValue("@id",
                NextId(pDb, pTransaction, PersonBiographyTable, "EVENT_ID"));
            command.Parameters.AddWithValue("@actorName", pRequest.ActorName ?? "");
            command.Parameters.AddWithValue("@category", pRequest.BiographyCategory ?? "");
            command.Parameters.AddWithValue("@age", pRequest.AgeAtEvent);
            command.Parameters.AddWithValue("@role", pRequest.BiographyRole ?? "");
            command.Parameters.AddWithValue("@roleLabel", pRequest.BiographyRoleLabel ?? "");
            AddHistoryParameters(command, pRequest);
            command.ExecuteNonQuery();
        }

        private static void AddHistoryParameters(SQLiteCommand pCommand,
            EraAtomicCommitRequest pRequest)
        {
            pCommand.Parameters.AddWithValue("@kingdom", pRequest.KingdomId);
            pCommand.Parameters.AddWithValue("@actor", pRequest.ActorId);
            pCommand.Parameters.AddWithValue("@time", pRequest.DecidedTime);
            pCommand.Parameters.AddWithValue("@year", pRequest.YearPrefix ?? "");
            pCommand.Parameters.AddWithValue("@yearRich", pRequest.YearPrefixRich ?? "");
            pCommand.Parameters.AddWithValue("@state", pRequest.StateName ?? "");
            pCommand.Parameters.AddWithValue("@color", pRequest.KingdomColor ?? "");
            pCommand.Parameters.AddWithValue("@content", pRequest.HistoryContent ?? "");
            pCommand.Parameters.AddWithValue("@rich", string.IsNullOrEmpty(
                pRequest.HistoryContentRich)
                ? pRequest.HistoryContent ?? ""
                : pRequest.HistoryContentRich);
        }

        private static bool TryReadExisting(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, EraAtomicCommitRequest pRequest,
            out EraAtomicCommitResult pResult)
        {
            pResult = EraAtomicCommitResult.Failed();
            if (pRequest == null || pRequest.ReignId < 0 ||
                string.IsNullOrWhiteSpace(pRequest.ChangeKind) ||
                string.IsNullOrWhiteSpace(pRequest.SourceEventId)) return false;
            try
            {
                using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
                command.CommandText = "SELECT ERA_ID,IFNULL(ERA_STEM,'') FROM " +
                                      EraTable + " WHERE REIGN_ID=@reign AND " +
                                      "CHANGE_KIND=@kind AND SOURCE_EVENT_ID=@source LIMIT 1";
                command.Parameters.AddWithValue("@reign", pRequest.ReignId);
                command.Parameters.AddWithValue("@kind", pRequest.ChangeKind.Trim());
                command.Parameters.AddWithValue("@source", pRequest.SourceEventId.Trim());
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                pResult = new EraAtomicCommitResult(true, true,
                    reader.GetInt64(0), reader.IsDBNull(1) ? "" : reader.GetString(1), "");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static long NextId(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, string pColumn)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(" + pColumn + "),-1)+1 FROM " + pTable;
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }
}
