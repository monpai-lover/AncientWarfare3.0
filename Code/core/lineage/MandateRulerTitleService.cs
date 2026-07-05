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
            if (HasExistingTitle(pReign.ReignId)) return;

            MandateReport report = MandateService.ReadReport();
            pKingdom.data.get(LineageKeys.MANDATE_PERIOD_ID, out long periodId, -1L);
            if (periodId < 0) periodId = report.period_id;
            if (periodId < 0) return;

            double endTime = pReign.EndTime > 0 ? pReign.EndTime : LineageService.CurTime();
            (int wins, int losses) = WarRecordWriter.GetWarRecord(pKingdom.id, pReign.StartTime, endTime);
            int reignIndex = pReign.ReignIndex <= 0 ? 1 : pReign.ReignIndex;
            bool refounder = report.origin_type == "restoration";
            bool lowOrigin = report.origin_type == "rebel" || report.claimant_kind == "rebel";

            int conquestScore = Math.Min(100,
                wins * 25 + Math.Max(0, pReign.EndCityCount - pReign.StartCityCount) * 12);
            int reformScore = Math.Min(100, report.dynasty_prestige + report.imperial_authority / 2);
            string temple = MandateRulerTitleRules.SelectTempleName(
                pReign.IsFounder != 0 || reignIndex == 1,
                lowOrigin, refounder, conquestScore, reformScore, reignIndex);
            temple = MandateRulerTitleRules.EnsureUniqueTempleName(temple, GetUsedTempleNames(periodId), reignIndex);
            string pair = MandateRulerTitleRules.SelectDoublePosthumousTitle(
                civil: Math.Max(0, pReign.EndPopulation - pReign.StartPopulation) / 5 + report.imperial_authority,
                war: wins * 20 - losses * 12,
                order: report.mandate_value,
                disaster: pEndReason == "kingdom_fell" ? 90 : 0);
            InsertTitle(pKingdom, pKing, pReign, periodId, temple, pair, pEndReason,
                "wins=" + wins + ";losses=" + losses + ";conquest=" + conquestScore + ";reform=" + reformScore);
        }

        private static void InsertTitle(Kingdom pKingdom, Actor pKing, ReignRecordWriter.ReignInfo pReign,
            long pPeriodId, string pTemple, string pPair, string pEndReason, string pScoreDetail)
        {
            long id = TableIdAllocator.Next(DB, MandateRulerTitleTableItem.GetTableName(), "RECORD_ID");
            string full = MandateRulerTitleRules.BuildFullTitle(pTemple, pPair);
            string color = HistoryColors.FromKingdom(pKingdom);
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
                ColumnVal.Create("FULL_TITLE", full),
                ColumnVal.Create("REASON_KEY", pEndReason ?? ""),
                ColumnVal.Create("SCORE_DETAIL", pScoreDetail ?? ""),
                ColumnVal.Create("DECIDED_TIME", LineageService.CurTime()));

            ReignRecordWriter.SetPosthumous(pReign.ReignId, full, color);
            string message = (pKing.getName() ?? "") + " \u8ffd\u4e0a\u5929\u547d\u5e99\u8c25\uff1a" + full;
            HistoryText rich = HistoryText.Actor(pKing) + " \u8ffd\u4e0a\u5929\u547d\u5e99\u8c25\uff1a" +
                               HistoryText.Colored(full, color);
            MandateService.RecordMandateEvent("mandate_ruler_title", pKingdom, pKing, null,
                0, MandateService.ReadReport().mandate_value, message);
            HistoryWriter.RecordKingdom(pKingdom, "mandate_ruler_title", rich, HistoryTarget.Actor(pKing));
            HistoryWriter.RecordPerson(pKing.data.id, pKingdom, pKing.getName(), "mandate_ruler_title",
                rich, ChronicleCategory.HONOR, HistoryTarget.Kingdom(pKingdom));
        }

        private static List<string> GetUsedTempleNames(long pPeriodId)
        {
            var result = new List<string>();
            if (!Ready || pPeriodId < 0) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT TEMPLE_NAME FROM " + MandateRulerTitleTableItem.GetTableName() +
                                  " WHERE PERIOD_ID=@p AND TEMPLE_NAME<>''";
                cmd.Parameters.AddWithValue("@p", pPeriodId);
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

        private static bool HasExistingTitle(long pReignId)
        {
            if (!Ready || pReignId < 0) return false;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT 1 FROM " + MandateRulerTitleTableItem.GetTableName() +
                                  " WHERE REIGN_ID=@r LIMIT 1";
                cmd.Parameters.AddWithValue("@r", pReignId);
                return cmd.ExecuteScalar() != null;
            }
            catch { return false; }
        }
    }
}
