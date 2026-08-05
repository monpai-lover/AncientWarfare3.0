using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.atlas
{
    internal static class KingdomAtlasZoneArchiveService
    {
        private static readonly object Gate = new object();

        internal static void CaptureCityEvent(City pCity, Kingdom pOldKingdom,
            Kingdom pNewKingdom, string pEventType, double pWorldTime)
        {
            if (pCity?.data == null || pCity.zones == null ||
                pCity.zones.Count == 0 || string.IsNullOrEmpty(pEventType)) return;
            SQLiteConnection db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return;
            long cityId = pCity.data.id;
            long kingdomId = pNewKingdom?.data?.id ?? pOldKingdom?.data?.id ?? -1L;
            string kingdomName = pNewKingdom?.data != null ? pNewKingdom.name : pOldKingdom?.name ?? "";
            string kingdomColor = HistoryColors.FromKingdom(
                pNewKingdom?.data != null ? pNewKingdom : pOldKingdom);
            string key = BuildSnapshotKey(cityId, pEventType, pWorldTime);
            lock (Gate)
            {
                try
                {
                    using (var exists = new SQLiteCommand(db))
                    {
                        exists.CommandText = "SELECT COUNT(1) FROM " +
                            KingdomAtlasZoneArchiveTableItem.GetTableName() +
                            " WHERE SNAPSHOT_KEY LIKE @key";
                        exists.Parameters.AddWithValue("@key", key + ":%");
                        if (Convert.ToInt32(exists.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                            return;
                    }

                    using (var tx = db.BeginTransaction())
                    {
                        long nextId = NextSnapshotId(db, tx);
                        for (int zoneIndex = 0; zoneIndex < pCity.zones.Count; zoneIndex++)
                        {
                            TileZone zone = pCity.zones[zoneIndex];
                            if (zone?.tiles == null) continue;
                            byte neighborMask = NeighborMask(zone);
                            for (int tileIndex = 0; tileIndex < zone.tiles.Length; tileIndex++)
                            {
                                WorldTile tile = zone.tiles[tileIndex];
                                if (tile == null || tile.data == null) continue;
                                bool water = tile.Type == null || tile.Type.liquid ||
                                    tile.Type.ocean || tile.Type.lava || !tile.Type.ground;
                                using (var cmd = new SQLiteCommand(db))
                                {
                                    cmd.Transaction = tx;
                                    cmd.CommandText = "INSERT INTO " +
                                        KingdomAtlasZoneArchiveTableItem.GetTableName() +
                                        " (SNAPSHOT_ID,CITY_ID,WORLD_TIME,EVENT_TYPE,KINGDOM_ID," +
                                        "KINGDOM_NAME,KINGDOM_COLOR,X,Y,WATER,NEIGHBOR_MASK,SNAPSHOT_KEY) " +
                                        "VALUES (@id,@city,@time,@type,@kingdom,@name,@color,@x,@y,@water,@mask,@key)";
                                    cmd.Parameters.AddWithValue("@id", nextId++);
                                    cmd.Parameters.AddWithValue("@city", cityId);
                                    cmd.Parameters.AddWithValue("@time", pWorldTime);
                                    cmd.Parameters.AddWithValue("@type", pEventType);
                                    cmd.Parameters.AddWithValue("@kingdom", kingdomId);
                                    cmd.Parameters.AddWithValue("@name", kingdomName ?? "");
                                    cmd.Parameters.AddWithValue("@color", kingdomColor ?? "");
                                    cmd.Parameters.AddWithValue("@x", tile.x);
                                    cmd.Parameters.AddWithValue("@y", tile.y);
                                    cmd.Parameters.AddWithValue("@water", water ? 1 : 0);
                                    cmd.Parameters.AddWithValue("@mask", neighborMask);
                                    cmd.Parameters.AddWithValue("@key", key + ":" + tileIndex.ToString(CultureInfo.InvariantCulture));
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }
                        tx.Commit();
                    }
                }
                catch (Exception error)
                {
                    try { ModClass.LogWarning("Kingdom atlas zone archive failed: " + error.Message); }
                    catch { }
                }
            }
        }

        internal static List<KingdomAtlasZoneSnapshot> Read(double pWorldTime,
            long pCityId = -1L)
        {
            var result = new List<KingdomAtlasZoneSnapshot>();
            SQLiteConnection db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return result;
            using (var cmd = new SQLiteCommand(db))
            {
                cmd.CommandText = "SELECT SNAPSHOT_ID,CITY_ID,WORLD_TIME,EVENT_TYPE," +
                    "KINGDOM_ID,KINGDOM_NAME,KINGDOM_COLOR,X,Y,WATER,NEIGHBOR_MASK " +
                    "FROM " + KingdomAtlasZoneArchiveTableItem.GetTableName() +
                    " WHERE WORLD_TIME<=@time" + (pCityId >= 0 ? " AND CITY_ID=@city" : "") +
                    " ORDER BY WORLD_TIME ASC,SNAPSHOT_ID ASC";
                cmd.Parameters.AddWithValue("@time", pWorldTime);
                if (pCityId >= 0) cmd.Parameters.AddWithValue("@city", pCityId);
                using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new KingdomAtlasZoneSnapshot
                        {
                            SnapshotId = reader.GetInt64(0),
                            CityId = reader.GetInt64(1),
                            WorldTime = reader.GetDouble(2),
                            EventType = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            KingdomId = reader.IsDBNull(4) ? -1L : reader.GetInt64(4),
                            KingdomName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            KingdomColor = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            X = reader.GetInt32(7),
                            Y = reader.GetInt32(8),
                            Water = !reader.IsDBNull(9) && reader.GetInt32(9) != 0,
                            NeighborMask = reader.IsDBNull(10) ? (byte)0 : (byte)reader.GetInt32(10)
                        });
                    }
                }
            }
            return result;
        }

        internal static bool FlushForSave(out string pError)
        {
            pError = "";
            try
            {
                SQLiteConnection db = LineageArchiveManager.Instance.OperatingDB;
                if (db == null) { pError = "lineage archive unavailable"; return false; }
                return LineageArchivePragmaService.CheckpointForSave(db);
            }
            catch (Exception error) { pError = error.Message; return false; }
        }

        private static long NextSnapshotId(SQLiteConnection pDb, SQLiteTransaction pTransaction)
        {
            using (var cmd = new SQLiteCommand(pDb))
            {
                cmd.Transaction = pTransaction;
                cmd.CommandText = "SELECT IFNULL(MAX(SNAPSHOT_ID),0)+1 FROM " +
                    KingdomAtlasZoneArchiveTableItem.GetTableName();
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        private static byte NeighborMask(TileZone pZone)
        {
            byte mask = 0;
            if (pZone?.neighbours == null) return mask;
            int count = Math.Min(8, pZone.neighbours.Length);
            for (int index = 0; index < count; index++)
                if (pZone.neighbours[index] != null) mask |= (byte)(1 << index);
            return mask;
        }

        private static string BuildSnapshotKey(long pCityId, string pType, double pTime)
        {
            return pCityId.ToString(CultureInfo.InvariantCulture) + ":" +
                (pType ?? "") + ":" + pTime.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
