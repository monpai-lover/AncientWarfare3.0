using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class DynastyTitleRegistryService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static int ReadLatestCycle(long pShiId, string pTitleType)
        {
            if (!Ready || pShiId < 0 || string.IsNullOrWhiteSpace(pTitleType)) return 0;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT IFNULL(MAX(CYCLE_NO),0) FROM " +
                                      DynastyTitleRegistryTableItem.GetTableName() +
                                      " WHERE SHI_ID=@shi AND TITLE_TYPE=@kind";
                command.Parameters.AddWithValue("@shi", pShiId);
                command.Parameters.AddWithValue("@kind", pTitleType.Trim());
                return Math.Max(0, Convert.ToInt32(command.ExecuteScalar()));
            }
            catch { return 0; }
        }

        public static HashSet<string> ReadUsed(long pShiId, string pTitleType, int pCycleNo)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!Ready || pShiId < 0 || string.IsNullOrWhiteSpace(pTitleType)) return result;
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT TITLE_VALUE FROM " +
                                  DynastyTitleRegistryTableItem.GetTableName() +
                                  " WHERE SHI_ID=@shi AND TITLE_TYPE=@kind AND CYCLE_NO=@cycle";
            command.Parameters.AddWithValue("@shi", pShiId);
            command.Parameters.AddWithValue("@kind", pTitleType.Trim());
            command.Parameters.AddWithValue("@cycle", Math.Max(0, pCycleNo));
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string value = reader.IsDBNull(0) ? "" : reader.GetString(0);
                if (!string.IsNullOrEmpty(value)) result.Add(value);
            }
            return result;
        }

        public static bool TryReserve(SQLiteConnection pDb, SQLiteTransaction pTransaction,
            long pShiId, string pTitleType, string pValue, int pCycleNo,
            long pActorId, long pReignId, double pTime)
        {
            if (pDb == null) throw new ArgumentNullException(nameof(pDb));
            if (pTransaction == null) throw new ArgumentNullException(nameof(pTransaction));
            if (pShiId < 0) throw new ArgumentOutOfRangeException(nameof(pShiId));
            if (string.IsNullOrWhiteSpace(pTitleType))
                throw new ArgumentException("Title type is required.", nameof(pTitleType));
            if (string.IsNullOrWhiteSpace(pValue))
                throw new ArgumentException("Title value is required.", nameof(pValue));

            try
            {
                using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
                command.CommandText = "INSERT INTO " +
                                      DynastyTitleRegistryTableItem.GetTableName() +
                                      " (REGISTRY_ID,SHI_ID,TITLE_TYPE,TITLE_VALUE,CYCLE_NO," +
                                      "ACTOR_ID,REIGN_ID,USED_TIME) VALUES (" +
                                      "(SELECT IFNULL(MAX(REGISTRY_ID),-1)+1 FROM " +
                                      DynastyTitleRegistryTableItem.GetTableName() +
                                      "),@shi,@kind,@value,@cycle,@actor,@reign,@time)";
                command.Parameters.AddWithValue("@shi", pShiId);
                command.Parameters.AddWithValue("@kind", pTitleType.Trim());
                command.Parameters.AddWithValue("@value", pValue.Trim());
                command.Parameters.AddWithValue("@cycle", Math.Max(0, pCycleNo));
                command.Parameters.AddWithValue("@actor", pActorId);
                command.Parameters.AddWithValue("@reign", pReignId);
                command.Parameters.AddWithValue("@time", pTime);
                command.ExecuteNonQuery();
                return true;
            }
            catch (SQLiteException error)
                when (error.ResultCode == SQLiteErrorCode.Constraint)
            {
                return false;
            }
        }
    }
}
