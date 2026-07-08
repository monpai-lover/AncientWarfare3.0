using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateRulerTitleService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static void OnMandateReignEnded(Kingdom pKingdom, Actor pKing,
            ReignRecordWriter.ReignInfo pReign, string pEndReason)
        {
            if (pKingdom?.data == null || pKing?.data == null || !pReign.IsValid || !Ready) return;

            MandateReport report = MandateService.ReadReport();
            pKingdom.data.get(LineageKeys.MANDATE_PERIOD_ID, out long periodId, -1L);
            if (periodId < 0) periodId = report.period_id;
            if (periodId < 0) return;
            PeriodTitleContext context = ReadPeriodContext(periodId, report);

            double endTime = pReign.EndTime > 0 ? pReign.EndTime : LineageService.CurTime();
            (int wins, int losses) = WarRecordWriter.GetWarRecord(pKingdom.id, pReign.StartTime, endTime);
            int reignIndex = MandateRulerTitleRules.ResolveMandateReignIndex(
                pReign.ReignIndex, CountPriorTitles(periodId, pReign.ReignId));
            bool founder = MandateRulerTitleRules.IsMandateFounderReign(
                pKing.data.id, context.FounderActorId, reignIndex);
            bool refounder = context.OriginType == "restoration";
            bool lowOrigin = context.OriginType == "rebel" || context.ClaimantKind == "rebel";

            int conquestScore = Math.Min(100,
                wins * 25 + Math.Max(0, pReign.EndCityCount - pReign.StartCityCount) * 12);
            int reformScore = Math.Min(100, context.DynastyPrestige + context.ImperialAuthority / 2);
            string temple = MandateRulerTitleRules.SelectTempleName(
                founder,
                lowOrigin, refounder, conquestScore, reformScore, reignIndex);
            temple = MandateRulerTitleRules.EnsureUniqueTempleName(
                temple, GetUsedTempleNames(periodId, pReign.ReignId), reignIndex);
            string pair = MandateRulerTitleRules.SelectDoublePosthumousTitle(
                civil: Math.Max(0, pReign.EndPopulation - pReign.StartPopulation) / 5 + context.ImperialAuthority,
                war: wins * 20 - losses * 12,
                order: context.MandateValue,
                disaster: pEndReason == "kingdom_fell" ? 90 : 0);
            pair = MandateRulerTitleRules.EnsureUniqueDoublePosthumousTitle(pair,
                GetUsedDoublePosthumousTitles(periodId, pReign.ReignId), pEndReason == "kingdom_fell", reignIndex);

            long existingRecordId = FindExistingTitleRecord(pReign.ReignId);
            string full = MandateRulerTitleRules.BuildFullTitle(temple, pair);
            if (existingRecordId >= 0 && ExistingTitleMatches(existingRecordId, temple, pair, full)) return;

            UpsertTitle(existingRecordId, pKingdom, pKing, pReign, periodId, temple, pair, full, pEndReason,
                "wins=" + wins + ";losses=" + losses + ";conquest=" + conquestScore + ";reform=" + reformScore);
        }

        public static void RepairMissingTempleTitles(List<MandatePeriodView> pPeriods)
        {
            if (!Ready || pPeriods == null || pPeriods.Count == 0) return;
            MandateReport report = MandateService.ReadReport();
            foreach (MandatePeriodView period in pPeriods)
            {
                if (period == null || period.period_id < 0) continue;
                RepairMissingTempleTitles(period, report);
            }
        }

        private static void UpsertTitle(long pExistingRecordId, Kingdom pKingdom, Actor pKing,
            ReignRecordWriter.ReignInfo pReign, long pPeriodId, string pTemple, string pPair, string pFull,
            string pEndReason, string pScoreDetail)
        {
            string color = HistoryColors.FromKingdom(pKingdom);
            if (pExistingRecordId >= 0)
            {
                DB.UpdateValue(MandateRulerTitleTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("RECORD_ID", pExistingRecordId) },
                    ColumnVal.Create("TEMPLE_NAME", pTemple ?? ""),
                    ColumnVal.Create("DOUBLE_POSTHUMOUS", pPair ?? ""),
                    ColumnVal.Create("FULL_TITLE", pFull ?? ""),
                    ColumnVal.Create("SCORE_DETAIL", pScoreDetail ?? ""),
                    ColumnVal.Create("DECIDED_TIME", LineageService.CurTime()));
            }
            else
            {
                long id = TableIdAllocator.Next(DB, MandateRulerTitleTableItem.GetTableName(), "RECORD_ID");
                DB.Insert(MandateRulerTitleTableItem.GetTableName(),
                    ColumnVal.Create("RECORD_ID", id),
                    ColumnVal.Create("PERIOD_ID", pPeriodId),
                    ColumnVal.Create("REIGN_ID", pReign.ReignId),
                    ColumnVal.Create("ACTOR_ID", pKing.data.id),
                    ColumnVal.Create("ACTOR_NAME", pKing.getName() ?? ""),
                    ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                    ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                    ColumnVal.Create("KINGDOM_COLOR", color),
                    ColumnVal.Create("TEMPLE_NAME", pTemple ?? ""),
                    ColumnVal.Create("DOUBLE_POSTHUMOUS", pPair ?? ""),
                    ColumnVal.Create("FULL_TITLE", pFull ?? ""),
                    ColumnVal.Create("REASON_KEY", pEndReason ?? ""),
                    ColumnVal.Create("SCORE_DETAIL", pScoreDetail ?? ""),
                    ColumnVal.Create("DECIDED_TIME", LineageService.CurTime()));
            }

            ReignRecordWriter.SetPosthumous(pReign.ReignId, pFull, color);
            string message = (pKing.getName() ?? "") + " \u8ffd\u4e0a\u5929\u547d\u5e99\u8c25\uff1a" + pFull;
            HistoryText rich = HistoryText.Actor(pKing) + " \u8ffd\u4e0a\u5929\u547d\u5e99\u8c25\uff1a" +
                               HistoryText.Colored(pFull, color);
            MandateService.RecordMandateEvent("mandate_ruler_title", pKingdom, pKing, null,
                0, MandateService.ReadReport().mandate_value, message);
            HistoryWriter.RecordKingdom(pKingdom, "mandate_ruler_title", rich, HistoryTarget.Actor(pKing));
            HistoryWriter.RecordPerson(pKing.data.id, pKingdom, pKing.getName(), "mandate_ruler_title",
                rich, ChronicleCategory.HONOR, HistoryTarget.Kingdom(pKingdom));
        }

        private static void RepairMissingTempleTitles(MandatePeriodView pPeriod, MandateReport pReport)
        {
            List<StoredTitleReign> rows = ReadStoredTitles(pPeriod.period_id);
            if (rows.Count == 0) return;

            PeriodTitleContext context = ReadPeriodContext(pPeriod.period_id, pReport);
            var usedTemples = new List<string>();
            var usedPairs = new List<string>();

            for (int i = 0; i < rows.Count; i++)
            {
                StoredTitleReign row = rows[i];
                int reignIndex = i + 1;
                bool founder = MandateRulerTitleRules.IsMandateFounderReign(
                    row.ActorId, context.FounderActorId, reignIndex);
                if (MandateRulerTitleRules.IsTempleNameValidForMandatePosition(row.TempleName, founder))
                {
                    usedTemples.Add(row.TempleName);
                    if (!string.IsNullOrEmpty(row.DoublePosthumous)) usedPairs.Add(row.DoublePosthumous);
                    continue;
                }

                bool refounder = context.OriginType == "restoration";
                bool lowOrigin = context.OriginType == "rebel" || context.ClaimantKind == "rebel";
                int conquestScore = Math.Min(100,
                    row.WarWins * 25 + Math.Max(0, row.EndCityCount - row.StartCityCount) * 12);
                int reformScore = Math.Min(100, context.DynastyPrestige + context.ImperialAuthority / 2);
                string temple = MandateRulerTitleRules.SelectTempleName(founder, lowOrigin, refounder,
                    conquestScore, reformScore, reignIndex);
                temple = MandateRulerTitleRules.EnsureUniqueTempleName(temple, usedTemples, reignIndex);
                if (string.IsNullOrEmpty(temple)) continue;

                string pair = row.DoublePosthumous;
                if (string.IsNullOrEmpty(pair))
                {
                    pair = MandateRulerTitleRules.SelectDoublePosthumousTitle(
                        Math.Max(0, row.EndPopulation - row.StartPopulation) / 5 + context.ImperialAuthority,
                        row.WarWins * 20 - row.WarLosses * 12,
                        context.MandateValue,
                        row.ReasonKey == "kingdom_fell" ? 90 : 0);
                    pair = MandateRulerTitleRules.EnsureUniqueDoublePosthumousTitle(
                        pair, usedPairs, row.ReasonKey == "kingdom_fell", reignIndex);
                }

                string color = string.IsNullOrEmpty(row.KingdomColor) ? pPeriod.kingdom_color : row.KingdomColor;
                string full = MandateRulerTitleRules.BuildFullTitle(temple, pair);
                UpdateStoredTitle(row.RecordId, row.ReignId, row.ActorId, row.ActorName,
                    pPeriod.period_id, temple, pair, full, color);
                usedTemples.Add(temple);
                if (!string.IsNullOrEmpty(pair)) usedPairs.Add(pair);
            }
        }

        private static List<string> GetUsedTempleNames(long pPeriodId, long pExcludeReignId = -1)
        {
            var result = new List<string>();
            if (!Ready || pPeriodId < 0) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT TEMPLE_NAME FROM " + MandateRulerTitleTableItem.GetTableName() +
                                  " WHERE PERIOD_ID=@p AND TEMPLE_NAME<>''" +
                                  (pExcludeReignId >= 0 ? " AND REIGN_ID<>@r" : "");
                cmd.Parameters.AddWithValue("@p", pPeriodId);
                if (pExcludeReignId >= 0) cmd.Parameters.AddWithValue("@r", pExcludeReignId);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string name = reader["TEMPLE_NAME"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(name)) result.Add(name);
                }
            }
            catch { }
            return result;
        }

        private static List<string> GetUsedDoublePosthumousTitles(long pPeriodId, long pExcludeReignId = -1)
        {
            var result = new List<string>();
            if (!Ready || pPeriodId < 0) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT DOUBLE_POSTHUMOUS FROM " + MandateRulerTitleTableItem.GetTableName() +
                                  " WHERE PERIOD_ID=@p AND DOUBLE_POSTHUMOUS<>''" +
                                  (pExcludeReignId >= 0 ? " AND REIGN_ID<>@r" : "") +
                                  " ORDER BY DECIDED_TIME, RECORD_ID";
                cmd.Parameters.AddWithValue("@p", pPeriodId);
                if (pExcludeReignId >= 0) cmd.Parameters.AddWithValue("@r", pExcludeReignId);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string title = reader["DOUBLE_POSTHUMOUS"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(title)) result.Add(title);
                }
            }
            catch { }
            return result;
        }

        private static long FindExistingTitleRecord(long pReignId)
        {
            if (!Ready || pReignId < 0) return -1;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT RECORD_ID FROM " + MandateRulerTitleTableItem.GetTableName() +
                                  " WHERE REIGN_ID=@r LIMIT 1";
                cmd.Parameters.AddWithValue("@r", pReignId);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? -1L : Convert.ToInt64(value);
            }
            catch { return -1; }
        }

        private static bool ExistingTitleMatches(long pRecordId, string pTemple, string pPair, string pFull)
        {
            if (!Ready || pRecordId < 0) return false;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT TEMPLE_NAME,DOUBLE_POSTHUMOUS,FULL_TITLE FROM " +
                                  MandateRulerTitleTableItem.GetTableName() +
                                  " WHERE RECORD_ID=@id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", pRecordId);
                using SQLiteDataReader r = cmd.ExecuteReader();
                if (!r.Read()) return false;
                return SafeStr(r, 0) == (pTemple ?? "") &&
                       SafeStr(r, 1) == (pPair ?? "") &&
                       SafeStr(r, 2) == (pFull ?? "");
            }
            catch { return false; }
        }

        private static int CountPriorTitles(long pPeriodId, long pExcludeReignId)
        {
            if (!Ready || pPeriodId < 0) return -1;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT COUNT(*) FROM " + MandateRulerTitleTableItem.GetTableName() +
                                  " WHERE PERIOD_ID=@p" +
                                  (pExcludeReignId >= 0 ? " AND REIGN_ID<>@r" : "");
                cmd.Parameters.AddWithValue("@p", pPeriodId);
                if (pExcludeReignId >= 0) cmd.Parameters.AddWithValue("@r", pExcludeReignId);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
            }
            catch { return -1; }
        }

        private static PeriodTitleContext ReadPeriodContext(long pPeriodId, MandateReport pFallback)
        {
            var context = new PeriodTitleContext
            {
                PeriodId = pPeriodId,
                FounderActorId = -1,
                OriginType = "native",
                ClaimantKind = "orthodox",
                MandateValue = pFallback?.period_id == pPeriodId ? pFallback.mandate_value : 0,
                ImperialAuthority = pFallback?.period_id == pPeriodId ? pFallback.imperial_authority : 0,
                DynastyPrestige = pFallback?.period_id == pPeriodId ? pFallback.dynasty_prestige : 0
            };
            if (!Ready || pPeriodId < 0) return context;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT FOUNDER_ACTOR_ID,ORIGIN_TYPE,CLAIMANT_KIND,START_MANDATE,END_MANDATE " +
                                  "FROM " + MandatePeriodTableItem.GetTableName() +
                                  " WHERE PERIOD_ID=@p LIMIT 1";
                cmd.Parameters.AddWithValue("@p", pPeriodId);
                using SQLiteDataReader r = cmd.ExecuteReader();
                if (!r.Read()) return context;
                context.FounderActorId = ToLong(r, 0, -1);
                string origin = SafeStr(r, 1);
                string claimant = SafeStr(r, 2);
                if (!string.IsNullOrEmpty(origin)) context.OriginType = origin;
                if (!string.IsNullOrEmpty(claimant)) context.ClaimantKind = claimant;
                if (context.MandateValue == 0) context.MandateValue = Math.Max(ToInt(r, 3, 0), ToInt(r, 4, 0));
            }
            catch { }
            return context;
        }

        private static List<StoredTitleReign> ReadStoredTitles(long pPeriodId)
        {
            var result = new List<StoredTitleReign>();
            if (!Ready || pPeriodId < 0) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    "SELECT t.RECORD_ID,t.REIGN_ID,t.ACTOR_ID,t.ACTOR_NAME,t.KINGDOM_ID,t.KINGDOM_NAME," +
                    "t.KINGDOM_COLOR,t.TEMPLE_NAME,t.DOUBLE_POSTHUMOUS,t.REASON_KEY," +
                    "IFNULL(r.START_POPULATION,0),IFNULL(r.END_POPULATION,0)," +
                    "IFNULL(r.START_CITY_COUNT,0),IFNULL(r.END_CITY_COUNT,0)," +
                    "IFNULL(r.WAR_WINS,0),IFNULL(r.WAR_LOSSES,0),IFNULL(r.START_TIME,0),IFNULL(r.END_TIME,0) " +
                    "FROM " + MandateRulerTitleTableItem.GetTableName() + " t " +
                    "LEFT JOIN " + KingdomReignTableItem.GetTableName() + " r ON r.REIGN_ID=t.REIGN_ID " +
                    "WHERE t.PERIOD_ID=@p ORDER BY IFNULL(r.START_TIME,t.DECIDED_TIME),t.RECORD_ID";
                cmd.Parameters.AddWithValue("@p", pPeriodId);
                using SQLiteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    result.Add(new StoredTitleReign
                    {
                        RecordId = ToLong(r, 0, -1),
                        ReignId = ToLong(r, 1, -1),
                        ActorId = ToLong(r, 2, -1),
                        ActorName = SafeStr(r, 3),
                        KingdomId = ToLong(r, 4, -1),
                        KingdomName = SafeStr(r, 5),
                        KingdomColor = SafeStr(r, 6),
                        TempleName = SafeStr(r, 7),
                        DoublePosthumous = SafeStr(r, 8),
                        ReasonKey = SafeStr(r, 9),
                        StartPopulation = ToInt(r, 10, 0),
                        EndPopulation = ToInt(r, 11, 0),
                        StartCityCount = ToInt(r, 12, 0),
                        EndCityCount = ToInt(r, 13, 0),
                        WarWins = ToInt(r, 14, 0),
                        WarLosses = ToInt(r, 15, 0),
                        StartTime = ToDouble(r, 16, 0),
                        EndTime = ToDouble(r, 17, 0)
                    });
                }
            }
            catch { }
            return result;
        }

        private static void UpdateStoredTitle(long pRecordId, long pReignId, long pActorId, string pActorName,
            long pPeriodId, string pTemple, string pPair, string pFull, string pColor)
        {
            if (!Ready || pRecordId < 0) return;
            string color = HistoryColors.Normalize(pColor);
            try
            {
                DB.UpdateValue(MandateRulerTitleTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("RECORD_ID", pRecordId) },
                    ColumnVal.Create("TEMPLE_NAME", pTemple ?? ""),
                    ColumnVal.Create("DOUBLE_POSTHUMOUS", pPair ?? ""),
                    ColumnVal.Create("FULL_TITLE", pFull ?? ""));
                ReignRecordWriter.SetPosthumous(pReignId, pFull, color);
                UpdateMandateTitleEventContent(pPeriodId, pActorId, pActorName, pFull);
            }
            catch { }
        }

        private static void UpdateMandateTitleEventContent(long pPeriodId, long pActorId, string pActorName,
            string pFull)
        {
            if (!Ready || pPeriodId < 0 || pActorId < 0 || string.IsNullOrEmpty(pFull)) return;
            try
            {
                DB.UpdateValue(MandateEventTableItem.GetTableName(),
                    new List<SimpleColumnConstraint>
                    {
                        SimpleColumnConstraint.CreateEq("PERIOD_ID", pPeriodId),
                        SimpleColumnConstraint.CreateEq("ACTOR_ID", pActorId),
                        SimpleColumnConstraint.CreateEq("EVENT_TYPE", "mandate_ruler_title")
                    },
                    ColumnVal.Create("CONTENT", (pActorName ?? "") + " \u8ffd\u4e0a\u5929\u547d\u5e99\u8c25\uff1a" + pFull));
            }
            catch { }
        }

        private static string SafeStr(SQLiteDataReader pReader, int pIndex)
        {
            try { return pReader.IsDBNull(pIndex) ? "" : Convert.ToString(pReader.GetValue(pIndex)) ?? ""; }
            catch { return ""; }
        }

        private static int ToInt(SQLiteDataReader pReader, int pIndex, int pDefault)
        {
            try { return pReader.IsDBNull(pIndex) ? pDefault : Convert.ToInt32(pReader.GetValue(pIndex)); }
            catch { return pDefault; }
        }

        private static long ToLong(SQLiteDataReader pReader, int pIndex, long pDefault)
        {
            try { return pReader.IsDBNull(pIndex) ? pDefault : Convert.ToInt64(pReader.GetValue(pIndex)); }
            catch { return pDefault; }
        }

        private static double ToDouble(SQLiteDataReader pReader, int pIndex, double pDefault)
        {
            try { return pReader.IsDBNull(pIndex) ? pDefault : Convert.ToDouble(pReader.GetValue(pIndex)); }
            catch { return pDefault; }
        }

        private struct PeriodTitleContext
        {
            public long PeriodId;
            public long FounderActorId;
            public string OriginType;
            public string ClaimantKind;
            public int MandateValue;
            public int ImperialAuthority;
            public int DynastyPrestige;
        }

        private struct StoredTitleReign
        {
            public long RecordId;
            public long ReignId;
            public long ActorId;
            public string ActorName;
            public long KingdomId;
            public string KingdomName;
            public string KingdomColor;
            public string TempleName;
            public string DoublePosthumous;
            public string ReasonKey;
            public int StartPopulation;
            public int EndPopulation;
            public int StartCityCount;
            public int EndCityCount;
            public int WarWins;
            public int WarLosses;
            public double StartTime;
            public double EndTime;
        }
    }
}
