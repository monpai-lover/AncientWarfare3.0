using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.atlas
{
    /// <summary>
    /// Reconstructs atlas nodes from the persisted history tables.  This
    /// class intentionally has no dependency on the live world, units,
    /// map-mode runtime state, or live kingdom colours.
    /// </summary>
    internal static class KingdomAtlasHistoryService
    {
        private static readonly Regex YearDigits = new Regex("[0-9]+",
            RegexOptions.Compiled);

        internal static List<KingdomAtlasNode> BuildNodes(long pKingdomId)
        {
            var rows = ReadTerritorialEvents();
            rows = KingdomAtlasRules.OrderAndDeduplicate(rows);
            List<KingdomAtlasVassalRelationSnapshot> relations =
                ReadVassalRelations();
            List<KingdomAtlasNodeDescriptor> descriptors =
                KingdomAtlasRules.BuildNodeDescriptors(rows, relations,
                    pKingdomId);
            var nodes = new List<KingdomAtlasNode>();
            for (int index = 0; index < descriptors.Count; index++)
            {
                KingdomAtlasNodeDescriptor descriptor = descriptors[index];
                NodeYearContext nodeDisplayYear = descriptor.NodeKind ==
                    KingdomAtlasNodeKind.City
                        ? new NodeYearContext(descriptor.CityEvent?.Year ?? 0,
                            descriptor.CityEvent?.YearText ?? "")
                        : ResolveNodeYear(pKingdomId, descriptor.WorldTime);
                KingdomAtlasHistoryEvent row = KingdomAtlasRules.BuildNodeEvent(
                    descriptor, nodeDisplayYear.Year,
                    nodeDisplayYear.YearText);
                if (row == null) continue;
                if (!HasReliableParticipantColours(row, relations)) continue;
                NodeYearContext oldChronicleYear = ResolveNodeYear(
                    row.OldKingdomId, descriptor.WorldTime);
                NodeYearContext newChronicleYear = ResolveNodeYear(
                    row.NewKingdomId, descriptor.WorldTime);
                var all = rows.Where(pEvent => KingdomAtlasRules.
                    IsEventAtOrBeforeNode(pEvent.WorldTime, pEvent.EventId,
                        descriptor.WorldTime,
                        descriptor.CityReplayEventId)).ToList();
                IReadOnlyDictionary<long, long> owners =
                    KingdomAtlasRules.ReplayCityOwnersAt(rows,
                        descriptor.WorldTime,
                        descriptor.CityReplayEventId);
                IReadOnlyList<KingdomAtlasVassalRelationSnapshot>
                    relationSnapshot = KingdomAtlasRules.
                        BuildRelationSnapshotAt(relations, descriptor);
                List<KingdomAtlasZoneSnapshot> snapshots =
                    TryReadZoneArchive(descriptor.WorldTime);
                var visible = new List<KingdomAtlasZoneCell>();
                HashSet<long> visibleOwners = KingdomAtlasRules.BuildVisibleOwnerIds(
                    new[] { row.OldKingdomId, row.NewKingdomId },
                    relationSnapshot, descriptor.WorldTime);
                AddVisibleCells(snapshots, owners, visibleOwners, visible);
                Dictionary<long, KingdomAtlasKingdomSnapshot> kingdoms =
                    BuildKingdomSnapshots(row, relationSnapshot);
                var historicalColors = kingdoms.ToDictionary(
                    pPair => pPair.Key, pPair => pPair.Value.Color);
                var node = new KingdomAtlasNode
                {
                    NodeKind = descriptor.NodeKind,
                    SourceId = descriptor.SourceId,
                    StableKey = descriptor.StableKey,
                    Relation = descriptor.Relation,
                    Event = row,
                    Events = all,
                    CityOwners = owners,
                    VisibleZones = visible,
                    VassalRelations = relationSnapshot,
                    Kingdoms = kingdoms,
                    DisplayColors = KingdomAtlasRules.BuildDisplayColors(
                        historicalColors, relationSnapshot,
                        descriptor.WorldTime),
                    OldChronicle = ReadChronicle(row.OldKingdomId,
                        descriptor.WorldTime, descriptor.CityReplayEventId,
                        oldChronicleYear.Year),
                    NewChronicle = ReadChronicle(row.NewKingdomId,
                        descriptor.WorldTime, descriptor.CityReplayEventId,
                        newChronicleYear.Year),
                    OldChronicleYearText = oldChronicleYear.YearText,
                    NewChronicleYearText = newChronicleYear.YearText
                };
                nodes.Add(node);
            }
            return nodes;
        }

        private static NodeYearContext ResolveNodeYear(long pKingdomId,
            double pWorldTime)
        {
            LineageArchiveManager manager = LineageArchiveManager.Instance;
            SQLiteConnection db = manager?.OperatingDB;
            if (db == null || pKingdomId < 0L) return default;
            try
            {
                using (var cmd = new SQLiteCommand(db))
                {
                    cmd.CommandText = "SELECT YEAR_PREFIX FROM KingdomHistory " +
                        "WHERE KINGDOM_ID=@id AND WORLD_TIME<=@time " +
                        "ORDER BY WORLD_TIME DESC,EVENT_ID DESC LIMIT 1";
                    cmd.Parameters.AddWithValue("@id", pKingdomId);
                    cmd.Parameters.AddWithValue("@time", pWorldTime);
                    object value = cmd.ExecuteScalar();
                    string yearText = value == null || value == DBNull.Value
                        ? ""
                        : Convert.ToString(value, CultureInfo.InvariantCulture);
                    return new NodeYearContext(ParseYear(yearText), yearText);
                }
            }
            catch
            {
                return default;
            }
        }

        private readonly struct NodeYearContext
        {
            internal NodeYearContext(int pYear, string pYearText)
            {
                Year = pYear;
                YearText = pYearText ?? "";
            }

            internal int Year { get; }
            internal string YearText { get; }
        }

        internal static bool HasReliableColours(IReadOnlyList<KingdomAtlasNode> pNodes)
        {
            if (pNodes == null) return false;
            if (pNodes.Count == 0) return true;
            for (int index = 0; index < pNodes.Count; index++)
            {
                KingdomAtlasHistoryEvent row = pNodes[index]?.Event;
                if (!HasReliableParticipantColours(row, pNodes[index]?.VassalRelations)) return false;
            }
            return true;
        }

        private static bool HasReliableParticipantColours(
            KingdomAtlasHistoryEvent pEvent,
            IReadOnlyList<KingdomAtlasVassalRelationSnapshot> pRelations = null)
        {
            if (pEvent == null) return false;
            bool oldOk = pEvent.OldKingdomId < 0L ||
                KingdomAtlasRules.TryResolveHistoricalColor(pEvent.OldKingdomColor) ||
                HasRelationColor(pEvent.OldKingdomId, pRelations);
            bool newOk = pEvent.NewKingdomId < 0L ||
                KingdomAtlasRules.TryResolveHistoricalColor(pEvent.NewKingdomColor) ||
                HasRelationColor(pEvent.NewKingdomId, pRelations);
            return oldOk && newOk;
        }

        private static bool HasRelationColor(long pKingdomId,
            IReadOnlyList<KingdomAtlasVassalRelationSnapshot> pRelations)
        {
            if (pKingdomId < 0L || pRelations == null) return false;
            for (int index = 0; index < pRelations.Count; index++)
            {
                KingdomAtlasVassalRelationSnapshot relation = pRelations[index];
                if (relation == null) continue;
                if (relation.VassalId == pKingdomId &&
                    KingdomAtlasRules.TryResolveHistoricalColor(relation.VassalColor)) return true;
                if (relation.SuzerainId == pKingdomId &&
                    KingdomAtlasRules.TryResolveHistoricalColor(relation.SuzerainColor)) return true;
            }
            return false;
        }

        private static List<KingdomAtlasHistoryEvent> ReadTerritorialEvents()
        {
            var result = new List<KingdomAtlasHistoryEvent>();
            LineageArchiveManager manager = LineageArchiveManager.Instance;
            SQLiteConnection db = manager?.OperatingDB;
            if (db == null) return result;

            Dictionary<long, List<KingdomTerritorialHistoryRow>> kingdomRows =
                ReadKingdomTerritorialRows(db);
            List<CityTerritorialHistoryRow> cityRows =
                ReadCityTerritorialRows(db);
            var ownerSnapshots = new Dictionary<long, CityOwnerSnapshot>();
            var consumedKingdomEventIds = new HashSet<long>();

            for (int index = 0; index < cityRows.Count; index++)
            {
                CityTerritorialHistoryRow city = cityRows[index];
                if (city == null || city.CityId < 0L) continue;
                long previousOwnerId = -1L;
                if (ownerSnapshots.TryGetValue(city.CityId,
                        out CityOwnerSnapshot previousOwner))
                    previousOwnerId = previousOwner.KingdomId;
                MatchCityTransferRows(city, previousOwnerId, kingdomRows,
                    consumedKingdomEventIds, out KingdomTerritorialHistoryRow lost,
                    out KingdomTerritorialHistoryRow gained);

                KingdomAtlasHistoryEvent normalized = NormalizeCityEvent(city,
                    lost, gained, ownerSnapshots);
                if (normalized == null) continue;
                result.Add(normalized);
                if (normalized.NewKingdomId >= 0L)
                {
                    ownerSnapshots[city.CityId] = new CityOwnerSnapshot
                    {
                        KingdomId = normalized.NewKingdomId,
                        Name = normalized.NewKingdomName,
                        Color = normalized.NewKingdomColor
                    };
                }
                else
                {
                    ownerSnapshots.Remove(city.CityId);
                }
            }
            return KingdomAtlasRules.OrderAndDeduplicate(result);
        }

        private static void MatchCityTransferRows(CityTerritorialHistoryRow pCity,
            long pPreviousOwnerId,
            IReadOnlyDictionary<long, List<KingdomTerritorialHistoryRow>> pRows,
            ISet<long> pConsumedEventIds,
            out KingdomTerritorialHistoryRow pLost,
            out KingdomTerritorialHistoryRow pGained)
        {
            pLost = null;
            pGained = null;
            if (pCity == null || pCity.EventType != "city_transfer") return;

            long contextOwnerId = pCity.ContextKingdomId;
            pLost = FindKingdomRow(pRows, pCity.CityId, pCity.WorldTime,
                "city_lost", pPreviousOwnerId, pConsumedEventIds);
            long minimumGainEventId = pLost?.EventId ?? -1L;
            pGained = FindKingdomRow(pRows, pCity.CityId, pCity.WorldTime,
                "city_gained", contextOwnerId, pConsumedEventIds,
                minimumGainEventId);

            if (pLost != null) pConsumedEventIds?.Add(pLost.EventId);
            if (pGained != null) pConsumedEventIds?.Add(pGained.EventId);
        }

        private static List<KingdomAtlasZoneSnapshot> TryReadZoneArchive(
            double pWorldTime)
        {
            try
            {
                return KingdomAtlasZoneArchiveService.Read(pWorldTime);
            }
            catch
            {
                // Legacy history databases did not have this optional table.
                return new List<KingdomAtlasZoneSnapshot>();
            }
        }

        private static List<CityTerritorialHistoryRow> ReadCityTerritorialRows(
            SQLiteConnection pDb)
        {
            var result = new List<CityTerritorialHistoryRow>();
            using (var cmd = new SQLiteCommand(pDb))
            {
                cmd.CommandText = "SELECT EVENT_ID,WORLD_TIME,YEAR_PREFIX," +
                    "SUBJECT_NAME,EVENT_TYPE,KINGDOM_NAME,KINGDOM_COLOR," +
                    "CONTEXT_KINGDOM_ID,CONTEXT_KINGDOM_NAME," +
                    "CONTEXT_KINGDOM_COLOR,CITY_ID,TARGET_ID FROM CityHistory " +
                    "WHERE EVENT_TYPE IN ('city_found','city_transfer') " +
                    "ORDER BY WORLD_TIME ASC,EVENT_ID ASC";
                using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long cityId = ReadLong(reader, 10, -1L);
                        if (cityId < 0L) cityId = ReadLong(reader, 11, -1L);
                        if (cityId < 0L) continue;
                        result.Add(new CityTerritorialHistoryRow
                        {
                            EventId = ReadLong(reader, 0, -1L),
                            WorldTime = ReadDouble(reader, 1),
                            YearText = ReadText(reader, 2),
                            CityName = ReadText(reader, 3),
                            EventType = ReadText(reader, 4),
                            KingdomName = ReadText(reader, 5),
                            KingdomColor = ReadText(reader, 6),
                            ContextKingdomId = ReadLong(reader, 7, -1L),
                            ContextKingdomName = ReadText(reader, 8),
                            ContextKingdomColor = ReadText(reader, 9),
                            CityId = cityId
                        });
                    }
                }
            }
            return result;
        }

        private static Dictionary<long, List<KingdomTerritorialHistoryRow>>
            ReadKingdomTerritorialRows(SQLiteConnection pDb)
        {
            var result = new Dictionary<long, List<KingdomTerritorialHistoryRow>>();
            using (var cmd = new SQLiteCommand(pDb))
            {
                cmd.CommandText = "SELECT EVENT_ID,WORLD_TIME,YEAR_PREFIX," +
                    "SUBJECT_NAME,EVENT_TYPE,KINGDOM_ID,CONTEXT_KINGDOM_ID," +
                    "CONTEXT_KINGDOM_NAME,CONTEXT_KINGDOM_COLOR,TARGET_ID " +
                    "FROM KingdomHistory WHERE EVENT_TYPE IN " +
                    "('city_lost','city_gained') ORDER BY WORLD_TIME ASC,EVENT_ID ASC";
                using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long cityId = ReadLong(reader, 9, -1L);
                        if (cityId < 0L) continue;
                        long kingdomId = ReadLong(reader, 5, -1L);
                        if (kingdomId < 0L)
                            kingdomId = ReadLong(reader, 6, -1L);
                        var row = new KingdomTerritorialHistoryRow
                        {
                            EventId = ReadLong(reader, 0, -1L),
                            WorldTime = ReadDouble(reader, 1),
                            EventType = ReadText(reader, 4),
                            KingdomId = kingdomId,
                            KingdomName = ReadText(reader, 7),
                            KingdomColor = ReadText(reader, 8),
                            CityName = ReadText(reader, 3)
                        };
                        if (!result.TryGetValue(cityId,
                                out List<KingdomTerritorialHistoryRow> rows))
                        {
                            rows = new List<KingdomTerritorialHistoryRow>();
                            result[cityId] = rows;
                        }
                        rows.Add(row);
                    }
                }
            }
            return result;
        }

        private static KingdomAtlasHistoryEvent NormalizeCityEvent(
            CityTerritorialHistoryRow pCity,
            KingdomTerritorialHistoryRow pLost,
            KingdomTerritorialHistoryRow pGained,
            IReadOnlyDictionary<long, CityOwnerSnapshot> pOwners)
        {
            long previousId = -1L;
            CityOwnerSnapshot previous = null;
            if (pOwners != null) pOwners.TryGetValue(pCity.CityId, out previous);
            if (previous != null) previousId = previous.KingdomId;

            long contextId = pCity.ContextKingdomId;
            long oldId = -1L;
            long newId = -1L;
            string oldName = previous?.Name ?? "";
            string oldColor = previous?.Color ?? "";
            string newName = FirstNonEmpty(pCity.ContextKingdomName,
                pCity.KingdomName);
            string newColor = FirstNonEmpty(pCity.ContextKingdomColor,
                pCity.KingdomColor);

            if (pLost != null && pLost.KingdomId >= 0L)
            {
                oldId = pLost.KingdomId;
                oldName = FirstNonEmpty(pLost.KingdomName, oldName);
                oldColor = FirstNonEmpty(pLost.KingdomColor, oldColor);
            }
            else if (previousId >= 0L)
            {
                oldId = previousId;
            }

            if (pGained != null && pGained.KingdomId >= 0L)
            {
                newId = pGained.KingdomId;
                newName = FirstNonEmpty(pGained.KingdomName, newName);
                newColor = FirstNonEmpty(pGained.KingdomColor, newColor);
            }
            else if (pCity.EventType == "city_found")
            {
                newId = contextId;
            }
            else if (contextId >= 0L && contextId != oldId)
            {
                newId = contextId;
            }
            else if (pLost == null && contextId >= 0L && oldId < 0L)
            {
                newId = contextId;
            }

            if (pCity.EventType == "city_transfer" &&
                pLost != null && pGained == null && newId == oldId)
                newId = -1L;
            if (pCity.EventType == "city_transfer" &&
                KingdomAtlasRules.IsDuplicateTransferWithoutEvidence(
                    previousId, contextId, pLost != null, pGained != null))
                return null;
            if (pCity.EventType == "city_transfer" &&
                oldId >= 0L && newId == oldId) return null;

            if (newId >= 0L && string.IsNullOrEmpty(newName) &&
                contextId == newId)
                newName = pCity.ContextKingdomName;
            return new KingdomAtlasHistoryEvent
            {
                EventId = pCity.EventId,
                WorldTime = pCity.WorldTime,
                Year = ParseYear(pCity.YearText),
                YearText = pCity.YearText,
                CityId = pCity.CityId,
                CityName = pCity.CityName,
                EventType = pCity.EventType,
                OldKingdomId = oldId,
                OldKingdomName = oldName,
                OldKingdomColor = oldColor,
                NewKingdomId = newId,
                NewKingdomName = newName,
                NewKingdomColor = newColor
            };
        }

        private static KingdomTerritorialHistoryRow FindKingdomRow(
            IReadOnlyDictionary<long, List<KingdomTerritorialHistoryRow>> pRows,
            long pCityId, double pWorldTime, string pEventType,
            long pExpectedKingdomId, ISet<long> pConsumedEventIds,
            long pMinimumEventId = -1L)
        {
            if (pRows == null || !pRows.TryGetValue(pCityId,
                    out List<KingdomTerritorialHistoryRow> rows)) return null;
            for (int index = 0; index < rows.Count; index++)
            {
                KingdomTerritorialHistoryRow row = rows[index];
                if (row == null || row.EventType != pEventType) continue;
                if (row.EventId < pMinimumEventId ||
                    pConsumedEventIds?.Contains(row.EventId) == true) continue;
                if (Math.Abs(row.WorldTime - pWorldTime) > 0.000001d)
                    continue;
                if (pExpectedKingdomId >= 0L &&
                    row.KingdomId != pExpectedKingdomId) continue;
                return row;
            }
            return null;
        }

        private static string FirstNonEmpty(params string[] pValues)
        {
            if (pValues == null) return "";
            for (int index = 0; index < pValues.Length; index++)
                if (!string.IsNullOrWhiteSpace(pValues[index]))
                    return pValues[index];
            return "";
        }

        private static string ReadText(SQLiteDataReader pReader, int pIndex)
        {
            return pReader == null || pReader.IsDBNull(pIndex)
                ? "" : pReader.GetValue(pIndex)?.ToString() ?? "";
        }

        private static long ReadLong(SQLiteDataReader pReader, int pIndex,
            long pFallback)
        {
            if (pReader == null || pReader.IsDBNull(pIndex)) return pFallback;
            try
            {
                return Convert.ToInt64(pReader.GetValue(pIndex),
                    CultureInfo.InvariantCulture);
            }
            catch { return pFallback; }
        }

        private static double ReadDouble(SQLiteDataReader pReader, int pIndex)
        {
            if (pReader == null || pReader.IsDBNull(pIndex)) return 0d;
            try
            {
                return Convert.ToDouble(pReader.GetValue(pIndex),
                    CultureInfo.InvariantCulture);
            }
            catch { return 0d; }
        }

        private sealed class CityTerritorialHistoryRow
        {
            public long EventId { get; set; }
            public double WorldTime { get; set; }
            public string YearText { get; set; } = "";
            public string CityName { get; set; } = "";
            public string EventType { get; set; } = "";
            public string KingdomName { get; set; } = "";
            public string KingdomColor { get; set; } = "";
            public long ContextKingdomId { get; set; } = -1L;
            public string ContextKingdomName { get; set; } = "";
            public string ContextKingdomColor { get; set; } = "";
            public long CityId { get; set; } = -1L;
        }

        private sealed class KingdomTerritorialHistoryRow
        {
            public long EventId { get; set; }
            public double WorldTime { get; set; }
            public string EventType { get; set; } = "";
            public long KingdomId { get; set; } = -1L;
            public string KingdomName { get; set; } = "";
            public string KingdomColor { get; set; } = "";
            public string CityName { get; set; } = "";
        }

        private sealed class CityOwnerSnapshot
        {
            public long KingdomId { get; set; } = -1L;
            public string Name { get; set; } = "";
            public string Color { get; set; } = "";
        }

        private static void AddVisibleCells(List<KingdomAtlasZoneSnapshot> pSnapshots,
            IReadOnlyDictionary<long, long> pOwners,
            ISet<long> pVisibleOwners,
            List<KingdomAtlasZoneCell> pOutput)
        {
            if (pSnapshots == null || pOutput == null) return;
            var latest = new Dictionary<long, KingdomAtlasZoneSnapshot>();
            for (int index = 0; index < pSnapshots.Count; index++)
            {
                KingdomAtlasZoneSnapshot row = pSnapshots[index];
                if (row == null || !pOwners.ContainsKey(row.CityId)) continue;
                if (!latest.TryGetValue(row.CityId, out KingdomAtlasZoneSnapshot current) ||
                    KingdomAtlasRules.ShouldReplaceSnapshot(row.WorldTime,
                        row.SnapshotId, current.WorldTime, current.SnapshotId))
                    latest[row.CityId] = row;
            }
            for (int index = 0; index < pSnapshots.Count; index++)
            {
                KingdomAtlasZoneSnapshot row = pSnapshots[index];
                if (row == null || !latest.TryGetValue(row.CityId,
                        out KingdomAtlasZoneSnapshot current) ||
                    !KingdomAtlasRules.IsSameSnapshotGroup(row.CityId,
                        row.EventType, row.WorldTime, current.CityId,
                        current.EventType, current.WorldTime)) continue;
                if (!pOwners.TryGetValue(row.CityId, out long owner) ||
                    pVisibleOwners == null || !pVisibleOwners.Contains(owner)) continue;
                pOutput.Add(new KingdomAtlasZoneCell(row.CityId, row.X, row.Y,
                    row.Water, row.NeighborMask));
            }
        }

        private static List<KingdomAtlasVassalRelationSnapshot>
            ReadVassalRelations()
        {
            var result = new List<KingdomAtlasVassalRelationSnapshot>();
            LineageArchiveManager manager = LineageArchiveManager.Instance;
            SQLiteConnection db = manager?.OperatingDB;
            if (db == null) return result;
            try
            {
                using (var cmd = new SQLiteCommand(db))
                {
                    cmd.CommandText = "SELECT RELATION_ID,VASSAL_ID,VASSAL_NAME," +
                        "VASSAL_COLOR,SUZERAIN_ID,SUZERAIN_NAME,SUZERAIN_COLOR," +
                        "CONTRACT_TIER,START_TIME,END_TIME FROM VassalRelation " +
                        "ORDER BY START_TIME ASC,RELATION_ID ASC";
                    using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            result.Add(new KingdomAtlasVassalRelationSnapshot
                            {
                                RelationId = reader.IsDBNull(0) ? -1L : reader.GetInt64(0),
                                VassalId = reader.IsDBNull(1) ? -1L : reader.GetInt64(1),
                                VassalName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                VassalColor = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                SuzerainId = reader.IsDBNull(4) ? -1L : reader.GetInt64(4),
                                SuzerainName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                SuzerainColor = reader.IsDBNull(6) ? "" : reader.GetString(6),
                                ContractTier = reader.IsDBNull(7) ? 0 : (int)reader.GetInt64(7),
                                StartTime = reader.IsDBNull(8) ? 0d : reader.GetDouble(8),
                                EndTime = reader.IsDBNull(9) ? -1d : reader.GetDouble(9)
                            });
                    }
                }
            }
            catch
            {
                result.Clear();
            }
            return result;
        }

        private static Dictionary<long, KingdomAtlasKingdomSnapshot>
            BuildKingdomSnapshots(KingdomAtlasHistoryEvent pEvent,
                IReadOnlyList<KingdomAtlasVassalRelationSnapshot> pRelations)
        {
            return KingdomAtlasRules.BuildKingdomSnapshots(pEvent, pRelations);
        }

        private static void AddKingdomSnapshot(
            Dictionary<long, KingdomAtlasKingdomSnapshot> pResult,
            long? pKingdomId, string pName, string pColor)
        {
            long kingdomId = pKingdomId ?? -1L;
            if (kingdomId < 0L || pResult.ContainsKey(kingdomId)) return;
            pResult[kingdomId] = new KingdomAtlasKingdomSnapshot
            {
                KingdomId = kingdomId,
                Name = pName ?? "",
                Color = pColor ?? ""
            };
        }

        private static void MergeKingdomSnapshot(
            Dictionary<long, KingdomAtlasKingdomSnapshot> pResult,
            long pKingdomId, string pName, string pColor)
        {
            if (pKingdomId < 0L) return;
            if (!pResult.TryGetValue(pKingdomId,
                    out KingdomAtlasKingdomSnapshot current))
            {
                AddKingdomSnapshot(pResult, pKingdomId, pName, pColor);
                return;
            }
            if (string.IsNullOrWhiteSpace(current.Name) &&
                !string.IsNullOrWhiteSpace(pName)) current.Name = pName;
            if (string.IsNullOrWhiteSpace(current.Color) &&
                !string.IsNullOrWhiteSpace(pColor)) current.Color = pColor;
        }

        private static List<KingdomAtlasChronicleRow> ReadChronicle(long pKingdomId,
            double pWorldTime, long pNodeEventId, int pNodeYear)
        {
            var result = new List<KingdomAtlasChronicleRow>();
            if (pKingdomId < 0L) return result;
            LineageArchiveManager manager = LineageArchiveManager.Instance;
            SQLiteConnection db = manager?.OperatingDB;
            if (db == null) return result;
            using (var cmd = new SQLiteCommand(db))
            {
                cmd.CommandText = "SELECT EVENT_ID,WORLD_TIME,YEAR_PREFIX,CONTENT,CONTENT_RICH,EVENT_TYPE " +
                    "FROM KingdomHistory WHERE KINGDOM_ID=@id AND WORLD_TIME<=@time " +
                    "ORDER BY WORLD_TIME ASC,EVENT_ID ASC";
                cmd.Parameters.AddWithValue("@id", pKingdomId);
                cmd.Parameters.AddWithValue("@time", pWorldTime);
                using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
                {
                    while (reader.Read())
                        {
                            long eventId = reader.GetInt64(0);
                            double eventTime = reader.GetDouble(1);
                            if (!KingdomAtlasRules.IsEventAtOrBeforeNode(
                                    eventTime, eventId, pWorldTime,
                                    pNodeEventId)) continue;
                            string yearText = reader.IsDBNull(2) ? "" :
                                reader.GetString(2);
                            int year = ParseYear(yearText);
                            if (!KingdomAtlasRules.IsEventInYear(year,
                                    pNodeYear)) continue;
                            result.Add(new KingdomAtlasChronicleRow
                            {
                                EventId = eventId,
                                WorldTime = eventTime,
                                Year = year,
                                YearText = yearText,
                                Content = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                ContentRich = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                EventType = reader.IsDBNull(5) ? "" : reader.GetString(5)
                            });
                        }
                }
            }
            return result;
        }

        private static string EventKey(long pCityId, double pTime)
        {
            return pCityId.ToString(CultureInfo.InvariantCulture) + ":" +
                pTime.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void Enqueue(
            Dictionary<string, Queue<KingdomAtlasHistoryEvent>> pQueues,
            string pKey, KingdomAtlasHistoryEvent pRow)
        {
            if (!pQueues.TryGetValue(pKey,
                    out Queue<KingdomAtlasHistoryEvent> queue))
            {
                queue = new Queue<KingdomAtlasHistoryEvent>();
                pQueues[pKey] = queue;
            }
            queue.Enqueue(pRow);
        }

        private static int ParseYear(string pText)
        {
            Match match = YearDigits.Match(pText ?? "");
            return match.Success && int.TryParse(match.Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int year) ? year : 0;
        }
    }
}
