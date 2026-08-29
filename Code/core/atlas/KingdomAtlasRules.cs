using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AncientWarfare3.core.lineage;

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

        internal static List<KingdomAtlasNodeDescriptor> BuildNodeDescriptors(
            IEnumerable<KingdomAtlasHistoryEvent> pEvents,
            IEnumerable<KingdomAtlasVassalRelationSnapshot> pRelations,
            long pKingdomId)
        {
            HashSet<long> scope = BuildAtlasScopeKingdomIds(pRelations,
                pKingdomId);
            var descriptors = new List<KingdomAtlasNodeDescriptor>();
            foreach (KingdomAtlasHistoryEvent row in OrderAndDeduplicate(pEvents))
            {
                if (!scope.Contains(row.OldKingdomId) &&
                    !scope.Contains(row.NewKingdomId)) continue;
                descriptors.Add(new KingdomAtlasNodeDescriptor
                {
                    NodeKind = KingdomAtlasNodeKind.City,
                    SourceId = row.EventId,
                    StableKey = BuildNodeStableKey(
                        KingdomAtlasNodeKind.City, row.EventId),
                    WorldTime = row.WorldTime,
                    CityReplayEventId = row.EventId,
                    CityEvent = row
                });
            }

            var relationKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (KingdomAtlasVassalRelationSnapshot relation in
                     pRelations ?? Enumerable.Empty<KingdomAtlasVassalRelationSnapshot>())
            {
                if (!IsValidRelationRecord(relation) ||
                    (relation.VassalId != pKingdomId &&
                     relation.SuzerainId != pKingdomId &&
                     !scope.Contains(relation.SuzerainId))) continue;
                AddRelationDescriptor(descriptors, relationKeys, relation,
                    KingdomAtlasNodeKind.VassalStart, relation.StartTime);
                if (relation.EndTime >= relation.StartTime)
                    AddRelationDescriptor(descriptors, relationKeys, relation,
                        KingdomAtlasNodeKind.VassalEnd, relation.EndTime);
            }

            return descriptors.OrderBy(pDescriptor => pDescriptor.WorldTime)
                .ThenBy(pDescriptor => (int)pDescriptor.NodeKind)
                .ThenBy(pDescriptor => pDescriptor.SourceId).ToList();
        }

        private static HashSet<long> BuildAtlasScopeKingdomIds(
            IEnumerable<KingdomAtlasVassalRelationSnapshot> pRelations,
            long pKingdomId)
        {
            var result = new HashSet<long>();
            if (pKingdomId < 0L) return result;
            result.Add(pKingdomId);
            bool changed;
            do
            {
                changed = false;
                foreach (KingdomAtlasVassalRelationSnapshot relation in
                         pRelations ?? Enumerable.Empty<KingdomAtlasVassalRelationSnapshot>())
                {
                    if (!IsValidRelationRecord(relation) ||
                        !result.Contains(relation.SuzerainId)) continue;
                    if (result.Add(relation.VassalId)) changed = true;
                }
            } while (changed);
            return result;
        }

        internal static IReadOnlyList<KingdomAtlasVassalRelationSnapshot>
            BuildRelationSnapshotAt(
                IEnumerable<KingdomAtlasVassalRelationSnapshot> pRelations,
                KingdomAtlasNodeDescriptor pDescriptor)
        {
            if (pDescriptor == null)
                return Array.Empty<KingdomAtlasVassalRelationSnapshot>();
            var result = (pRelations ??
                    Enumerable.Empty<KingdomAtlasVassalRelationSnapshot>())
                .Where(pRelation => IsRelationAt(pRelation,
                    pDescriptor.WorldTime))
                .OrderBy(pRelation => pRelation.RelationId).ToList();
            if (pDescriptor.NodeKind == KingdomAtlasNodeKind.VassalStart &&
                IsValidRelationRecord(pDescriptor.Relation) &&
                result.All(pRelation => pRelation.RelationId !=
                    pDescriptor.Relation.RelationId))
                result.Add(pDescriptor.Relation);
            if (pDescriptor.NodeKind == KingdomAtlasNodeKind.VassalEnd)
                result.RemoveAll(pRelation => pRelation.RelationId ==
                    pDescriptor.SourceId);
            return result.OrderBy(pRelation => pRelation.RelationId).ToList();
        }

        internal static KingdomAtlasHistoryEvent BuildNodeEvent(
            KingdomAtlasNodeDescriptor pDescriptor, int pYear,
            string pYearText)
        {
            if (pDescriptor?.NodeKind == KingdomAtlasNodeKind.City)
                return pDescriptor.CityEvent;
            KingdomAtlasVassalRelationSnapshot relation =
                pDescriptor?.Relation;
            if (relation == null) return null;
            return new KingdomAtlasHistoryEvent
            {
                EventId = -1L,
                WorldTime = pDescriptor.WorldTime,
                Year = pYear,
                YearText = pYearText ?? "",
                CityId = -1L,
                EventType = pDescriptor.NodeKind ==
                    KingdomAtlasNodeKind.VassalEnd
                        ? "vassal_end"
                        : "vassal_start",
                OldKingdomId = relation.SuzerainId,
                OldKingdomName = relation.SuzerainName ?? "",
                OldKingdomColor = relation.SuzerainColor ?? "",
                NewKingdomId = relation.VassalId,
                NewKingdomName = relation.VassalName ?? "",
                NewKingdomColor = relation.VassalColor ?? ""
            };
        }

        internal static Dictionary<long, KingdomAtlasKingdomSnapshot>
            BuildKingdomSnapshots(KingdomAtlasHistoryEvent pEvent,
                IEnumerable<KingdomAtlasVassalRelationSnapshot> pRelations)
        {
            var result = new Dictionary<long, KingdomAtlasKingdomSnapshot>();
            SetKingdomSnapshot(result, pEvent?.OldKingdomId ?? -1L,
                pEvent?.OldKingdomName, pEvent?.OldKingdomColor, false);
            SetKingdomSnapshot(result, pEvent?.NewKingdomId ?? -1L,
                pEvent?.NewKingdomName, pEvent?.NewKingdomColor, false);
            foreach (KingdomAtlasVassalRelationSnapshot relation in
                     (pRelations ?? Enumerable.Empty<KingdomAtlasVassalRelationSnapshot>())
                     .Where(pRelation => pRelation != null)
                     .OrderBy(pRelation => pRelation.StartTime)
                     .ThenBy(pRelation => pRelation.RelationId))
            {
                SetKingdomSnapshot(result, relation.VassalId,
                    relation.VassalName, relation.VassalColor, true);
                SetKingdomSnapshot(result, relation.SuzerainId,
                    relation.SuzerainName, relation.SuzerainColor, true);
            }
            return result;
        }

        private static void SetKingdomSnapshot(
            IDictionary<long, KingdomAtlasKingdomSnapshot> pResult,
            long pKingdomId, string pName, string pColor, bool pOverwrite)
        {
            if (pKingdomId < 0L) return;
            if (!pResult.TryGetValue(pKingdomId,
                    out KingdomAtlasKingdomSnapshot snapshot))
            {
                snapshot = new KingdomAtlasKingdomSnapshot
                {
                    KingdomId = pKingdomId
                };
                pResult[pKingdomId] = snapshot;
            }
            if (!string.IsNullOrWhiteSpace(pName) &&
                (pOverwrite || string.IsNullOrWhiteSpace(snapshot.Name)))
                snapshot.Name = pName;
            if (!string.IsNullOrWhiteSpace(pColor) &&
                (pOverwrite || string.IsNullOrWhiteSpace(snapshot.Color)))
                snapshot.Color = pColor;
        }

        internal static string BuildNodeStableKey(KingdomAtlasNodeKind pKind,
            long pSourceId)
        {
            string prefix;
            switch (pKind)
            {
                case KingdomAtlasNodeKind.VassalStart:
                    prefix = "vassal_start";
                    break;
                case KingdomAtlasNodeKind.VassalEnd:
                    prefix = "vassal_end";
                    break;
                default:
                    prefix = "city";
                    break;
            }
            return prefix + ":" +
                pSourceId.ToString(CultureInfo.InvariantCulture);
        }

        private static void AddRelationDescriptor(
            ICollection<KingdomAtlasNodeDescriptor> pDescriptors,
            ISet<string> pKeys, KingdomAtlasVassalRelationSnapshot pRelation,
            KingdomAtlasNodeKind pKind, double pWorldTime)
        {
            string stableKey = BuildNodeStableKey(pKind,
                pRelation.RelationId);
            if (!pKeys.Add(stableKey)) return;
            pDescriptors.Add(new KingdomAtlasNodeDescriptor
            {
                NodeKind = pKind,
                SourceId = pRelation.RelationId,
                StableKey = stableKey,
                WorldTime = pWorldTime,
                CityReplayEventId = long.MaxValue,
                Relation = pRelation
            });
        }

        private static bool IsValidRelationRecord(
            KingdomAtlasVassalRelationSnapshot pRelation)
        {
            return pRelation != null && pRelation.RelationId >= 0L &&
                pRelation.StartTime >= 0d && pRelation.VassalId >= 0L &&
                pRelation.SuzerainId >= 0L &&
                pRelation.VassalId != pRelation.SuzerainId;
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

        internal static bool IsDuplicateTransferWithoutEvidence(
            long pPreviousOwnerId, long pContextOwnerId, bool pHasLoss,
            bool pHasGain)
        {
            return pPreviousOwnerId >= 0L &&
                   pContextOwnerId == pPreviousOwnerId &&
                   !pHasLoss && !pHasGain;
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

        public static Dictionary<long, long> ReplayCityOwnersAt(
            IEnumerable<KingdomAtlasHistoryEvent> pEvents,
            double pNodeTime, long pNodeEventId)
        {
            var result = new Dictionary<long, long>();
            if (pEvents == null) return result;
            IEnumerable<KingdomAtlasHistoryEvent> ordered = pEvents
                .Where(pEvent => pEvent != null && pEvent.CityId >= 0L &&
                    IsTerritorialEvent(pEvent.EventType))
                .OrderBy(pEvent => pEvent.WorldTime)
                .ThenBy(pEvent => pEvent.EventId);
            foreach (KingdomAtlasHistoryEvent row in ordered)
            {
                if (!IsEventAtOrBeforeNode(row.WorldTime, row.EventId,
                        pNodeTime, pNodeEventId)) continue;
                if (row.NewKingdomId >= 0L)
                {
                    result[row.CityId] = row.NewKingdomId;
                    continue;
                }
                if (row.EventType == "city_lost" ||
                    row.EventType == "city_transfer")
                    result[row.CityId] = -1L;
            }
            return result;
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
                TryResolveHierarchicalDisplayColor(owner, source, pRelations,
                    pWorldTime, result, new HashSet<long>(), out _);
            return result;
        }

        private static bool TryResolveHierarchicalDisplayColor(long pOwnerId,
            IReadOnlyDictionary<long, KingdomAtlasColor> pSource,
            IReadOnlyList<KingdomAtlasVassalRelationSnapshot> pRelations,
            double pWorldTime,
            IDictionary<long, KingdomAtlasColor> pResolved,
            ISet<long> pVisiting, out KingdomAtlasColor pColor)
        {
            if (pResolved.TryGetValue(pOwnerId, out pColor)) return true;
            if (!pVisiting.Add(pOwnerId))
                return pSource.TryGetValue(pOwnerId, out pColor);

            bool hasOwnColor = pSource.TryGetValue(pOwnerId,
                out KingdomAtlasColor ownColor);
            KingdomAtlasVassalRelationSnapshot relation = FindRelation(
                pOwnerId, pRelations, pWorldTime);
            KingdomAtlasColor suzerainColor = default;
            bool hasSuzerainColor = relation != null &&
                relation.SuzerainId >= 0L &&
                relation.SuzerainId != pOwnerId &&
                TryResolveHierarchicalDisplayColor(relation.SuzerainId,
                    pSource, pRelations, pWorldTime, pResolved, pVisiting,
                    out suzerainColor);

            bool resolved = false;
            if (hasOwnColor && hasSuzerainColor)
            {
                pColor = relation.ContractTier ==
                    VassalContractTierRules.Tributary
                        ? BlendTributaryDisplayColor(suzerainColor, ownColor)
                        : BlendSubjectDisplayColor(suzerainColor, ownColor);
                resolved = true;
            }
            else if (hasOwnColor)
            {
                pColor = ownColor;
                resolved = true;
            }
            else if (hasSuzerainColor)
            {
                pColor = suzerainColor;
                resolved = true;
            }

            pVisiting.Remove(pOwnerId);
            if (resolved) pResolved[pOwnerId] = pColor;
            return resolved;
        }

        private static KingdomAtlasColor BlendSubjectDisplayColor(
            KingdomAtlasColor pSuzerain, KingdomAtlasColor pSubject)
        {
            return new KingdomAtlasColor(
                BlendSubjectColorChannel(pSuzerain.Red, pSubject.Red),
                BlendSubjectColorChannel(pSuzerain.Green, pSubject.Green),
                BlendSubjectColorChannel(pSuzerain.Blue, pSubject.Blue),
                pSuzerain.Alpha);
        }

        private static byte BlendSubjectColorChannel(byte pSuzerain,
            byte pSubject)
        {
            return (byte)((pSuzerain * 4 + pSubject + 2) / 5);
        }

        private static KingdomAtlasColor BlendTributaryDisplayColor(
            KingdomAtlasColor pSuzerain, KingdomAtlasColor pSubject)
        {
            return new KingdomAtlasColor(
                BlendTributaryColorChannel(pSuzerain.Red, pSubject.Red),
                BlendTributaryColorChannel(pSuzerain.Green, pSubject.Green),
                BlendTributaryColorChannel(pSuzerain.Blue, pSubject.Blue),
                pSubject.Alpha);
        }

        private static byte BlendTributaryColorChannel(byte pSuzerain,
            byte pSubject)
        {
            return (byte)((pSubject * 2 + pSuzerain * 3 + 2) / 5);
        }

        internal static HashSet<long> BuildVisibleOwnerIds(
            IEnumerable<long> pParticipants,
            IReadOnlyList<KingdomAtlasVassalRelationSnapshot> pRelations,
            double pWorldTime)
        {
            return BuildVisibleOwnerIds(pParticipants, pRelations,
                pWorldTime, -1L);
        }

        internal static HashSet<long> BuildVisibleOwnerIds(
            IEnumerable<long> pParticipants,
            IReadOnlyList<KingdomAtlasVassalRelationSnapshot> pRelations,
            double pWorldTime, long pScopeKingdomId)
        {
            var result = new HashSet<long>();
            if (pParticipants != null)
                foreach (long participant in pParticipants)
                    if (participant >= 0L) result.Add(participant);
            if (pScopeKingdomId >= 0L) result.Add(pScopeKingdomId);
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
                (pRelation.EndTime < 0d || pRelation.EndTime > pWorldTime + epsilon);
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

        public static string BuildPreviewCacheRelativePath(long pKingdomId,
            int pResolution, int pIndex, long pEventId, int pFontIndex = 0)
        {
            return Path.Combine("preview-cache", "font_" +
                pFontIndex.ToString(CultureInfo.InvariantCulture),
                BuildOutputStem(pKingdomId, pResolution, pIndex, pEventId) +
                ".png");
        }

        public static float CalculateLabelPixelSize(float pRenderedWorldSize,
            int pResolution, int pWorldWidth, int pWorldHeight)
        {
            int resolution = Math.Max(64, Math.Min(8192, pResolution));
            int worldSpan = Math.Max(1, Math.Max(pWorldWidth, pWorldHeight));
            float scaled = Math.Max(0f, pRenderedWorldSize) *
                resolution / worldSpan;
            const float minimum = 4f;
            float maximum = Math.Max(8f, resolution * 0.08f);
            return Math.Max(minimum, Math.Min(maximum, scaled));
        }

        public static float ScaleAtlasCountryLabelForTerritory(
            float pPixelSize, int pLandTileCount, int pWorldWidth,
            int pWorldHeight)
        {
            const float minimumPixelSize = 4f;
            const float fullScaleTerritoryRatio = 0.20f;
            const float minimumTerritoryScale = 0.25f;
            long worldArea = Math.Max(1L,
                (long)Math.Max(1, pWorldWidth) * Math.Max(1, pWorldHeight));
            float territoryRatio = Math.Max(0, pLandTileCount) /
                                   (float)worldArea;
            float normalized = Math.Max(0f, Math.Min(1f,
                territoryRatio / fullScaleTerritoryRatio));
            float territoryScale = Math.Max(minimumTerritoryScale,
                (float)Math.Sqrt(normalized));
            return Math.Max(minimumPixelSize,
                Math.Max(0f, pPixelSize) * territoryScale);
        }

        public static string SanitizeChronicleDisplayText(string pText)
        {
            if (string.IsNullOrEmpty(pText)) return pText ?? "";
            char[] characters = pText.ToCharArray();
            for (int index = 0; index < characters.Length; index++)
                if (char.IsPunctuation(characters[index]))
                    characters[index] = ' ';
            return new string(characters);
        }

        public static string ColorizeChronicleEntities(string pText,
            IEnumerable<string> pEntityNames, string pColor)
        {
            if (string.IsNullOrEmpty(pText) ||
                string.IsNullOrWhiteSpace(pColor)) return pText ?? "";
            var names = (pEntityNames ?? Enumerable.Empty<string>())
                .Where(pName => !string.IsNullOrWhiteSpace(pName))
                .Select(pName => pName.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(pName => pName.Length)
                .ToList();
            if (names.Count == 0) return pText;
            string color = pColor.Trim();
            if (!color.StartsWith("#", StringComparison.Ordinal)) color = "#" + color;
            var result = new StringBuilder(pText);
            for (int index = 0; index < names.Count; index++)
            {
                string name = names[index];
                string escaped = EscapeRichText(name);
                result.Replace(escaped, "<color=" + color + ">" + escaped + "</color>");
            }
            return result.ToString();
        }

        private static string EscapeRichText(string pText)
        {
            return (pText ?? "").Replace("&", "&amp;")
                .Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
