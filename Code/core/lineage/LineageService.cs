using System.Collections.Generic;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;
using Random = UnityEngine.Random;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     姓族 / 氏支 / 命名 / 贵族身份的唯一权威入口(对应 docs 任务书 §4 LineageService)。
    ///
    ///     设计原则:
    ///     - 所有姓氏相关写操作都经过本类,Patch 只负责"在正确时机调用本类",不直接写 actor.data。
    ///     - 数据双写:运行时态写 actor.data(随存档序列化、即时可读);持久档案写 SQLite(死人可查)。
    ///     - 仅处理 Xia(asset.id=="Xia"),其余种族一律跳过。
    ///
    ///     职责拆分:本类做出生/晋升/继承/衰落/命名/婚配/合流;查询接口在 LineageQuery。
    /// </summary>
    internal static class LineageService
    {
        public const string XIA_ASSET_ID = "Xia";

        public static bool IsXia(Actor pActor)
        {
            return pActor?.asset != null && pActor.asset.id == XIA_ASSET_ID;
        }

        // ───────────────────────────── 出生 ─────────────────────────────

        /// <summary>
        ///     基础出生初始化:写单名 + 写初始档案,不做父系继承。
        ///     由 Actor.newCreature Postfix 调用(个体已初始化,但此时父母尚未设 —— 见 BabyMaker 时序)。
        ///     覆盖世界初始 spawn / 奇迹生成的"开国第一代"Xia(无父母谱系)。
        /// </summary>
        public static void OnActorBorn(Actor pActor)
        {
            if (!IsXia(pActor)) return;

            EnsureGivenName(pActor);
            ApplyDisplayName(pActor);
            ArchiveActor(pActor, pAlive: true);
        }

        /// <summary>
        ///     繁殖出生:父系继承谱系 + 记亲子边 + 重算显示名 + 更新档案。
        ///     由 BabyHelper.applyParentsMeta Postfix 调用(此时 setParent1/2 已完成,且直接给父母对象,
        ///     比从 parent_id 反查更可靠)。p2 可为 null(孢子/单亲繁殖)。
        /// </summary>
        public static void OnActorBornWithParents(Actor pBaby, Actor pParent1, Actor pParent2)
        {
            if (!IsXia(pBaby)) return;

            EnsureGivenName(pBaby);
            InheritFromParents(pBaby, pParent1, pParent2);
            RecordFamilyEdges(pBaby);
            ApplyDisplayName(pBaby);
            ArchiveActor(pBaby, pAlive: true);
        }

        /// <summary>
        ///     确保有单名 aw_given_name(取当前游戏名首字作单名)。
        ///     合流前个人名一律单字(任务书:姓氏合流之前所有人只取一个字符),
        ///     故无论游戏名是单/双/三字,这里都收窄为**首字**。已写过的不覆盖(幂等)。
        /// </summary>
        private static void EnsureGivenName(Actor pActor)
        {
            pActor.data.get(LineageKeys.GIVEN_NAME, out string given, "");
            if (!string.IsNullOrEmpty(given)) return;

            // 用游戏已生成的名字取首字作单名(中文 BMP 单字,Substring(0,1) 安全)。
            string raw = pActor.getName();
            string single = FirstChar(raw) ?? "";
            pActor.data.set(LineageKeys.GIVEN_NAME, single);
        }

        /// <summary>
        ///     父系继承:取男性一方的父母作"父亲"(都非男则取有谱系一方),继承其
        ///     lineage/shi/family/clan,noble_distance=父+1;父亲无谱系则本人也无谱系
        ///     (合流前不从母亲继承,任务书 §2)。
        /// </summary>
        private static void InheritFromParents(Actor pActor, Actor pParent1, Actor pParent2)
        {
            Actor father = PickFather(pParent1, pParent2);
            if (father == null) return;

            father.data.get(LineageKeys.LINEAGE_ID, out long lid, -1);
            if (lid < 0) return; // 父亲无谱系

            father.data.get(LineageKeys.SHI_ID, out long sid, -1);
            father.data.get(LineageKeys.FAMILY_NAME, out string fam, "");
            father.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            father.data.get(LineageKeys.NOBLE_DISTANCE, out int dist, 99);

            pActor.data.set(LineageKeys.LINEAGE_ID, lid);
            pActor.data.set(LineageKeys.SHI_ID, sid);
            pActor.data.set(LineageKeys.FAMILY_NAME, fam);
            pActor.data.set(LineageKeys.CLAN_NAME, clan);
            pActor.data.set(LineageKeys.NOBLE_DISTANCE, dist + 1);
            pActor.data.set(LineageKeys.LINEAGE_STATUS,
                dist + 1 >= LineageKeys.NOBLE_DECAY_DISTANCE ? LineageStatus.COMMON : LineageStatus.NOBLE);
        }

        /// <summary>从两个父母里挑"父亲":优先男性;都非男(或缺失)则取有谱系的一方。</summary>
        private static Actor PickFather(Actor pParent1, Actor pParent2)
        {
            if (pParent1 != null && pParent1.isSexMale()) return pParent1;
            if (pParent2 != null && pParent2.isSexMale()) return pParent2;
            if (pParent1 != null && HasLineageData(pParent1)) return pParent1;
            if (pParent2 != null && HasLineageData(pParent2)) return pParent2;
            return null;
        }

        private static bool HasLineageData(Actor pActor)
        {
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lid, -1);
            return lid >= 0;
        }

        /// <summary>把 parent_id_1/2 写入 FamilyEdge 持久亲子边表(死后家族树仍可绘制)。</summary>
        private static void RecordFamilyEdges(Actor pActor)
        {
            long childId = pActor.data.id;
            pActor.data.get(LineageKeys.LINEAGE_ID, out long childLineage, -1);

            UpsertFamilyEdge(childId, pActor.data.parent_id_1, 1, childLineage);
            UpsertFamilyEdge(childId, pActor.data.parent_id_2, 2, childLineage);
        }

        private static void UpsertFamilyEdge(long pChildId, long pParentId, int pSlot, long pChildLineage)
        {
            if (pParentId < 0) return;
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null || !LineageArchiveManager.Instance.InitializeSuccessful) return;

            long edgeId = pChildId * 10 + pSlot;
            string table = FamilyEdgeTableItem.GetTableName();

            if (db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("EDGE_ID", edgeId)))
            {
                db.UpdateValue(table,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("EDGE_ID", edgeId) },
                    ColumnVal.Create("PARENT_ID", pParentId),
                    ColumnVal.Create("CHILD_LINEAGE_ID", pChildLineage));
                return;
            }

            db.Insert(table,
                ColumnVal.Create("EDGE_ID", edgeId),
                ColumnVal.Create("CHILD_ID", pChildId),
                ColumnVal.Create("PARENT_ID", pParentId),
                ColumnVal.Create("PARENT_SLOT", pSlot),
                ColumnVal.Create("CHILD_LINEAGE_ID", pChildLineage),
                ColumnVal.Create("CREATED_TIME", CurTime()));
        }

        // ───────────────────────────── 晋升 ─────────────────────────────

        /// <summary>
        ///     成为城主时的统一入口(分流):
        ///     - 无谱系 → 基线 OnActorPromoted(建姓族+氏支,初次贵族)。
        ///     - 已有谱系(父系继承来的)且有贵族父亲 → OnNobleChildFounding(多余 male 子嗣分封新氏支,
        ///       长子/继承人留原氏)。
        ///     国王(setKing)不走分流,直接 OnActorPromoted —— 国王是大宗,不"分封"。
        /// </summary>
        public static void OnCityLeaderAppointed(Actor pActor)
        {
            if (!IsXia(pActor)) return;

            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1);
            if (lineageId < 0)
            {
                OnActorPromoted(pActor, NobleTrigger.CityLeader); // 无谱系:基线建姓族+氏支
                return;
            }

            // 已有谱系:尝试分封(内部会判长子/继承人则不分)。分封同时刷新贵族身份。
            OnNobleChildFounding(pActor);
        }

        /// <summary>成为国王/城主/成名者时赋予或刷新贵族身份。由晋升 Hook 调用。</summary>
        public static void OnActorPromoted(Actor pActor, NobleTrigger pTrigger)
        {
            if (!IsXia(pActor)) return;

            EnsureLineageForNoble(pActor, pTrigger);

            // 本人即贵族:距离归零、加 guizu、状态 noble。
            pActor.data.set(LineageKeys.NOBLE_DISTANCE, 0);
            pActor.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.NOBLE);
            if (!pActor.hasTrait(LineageKeys.TRAIT_GUIZU)) pActor.addTrait(LineageKeys.TRAIT_GUIZU);

            ApplyDisplayName(pActor);
            ArchiveActor(pActor, pAlive: true);
        }

        /// <summary>无谱系贵族:随机古姓建姓族,按封地/城/国生成氏建氏支;已有谱系则沿用。</summary>
        public static void EnsureLineageForNoble(Actor pActor, NobleTrigger pTrigger)
        {
            pActor.data.get(LineageKeys.LINEAGE_ID, out long existing, -1);
            if (existing >= 0) return; // 已有谱系,沿用

            // 1) 姓族:随机古姓
            string familyName = LineageNamePool.RandomSurname();
            long lineageId = LineageIdAllocator.NextLineageId();
            InsertLineageGroup(lineageId, familyName, pActor);

            // 2) 氏支:合流前 50% 随机氏 / 50% 城名首字(见 GenerateShiName)
            (string clanName, string sourceType) = GenerateShiName(pActor);
            if (pTrigger == NobleTrigger.Figure) sourceType = ShiSourceType.SPECIAL_FIGURE;

            long shiId = LineageIdAllocator.NextShiId();
            InsertShiBranch(shiId, lineageId, clanName, pActor, sourceType);

            // 3) 回写 actor.data
            pActor.data.set(LineageKeys.LINEAGE_ID, lineageId);
            pActor.data.set(LineageKeys.SHI_ID, shiId);
            pActor.data.set(LineageKeys.FAMILY_NAME, familyName);
            pActor.data.set(LineageKeys.CLAN_NAME, clanName);
            pActor.data.set(LineageKeys.CHINESE_FAMILY_NAME, familyName);
        }

        /// <summary>
        ///     氏支分封:符合分封条件的子嗣去新 city 当 leader 时,从父姓族**分出新氏支**
        ///     (同姓不同氏,source=enfeoffed)。分封条件见 IsEnfeoffmentCandidate(严格:仅 king 子辈、
        ///     本宗有冗余、非长子)。不符合者留原氏,但当了城主仍刷新贵族身份。
        ///     由 AW_PromotionPatch.SetLeader_Postfix 在"已有谱系者再当 leader"时调用。
        /// </summary>
        public static void OnNobleChildFounding(Actor pChild)
        {
            if (!IsXia(pChild)) return;

            pChild.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1);
            if (lineageId < 0) return;

            if (IsEnfeoffmentCandidate(pChild))
            {
                // 从父姓族分出新氏支(同姓不同氏)
                (string clanName, string sourceType) = GenerateShiName(pChild);
                long shiId = LineageIdAllocator.NextShiId();
                InsertShiBranch(shiId, lineageId, clanName, pChild, sourceType);

                pChild.data.set(LineageKeys.SHI_ID, shiId);
                pChild.data.set(LineageKeys.CLAN_NAME, clanName);
            }

            // 无论是否分封,城主本人都是当代贵族:距离归零、加 guizu。
            pChild.data.set(LineageKeys.NOBLE_DISTANCE, 0);
            pChild.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.NOBLE);
            if (!pChild.hasTrait(LineageKeys.TRAIT_GUIZU)) pChild.addTrait(LineageKeys.TRAIT_GUIZU);

            ApplyDisplayName(pChild);
            ArchiveActor(pChild, pAlive: true);
        }

        /// <summary>
        ///     是否"分封候选"(模块 B 分封 + 模块 E 积极建城共用,严格化以防本宗绝嗣):
        ///     ① Xia∧成年∧male∧有谱系;
        ///     ② 父亲必须是**当前国王**(只分 king 的子辈,孙辈不分 —— 孙辈等其父即位成大宗后才轮到);
        ///     ③ 非长子(父亲活 male 子嗣里出生最早者留本宗作继承人);
        ///     ④ **本宗冗余保护**:父亲的同氏支(留原氏)成年活 male 必须 ≥2,才允许把第 3 个起的分出去
        ///        —— 即长子 + 至少一个备胎留本宗,避免本宗只剩独苗易绝嗣。
        /// </summary>
        public static bool IsEnfeoffmentCandidate(Actor pActor)
        {
            if (!IsXia(pActor)) return false;
            if (!pActor.isAdult() || !pActor.isSexMale()) return false;

            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1);
            if (lineageId < 0) return false;

            Actor father = FindNobleFather(pActor);
            if (father == null) return false;

            // ② 只分当前国王的子辈
            if (!IsCurrentKing(father)) return false;

            // ③ 长子留本宗
            if (IsEldestSon(father, pActor)) return false;

            // ④ 本宗冗余保护:除申请人 pActor 外,本宗(与父同氏支)还须留 ≥2 个成年活 male
            //    (长子 + 至少一个备胎),才允许把 pActor 分出去 —— 防本宗剩独苗绝嗣。
            if (CountHomeBranchAdultMales(father, pExclude: pActor) < 2) return false;

            return true;
        }

        /// <summary>father 是否当前所在国家的在位国王。</summary>
        private static bool IsCurrentKing(Actor pFather)
        {
            return pFather.isKing() || pFather.kingdom?.king == pFather;
        }

        /// <summary>
        ///     数本宗冗余:父亲的活成年 male 子嗣里,与父亲同氏支(留原氏、未分封出去)的数量,
        ///     **排除 pExclude(申请分封者自己)**。≥2 表示申请人分走后本宗仍有长子+备胎,不致绝嗣。
        /// </summary>
        private static int CountHomeBranchAdultMales(Actor pFather, Actor pExclude = null)
        {
            pFather.data.get(LineageKeys.SHI_ID, out long fatherShi, -1);
            int count = 0;
            foreach (var c in pFather.getChildren(pOnlyCurrentFamily: false))
            {
                if (c == null || c == pExclude || c.isRekt() || !c.isSexMale() || !c.isAdult()) continue;
                c.data.get(LineageKeys.SHI_ID, out long childShi, -1);
                if (childShi == fatherShi) count++; // 同父氏支 = 仍在本宗
            }

            return count;
        }

        /// <summary>找 pChild 的有谱系父亲:取 parent 里男性且有 lineage_id 的一方。</summary>
        private static Actor FindNobleFather(Actor pChild)
        {
            foreach (long pid in new[] { pChild.data.parent_id_1, pChild.data.parent_id_2 })
            {
                if (pid < 0) continue;
                var p = World.world.units.get(pid);
                if (p == null || !p.isSexMale()) continue;
                p.data.get(LineageKeys.LINEAGE_ID, out long lid, -1);
                if (lid >= 0) return p;
            }

            return null;
        }

        /// <summary>pChild 是否为父亲活 male 子嗣里出生最早者(=继承人/长子,留原氏)。</summary>
        private static bool IsEldestSon(Actor pFather, Actor pChild)
        {
            Actor eldest = null;
            double eldestTime = double.MaxValue;
            foreach (var c in pFather.getChildren(pOnlyCurrentFamily: false))
            {
                if (c == null || c.isRekt() || !c.isSexMale()) continue;
                if (c.data.created_time < eldestTime)
                {
                    eldestTime = c.data.created_time;
                    eldest = c;
                }
            }

            return eldest == pChild;
        }

        /// <summary>
        ///     氏名生成(合流前规则):50% 从词库随机取氏(source=random),
        ///     50% 取所在城名第一个字作氏(source=enfeoffed 封地)。
        ///     城名取不到时回退随机氏。返回 (氏名, 来源类型)。
        ///     **取自城名时只取 city 第一个字**(单字);**随机氏池保留原样**——复氏(慕容/夏后…)允许整取。
        ///     **氏 ≠ 姓**:生成的氏若与本人 family_name 相同(城名首字恰为姓、或随机池命中姓字),
        ///     则重 roll 随机氏直到不同(有限次兜底),避免"氏取成姓的字符"。
        /// </summary>
        private static (string clanName, string sourceType) GenerateShiName(Actor pActor)
        {
            pActor.data.get(LineageKeys.FAMILY_NAME, out string family, "");

            bool useCityName = LineageNamePool.Rng.NextDouble() < 0.5; // 私有RNG,避免全局序列被固定播种
            if (useCityName)
            {
                string cityFirst = FirstChar(pActor.city?.data?.name); // 取城名时只取 city 首字(单字)
                if (!string.IsNullOrEmpty(cityFirst) && cityFirst != family)
                    return (cityFirst, ShiSourceType.ENFEOFFED);
                // 城名取不到或与姓同字 → 回退随机氏(下方保证≠姓;复氏原样保留)
            }

            string shi = LineageNamePool.RandomShi(); // 随机氏原样(复氏如慕容/夏后整取,不收窄)
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(family) && shi == family; i++)
                shi = LineageNamePool.RandomShi();
            return (shi, ShiSourceType.RANDOM);
        }

        private static string FirstChar(string pName)
        {
            return string.IsNullOrEmpty(pName) ? null : pName.Substring(0, 1);
        }

        // ──────────────────────────── 身份衰落 ────────────────────────────

        /// <summary>按 noble_distance 添加/移除 guizu。距离≥3 且本人非当前贵族 → 退回平民。</summary>
        public static void RefreshNobleStatus(Actor pActor)
        {
            if (!IsXia(pActor)) return;

            pActor.data.get(LineageKeys.NOBLE_DISTANCE, out int dist, 99);
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineage, -1);
            if (lineage < 0) return; // 无谱系无所谓贵族衰落

            if (dist >= LineageKeys.NOBLE_DECAY_DISTANCE)
            {
                if (pActor.hasTrait(LineageKeys.TRAIT_GUIZU)) pActor.removeTrait(LineageKeys.TRAIT_GUIZU);
                pActor.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.COMMON);
            }
            else
            {
                if (!pActor.hasTrait(LineageKeys.TRAIT_GUIZU)) pActor.addTrait(LineageKeys.TRAIT_GUIZU);
                pActor.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.NOBLE);
            }

            ApplyDisplayName(pActor);
        }

        // ───────────────────────────── 命名 ─────────────────────────────

        /// <summary>
        ///     按性别 / 身份 / 国策状态重写显示名(任务书 §2 命名规则)。
        ///     合流前:贵族男=氏+名 / 贵族女=名+姓 / 平民奴隶=单名。
        ///     合流后:所有 Xia=氏+名。
        ///     写回 actor.data display_name;实际改游戏内名由调用方决定(避免在出生中途改名引发递归)。
        /// </summary>
        public static void ApplyDisplayName(Actor pActor)
        {
            if (!IsXia(pActor)) return;

            pActor.data.get(LineageKeys.GIVEN_NAME, out string given, "");
            pActor.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            pActor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            pActor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);

            if (string.IsNullOrEmpty(given)) given = pActor.getName();

            string display;
            bool integrated = IsKingdomIntegrated(pActor.kingdom);

            if (integrated)
            {
                // 合流后:氏 + 名(无旧氏者应已在合流时补氏)
                display = string.IsNullOrEmpty(clan) ? given : clan + given;
            }
            else if (status == LineageStatus.NOBLE && !string.IsNullOrEmpty(family))
            {
                // 合流前贵族:男 氏+名,女 名+姓
                if (pActor.isSexMale())
                    display = (string.IsNullOrEmpty(clan) ? family : clan) + given;
                else
                    display = given + family;
            }
            else
            {
                // 平民 / 奴隶 / 无谱系:单名
                display = given;
            }

            pActor.data.set("display_name", display);

            // 把全名写回游戏内真名(否则晋升/合流后地图/窗口仍显旧名 —— 用户反馈"始祖变贵族后名字没变")。
            // 调用方均为 Postfix(出生/晋升/合流/衰落),非出生中途,setName 安全不递归。
            if (!string.IsNullOrEmpty(display) && pActor.getName() != display)
                pActor.setName(display);
        }

        // ───────────────────────────── 归档 ─────────────────────────────

        /// <summary>出生 / 晋升 / 死亡 / 存档前统一 upsert 档案。pAlive=false 标记死亡。</summary>
        public static void ArchiveActor(Actor pActor, bool pAlive)
        {
            LineageArchiveWriter.Upsert(pActor, pAlive);
        }

        // ──────────────────────────── 合流国策 ────────────────────────────

        /// <summary>该国是否已完成姓氏合流(读 kingdom.data)。kingdom 为 null 视为未合流。</summary>
        public static bool IsKingdomIntegrated(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_INTEGRATED, out bool integrated, false);
            return integrated;
        }

        /// <summary>
        ///     国策完成时:扫该国所有 Xia,有旧氏沿用、无旧氏从随机氏池补,统一氏+名,标记合流。
        ///     当前 AW3 国策系统未迁移,本方法供后续国策接入时调用(阶段3 服务桩)。
        /// </summary>
        public static void ApplyNameIntegration(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;

            pKingdom.data.set(LineageKeys.KINGDOM_INTEGRATED, true);
            UpsertKingdomState(pKingdom, pIntegrated: true);

            foreach (var actor in new List<Actor>(pKingdom.getUnits()))
            {
                if (!IsXia(actor)) continue;

                actor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
                if (string.IsNullOrEmpty(clan))
                {
                    clan = LineageNamePool.RandomShi();
                    actor.data.set(LineageKeys.CLAN_NAME, clan);
                }

                actor.data.set(LineageKeys.NAME_INTEGRATED, true);
                ApplyDisplayName(actor);
                ArchiveActor(actor, pAlive: true);
            }
        }

        // ──────────────────────────── 同姓不婚 ────────────────────────────

        /// <summary>
        ///     合流前同姓不婚:双方都是 Xia、都有姓、所在国都未合流、姓相同 → 不可恋爱。
        ///     合流后不因隐藏旧姓阻止婚姻。返回 true=允许,false=禁止。
        /// </summary>
        public static bool CanFallInLoveByLineage(Actor pA, Actor pB)
        {
            if (!IsXia(pA) || !IsXia(pB)) return true;

            // 任一方所在国已合流 → 不再限制
            if (IsKingdomIntegrated(pA.kingdom) || IsKingdomIntegrated(pB.kingdom)) return true;

            pA.data.get(LineageKeys.FAMILY_NAME, out string fa, "");
            pB.data.get(LineageKeys.FAMILY_NAME, out string fb, "");
            if (string.IsNullOrEmpty(fa) || string.IsNullOrEmpty(fb)) return true;

            return fa != fb; // 同姓 → false(不可)
        }

        // ──────────────────────── 内部:写姓族/氏支/国家状态 ────────────────────────

        internal static void InsertLineageGroup(long pLineageId, string pFamilyName, Actor pFounder)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return;
            db.Insert(LineageGroupTableItem.GetTableName(),
                ColumnVal.Create("LINEAGE_ID", pLineageId),
                ColumnVal.Create("FAMILY_NAME", pFamilyName),
                ColumnVal.Create("FOUNDER_ACTOR_ID", pFounder.data.id),
                ColumnVal.Create("FOUNDER_NAME", pFounder.getName()),
                ColumnVal.Create("CREATED_TIME", CurTime()),
                ColumnVal.Create("ORIGIN_KINGDOM_ID", pFounder.kingdom?.id ?? -1),
                ColumnVal.Create("ORIGIN_CITY_ID", pFounder.city?.data?.id ?? -1),
                ColumnVal.Create("IS_EXTINCT", 0));
        }

        internal static void InsertShiBranch(long pShiId, long pLineageId, string pClanName, Actor pFounder,
            string pSourceType)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return;
            db.Insert(ShiBranchTableItem.GetTableName(),
                ColumnVal.Create("SHI_ID", pShiId),
                ColumnVal.Create("LINEAGE_ID", pLineageId),
                ColumnVal.Create("CLAN_NAME", pClanName),
                ColumnVal.Create("FOUNDER_ACTOR_ID", pFounder.data.id),
                ColumnVal.Create("SOURCE_TYPE", pSourceType),
                ColumnVal.Create("ORIGIN_KINGDOM_ID", pFounder.kingdom?.id ?? -1),
                ColumnVal.Create("ORIGIN_CITY_ID", pFounder.city?.data?.id ?? -1),
                ColumnVal.Create("ORIGIN_ORIGINAL_CLAN_ID", pFounder.clan?.data?.id ?? -1),
                ColumnVal.Create("CREATED_TIME", CurTime()),
                ColumnVal.Create("IS_EXTINCT", 0));
        }

        private static void UpsertKingdomState(Kingdom pKingdom, bool pIntegrated)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return;
            string table = KingdomLineageStateTableItem.GetTableName();
            long kid = pKingdom.id;

            if (db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("KINGDOM_ID", kid)))
            {
                db.UpdateValue(table,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("KINGDOM_ID", kid) },
                    ColumnVal.Create("NAME_INTEGRATED", pIntegrated ? 1 : 0),
                    ColumnVal.Create("INTEGRATION_TIME", CurTime()));
                return;
            }

            db.Insert(table,
                ColumnVal.Create("KINGDOM_ID", kid),
                ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                ColumnVal.Create("NAME_INTEGRATED", pIntegrated ? 1 : 0),
                ColumnVal.Create("INTEGRATION_TIME", CurTime()));
        }

        internal static double CurTime()
        {
            return World.world?.getCurWorldTime() ?? 0;
        }
    }
}
