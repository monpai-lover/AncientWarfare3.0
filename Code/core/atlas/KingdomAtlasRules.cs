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

        internal static long ResolveDisplayOwner(long pOwnerId,
            IReadOnlyList<KingdomAtlasVassalRelationSnapshot> pRelations,
            double pWorldTime)
        {
            if (pOwnerId < 0L || pRelations == null || pRelations.Count == 0)
                return pOwnerId;
            long original = pOwnerId;
            long current = pOwnerId;
            var visited = new HashSet<long>();
            for (int depth = 0; depth < 32; depth++)
            {
                if (!visited.Add(current)) return original;
                KingdomAtlasVassalRelationSnapshot relation = FindRelation(
                    current, pRelations, pWorldTime);
                if (relation == null || relation.SuzerainId < 0L ||
                    relation.SuzerainId == current) return current;
                current = relation.SuzerainId;
            }
            return original;
        }

        internal static Dictionary<long, KingdomAtlasColor> BuildDisplayColors(
            IReadOnlyDictionary<long, string> pHistoricalColors,
            IReadOnlyList<KingdomAtlasVassalRelationSnapshot> pRelations,
            double pWorldTime)
        {
            var source = new Dictionary<long, KingdomAtlasColor>();
            if (pHistoricalColors != null)
                foreach (KeyValuePair<long, string> pair in pHistoricalColors)
                    if (TryParseColor(pair.Value, out KingdomAtlasColor color))
                        source[pair.Key] = color;
            if (pRelations != null)
                for (int index = 0; index < pRelations.Count; index++)
                {
                    KingdomAtlasVassalRelationSnapshot relation = pRelations[index];
                    if (relation == null || relation.StartTime > pWorldTime) continue;
                    if (!source.ContainsKey(relation.VassalId) &&
                        TryParseColor(relation.VassalColor, out KingdomAtlasColor vassalColor))
                        source[relation.VassalId] = vassalColor;
                    if (!source.ContainsKey(relation.SuzerainId) &&
                        TryParseColor(relation.SuzerainColor, out KingdomAtlasColor suzerainColor))
                        source[relation.SuzerainId] = suzerainColor;
                }
            var result = new Dictionary<long, KingdomAtlasColor>();
            var owners = new HashSet<long>(source.Keys);
            if (pRelations != null)
                for (int index = 0; index < pRelations.Count; index++)
                {
                    KingdomAtlasVassalRelationSnapshot relation = pRelations[index];
                    if (relation == null || relation.StartTime > pWorldTime) continue;
                    owners.Add(relation.VassalId);
                    owners.Add(relation.SuzerainId);
                }
            foreach (long owner in owners)
            {
                long displayOwner = ResolveDisplayOwner(owner, pRelations, pWorldTime);
                if (source.TryGetValue(displayOwner, out KingdomAtlasColor displayColor))
                    result[owner] = displayColor;
                else if (source.TryGetValue(owner, out KingdomAtlasColor ownColor))
                    result[owner] = ownColor;
            }
            return result;
        }

        internal static HashSet<long> BuildVisibleOwnerIds(
            IEnumerable<long> pParticipants,
            IReadOnlyList<KingdomAtlasVassalRelationSnapshot> pRelations,
            double pWorldTime)
        {
            var result = new HashSet<long>();
            if (pParticipants != null)
                foreach (long participant in pParticipants)
                    if (participant >= 0L) result.Add(participant);
            bool changed;
            do
            {
                changed = false;
                if (pRelations == null) break;
                for (int index = 0; index < pRelations.Count; index++)
                {
                    KingdomAtlasVassalRelationSnapshot relation = pRelations[index];
                    if (relation == null || !IsRelationAt(relation, pWorldTime) ||
                        !result.Contains(relation.SuzerainId) || relation.VassalId < 0L)
                        continue;
                    if (result.Add(relation.VassalId)) changed = true;
                }
            } while (changed);
            return result;
        }

        internal static bool IsRelationAt(KingdomAtlasVassalRelationSnapshot pRelation,
            double pWorldTime)
        {
            if (pRelation == null || pRelation.VassalId < 0L ||
                pRelation.SuzerainId < 0L || pRelation.VassalId == pRelation.SuzerainId)
                return false;
            const double epsilon = 0.000000001d;
            return pRelation.StartTime <= pWorldTime + epsilon &&
                (pRelation.EndTime < 0d || pRelation.EndTime + epsilon >= pWorldTime);
        }

        private static KingdomAtlasVassalRelationSnapshot FindRelation(long pVassalId,
            IReadOnlyList<KingdomAtlasVassalRelationSnapshot> pRelations,
            double pWorldTime)
        {
            KingdomAtlasVassalRelationSnapshot selected = null;
            for (int index = 0; index < pRelations.Count; index++)
            {
                KingdomAtlasVassalRelationSnapshot relation = pRelations[index];
                if (relation == null || relation.VassalId != pVassalId ||
                    !IsRelationAt(relation, pWorldTime)) continue;
                if (selected == null || ShouldReplaceSnapshot(relation.StartTime,
                        relation.RelationId, selected.StartTime, selected.RelationId))
                    selected = relation;
            }
            return selected;
        }

        public static bool IsCompleteTransfer(long pOldKingdomId,
            long pNewKingdomId)
        {
            return pOldKingdomId >= 0L && pNewKingdomId >= 0L &&
                pOldKingdomId != pNewKingdomId;
        }

        public static int Percent(int pCompleted, int pTotal)
        {
            if (pTotal <= 0) return 0;
            return Math.Max(0, Math.Min(100, (int)Math.Round(Math.Max(0, pCompleted) * 100d / pTotal)));
        }

        public static bool ShouldReplaceSnapshot(double pCandidateTime,
            long pCandidateId, double pCurrentTime, long pCurrentId)
        {
            const double epsilon = 0.000000001d;
            if (pCandidateTime > pCurrentTime + epsilon) return true;
            return Math.Abs(pCandidateTime - pCurrentTime) <= epsilon &&
                pCandidateId > pCurrentId;
        }

        public static bool IsSameSnapshotGroup(long pCandidateCityId,
            string pCandidateEventType, double pCandidateTime,
            long pCurrentCityId, string pCurrentEventType,
            double pCurrentTime)
        {
            const double epsilon = 0.000000001d;
            return pCandidateCityId == pCurrentCityId &&
                string.Equals(pCandidateEventType ?? "",
                    pCurrentEventType ?? "", StringComparison.Ordinal) &&
                Math.Abs(pCandidateTime - pCurrentTime) <= epsilon;
        }

        public static bool IsEventAtOrBeforeNode(double pEventTime,
            long pEventId, double pNodeTime, long pNodeId)
        {
            const double epsilon = 0.000000001d;
            if (pEventTime < pNodeTime - epsilon) return true;
            return Math.Abs(pEventTime - pNodeTime) <= epsilon &&
                pEventId <= pNodeId;
        }

        public static string BuildSnapshotTileKey(long pCityId,
            string pEventType, double pWorldTime, int pZoneId, int pX,
            int pY)
        {
            return pCityId.ToString(CultureInfo.InvariantCulture) + ":" +
                (pEventType ?? "") + ":" +
                pWorldTime.ToString("R", CultureInfo.InvariantCulture) + ":" +
                pZoneId.ToString(CultureInfo.InvariantCulture) + ":" +
                pX.ToString(CultureInfo.InvariantCulture) + ":" +
                pY.ToString(CultureInfo.InvariantCulture);
        }

        public static bool IsEventInYear(int pEventYear, int pNodeYear)
        {
            return pNodeYear <= 0 || pEventYear == pNodeYear;
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
