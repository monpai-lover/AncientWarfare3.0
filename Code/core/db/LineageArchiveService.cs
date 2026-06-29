using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     人物档案读写入口(对应 docs 任务书的 ArchiveActor / 查询接口的雏形)。
    ///     阶段1 只做最小可用:把一个 actor 的核心信息 upsert 进 ActorArchive,死亡时标记 is_alive=0。
    ///     谱系/氏支/家族树等字段后续阶段填充。
    /// </summary>
    public static class LineageArchiveService
    {
        private const string XIA_ASSET_ID = "Xia";

        /// <summary>该 actor 是否需要进档案(阶段1:Xia 种族即记;后续按"有姓氏"细化)。</summary>
        public static bool ShouldArchive(Actor pActor)
        {
            if (pActor?.asset == null) return false;
            return pActor.asset.id == XIA_ASSET_ID;
        }

        /// <summary>出生/晋升/死亡/存档前统一 upsert 一个 actor 的档案。pAlive=false 表示标记死亡。</summary>
        public static void ArchiveActor(Actor pActor, bool pAlive)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null || !LineageArchiveManager.Instance.InitializeSuccessful) return;
            if (!ShouldArchive(pActor)) return;

            long id = pActor.data.id;
            string table = ActorArchiveTableItem.GetTableName();

            // 读 actor 上已有的姓/氏(批A XiaNaming 写的 family_name/clan_name)
            pActor.data.get("family_name", out string familyName, "");
            pActor.data.get("clan_name", out string clanName, "");

            string assetId = pActor.asset.id;
            string name = pActor.getName();
            int sex = pActor.isSexMale() ? 0 : 1;
            double birth = pActor.data.created_time;
            long kingdomId = pActor.kingdom?.id ?? -1;
            string kingdomName = pActor.kingdom?.name ?? "";
            long cityId = pActor.city?.data?.id ?? -1;
            string cityName = pActor.city?.data?.name ?? "";
            long clanId = pActor.clan?.data?.id ?? -1;
            int head = pActor.data.head;
            // 新版 ActorData 无 skin/skin_set(皮肤改走 subspecies/phenotype),头像重建阶段3再处理

            bool exists = db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("ID", id));

            if (exists)
            {
                var cols = new List<ColumnVal>
                {
                    ColumnVal.Create("CURR_NAME", name),
                    ColumnVal.Create("DISPLAY_NAME", name),
                    ColumnVal.Create("FAMILY_NAME", familyName),
                    ColumnVal.Create("CLAN_NAME", clanName),
                    ColumnVal.Create("KINGDOM_ID", kingdomId),
                    ColumnVal.Create("KINGDOM_NAME", kingdomName),
                    ColumnVal.Create("CITY_ID", cityId),
                    ColumnVal.Create("CITY_NAME", cityName),
                    ColumnVal.Create("IS_ALIVE", pAlive ? 1 : 0)
                };
                if (!pAlive) cols.Add(ColumnVal.Create("DEATH_TIME", World.world.getCurWorldTime()));

                db.UpdateValue(table,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ID", id) },
                    cols.ToArray());
            }
            else
            {
                db.Insert(table,
                    ColumnVal.Create("ID", id),
                    ColumnVal.Create("GIVEN_NAME", name),
                    ColumnVal.Create("DISPLAY_NAME", name),
                    ColumnVal.Create("FAMILY_NAME", familyName),
                    ColumnVal.Create("CLAN_NAME", clanName),
                    ColumnVal.Create("ASSET_ID", assetId),
                    ColumnVal.Create("SEX", sex),
                    ColumnVal.Create("STATUS", "none"),
                    ColumnVal.Create("KINGDOM_ID", kingdomId),
                    ColumnVal.Create("KINGDOM_NAME", kingdomName),
                    ColumnVal.Create("CITY_ID", cityId),
                    ColumnVal.Create("CITY_NAME", cityName),
                    ColumnVal.Create("ORIGINAL_CLAN_ID", clanId),
                    ColumnVal.Create("BIRTH_TIME", birth),
                    ColumnVal.Create("DEATH_TIME", pAlive ? -1.0 : World.world.getCurWorldTime()),
                    ColumnVal.Create("IS_ALIVE", pAlive ? 1 : 0),
                    ColumnVal.Create("HEAD", head));
            }
        }

        /// <summary>调试用:统计档案总行数(阶段1 验证存读)。</summary>
        public static int CountArchived()
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return -1;
            using var cmd = new SQLiteCommand(db);
            cmd.CommandText = $"SELECT COUNT(*) FROM {ActorArchiveTableItem.GetTableName()}";
            return (int)(long)cmd.ExecuteScalar();
        }
    }
}
