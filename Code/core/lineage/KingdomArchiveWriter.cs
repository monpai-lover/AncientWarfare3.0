using System;
using System.Collections.Generic;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     把一个 Kingdom 的旗帜/颜色/名快照 upsert 进 KingdomArchive 表。
    ///     建国/换君时 Upsert(刷新名+旗+存活),亡国时 MarkDestroyed。
    ///     由 ChronicleEvents 调用。
    /// </summary>
    internal static class KingdomArchiveWriter
    {
        public static void Upsert(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null || !LineageArchiveManager.Instance.InitializeSuccessful) return;

            long id = pKingdom.id;
            string table = KingdomArchiveTableItem.GetTableName();

            string name = pKingdom.name ?? "";
            string colorText = pKingdom.getColor()?.color_text ?? "";
            int colorId = pKingdom.data.color_id;
            int bannerIcon = pKingdom.data.banner_icon_id;
            int bannerBg = pKingdom.data.banner_background_id;
            string bannerId = pKingdom.getActorAsset()?.banner_id ?? "";
            Actor founder = pKingdom.king;
            long founderId = founder?.data?.id ?? -1;
            string founderName = founder?.getName() ?? "";
            City capital = ResolveCapital(pKingdom);
            long capitalId = capital?.data?.id ?? -1;
            string capitalName = capital?.data?.name ?? "";
            double now = World.world.getCurWorldTime();

            try
            {
                bool exists = db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("KINGDOM_ID", id));
                if (exists)
                {
                    var extra = ReadSnapshot(table, id);
                    if (extra.founderId >= 0)
                    {
                        founderId = extra.founderId;
                        founderName = extra.founderName;
                    }
                    if (capitalId < 0 && extra.capitalId >= 0)
                    {
                        capitalId = extra.capitalId;
                        capitalName = extra.capitalName;
                    }

                    db.UpdateValue(table,
                        new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("KINGDOM_ID", id) },
                        ColumnVal.Create("KINGDOM_NAME", name),
                        ColumnVal.Create("COLOR_TEXT", colorText),
                        ColumnVal.Create("COLOR_ID", colorId),
                        ColumnVal.Create("BANNER_ICON_ID", bannerIcon),
                        ColumnVal.Create("BANNER_BACKGROUND_ID", bannerBg),
                        ColumnVal.Create("BANNER_ID", bannerId),
                        ColumnVal.Create("FOUNDER_ACTOR_ID", founderId),
                        ColumnVal.Create("FOUNDER_NAME", founderName),
                        ColumnVal.Create("CAPITAL_CITY_ID", capitalId),
                        ColumnVal.Create("CAPITAL_CITY_NAME", capitalName),
                        ColumnVal.Create("IS_ALIVE", 1));
                    return;
                }

                db.Insert(table,
                    ColumnVal.Create("KINGDOM_ID", id),
                    ColumnVal.Create("KINGDOM_NAME", name),
                    ColumnVal.Create("COLOR_TEXT", colorText),
                    ColumnVal.Create("COLOR_ID", colorId),
                    ColumnVal.Create("BANNER_ICON_ID", bannerIcon),
                    ColumnVal.Create("BANNER_BACKGROUND_ID", bannerBg),
                    ColumnVal.Create("BANNER_ID", bannerId),
                    ColumnVal.Create("FOUNDER_ACTOR_ID", founderId),
                    ColumnVal.Create("FOUNDER_NAME", founderName),
                    ColumnVal.Create("CAPITAL_CITY_ID", capitalId),
                    ColumnVal.Create("CAPITAL_CITY_NAME", capitalName),
                    ColumnVal.Create("IS_ALIVE", 1),
                    ColumnVal.Create("FOUNDED_TIME", now),
                    ColumnVal.Create("DESTROYED_TIME", -1.0));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("KingdomArchiveWriter.Upsert 失败:" + e.Message);
            }
        }

        /// <summary>仅当档案无此王国行时,用当前(可能半失效)数据补一行兜底,不覆盖已有行。</summary>
        public static void EnsureRow(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null || !LineageArchiveManager.Instance.InitializeSuccessful) return;
            string table = KingdomArchiveTableItem.GetTableName();
            try
            {
                if (db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdom.id))) return;
                Upsert(pKingdom); // 无行才走 Upsert(内部会 INSERT)
            }
            catch { /* 兜底失败不致命 */ }
        }

        private static (long founderId, string founderName, long capitalId, string capitalName) ReadSnapshot(string pTable, long pKingdomId)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return (-1, "", -1, "");
            try
            {
                using var cmd = new System.Data.SQLite.SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT IFNULL(FOUNDER_ACTOR_ID, -1), IFNULL(FOUNDER_NAME, ''), " +
                    $"IFNULL(CAPITAL_CITY_ID, -1), IFNULL(CAPITAL_CITY_NAME, '') FROM {pTable} " +
                    $"WHERE KINGDOM_ID=@kid LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                using var r = (System.Data.SQLite.SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return (-1, "", -1, "");
                return (r.IsDBNull(0) ? -1 : r.GetInt64(0),
                    r.IsDBNull(1) ? "" : r.GetString(1),
                    r.IsDBNull(2) ? -1 : r.GetInt64(2),
                    r.IsDBNull(3) ? "" : r.GetString(3));
            }
            catch { return (-1, "", -1, ""); }
        }

        private static City ResolveCapital(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            if (pKingdom.capital?.data != null) return pKingdom.capital;

            try
            {
                if (pKingdom.data.capitalID >= 0)
                {
                    City byId = World.world?.cities?.get(pKingdom.data.capitalID);
                    if (byId?.data != null) return byId;
                }
            }
            catch { }

            try
            {
                if (pKingdom.cities != null)
                {
                    foreach (City city in pKingdom.cities)
                    {
                        if (city?.data == null) continue;
                        if (!city.isAlive()) continue;
                        return city;
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>把世界上当前所有存活文明王国 upsert 进档案(读档/新世界后调用,让老存档/已有王国立即进名册)。</summary>
        public static void BackfillAll()
        {
            var kingdoms = World.world?.kingdoms;
            if (kingdoms == null) return;
            foreach (Kingdom k in kingdoms)
            {
                if (k == null || k.isRekt() || !k.isCiv()) continue;
                Upsert(k);
            }
        }

        public static void MarkDestroyed(Kingdom pKingdom)
        {
            if (pKingdom == null) return;
            Upsert(pKingdom);
            MarkDestroyed(pKingdom.id);
        }

        public static void MarkDestroyed(long pKingdomId)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null || !LineageArchiveManager.Instance.InitializeSuccessful) return;
            string table = KingdomArchiveTableItem.GetTableName();
            try
            {
                if (!db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdomId))) return;
                db.UpdateValue(table,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdomId) },
                    ColumnVal.Create("IS_ALIVE", 0),
                    ColumnVal.Create("DESTROYED_TIME", World.world.getCurWorldTime()));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("KingdomArchiveWriter.MarkDestroyed 失败:" + e.Message);
            }
        }
    }
}
