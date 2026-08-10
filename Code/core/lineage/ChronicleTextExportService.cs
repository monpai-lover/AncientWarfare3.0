using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ChronicleTextExportService
    {
        public static ChronicleTextExportSnapshot ReadSnapshot(
            ChronicleTextExportSource pSource, long pContextId)
        {
            if (pSource == ChronicleTextExportSource.Kingdom)
                return ChronicleTextExportSnapshot.ForKingdom(
                    MapDynasties(HistoryQuery.GetKingdomDynasties(pContextId)));
            if (pSource == ChronicleTextExportSource.City)
                return ChronicleTextExportSnapshot.ForCity(
                    MapPeriods(HistoryQuery.GetCityPeriods(pContextId)));
            return ChronicleTextExportSnapshot.ForPerson(
                MapEvents(HistoryQuery.ReadPerson(pContextId)));
        }

        private static List<ChronicleTextExportDynasty> MapDynasties(
            IList<DynastyView> pDynasties)
        {
            var result = new List<ChronicleTextExportDynasty>();
            if (pDynasties == null) return result;
            foreach (DynastyView dynasty in pDynasties)
            {
                if (dynasty == null) continue;
                List<ChronicleTextExportPeriod> reigns = MapPeriods(
                    dynasty.reigns);
                string start = FirstDate(reigns, pUseEnd: false);
                string end = LastDate(reigns, pUseEnd: true);
                result.Add(new ChronicleTextExportDynasty(
                    DynastyTitle(dynasty), start, end, reigns));
            }
            return result;
        }

        private static List<ChronicleTextExportPeriod> MapPeriods(
            IList<ReignPeriod> pPeriods)
        {
            var result = new List<ChronicleTextExportPeriod>();
            if (pPeriods == null) return result;
            for (int index = 0; index < pPeriods.Count; index++)
            {
                ReignPeriod period = pPeriods[index];
                if (period == null) continue;
                ReignPeriod next = index + 1 < pPeriods.Count
                    ? pPeriods[index + 1]
                    : null;
                List<ChronicleTextExportEvent> events = MapEvents(
                    period.events);
                string start = PeriodStartDate(period, events);
                string end = PeriodEndDate(period, next, events);
                result.Add(new ChronicleTextExportPeriod(PeriodTitle(period),
                    start, end, events));
            }
            return result;
        }

        private static List<ChronicleTextExportEvent> MapEvents(
            IList<HistoryEntry> pEntries)
        {
            var result = new List<ChronicleTextExportEvent>();
            if (pEntries == null) return result;
            foreach (HistoryEntry entry in pEntries)
            {
                if (entry == null) continue;
                result.Add(new ChronicleTextExportEvent(entry.year_prefix,
                    entry.content));
            }
            return result;
        }

        private static string DynastyTitle(DynastyView pDynasty)
        {
            if (pDynasty.is_interregnum_group)
                return "无王时期";
            if (!string.IsNullOrWhiteSpace(pDynasty.dynasty_name))
                return pDynasty.dynasty_name;
            if (!string.IsNullOrWhiteSpace(pDynasty.clan_name))
                return pDynasty.clan_name + "氏统治";
            return "早期";
        }

        private static string PeriodTitle(ReignPeriod pPeriod)
        {
            if (pPeriod.is_city_period)
            {
                if (!string.IsNullOrWhiteSpace(pPeriod.owner_name))
                    return pPeriod.owner_name;
                return "无所属";
            }
            if (!string.IsNullOrWhiteSpace(pPeriod.posthumous_title))
                return pPeriod.posthumous_title;
            if (!string.IsNullOrWhiteSpace(pPeriod.king_name))
                return pPeriod.king_name;
            return "无王时期";
        }

        private static string PeriodStartDate(ReignPeriod pPeriod,
            IList<ChronicleTextExportEvent> pEvents)
        {
            if (!string.IsNullOrWhiteSpace(pPeriod.year_prefix_snapshot))
                return pPeriod.year_prefix_snapshot;
            return FirstEventDate(pEvents);
        }

        private static string PeriodEndDate(ReignPeriod pPeriod,
            ReignPeriod pNext, IList<ChronicleTextExportEvent> pEvents)
        {
            if (pNext != null &&
                !string.IsNullOrWhiteSpace(pNext.year_prefix_snapshot))
                return pNext.year_prefix_snapshot;
            return LastEventDate(pEvents);
        }

        private static string FirstDate(
            IList<ChronicleTextExportPeriod> pPeriods, bool pUseEnd)
        {
            if (pPeriods == null) return string.Empty;
            foreach (ChronicleTextExportPeriod period in pPeriods)
            {
                if (period == null) continue;
                string date = pUseEnd ? period.EndDate : period.StartDate;
                if (!string.IsNullOrWhiteSpace(date)) return date;
            }
            return string.Empty;
        }

        private static string LastDate(
            IList<ChronicleTextExportPeriod> pPeriods, bool pUseEnd)
        {
            if (pPeriods == null) return string.Empty;
            for (int index = pPeriods.Count - 1; index >= 0; index--)
            {
                ChronicleTextExportPeriod period = pPeriods[index];
                if (period == null) continue;
                string date = pUseEnd ? period.EndDate : period.StartDate;
                if (!string.IsNullOrWhiteSpace(date)) return date;
            }
            return string.Empty;
        }

        private static string FirstEventDate(
            IList<ChronicleTextExportEvent> pEvents)
        {
            if (pEvents == null) return string.Empty;
            foreach (ChronicleTextExportEvent historyEvent in pEvents)
                if (historyEvent != null &&
                    !string.IsNullOrWhiteSpace(historyEvent.ChronicleDate))
                    return historyEvent.ChronicleDate;
            return string.Empty;
        }

        private static string LastEventDate(
            IList<ChronicleTextExportEvent> pEvents)
        {
            if (pEvents == null) return string.Empty;
            for (int index = pEvents.Count - 1; index >= 0; index--)
            {
                ChronicleTextExportEvent historyEvent = pEvents[index];
                if (historyEvent != null &&
                    !string.IsNullOrWhiteSpace(historyEvent.ChronicleDate))
                    return historyEvent.ChronicleDate;
            }
            return string.Empty;
        }
    }
}
