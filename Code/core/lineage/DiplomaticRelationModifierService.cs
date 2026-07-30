using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class DiplomaticRelationModifierService
    {
        private static readonly DiplomaticRelationModifierCache Cache =
            new DiplomaticRelationModifierCache();
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance
                                         .InitializeSuccessful;

        public static bool Upsert(long pKingdomA, long pKingdomB,
            string pSourceType, long pSourceId, int pValue,
            int pStartYear, int pUntilYear)
        {
            if (!IsValid(pKingdomA, pKingdomB, pSourceType, pSourceId))
                return false;
            try
            {
                long modifierId = TableIdAllocator.Next(DB,
                    DiplomaticRelationModifierTableItem.GetTableName(),
                    "MODIFIER_ID");
                return UpsertCore(null, modifierId, pKingdomA, pKingdomB,
                    pSourceType, pSourceId, pValue, pStartYear, pUntilYear);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomatic relation modifier write failed: " +
                                    exception.Message);
                return false;
            }
        }

        internal static bool Upsert(SQLiteTransaction pTransaction,
            long pModifierId, long pKingdomA, long pKingdomB,
            string pSourceType, long pSourceId, int pValue,
            int pStartYear, int pUntilYear)
        {
            if (pTransaction == null || pModifierId < 0 ||
                !IsValid(pKingdomA, pKingdomB, pSourceType, pSourceId))
                return false;
            try
            {
                return UpsertCore(pTransaction, pModifierId,
                    pKingdomA, pKingdomB, pSourceType, pSourceId,
                    pValue, pStartYear, pUntilYear);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Transactional diplomatic modifier write failed: " +
                    exception.Message);
                return false;
            }
        }

        public static int ReadCached(long pKingdomA, long pKingdomB,
            int pCurrentYear)
        {
            if (!Ready || pKingdomA < 0 || pKingdomB < 0 ||
                pKingdomA == pKingdomB) return 0;
            return Cache.Read(pKingdomA, pKingdomB, pCurrentYear,
                LoadPair);
        }

        public static bool DeactivateSource(string pSourceType,
            long pSourceId)
        {
            if (!Ready || string.IsNullOrEmpty(pSourceType) || pSourceId < 0)
                return false;
            try
            {
                long first;
                long second;
                using (var read = new SQLiteCommand(
                           "SELECT KINGDOM_A_ID,KINGDOM_B_ID FROM " +
                           "DiplomaticRelationModifier WHERE SOURCE_TYPE=@type " +
                           "AND SOURCE_ID=@source AND ACTIVE=1 LIMIT 1", DB))
                {
                    read.Parameters.AddWithValue("@type", pSourceType);
                    read.Parameters.AddWithValue("@source", pSourceId);
                    using SQLiteDataReader reader = read.ExecuteReader();
                    if (!reader.Read()) return true;
                    first = reader.GetInt64(0);
                    second = reader.GetInt64(1);
                }
                using var update = new SQLiteCommand(
                    "UPDATE DiplomaticRelationModifier SET ACTIVE=0 WHERE " +
                    "SOURCE_TYPE=@type AND SOURCE_ID=@source AND ACTIVE=1", DB);
                update.Parameters.AddWithValue("@type", pSourceType);
                update.Parameters.AddWithValue("@source", pSourceId);
                update.ExecuteNonQuery();
                Cache.Invalidate(first, second);
                return true;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomatic relation modifier close failed: " +
                                    exception.Message);
                return false;
            }
        }

        public static void ClearRuntime()
        {
            Cache.Clear();
        }

        private static bool IsValid(long pKingdomA, long pKingdomB,
            string pSourceType, long pSourceId)
        {
            return Ready && pKingdomA >= 0 && pKingdomB >= 0 &&
                   pKingdomA != pKingdomB &&
                   !string.IsNullOrEmpty(pSourceType) && pSourceId >= 0;
        }

        private static bool UpsertCore(SQLiteTransaction pTransaction,
            long pModifierId, long pKingdomA, long pKingdomB,
            string pSourceType, long pSourceId, int pValue,
            int pStartYear, int pUntilYear)
        {
            DiplomacyKingdomPair pair =
                DiplomacyConversationRules.NormalizePair(
                    pKingdomA, pKingdomB);
            using var update = new SQLiteCommand(DB)
            {
                Transaction = pTransaction,
                CommandText =
                    "UPDATE DiplomaticRelationModifier SET " +
                    "KINGDOM_A_ID=@a,KINGDOM_B_ID=@b,VALUE=@value," +
                    "START_YEAR=@start,UNTIL_YEAR=@until,ACTIVE=1 " +
                    "WHERE SOURCE_TYPE=@type AND SOURCE_ID=@source"
            };
            update.Parameters.AddWithValue("@a", pair.FirstKingdomId);
            update.Parameters.AddWithValue("@b", pair.SecondKingdomId);
            update.Parameters.AddWithValue("@value", pValue);
            update.Parameters.AddWithValue("@start", pStartYear);
            update.Parameters.AddWithValue("@until", pUntilYear);
            update.Parameters.AddWithValue("@type", pSourceType);
            update.Parameters.AddWithValue("@source", pSourceId);
            if (update.ExecuteNonQuery() == 0)
            {
                using var insert = new SQLiteCommand(DB)
                {
                    Transaction = pTransaction,
                    CommandText =
                        "INSERT INTO DiplomaticRelationModifier " +
                        "(MODIFIER_ID,KINGDOM_A_ID,KINGDOM_B_ID," +
                        "SOURCE_TYPE,SOURCE_ID,VALUE,START_YEAR," +
                        "UNTIL_YEAR,ACTIVE) VALUES " +
                        "(@id,@a,@b,@type,@source,@value,@start,@until,1)"
                };
                insert.Parameters.AddWithValue("@id", pModifierId);
                insert.Parameters.AddWithValue("@a", pair.FirstKingdomId);
                insert.Parameters.AddWithValue("@b", pair.SecondKingdomId);
                insert.Parameters.AddWithValue("@type", pSourceType);
                insert.Parameters.AddWithValue("@source", pSourceId);
                insert.Parameters.AddWithValue("@value", pValue);
                insert.Parameters.AddWithValue("@start", pStartYear);
                insert.Parameters.AddWithValue("@until", pUntilYear);
                if (insert.ExecuteNonQuery() != 1) return false;
            }
            Cache.Invalidate(pKingdomA, pKingdomB);
            return true;
        }

        private static DiplomaticRelationModifierLoad LoadPair(
            DiplomacyKingdomPair pPair, int pCurrentYear)
        {
            int value = 0;
            int validUntil = int.MaxValue;
            try
            {
                using var command = new SQLiteCommand(
                    "SELECT VALUE,UNTIL_YEAR FROM " +
                    "DiplomaticRelationModifier WHERE KINGDOM_A_ID=@a AND KINGDOM_B_ID=@b AND ACTIVE=1 AND UNTIL_YEAR>=@year " +
                    "ORDER BY UNTIL_YEAR,MODIFIER_ID", DB);
                command.Parameters.AddWithValue("@a", pPair.FirstKingdomId);
                command.Parameters.AddWithValue("@b", pPair.SecondKingdomId);
                command.Parameters.AddWithValue("@year", pCurrentYear);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    value += reader.GetInt32(0);
                    validUntil = Math.Min(validUntil, reader.GetInt32(1));
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomatic relation modifier read failed: " +
                                    exception.Message);
                validUntil = pCurrentYear;
            }
            return new DiplomaticRelationModifierLoad(value, validUntil);
        }
    }
}
