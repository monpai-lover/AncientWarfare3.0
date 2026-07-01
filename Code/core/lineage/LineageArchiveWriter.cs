using System.Collections.Generic;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     把一个 Xia actor 的完整档案 upsert 进 ActorArchive 表(含谱系/氏支/亲子/贵族字段)。
    ///     替代阶段1 的 LineageArchiveService.ArchiveActor(那个只写了核心字段)。
    ///     由 LineageService.ArchiveActor 统一调用。
    /// </summary>
    internal static class LineageArchiveWriter
    {
        public static void Upsert(Actor pActor, bool pAlive)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null || !LineageArchiveManager.Instance.InitializeSuccessful) return;
            if (!LineageService.IsXia(pActor)) return;

            long id = pActor.data.id;
            string table = ActorArchiveTableItem.GetTableName();

            // ── 读 actor.data 上的姓氏/谱系字段 ──
            pActor.data.get(LineageKeys.GIVEN_NAME, out string given, "");
            pActor.data.get("display_name", out string display, "");
            pActor.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            pActor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1);
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1);
            pActor.data.get(LineageKeys.NOBLE_DISTANCE, out int nobleDist, 99);
            pActor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
            pActor.data.get(LineageKeys.NAME_INTEGRATED, out bool integrated, false);
            pActor.data.get(LineageKeys.FOUNDED_BRANCH_SHI_ID, out long foundedBranchShi, -1); // 称王分封:开的新支 id,无则 -1
            pActor.data.get(LineageKeys.DEATH_CAUSE, out string deathCause, "");

            string name = pActor.getName();
            if (string.IsNullOrEmpty(given)) given = name;
            if (string.IsNullOrEmpty(display)) display = name;

            int sex = pActor.isSexMale() ? 0 : 1;
            double birth = pActor.data.created_time;
            long kingdomId = pActor.kingdom?.id ?? -1;
            string kingdomName = pActor.kingdom?.name ?? "";
            string kingdomColor = pActor.kingdom?.getColor()?.color_text ?? "";
            long cityId = pActor.city?.data?.id ?? -1;
            string cityName = pActor.city?.data?.name ?? "";
            long clanId = pActor.clan?.data?.id ?? -1;
            string clanColorText = pActor.clan?.getColor()?.color_text ?? "";
            int clanColorId = pActor.clan?.data?.color_id ?? -1;
            int clanBannerIconId = pActor.clan?.data?.banner_icon_id ?? -1;
            int clanBannerBackgroundId = pActor.clan?.data?.banner_background_id ?? -1;
            long parent1 = pActor.data.parent_id_1;
            long parent2 = pActor.data.parent_id_2;
            int generation = pActor.data.generation;
            int head = pActor.data.head;
            int phenotypeIndex = pActor.data.phenotype_index;   // 死者画像重建用真实肤色 phenotype
            int phenotypeShade = pActor.data.phenotype_shade;

            bool exists = db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("ID", id));

            if (exists)
            {
                var cols = new List<ColumnVal>
                {
                    ColumnVal.Create("GIVEN_NAME", given),
                    ColumnVal.Create("DISPLAY_NAME", display),
                    ColumnVal.Create("FAMILY_NAME", family),
                    ColumnVal.Create("CLAN_NAME", clan),
                    ColumnVal.Create("LINEAGE_ID", lineageId),
                    ColumnVal.Create("SHI_ID", shiId),
                    ColumnVal.Create("SEX", sex),
                    ColumnVal.Create("STATUS", status),
                    ColumnVal.Create("NOBLE_DISTANCE", nobleDist),
                    ColumnVal.Create("NAME_INTEGRATED", integrated ? 1 : 0),
                    ColumnVal.Create("KINGDOM_ID", kingdomId),
                    ColumnVal.Create("KINGDOM_NAME", kingdomName),
                    ColumnVal.Create("CITY_ID", cityId),
                    ColumnVal.Create("CITY_NAME", cityName),
                    ColumnVal.Create("ORIGINAL_CLAN_ID", clanId),
                    ColumnVal.Create("CLAN_COLOR_TEXT", clanColorText),
                    ColumnVal.Create("CLAN_COLOR_ID", clanColorId),
                    ColumnVal.Create("CLAN_BANNER_ICON_ID", clanBannerIconId),
                    ColumnVal.Create("CLAN_BANNER_BACKGROUND_ID", clanBannerBackgroundId),
                    ColumnVal.Create("PARENT_ID_1", parent1),
                    ColumnVal.Create("PARENT_ID_2", parent2),
                    ColumnVal.Create("GENERATION", generation),
                    ColumnVal.Create("HEAD", head),
                    ColumnVal.Create("PHENOTYPE_INDEX", phenotypeIndex),
                    ColumnVal.Create("PHENOTYPE_SHADE", phenotypeShade),
                    ColumnVal.Create("FOUNDED_BRANCH_SHI_ID", foundedBranchShi),
                    ColumnVal.Create("IS_ALIVE", pAlive ? 1 : 0)
                };
                if (!pAlive)
                {
                    cols.Add(ColumnVal.Create("DEATH_TIME", LineageService.CurTime()));
                    cols.Add(ColumnVal.Create("DEATH_CAUSE", deathCause ?? ""));
                }
                // 仅在能取到国家颜色时更新,避免亡国/无国的死亡 upsert 把已存色覆盖成空(用户要求亡国不丢色)。
                if (!string.IsNullOrEmpty(kingdomColor)) cols.Add(ColumnVal.Create("KINGDOM_COLOR", kingdomColor));

                db.UpdateValue(table,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ID", id) },
                    cols.ToArray());
                return;
            }

            db.Insert(table,
                ColumnVal.Create("ID", id),
                ColumnVal.Create("GIVEN_NAME", given),
                ColumnVal.Create("DISPLAY_NAME", display),
                ColumnVal.Create("FAMILY_NAME", family),
                ColumnVal.Create("CLAN_NAME", clan),
                ColumnVal.Create("LINEAGE_ID", lineageId),
                ColumnVal.Create("SHI_ID", shiId),
                ColumnVal.Create("ASSET_ID", pActor.asset.id),
                ColumnVal.Create("SEX", sex),
                ColumnVal.Create("STATUS", status),
                ColumnVal.Create("NOBLE_DISTANCE", nobleDist),
                ColumnVal.Create("NAME_INTEGRATED", integrated ? 1 : 0),
                ColumnVal.Create("KINGDOM_ID", kingdomId),
                ColumnVal.Create("KINGDOM_NAME", kingdomName),
                ColumnVal.Create("KINGDOM_COLOR", kingdomColor),
                ColumnVal.Create("CITY_ID", cityId),
                ColumnVal.Create("CITY_NAME", cityName),
                ColumnVal.Create("ORIGINAL_CLAN_ID", clanId),
                ColumnVal.Create("CLAN_COLOR_TEXT", clanColorText),
                ColumnVal.Create("CLAN_COLOR_ID", clanColorId),
                ColumnVal.Create("CLAN_BANNER_ICON_ID", clanBannerIconId),
                ColumnVal.Create("CLAN_BANNER_BACKGROUND_ID", clanBannerBackgroundId),
                ColumnVal.Create("PARENT_ID_1", parent1),
                ColumnVal.Create("PARENT_ID_2", parent2),
                ColumnVal.Create("GENERATION", generation),
                ColumnVal.Create("BIRTH_TIME", birth),
                ColumnVal.Create("DEATH_TIME", pAlive ? -1.0 : LineageService.CurTime()),
                ColumnVal.Create("DEATH_CAUSE", pAlive ? "" : (deathCause ?? "")),
                ColumnVal.Create("IS_ALIVE", pAlive ? 1 : 0),
                ColumnVal.Create("HEAD", head),
                ColumnVal.Create("SKIN", 0),
                ColumnVal.Create("SKIN_SET", 0),
                ColumnVal.Create("PHENOTYPE_INDEX", phenotypeIndex),
                ColumnVal.Create("PHENOTYPE_SHADE", phenotypeShade),
                ColumnVal.Create("FOUNDED_BRANCH_SHI_ID", foundedBranchShi));
        }
    }
}
