using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.ui;
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
        public static void Upsert(Actor pActor, bool pAlive, bool pTraceOnly = false)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null || !LineageArchiveManager.Instance.InitializeSuccessful) return;
            if (!LineageService.UsesAwLineageSystem(pActor) && (!pTraceOnly || !LineageService.IsHuman(pActor))) return;

            long id = pActor.data.id;
            string table = ActorArchiveTableItem.GetTableName();
            ActorArchiveTableItem previous = LineageArchiveReader.ReadRow(id);

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
            var nobleBlood = ResolveNobleBloodSnapshot(pActor, previous, nobleDist);

            string name = pActor.getName();
            if (string.IsNullOrEmpty(given)) given = name;
            if (string.IsNullOrEmpty(display)) display = name;

            int sex = pActor.isSexMale() ? 0 : 1;
            long subspeciesId = pActor.subspecies?.getID() ?? -1L;
            string subspeciesName = pActor.subspecies?.data?.name ?? "";
            double birth = pActor.data.created_time;
            var kingdomSnapshot = ResolveActorKingdomSnapshot(pActor, previous);
            long kingdomId = kingdomSnapshot.kingdomId;
            string kingdomName = kingdomSnapshot.kingdomName;
            string kingdomColor = kingdomSnapshot.kingdomColor;
            var citySnapshot = ResolveActorCitySnapshot(pActor, previous);
            long cityId = citySnapshot.cityId;
            string cityName = citySnapshot.cityName;
            var socialSnapshot = ResolveSocialTitleSnapshot(pActor, kingdomName, kingdomColor, cityName);
            string socialTitle = socialSnapshot.title;
            string socialTitleColor = socialSnapshot.color;
            long clanId = pActor.clan?.data?.id ?? -1;
            string clanColorText = pActor.clan?.getColor()?.color_text ?? "";
            int clanColorId = pActor.clan?.data?.color_id ?? -1;
            int clanBannerIconId = pActor.clan?.data?.banner_icon_id ?? -1;
            int clanBannerBackgroundId = pActor.clan?.data?.banner_background_id ?? -1;
            long parent1 = pActor.data.parent_id_1;
            long parent2 = pActor.data.parent_id_2;
            int generation = pActor.data.generation;
            int head = pActor.data.head;
            int ageOvergrowth = pActor.data.age_overgrowth;
            int phenotypeIndex = pActor.data.phenotype_index;   // 死者画像重建用真实肤色 phenotype
            int phenotypeShade = pActor.data.phenotype_shade;

            bool exists = previous != null || db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("ID", id));
            if (exists && pAlive && IsArchivedDead(previous))
                return;

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
                    ColumnVal.Create("SUBSPECIES_ID", subspeciesId),
                    ColumnVal.Create("SUBSPECIES_NAME", subspeciesName),
                    ColumnVal.Create("SEX", sex),
                    ColumnVal.Create("STATUS", status),
                    ColumnVal.Create("NOBLE_DISTANCE", nobleDist),
                    ColumnVal.Create("EVER_NOBLE_BLOOD", nobleBlood.ever),
                    ColumnVal.Create("NOBLE_ORIGIN_ACTOR_ID", nobleBlood.originId),
                    ColumnVal.Create("NOBLE_ORIGIN_NAME", nobleBlood.originName),
                    ColumnVal.Create("NOBLE_ORIGIN_DISTANCE", nobleBlood.distance),
                    ColumnVal.Create("NAME_INTEGRATED", integrated ? 1 : 0),
                    ColumnVal.Create("KINGDOM_ID", kingdomId),
                    ColumnVal.Create("KINGDOM_NAME", kingdomName),
                    ColumnVal.Create("CITY_ID", cityId),
                    ColumnVal.Create("CITY_NAME", cityName),
                    ColumnVal.Create("SOCIAL_TITLE", socialTitle),
                    ColumnVal.Create("SOCIAL_TITLE_COLOR", socialTitleColor),
                    ColumnVal.Create("PARENT_ID_1", parent1),
                    ColumnVal.Create("PARENT_ID_2", parent2),
                    ColumnVal.Create("GENERATION", generation),
                    ColumnVal.Create("HEAD", head),
                    ColumnVal.Create("AGE_OVERGROWTH", ageOvergrowth),
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

                if (clanId >= 0)
                {
                    cols.Add(ColumnVal.Create("ORIGINAL_CLAN_ID", clanId));
                    cols.Add(ColumnVal.Create("CLAN_COLOR_TEXT", clanColorText));
                    cols.Add(ColumnVal.Create("CLAN_COLOR_ID", clanColorId));
                    cols.Add(ColumnVal.Create("CLAN_BANNER_ICON_ID", clanBannerIconId));
                    cols.Add(ColumnVal.Create("CLAN_BANNER_BACKGROUND_ID", clanBannerBackgroundId));
                }

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
                ColumnVal.Create("SUBSPECIES_ID", subspeciesId),
                ColumnVal.Create("SUBSPECIES_NAME", subspeciesName),
                ColumnVal.Create("SEX", sex),
                ColumnVal.Create("STATUS", status),
                ColumnVal.Create("NOBLE_DISTANCE", nobleDist),
                ColumnVal.Create("EVER_NOBLE_BLOOD", nobleBlood.ever),
                ColumnVal.Create("NOBLE_ORIGIN_ACTOR_ID", nobleBlood.originId),
                ColumnVal.Create("NOBLE_ORIGIN_NAME", nobleBlood.originName),
                ColumnVal.Create("NOBLE_ORIGIN_DISTANCE", nobleBlood.distance),
                ColumnVal.Create("NAME_INTEGRATED", integrated ? 1 : 0),
                ColumnVal.Create("KINGDOM_ID", kingdomId),
                ColumnVal.Create("KINGDOM_NAME", kingdomName),
                ColumnVal.Create("KINGDOM_COLOR", kingdomColor),
                ColumnVal.Create("CITY_ID", cityId),
                ColumnVal.Create("CITY_NAME", cityName),
                ColumnVal.Create("SOCIAL_TITLE", socialTitle),
                ColumnVal.Create("SOCIAL_TITLE_COLOR", socialTitleColor),
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
                ColumnVal.Create("AGE_OVERGROWTH", ageOvergrowth),
                ColumnVal.Create("PHENOTYPE_INDEX", phenotypeIndex),
                ColumnVal.Create("PHENOTYPE_SHADE", phenotypeShade),
                ColumnVal.Create("FOUNDED_BRANCH_SHI_ID", foundedBranchShi));
        }

        private static (int ever, long originId, string originName, int distance) ResolveNobleBloodSnapshot(
            Actor pActor, ActorArchiveTableItem previous, int pNobleDistance)
        {
            pActor.data.get(LineageKeys.EVER_NOBLE_BLOOD, out bool ever, false);
            pActor.data.get(LineageKeys.NOBLE_ORIGIN_ACTOR_ID, out long originId, -1L);
            pActor.data.get(LineageKeys.NOBLE_ORIGIN_NAME, out string originName, "");
            pActor.data.get(LineageKeys.NOBLE_ORIGIN_DISTANCE, out int distance, 99);

            if (ever)
                return (1, originId, originName ?? "", distance);

            if (previous != null && previous.ever_noble_blood != 0)
            {
                pActor.data.set(LineageKeys.EVER_NOBLE_BLOOD, true);
                pActor.data.set(LineageKeys.NOBLE_ORIGIN_ACTOR_ID, previous.noble_origin_actor_id);
                pActor.data.set(LineageKeys.NOBLE_ORIGIN_NAME, previous.noble_origin_name ?? "");
                pActor.data.set(LineageKeys.NOBLE_ORIGIN_DISTANCE, previous.noble_origin_distance);
                return (1, previous.noble_origin_actor_id, previous.noble_origin_name ?? "",
                    previous.noble_origin_distance);
            }

            pActor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
            if (pNobleDistance == 0 && status == LineageStatus.NOBLE)
            {
                string name = pActor.getName() ?? "";
                pActor.data.set(LineageKeys.EVER_NOBLE_BLOOD, true);
                pActor.data.set(LineageKeys.NOBLE_ORIGIN_ACTOR_ID, pActor.data.id);
                pActor.data.set(LineageKeys.NOBLE_ORIGIN_NAME, name);
                pActor.data.set(LineageKeys.NOBLE_ORIGIN_DISTANCE, 0);
                return (1, pActor.data.id, name, 0);
            }

            return (0, -1L, "", 99);
        }

        private static bool IsArchivedDead(ActorArchiveTableItem pRow)
        {
            return pRow != null && (pRow.is_alive == 0 || pRow.death_time > 0);
        }

        private static (long kingdomId, string kingdomName, string kingdomColor) ResolveActorKingdomSnapshot(
            Actor pActor, ActorArchiveTableItem previous)
        {
            Kingdom kingdom = pActor?.kingdom;
            if (ShouldPreserveArchivedKingdomForMad(pActor, kingdom, previous))
                return (previous.kingdom_id, previous.kingdom_name ?? "", previous.kingdom_color ?? "");

            return (kingdom?.id ?? -1L, kingdom?.name ?? "", kingdom?.getColor()?.color_text ?? "");
        }

        private static bool ShouldPreserveArchivedKingdomForMad(Actor pActor, Kingdom pKingdom,
            ActorArchiveTableItem previous)
        {
            if (previous == null) return false;
            if (previous.kingdom_id < 0 && string.IsNullOrEmpty(previous.kingdom_name)) return false;
            return (pActor?.hasTrait("madness") ?? false) || pKingdom?.asset?.id == "mad";
        }

        private static (long cityId, string cityName) ResolveActorCitySnapshot(Actor pActor,
            ActorArchiveTableItem previous)
        {
            City city = pActor?.city;
            if (city == null && pActor?.data != null && pActor.data.cityID >= 0)
                city = World.world?.cities?.get(pActor.data.cityID);

            if (city?.data != null)
                return (city.data.id, city.data.name ?? "");

            if (previous != null && (previous.city_id >= 0 || !string.IsNullOrEmpty(previous.city_name)))
                return (previous.city_id, previous.city_name ?? "");

            return (-1L, "");
        }

        private static (string title, string color) ResolveSocialTitleSnapshot(Actor pActor,
            string pKingdomName, string pKingdomColor, string pCityName)
        {
            if (pActor?.data == null) return ("", "");
            string color = pKingdomColor ?? "";

            try
            {
                pActor.data.get(LineageKeys.CAPTIVE_NOBLE_TITLE, out string captiveTitle, "");
                if (!string.IsNullOrEmpty(captiveTitle))
                {
                    pActor.data.get(LineageKeys.CAPTIVE_NOBLE_COLOR, out string captiveColor, "");
                    return (captiveTitle, string.IsNullOrEmpty(captiveColor) ? color : captiveColor);
                }

                pActor.data.get(LineageKeys.FORMER_KING_TITLE, out string formerTitle, "");
                if (!string.IsNullOrEmpty(formerTitle))
                {
                    pActor.data.get(LineageKeys.FORMER_KINGDOM_COLOR, out string formerColor, "");
                    return (formerTitle, string.IsNullOrEmpty(formerColor) ? color : formerColor);
                }
            }
            catch { }

            try
            {
                if (pActor.isKing())
                {
                    if (RepublicGovernmentService.IsRepublic(pActor.kingdom))
                        return (GovernmentTitleRules.BuildSocialTitle(
                            pKingdomName, pIsHead: true, pIsElder: false), color);
                    string titleChar = KingdomTitleService.GetTitleChar(KingdomTitleService.GetTitle(pActor.kingdom));
                    return (string.IsNullOrEmpty(pKingdomName) ? "\u541B\u4E3B" : pKingdomName + titleChar, color);
                }
            }
            catch { }

            var roles = new List<string>();
            try
            {
                pActor.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
                if (isHeir || HeirService.IsCurrentHeir(pActor.kingdom, pActor))
                    roles.Add(HeirTitleRules.BuildSocialTitle(pKingdomName, pActor.kingdom));
            }
            catch { }

            try
            {
                if (GeneralService.IsFiefHolder(pActor))
                {
                    City fief = FiefService.GetFiefCity(pActor);
                    string fiefName = fief?.data?.name ?? pCityName;
                    roles.Add(string.IsNullOrEmpty(fiefName) ? "\u5C01\u5730\u5927\u5C06" : fiefName + " \u5C01\u5730\u5927\u5C06");
                }
                else if (GeneralService.IsGeneral(pActor)) roles.Add("\u5927\u5C06");
            }
            catch { }

            try
            {
                if (pActor.isCityLeader())
                    roles.Add(string.IsNullOrEmpty(pCityName) ? "\u592A\u5B88" : pCityName + " \u592A\u5B88");
            }
            catch { }

            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            if (!string.IsNullOrEmpty(office))
                roles.Add(AW_L10n.Text("aw_court_office_" + office, office));

            string combined = CourtTitleRules.Combine(roles.ToArray());
            return (combined, string.IsNullOrEmpty(combined) ? "" : color);
        }
    }
}
