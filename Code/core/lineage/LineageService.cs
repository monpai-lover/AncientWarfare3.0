using System.Collections.Generic;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

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

        /// <summary>确保有单名 aw_given_name(取当前游戏名作单名兜底)。</summary>
        private static void EnsureGivenName(Actor pActor)
        {
            pActor.data.get(LineageKeys.GIVEN_NAME, out string given, "");
            if (!string.IsNullOrEmpty(given)) return;

            // 用游戏已生成的名字作单名素材;中文名 mod 在时这里通常已是单名/双名。
            string raw = pActor.getName();
            pActor.data.set(LineageKeys.GIVEN_NAME, string.IsNullOrEmpty(raw) ? "" : raw);
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

            // 2) 氏支:优先城市名/国名作氏,失败再随机氏
            string clanName = GenerateShiName(pActor);
            string sourceType = clanName != null ? ShiSourceType.ENFEOFFED : ShiSourceType.RANDOM;
            if (clanName == null) clanName = LineageNamePool.RandomShi();
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

        /// <summary>氏名生成:用城名首字 / 国名首字作氏(封地优先);拿不到返回 null 让调用方随机。</summary>
        private static string GenerateShiName(Actor pActor)
        {
            string cityName = pActor.city?.data?.name;
            if (!string.IsNullOrEmpty(cityName)) return FirstChar(cityName);

            string kingdomName = pActor.kingdom?.name;
            if (!string.IsNullOrEmpty(kingdomName)) return FirstChar(kingdomName);

            return null;
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

        private static void InsertLineageGroup(long pLineageId, string pFamilyName, Actor pFounder)
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

        private static void InsertShiBranch(long pShiId, long pLineageId, string pClanName, Actor pFounder,
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
