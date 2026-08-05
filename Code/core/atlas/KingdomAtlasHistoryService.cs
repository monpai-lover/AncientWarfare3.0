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
            var nodes = new List<KingdomAtlasNode>();
            for (int index = 0; index < rows.Count; index++)
            {
                KingdomAtlasHistoryEvent row = rows[index];
                if (row == null || (row.OldKingdomId != pKingdomId &&
                    row.NewKingdomId != pKingdomId)) continue;
                if (!HasReliableParticipantColours(row, relations)) continue;
                var all = rows.Where(pEvent => KingdomAtlasRules.
                    IsEventAtOrBeforeNode(pEvent.WorldTime, pEvent.EventId,
                        row.WorldTime, row.EventId)).ToList();
                IReadOnlyDictionary<long, long> owners = ReplayOwners(all);
                List<KingdomAtlasZoneSnapshot> snapshots =
                    KingdomAtlasZoneArchiveService.Read(row.WorldTime);
                var visible = new List<KingdomAtlasZoneCell>();
                HashSet<long> visibleOwners = KingdomAtlasRules.BuildVisibleOwnerIds(
                    new[] { row.OldKingdomId, row.NewKingdomId }, relations,
                    row.WorldTime);
                AddVisibleCells(snapshots, owners, visibleOwners, visible);
                Dictionary<long, KingdomAtlasKingdomSnapshot> kingdoms =
                    BuildKingdomSnapshots(row, relations);
                var historicalColors = kingdoms.ToDictionary(
                    pPair => pPair.Key, pPair => pPair.Value.Color);
                var node = new KingdomAtlasNode
                {
                    Event = row,
                    Events = all,
                    CityOwners = owners,
                    VisibleZones = visible,
                    VassalRelations = relations,
                    Kingdoms = kingdoms,
                    DisplayColors = KingdomAtlasRules.BuildDisplayColors(
                        historicalColors, relations, row.WorldTime),
                    OldChronicle = ReadChronicle(row.OldKingdomId, row.WorldTime,
                        row.EventId, row.Year),
                    NewChronicle = ReadChronicle(row.NewKingdomId, row.WorldTime,
                        row.EventId, row.Year)
                };
                nodes.Add(node);
            }
            return nodes;
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
            var lost = new Dictionary<string, Queue<KingdomAtlasHistoryEvent>>();
            var gained = new Dictionary<string, Queue<KingdomAtlasHistoryEvent>>();
            using (var cmd = new SQLiteCommand(db))
            {
                cmd.CommandText = "SELECT EVENT_ID,WORLD_TIME,YEAR_PREFIX,SUBJECT_NAME," +
                    "EVENT_TYPE,CONTEXT_KINGDOM_ID,CONTEXT_KINGDOM_NAME," +
                    "CONTEXT_KINGDOM_COLOR,TARGET_ID FROM KingdomHistory " +
                    "WHERE EVENT_TYPE IN ('city_lost','city_gained') " +
                    "ORDER BY WORLD_TIME ASC,EVENT_ID ASC";
                using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long cityId = reader.IsDBNull(8) ? -1L : reader.GetInt64(8);
                        if (cityId < 0L) continue;
                        var row = new KingdomAtlasHistoryEvent
                        {
                            EventId = reader.GetInt64(0),
                            WorldTime = reader.GetDouble(1),
                            YearText = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Year = ParseYear(reader.IsDBNull(2) ? "" : reader.GetString(2)),
                            CityId = cityId,
                            CityName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            EventType = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            OldKingdomId = -1L,
                            NewKingdomId = -1L
                        };
                        long kingdomId = reader.IsDBNull(5) ? -1L : reader.GetInt64(5);
                        string name = reader.IsDBNull(6) ? "" : reader.GetString(6);
                        string color = reader.IsDBNull(7) ? "" : reader.GetString(7);
                        if (row.EventType == "city_lost")
                        {
                            row.OldKingdomId = kingdomId;
                            row.OldKingdomName = name;
                            row.OldKingdomColor = color;
                            Enqueue(lost, EventKey(cityId, row.WorldTime), row);
                        }
                        else
                        {
                            row.NewKingdomId = kingdomId;
                            row.NewKingdomName = name;
                            row.NewKingdomColor = color;
                            Enqueue(gained, EventKey(cityId, row.WorldTime), row);
                        }
                    }
                }
            }
            var keys = new HashSet<string>(lost.Keys);
            keys.UnionWith(gained.Keys);
            foreach (string key in keys)
            {
                lost.TryGetValue(key, out Queue<KingdomAtlasHistoryEvent> losses);
                gained.TryGetValue(key, out Queue<KingdomAtlasHistoryEvent> gains);
                while (losses != null && losses.Count > 0)
                {
                    KingdomAtlasHistoryEvent loss = losses.Dequeue();
                    if (gains == null || gains.Count == 0) continue;
                    KingdomAtlasHistoryEvent gain = gains.Dequeue();
                    if (KingdomAtlasRules.IsCompleteTransfer(
                            loss.OldKingdomId, gain.NewKingdomId))
                        result.Add(Merge(loss, gain));
                }
            }

            using (var cmd = new SQLiteCommand(db))
            {
                cmd.CommandText = "SELECT EVENT_ID,WORLD_TIME,YEAR_PREFIX,SUBJECT_NAME," +
                    "EVENT_TYPE,CONTEXT_KINGDOM_ID,CONTEXT_KINGDOM_NAME," +
                    "CONTEXT_KINGDOM_COLOR,CITY_ID FROM CityHistory " +
                    "WHERE EVENT_TYPE IN ('city_found','city_transfer') " +
                    "ORDER BY WORLD_TIME ASC,EVENT_ID ASC";
                using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long cityId = reader.IsDBNull(8) ? -1L : reader.GetInt64(8);
                        if (cityId < 0L) continue;
                        string type = reader.IsDBNull(4) ? "" : reader.GetString(4);
                        if (type != "city_found") continue;
                        result.Add(new KingdomAtlasHistoryEvent
                        {
                            EventId = reader.GetInt64(0),
                            WorldTime = reader.GetDouble(1),
                            YearText = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Year = ParseYear(reader.IsDBNull(2) ? "" : reader.GetString(2)),
                            CityId = cityId,
                            CityName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            EventType = type,
                            NewKingdomId = reader.IsDBNull(5) ? -1L : reader.GetInt64(5),
                            NewKingdomName = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            NewKingdomColor = reader.IsDBNull(7) ? "" : reader.GetString(7)
                        });
                    }
                }
            }
            return result;
        }

        private static KingdomAtlasHistoryEvent Merge(KingdomAtlasHistoryEvent pLost,
            KingdomAtlasHistoryEvent pGain)
        {
            return new KingdomAtlasHistoryEvent
            {
                EventId = Math.Min(pLost.EventId, pGain.EventId),
                WorldTime = pLost.WorldTime,
                Year = pLost.Year != 0 ? pLost.Year : pGain.Year,
                YearText = pLost.YearText ?? pGain.YearText ?? "",
                CityId = pLost.CityId,
                CityName = string.IsNullOrEmpty(pGain.CityName) ? pLost.CityName : pGain.CityName,
                EventType = "city_transfer",
                OldKingdomId = pLost.OldKingdomId,
                OldKingdomName = pLost.OldKingdomName,
                OldKingdomColor = pLost.OldKingdomColor,
                NewKingdomId = pGain.NewKingdomId,
                NewKingdomName = pGain.NewKingdomName,
                NewKingdomColor = pGain.NewKingdomColor
            };
        }

        private static Dictionary<long, long> ReplayOwners(
            IReadOnlyList<KingdomAtlasHistoryEvent> pRows)
        {
            var result = new Dictionary<long, long>();
            if (pRows == null) return result;
            for (int index = 0; index < pRows.Count; index++)
            {
                KingdomAtlasHistoryEvent row = pRows[index];
                if (row == null || row.CityId < 0L) continue;
                result[row.CityId] = row.NewKingdomId >= 0L
                    ? row.NewKingdomId : -1L;
            }
            return result;
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
            var result = new Dictionary<long, KingdomAtlasKingdomSnapshot>();
            AddKingdomSnapshot(result, pEvent?.OldKingdomId,
                pEvent?.OldKingdomName, pEvent?.OldKingdomColor);
            AddKingdomSnapshot(result, pEvent?.NewKingdomId,
                pEvent?.NewKingdomName, pEvent?.NewKingdomColor);
            if (pRelations == null) return result;
            for (int index = 0; index < pRelations.Count; index++)
            {
                KingdomAtlasVassalRelationSnapshot relation = pRelations[index];
                if (relation == null || relation.StartTime > pEvent.WorldTime) continue;
                MergeKingdomSnapshot(result, relation.VassalId,
                    relation.VassalName, relation.VassalColor);
                MergeKingdomSnapshot(result, relation.SuzerainId,
                    relation.SuzerainName, relation.SuzerainColor);
            }
            return result;
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
