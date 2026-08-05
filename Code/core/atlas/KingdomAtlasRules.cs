using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AncientWarfare3.core.atlas
{
    internal static class KingdomAtlasRules
    {
        public static List<KingdomAtlasHistoryEvent> OrderAndDeduplicate(IEnumerable<KingdomAtlasHistoryEvent> pEvents)
        {
            var rows = (pEvents ?? Enumerable.Empty<KingdomAtlasHistoryEvent>())
                .Where(pEvent => pEvent != null && pEvent.EventId >= 0L)
                .OrderBy(pEvent => pEvent.WorldTime).ThenBy(pEvent => pEvent.EventId).ToList();
            var result = new List<KingdomAtlasHistoryEvent>(rows.Count);
            var ids = new HashSet<long>();
            foreach (KingdomAtlasHistoryEvent row in rows) if (ids.Add(row.EventId)) result.Add(row);
            return result;
        }

        public static List<KingdomAtlasHistoryEvent> SelectParticipants(
            IEnumerable<KingdomAtlasHistoryEvent> pEvents,
            long pOldKingdomId, long pNewKingdomId)
        {
            var ordered = OrderAndDeduplicate(pEvents);
            ordered.RemoveAll(pEvent => pEvent == null ||
                (pEvent.OldKingdomId != pOldKingdomId &&
                 pEvent.OldKingdomId != pNewKingdomId &&
                 pEvent.NewKingdomId != pOldKingdomId &&
                 pEvent.NewKingdomId != pNewKingdomId));
            return ordered;
        }

        public static bool IsTerritorialEvent(string pEventType)
        {
            return string.Equals(pEventType, "city_gained",
                       StringComparison.Ordinal) ||
                   string.Equals(pEventType, "city_lost",
                       StringComparison.Ordinal) ||
                   string.Equals(pEventType, "city_transfer",
                       StringComparison.Ordinal) ||
                   string.Equals(pEventType, "city_found",
                       StringComparison.Ordinal);
        }

        public static long ResolveOwnerAt(IReadOnlyList<KingdomAtlasHistoryEvent> pEvents,
            long pCityId, double pWorldTime)
        {
            if (pEvents == null) return -1L;
            long owner = -1L;
            for (int index = 0; index < pEvents.Count; index++)
            {
                KingdomAtlasHistoryEvent row = pEvents[index];
                if (row == null || row.CityId != pCityId ||
                    row.WorldTime > pWorldTime) continue;
                if (row.NewKingdomId >= 0L) owner = row.NewKingdomId;
                else if (row.OldKingdomId >= 0L && owner < 0L)
                    owner = row.OldKingdomId;
            }
            return owner;
        }

        public static bool IsReliableResolution(int pWidth, int pHeight)
        {
            return pWidth >= 64 && pHeight >= 64 &&
                   pWidth <= 8192 && pHeight <= 8192;
        }

        public static bool IsVisibleOwner(long pOwnerId, long pOldId, long pNewId) => pOwnerId >= 0L && (pOwnerId == pOldId || pOwnerId == pNewId);

        public static int Percent(int pCompleted, int pTotal)
        {
            if (pTotal <= 0) return 0;
            return Math.Max(0, Math.Min(100, (int)Math.Round(Math.Max(0, pCompleted) * 100d / pTotal)));
        }

        public static bool TryParseColor(string pValue, out KingdomAtlasColor pColor)
        {
            pColor = default;
            if (string.IsNullOrWhiteSpace(pValue)) return false;
            string value = pValue.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal)) value = value.Substring(1);
            if (value.Length != 6 && value.Length != 8) return false;
            if (!byte.TryParse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) ||
                !byte.TryParse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) ||
                !byte.TryParse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue)) return false;
            byte alpha = 255;
            if (value.Length == 8 && !byte.TryParse(value.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out alpha)) return false;
            pColor = new KingdomAtlasColor(red, green, blue, alpha);
            return true;
        }

        public static bool TryResolveHistoricalColor(params string[] pValues)
        {
            if (pValues == null) return false;
            foreach (string value in pValues) if (TryParseColor(value, out _)) return true;
            return false;
        }

        public static string BuildGenerationKey(long pEventId, int pResolution, string pGeometryVersion) =>
            pEventId.ToString(CultureInfo.InvariantCulture) + ":" + pResolution.ToString(CultureInfo.InvariantCulture) + ":" + (pGeometryVersion ?? "");

        public static string BuildOutputStem(long pKingdomId, int pResolution, int pIndex, long pEventId) =>
            "kingdom_" + pKingdomId.ToString(CultureInfo.InvariantCulture) + "_" + pResolution.ToString(CultureInfo.InvariantCulture) + "_node_" + pIndex.ToString("0000", CultureInfo.InvariantCulture) + "_event_" + pEventId.ToString(CultureInfo.InvariantCulture);
    }
}
