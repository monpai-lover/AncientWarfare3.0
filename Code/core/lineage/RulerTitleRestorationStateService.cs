using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct RulerTitleRestorationState
    {
        public readonly bool WasFormerMandateShi;
        public readonly bool RestoredPending;
        public readonly bool SelfRestorationCompleted;
        public readonly bool RegainedMandate;
        public readonly long SelfRestorationActorId;
        public readonly long RegainedMandateActorId;

        public RulerTitleRestorationState(bool pWasFormerMandateShi,
            bool pRestoredPending, bool pSelfRestorationCompleted,
            bool pRegainedMandate, long pSelfRestorationActorId,
            long pRegainedMandateActorId)
        {
            WasFormerMandateShi = pWasFormerMandateShi;
            RestoredPending = pRestoredPending;
            SelfRestorationCompleted = pSelfRestorationCompleted;
            RegainedMandate = pRegainedMandate;
            SelfRestorationActorId = pSelfRestorationActorId;
            RegainedMandateActorId = pRegainedMandateActorId;
        }
    }

    internal static class RulerTitleRestorationStateService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static void MarkMandateLost(Kingdom pKingdom)
        {
            long shiId = ResolveShiId(pKingdom);
            if (shiId < 0) return;
            Update(shiId,
                ColumnVal.Create("WAS_FORMER_MANDATE", 1),
                ColumnVal.Create("RESTORED_PENDING", 1),
                ColumnVal.Create("REGAINED_MANDATE", 0),
                ColumnVal.Create("REGAINED_MANDATE_ACTOR_ID", -1L));
        }

        public static void MarkAutonomousRestorationCompleted(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.get(LineageKeys.RESTORATION_MODE, out string mode, "");
            pKingdom.data.get(LineageKeys.RESTORATION_COMPLETED, out bool completed, false);
            pKingdom.data.get(LineageKeys.RESTORATION_ORIGINAL_MANDATE_PERIOD_ID,
                out long originalMandatePeriodId, -1L);
            if (!completed || mode != "self_restoration" || originalMandatePeriodId < 0) return;

            long shiId = ResolveShiId(pKingdom);
            if (shiId < 0) return;
            long rulerId = pKingdom.king?.data?.id ?? -1L;
            Update(shiId,
                ColumnVal.Create("WAS_FORMER_MANDATE", 1),
                ColumnVal.Create("RESTORED_PENDING", 1),
                ColumnVal.Create("SELF_RESTORATION_COMPLETED", 1),
                ColumnVal.Create("REGAINED_MANDATE", 0),
                ColumnVal.Create("SELF_RESTORATION_ACTOR_ID", rulerId),
                ColumnVal.Create("REGAINED_MANDATE_ACTOR_ID", -1L));
        }

        public static void MarkMandateRegained(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.get(LineageKeys.MANDATE_ORIGIN_TYPE, out string origin, "");
            if (origin != "self_restoration") return;
            long shiId = ResolveShiId(pKingdom);
            RulerTitleRestorationState state = Read(shiId);
            if (shiId < 0 || !state.WasFormerMandateShi ||
                !state.SelfRestorationCompleted) return;
            long rulerId = pKingdom.king?.data?.id ?? -1L;
            Update(shiId,
                ColumnVal.Create("RESTORED_PENDING", 0),
                ColumnVal.Create("REGAINED_MANDATE", 1),
                ColumnVal.Create("REGAINED_MANDATE_ACTOR_ID", rulerId));
        }

        public static RulerTitleRestorationState Read(long pShiId)
        {
            if (!Ready || pShiId < 0) return default;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT IFNULL(WAS_FORMER_MANDATE,0)," +
                                      "IFNULL(RESTORED_PENDING,0)," +
                                      "IFNULL(SELF_RESTORATION_COMPLETED,0)," +
                                      "IFNULL(REGAINED_MANDATE,0)," +
                                      "IFNULL(SELF_RESTORATION_ACTOR_ID,-1)," +
                                      "IFNULL(REGAINED_MANDATE_ACTOR_ID,-1) FROM " +
                                      ShiBranchTableItem.GetTableName() +
                                      " WHERE SHI_ID=@shi LIMIT 1";
                command.Parameters.AddWithValue("@shi", pShiId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return default;
                return new RulerTitleRestorationState(
                    ValueInt(reader, 0) != 0,
                    ValueInt(reader, 1) != 0,
                    ValueInt(reader, 2) != 0,
                    ValueInt(reader, 3) != 0,
                    ValueLong(reader, 4, -1),
                    ValueLong(reader, 5, -1));
            }
            catch { return default; }
        }

        private static long ResolveShiId(Kingdom pKingdom)
        {
            if (!Ready || pKingdom?.data == null) return -1;
            if (pKingdom.king?.data != null)
            {
                pKingdom.king.data.get(LineageKeys.SHI_ID, out long actorShiId, -1L);
                if (actorShiId >= 0) return actorShiId;
            }
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT IFNULL(SHI_ID,-1) FROM " +
                                      DynastyPeriodTableItem.GetTableName() +
                                      " WHERE KINGDOM_ID=@kingdom AND END_TIME=-1 " +
                                      "ORDER BY START_TIME DESC LIMIT 1";
                command.Parameters.AddWithValue("@kingdom", pKingdom.id);
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? -1L
                    : Convert.ToInt64(value);
            }
            catch { return -1; }
        }

        private static void Update(long pShiId, params ColumnVal[] pValues)
        {
            if (!Ready || pShiId < 0 || pValues == null || pValues.Length == 0) return;
            try
            {
                DB.UpdateValue(ShiBranchTableItem.GetTableName(),
                    new List<SimpleColumnConstraint>
                    {
                        SimpleColumnConstraint.CreateEq("SHI_ID", pShiId)
                    },
                    pValues);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Ruler restoration title state failed: " + error.Message);
            }
        }

        private static int ValueInt(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0 : Convert.ToInt32(pReader.GetValue(pIndex));
        }

        private static long ValueLong(SQLiteDataReader pReader, int pIndex, long pDefault)
        {
            return pReader.IsDBNull(pIndex) ? pDefault : Convert.ToInt64(pReader.GetValue(pIndex));
        }
    }
}
