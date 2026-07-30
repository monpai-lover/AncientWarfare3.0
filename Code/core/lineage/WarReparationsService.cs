using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class WarReparationsService
    {
        private const int MaximumInstallmentsPerCall = 16;
        private static bool _indexChecked;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        public static int Process(Kingdom payer)
        {
            SQLiteConnection db = DB;
            if (db == null || payer?.data == null || payer.isRekt())
                return 0;
            EnsureIndex(db);
            int year = SafeYear();
            List<Obligation> due = ReadDue(db, payer.id, year);
            int settled = 0;
            for (int i = 0; i < due.Count; i++)
            {
                Obligation row = due[i];
                if (!WarReparationsRules.IsDue(true, year,
                        row.NextDueYear, row.EndYear))
                {
                    if (year > row.EndYear) CloseExpired(db, row.Id);
                    continue;
                }
                Kingdom recipient = WarPeaceSettlementWorld.FindKingdom(
                    row.RecipientKingdomId);
                City source = payer.capital;
                City target = recipient?.capital;
                if (source == null || target == null ||
                    row.AnnualAmount <= 0 ||
                    string.IsNullOrWhiteSpace(row.ResourceId) ||
                    source.getResourcesAmount(row.ResourceId) <
                    row.AnnualAmount || !target.hasStockpiles()) continue;

                int sourceBefore = source.getResourcesAmount(row.ResourceId);
                int targetBefore = target.getResourcesAmount(row.ResourceId);
                try
                {
                    if (!WarPeaceResourceTransferService.TryTransferExact(
                            source, target, row.ResourceId,
                            row.AnnualAmount, out _))
                    {
                        continue;
                    }
                    if (!Advance(db, row, year))
                    {
                        Restore(target, row.ResourceId, targetBefore);
                        Restore(source, row.ResourceId, sourceBefore);
                        continue;
                    }
                    settled++;
                }
                catch (Exception error)
                {
                    try
                    {
                        Restore(target, row.ResourceId, targetBefore);
                        Restore(source, row.ResourceId, sourceBefore);
                    }
                    catch { }
                    ModClass.LogWarning("War reparations payment failed: " +
                                        error.Message);
                }
            }
            return settled;
        }

        private static List<Obligation> ReadDue(SQLiteConnection db,
            long payerId, int year)
        {
            var result = new List<Obligation>();
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT OBLIGATION_ID," +
                    "RECIPIENT_KINGDOM_ID,RESOURCE_ID,ANNUAL_AMOUNT," +
                    "END_YEAR,NEXT_DUE_YEAR,TOTAL_PAID FROM " +
                    WarReparationsObligationTableItem.GetTableName() +
                    " WHERE PAYER_KINGDOM_ID=@payer AND ACTIVE=1 " +
                    "AND NEXT_DUE_YEAR<=@year ORDER BY NEXT_DUE_YEAR ASC," +
                    "OBLIGATION_ID ASC LIMIT @limit";
                command.Parameters.AddWithValue("@payer", payerId);
                command.Parameters.AddWithValue("@year", year);
                command.Parameters.AddWithValue("@limit",
                    MaximumInstallmentsPerCall);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    result.Add(new Obligation
                    {
                        Id = reader.GetInt64(0),
                        RecipientKingdomId = reader.GetInt64(1),
                        ResourceId = reader.GetString(2),
                        AnnualAmount = reader.GetInt32(3),
                        EndYear = reader.GetInt32(4),
                        NextDueYear = reader.GetInt32(5),
                        TotalPaid = reader.GetInt32(6)
                    });
            }
            catch (Exception error)
            {
                ModClass.LogWarning("War reparations read failed: " +
                                    error.Message);
            }
            return result;
        }

        private static bool Advance(SQLiteConnection db, Obligation row,
            int paidYear)
        {
            int next = WarReparationsRules.NextDueYear(paidYear,
                row.EndYear);
            using var command = new SQLiteCommand(db);
            command.CommandText = "UPDATE " +
                WarReparationsObligationTableItem.GetTableName() +
                " SET TOTAL_PAID=@paid,NEXT_DUE_YEAR=@next," +
                "ACTIVE=@active WHERE OBLIGATION_ID=@id AND ACTIVE=1 " +
                "AND NEXT_DUE_YEAR=@expected";
            command.Parameters.AddWithValue("@paid",
                row.TotalPaid + row.AnnualAmount);
            command.Parameters.AddWithValue("@next", next);
            command.Parameters.AddWithValue("@active", next < 0 ? 0 : 1);
            command.Parameters.AddWithValue("@id", row.Id);
            command.Parameters.AddWithValue("@expected",
                row.NextDueYear);
            return command.ExecuteNonQuery() == 1;
        }

        private static void CloseExpired(SQLiteConnection db, long id)
        {
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "UPDATE " +
                    WarReparationsObligationTableItem.GetTableName() +
                    " SET ACTIVE=0,NEXT_DUE_YEAR=-1 WHERE " +
                    "OBLIGATION_ID=@id AND ACTIVE=1";
                command.Parameters.AddWithValue("@id", id);
                command.ExecuteNonQuery();
            }
            catch { }
        }

        private static void EnsureIndex(SQLiteConnection db)
        {
            if (_indexChecked) return;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "CREATE INDEX IF NOT EXISTS " +
                    "idx_WarReparations_payer_due ON " +
                    WarReparationsObligationTableItem.GetTableName() +
                    " (PAYER_KINGDOM_ID,ACTIVE,NEXT_DUE_YEAR," +
                    "OBLIGATION_ID)";
                command.ExecuteNonQuery();
                _indexChecked = true;
            }
            catch { }
        }

        private static void Restore(City city, string resourceId,
            int expected)
        {
            WarPeaceResourceTransferService.RestoreAmount(city,
                resourceId, expected);
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }

        private sealed class Obligation
        {
            public long Id;
            public long RecipientKingdomId;
            public string ResourceId = "";
            public int AnnualAmount;
            public int EndYear;
            public int NextDueYear;
            public int TotalPaid;
        }
    }
}
