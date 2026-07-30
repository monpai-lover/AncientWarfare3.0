using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class EraRecordWriter
    {
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance.InitializeSuccessful;
        private static string Table => EraPeriodTableItem.GetTableName();

        public static EraAtomicCommitResult TryCommit(
            EraAtomicCommitRequest pRequest)
        {
            return Ready
                ? EraAtomicPersistence.TryCommit(DB, pRequest)
                : EraAtomicCommitResult.Failed("lineage archive unavailable");
        }

        public static EraAtomicCommitResult TryRecoverLegacyCurrent(
            EraAtomicCommitRequest pRequest)
        {
            return Ready
                ? EraAtomicPersistence.TryRecoverLegacyCurrent(DB, pRequest)
                : EraAtomicCommitResult.Failed(
                    "lineage archive unavailable");
        }

        public static bool TryReadEvent(long pReignId, EraChangeKind pKind,
            string pSourceEventId, out long pEraId, out string pEraName,
            out double pStartTime)
        {
            pEraId = -1;
            pEraName = "";
            pStartTime = -1;
            if (!Ready || pReignId < 0 || string.IsNullOrWhiteSpace(pSourceEventId))
                return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT ERA_ID,IFNULL(ERA_STEM,''),START_TIME FROM " +
                                      Table + " WHERE REIGN_ID=@reign AND CHANGE_KIND=@kind " +
                                      "AND SOURCE_EVENT_ID=@source LIMIT 1";
                command.Parameters.AddWithValue("@reign", pReignId);
                command.Parameters.AddWithValue("@kind", KindId(pKind));
                command.Parameters.AddWithValue("@source", pSourceEventId.Trim());
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                pEraId = reader.GetInt64(0);
                pEraName = reader.IsDBNull(1) ? "" : reader.GetString(1);
                pStartTime = reader.IsDBNull(2) ? -1 : reader.GetDouble(2);
                return EraNameRules.IsValidCustom(pEraName);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryReadCurrent(long pKingdomId, out long pEraId,
            out string pEraName, out double pStartTime)
        {
            pEraId = -1;
            pEraName = "";
            pStartTime = -1;
            if (!Ready || pKingdomId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT ERA_ID,IFNULL(ERA_STEM,''),START_TIME FROM " +
                                      Table + " WHERE KINGDOM_ID=@kingdom AND END_TIME=-1 " +
                                      "ORDER BY START_TIME DESC,ERA_ID DESC LIMIT 1";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                pEraId = reader.GetInt64(0);
                pEraName = reader.IsDBNull(1) ? "" : reader.GetString(1);
                pStartTime = reader.IsDBNull(2) ? -1 : reader.GetDouble(2);
                return EraNameRules.IsValidCustom(pEraName);
            }
            catch
            {
                return false;
            }
        }

        public static int YearsSinceLastActiveChange(long pKingdomId,
            int pCurrentYear)
        {
            if (!Ready || pKingdomId < 0) return int.MaxValue;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT MAX(START_YEAR) FROM " + Table +
                                      " WHERE KINGDOM_ID=@kingdom AND " +
                                      "CHANGE_KIND IN ('voluntary','ai_major_event')";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                object value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value) return int.MaxValue;
                return Math.Max(0, pCurrentYear - Convert.ToInt32(value));
            }
            catch
            {
                return 0;
            }
        }

        public static void CloseOpenEra(long pKingdomId)
        {
            if (!Ready || pKingdomId < 0) return;
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                using var command = new SQLiteCommand(DB) { Transaction = transaction };
                command.CommandText = "UPDATE " + Table +
                                      " SET END_TIME=@time WHERE KINGDOM_ID=@kingdom " +
                                      "AND END_TIME=-1";
                command.Parameters.AddWithValue("@time", LineageService.CurTime());
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.ExecuteNonQuery();
                transaction.Commit();
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Era close failed: " + error.Message);
            }
        }

        public static string KindId(EraChangeKind pKind)
        {
            return EraNameRules.KindId(pKind);
        }
    }
}
