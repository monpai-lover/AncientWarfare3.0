using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateHistoryQuery
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static List<MandatePeriodView> GetPeriods()
        {
            var periods = ReadPeriods();
            if (periods.Count == 0) return periods;

            var eventsByPeriod = ReadEventsByPeriod();
            foreach (MandatePeriodView period in periods)
            {
                period.reigns.AddRange(ReadReignsForPeriod(period));
                if (period.reigns.Count == 0)
                    period.reigns.Add(BuildFallbackReign(period));

                if (eventsByPeriod.TryGetValue(period.period_id, out List<MandateHistoryEvent> events))
                    AssignEvents(period, events);

                RemoveEmptyFallbackReigns(period);
            }

            return periods;
        }

        private static List<MandatePeriodView> ReadPeriods()
        {
            var result = new List<MandatePeriodView>();
            if (!Ready) return result;

            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    "SELECT PERIOD_ID,KINGDOM_ID,KINGDOM_NAME,KINGDOM_COLOR,DYNASTY_NAME,FOUNDER_ACTOR_ID,FOUNDER_NAME," +
                    "START_TIME,END_TIME,END_REASON,START_MANDATE,END_MANDATE,LEGAL_CORE_COUNT,ORIGIN_TYPE," +
                    "REBEL_ORIGIN_KINGDOM_ID,REBEL_ORIGIN_KINGDOM_NAME,CLAIMANT_KIND " +
                    "FROM " + MandatePeriodTableItem.GetTableName() + " ORDER BY START_TIME ASC, PERIOD_ID ASC";
                using SQLiteDataReader r = cmd.ExecuteReader();
                int idx = 0;
                while (r.Read())
                {
                    result.Add(new MandatePeriodView
                    {
                        index = idx++,
                        period_id = ToLong(r, 0, -1),
                        kingdom_id = ToLong(r, 1, -1),
                        kingdom_name = SafeStr(r, 2),
                        kingdom_color = SafeStr(r, 3),
                        dynasty_name = SafeStr(r, 4),
                        founder_actor_id = ToLong(r, 5, -1),
                        founder_name = SafeStr(r, 6),
                        start_time = ToDouble(r, 7, -1),
                        end_time = ToDouble(r, 8, -1),
                        end_reason = SafeStr(r, 9),
                        start_mandate = ToInt(r, 10),
                        end_mandate = ToInt(r, 11),
                        legal_core_count = ToInt(r, 12),
                        origin_type = SafeStr(r, 13),
                        rebel_origin_kingdom_id = ToLong(r, 14, -1),
                        rebel_origin_kingdom_name = SafeStr(r, 15),
                        claimant_kind = SafeStr(r, 16)
                    });
                }
            }
            catch { }

            FillKingdomArchiveSnapshots(result);
            return result;
        }

        private static void FillKingdomArchiveSnapshots(List<MandatePeriodView> pPeriods)
        {
            if (!Ready || pPeriods == null || pPeriods.Count == 0) return;
            foreach (MandatePeriodView period in pPeriods)
            {
                if (period == null || period.kingdom_id < 0) continue;
                try
                {
                    using var cmd = new SQLiteCommand(DB);
                    cmd.CommandText =
                        "SELECT COLOR_TEXT,COLOR_ID,BANNER_ICON_ID,BANNER_BACKGROUND_ID,BANNER_ID " +
                        "FROM " + KingdomArchiveTableItem.GetTableName() + " WHERE KINGDOM_ID=@kid LIMIT 1";
                    cmd.Parameters.AddWithValue("@kid", period.kingdom_id);
                    using SQLiteDataReader r = cmd.ExecuteReader();
                    if (!r.Read()) continue;
                    if (string.IsNullOrEmpty(period.kingdom_color)) period.kingdom_color = SafeStr(r, 0);
                    period.kingdom_color_id = ToInt(r, 1, -1);
                    period.banner_icon_id = ToInt(r, 2, -1);
                    period.banner_background_id = ToInt(r, 3, -1);
                    period.banner_id = SafeStr(r, 4);
                }
                catch { }
            }
        }

        private static Dictionary<long, List<MandateHistoryEvent>> ReadEventsByPeriod()
        {
            var result = new Dictionary<long, List<MandateHistoryEvent>>();
            if (!Ready) return result;

            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    "SELECT EVENT_ID,PERIOD_ID,EVENT_TYPE,KINGDOM_ID,KINGDOM_NAME,KINGDOM_COLOR,ACTOR_ID,ACTOR_NAME," +
                    "CITY_ID,CITY_NAME,WORLD_TIME,YEAR_PREFIX,VALUE_DELTA,MANDATE_VALUE,IMPERIAL_AUTHORITY,CONTENT " +
                    "FROM " + MandateEventTableItem.GetTableName() + " ORDER BY WORLD_TIME ASC, EVENT_ID ASC";
                using SQLiteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var e = new MandateHistoryEvent
                    {
                        event_id = ToLong(r, 0, -1),
                        period_id = ToLong(r, 1, -1),
                        event_type = SafeStr(r, 2),
                        kingdom_id = ToLong(r, 3, -1),
                        kingdom_name = SafeStr(r, 4),
                        kingdom_color = SafeStr(r, 5),
                        actor_id = ToLong(r, 6, -1),
                        actor_name = SafeStr(r, 7),
                        city_id = ToLong(r, 8, -1),
                        city_name = SafeStr(r, 9),
                        world_time = ToDouble(r, 10, -1),
                        year_prefix = SafeStr(r, 11),
                        value_delta = ToInt(r, 12),
                        mandate_value = ToInt(r, 13),
                        imperial_authority = ToInt(r, 14),
                        content = SafeStr(r, 15)
                    };
                    if (!result.TryGetValue(e.period_id, out List<MandateHistoryEvent> list))
                    {
                        list = new List<MandateHistoryEvent>();
                        result[e.period_id] = list;
                    }
                    list.Add(e);
                }
            }
            catch { }

            return result;
        }

        private static List<MandateReignView> ReadReignsForPeriod(MandatePeriodView pPeriod)
        {
            var result = new List<MandateReignView>();
            if (!Ready || pPeriod == null || pPeriod.kingdom_id < 0) return result;

            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    "SELECT reign.KING_ACTOR_ID,reign.KING_NAME,reign.KING_COLOR," +
                    "reign.START_TIME,reign.END_TIME,reign.YEAR_NAME_STEM," +
                    "reign.YEAR_NAME_COLOR,IFNULL(title.FULL_TITLE,'')," +
                    "IFNULL(title.FULL_TITLE_COLOR,'') FROM " +
                    KingdomReignTableItem.GetTableName() + " reign LEFT JOIN " +
                    PosthumousTitleTableItem.GetTableName() + " title ON " +
                    "title.REIGN_ID=reign.REIGN_ID AND title.IS_RETROSPECTIVE=0 " +
                    "WHERE reign.KINGDOM_ID=@kid ORDER BY reign.START_TIME ASC";
                cmd.Parameters.AddWithValue("@kid", pPeriod.kingdom_id);
                using SQLiteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    double start = ToDouble(r, 3, -1);
                    double end = ToDouble(r, 4, -1);
                    if (!Overlaps(start, end, pPeriod.start_time, pPeriod.end_time)) continue;

                    string stem = SafeStr(r, 5);
                    result.Add(new MandateReignView
                    {
                        has_king = true,
                        king_actor_id = ToLong(r, 0, -1),
                        king_name = SafeStr(r, 1),
                        king_color = SafeStr(r, 2),
                        start_time = Math.Max(start, pPeriod.start_time),
                        end_time = ClampEnd(end, pPeriod.end_time),
                        year_prefix_snapshot = string.IsNullOrEmpty(stem) ? "" : stem + "\u5143\u5E74",
                        posthumous_title = SafeStr(r, 7),
                        posthumous_color = SafeStr(r, 8)
                    });
                }
            }
            catch { }

            return result;
        }

        private static MandateReignView BuildFallbackReign(MandatePeriodView pPeriod)
        {
            return new MandateReignView
            {
                has_king = pPeriod != null && pPeriod.founder_actor_id >= 0,
                king_actor_id = pPeriod?.founder_actor_id ?? -1,
                king_name = pPeriod?.founder_name ?? "",
                king_color = pPeriod?.kingdom_color ?? "",
                start_time = pPeriod?.start_time ?? -1,
                end_time = pPeriod?.end_time ?? -1,
                year_prefix_snapshot = ""
            };
        }

        private static void AssignEvents(MandatePeriodView pPeriod, List<MandateHistoryEvent> pEvents)
        {
            if (pPeriod == null || pEvents == null) return;
            foreach (MandateHistoryEvent e in pEvents)
            {
                MandateReignView target = FindActorTargetReign(pPeriod, e) ?? FindTimeTargetReign(pPeriod, e);
                if (target == null)
                {
                    target = pPeriod.reigns.Count > 0 ? pPeriod.reigns[0] : BuildFallbackReign(pPeriod);
                    if (pPeriod.reigns.Count == 0) pPeriod.reigns.Add(target);
                }
                target.events.Add(e);
            }
        }

        private static MandateReignView FindActorTargetReign(MandatePeriodView pPeriod, MandateHistoryEvent pEvent)
        {
            if (pPeriod == null || pEvent == null) return null;
            if (!MandateHistoryEventAssignmentRules.ShouldPreferActorReign(pEvent.event_type, pEvent.actor_id))
                return null;

            foreach (MandateReignView reign in pPeriod.reigns)
            {
                if (reign == null || !reign.has_king) continue;
                if (reign.king_actor_id == pEvent.actor_id) return reign;
            }
            return null;
        }

        private static MandateReignView FindTimeTargetReign(MandatePeriodView pPeriod, MandateHistoryEvent pEvent)
        {
            if (pPeriod == null || pEvent == null) return null;
            foreach (MandateReignView reign in pPeriod.reigns)
            {
                if (!Contains(reign.start_time, reign.end_time, pEvent.world_time)) continue;
                return reign;
            }
            return null;
        }

        private static void RemoveEmptyFallbackReigns(MandatePeriodView pPeriod)
        {
            if (pPeriod == null || pPeriod.reigns.Count <= 1) return;
            pPeriod.reigns.RemoveAll(r => r != null && !r.has_king && r.events.Count == 0);
        }

        private static bool Overlaps(double pStart, double pEnd, double pPeriodStart, double pPeriodEnd)
        {
            if (pStart < 0 || pPeriodStart < 0) return false;
            double end = pEnd < 0 ? double.MaxValue : pEnd;
            double periodEnd = pPeriodEnd < 0 ? double.MaxValue : pPeriodEnd;
            return pStart < periodEnd && end >= pPeriodStart;
        }

        private static bool Contains(double pStart, double pEnd, double pTime)
        {
            if (pTime < 0) return false;
            bool afterStart = pStart < 0 || pTime >= pStart;
            bool beforeEnd = pEnd < 0 || pTime < pEnd;
            return afterStart && beforeEnd;
        }

        private static double ClampEnd(double pEnd, double pPeriodEnd)
        {
            if (pPeriodEnd < 0) return pEnd;
            if (pEnd < 0) return pPeriodEnd;
            return Math.Min(pEnd, pPeriodEnd);
        }

        private static string SafeStr(SQLiteDataReader pReader, int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal) ? "" : Convert.ToString(pReader.GetValue(pOrdinal));
        }

        private static int ToInt(SQLiteDataReader pReader, int pOrdinal, int pDefault = 0)
        {
            return pReader.IsDBNull(pOrdinal) ? pDefault : Convert.ToInt32(pReader.GetValue(pOrdinal));
        }

        private static long ToLong(SQLiteDataReader pReader, int pOrdinal, long pDefault = 0)
        {
            return pReader.IsDBNull(pOrdinal) ? pDefault : Convert.ToInt64(pReader.GetValue(pOrdinal));
        }

        private static double ToDouble(SQLiteDataReader pReader, int pOrdinal, double pDefault = 0)
        {
            return pReader.IsDBNull(pOrdinal) ? pDefault : Convert.ToDouble(pReader.GetValue(pOrdinal));
        }
    }
}
