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
            var nodes = new List<KingdomAtlasNode>();
            for (int index = 0; index < rows.Count; index++)
            {
                KingdomAtlasHistoryEvent row = rows[index];
                if (row == null || (row.OldKingdomId != pKingdomId &&
                    row.NewKingdomId != pKingdomId)) continue;
                if (!HasReliableParticipantColours(row)) continue;
                var all = rows.Where(pEvent => pEvent.WorldTime <= row.WorldTime).ToList();
                IReadOnlyDictionary<long, long> owners = ReplayOwners(all);
                List<KingdomAtlasZoneSnapshot> snapshots =
                    KingdomAtlasZoneArchiveService.Read(row.WorldTime);
                var visible = new List<KingdomAtlasZoneCell>();
                AddVisibleCells(snapshots, owners, row.OldKingdomId,
                    row.NewKingdomId, visible);
                var node = new KingdomAtlasNode
                {
                    Event = row,
                    Events = all,
                    CityOwners = owners,
                    VisibleZones = visible,
                    OldChronicle = ReadChronicle(row.OldKingdomId, row.WorldTime),
                    NewChronicle = ReadChronicle(row.NewKingdomId, row.WorldTime)
                };
                nodes.Add(node);
            }
            return nodes;
        }

        internal static bool HasReliableColours(IReadOnlyList<KingdomAtlasNode> pNodes)
        {
            if (pNodes == null || pNodes.Count == 0) return false;
            for (int index = 0; index < pNodes.Count; index++)
            {
                KingdomAtlasHistoryEvent row = pNodes[index]?.Event;
                if (!HasReliableParticipantColours(row)) return false;
            }
            return true;
        }

        private static bool HasReliableParticipantColours(
            KingdomAtlasHistoryEvent pEvent)
        {
            if (pEvent == null) return false;
            bool oldOk = pEvent.OldKingdomId < 0L ||
                KingdomAtlasRules.TryResolveHistoricalColor(pEvent.OldKingdomColor);
            bool newOk = pEvent.NewKingdomId < 0L ||
                KingdomAtlasRules.TryResolveHistoricalColor(pEvent.NewKingdomColor);
            return oldOk && newOk;
        }

        private static List<KingdomAtlasHistoryEvent> ReadTerritorialEvents()
        {
            var result = new List<KingdomAtlasHistoryEvent>();
            SQLiteConnection db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return result;
            var lost = new Dictionary<string, KingdomAtlasHistoryEvent>();
            var gained = new Dictionary<string, KingdomAtlasHistoryEvent>();
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
                            lost[EventKey(cityId, row.WorldTime)] = row;
                        }
                        else
                        {
                            row.NewKingdomId = kingdomId;
                            row.NewKingdomName = name;
                            row.NewKingdomColor = color;
                            gained[EventKey(cityId, row.WorldTime)] = row;
                        }
                    }
                }
            }
            foreach (var pair in lost)
            {
                if (!gained.TryGetValue(pair.Key, out KingdomAtlasHistoryEvent gain))
                    result.Add(pair.Value);
                else result.Add(Merge(pair.Value, gain));
            }
            foreach (var pair in gained)
                if (!lost.ContainsKey(pair.Key)) result.Add(pair.Value);

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
            IReadOnlyDictionary<long, long> pOwners, long pOldId, long pNewId,
            List<KingdomAtlasZoneCell> pOutput)
        {
            if (pSnapshots == null || pOutput == null) return;
            var latest = new Dictionary<long, double>();
            for (int index = 0; index < pSnapshots.Count; index++)
            {
                KingdomAtlasZoneSnapshot row = pSnapshots[index];
                if (row == null || !pOwners.ContainsKey(row.CityId)) continue;
                if (!latest.TryGetValue(row.CityId, out double time) || row.WorldTime >= time)
                    latest[row.CityId] = row.WorldTime;
            }
            for (int index = 0; index < pSnapshots.Count; index++)
            {
                KingdomAtlasZoneSnapshot row = pSnapshots[index];
                if (row == null || !latest.TryGetValue(row.CityId, out double time) ||
                    Math.Abs(time - row.WorldTime) > 0.0000001d) continue;
                if (!pOwners.TryGetValue(row.CityId, out long owner) ||
                    !KingdomAtlasRules.IsVisibleOwner(owner, pOldId, pNewId)) continue;
                pOutput.Add(new KingdomAtlasZoneCell(row.CityId, row.X, row.Y,
                    row.Water, row.NeighborMask));
            }
        }

        private static List<KingdomAtlasChronicleRow> ReadChronicle(long pKingdomId,
            double pWorldTime)
        {
            var result = new List<KingdomAtlasChronicleRow>();
            if (pKingdomId < 0L) return result;
            SQLiteConnection db = LineageArchiveManager.Instance.OperatingDB;
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
                        result.Add(new KingdomAtlasChronicleRow
                        {
                            EventId = reader.GetInt64(0),
                            WorldTime = reader.GetDouble(1),
                            YearText = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Content = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            ContentRich = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            EventType = reader.IsDBNull(5) ? "" : reader.GetString(5)
                        });
                }
            }
            return result;
        }

        private static string EventKey(long pCityId, double pTime)
        {
            return pCityId.ToString(CultureInfo.InvariantCulture) + ":" +
                pTime.ToString("R", CultureInfo.InvariantCulture);
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
