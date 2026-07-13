using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.schools;
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
        private const string HUMAN_ASSET_ID = "human";
        private const int MIN_SHI_ALIVE_FOR_NEW_BRANCH = 8;
        // 建支须"连续 4 代非嫡系(未任贵族)子孙,然后成王"——只有关系稀薄的旁支才可开新氏支。
        private const int MIN_CADET_DISTANCE_FOR_NEW_BRANCH = 4;
        private const int MIN_HOME_BRANCH_ADULT_MALES_AFTER_BRANCH = 2;
        private const int MAX_PROMOTION_DESCENDANT_SYNC = 512;

        public static bool IsXia(Actor pActor)
        {
            return pActor?.asset != null && pActor.asset.id == XIA_ASSET_ID;
        }

        public static bool IsXiaKingdom(Kingdom pKingdom)
        {
            if (pKingdom == null) return false;
            try
            {
                if (pKingdom.data == null) return false;
                if (pKingdom.data.original_actor_asset == XIA_ASSET_ID) return true;
                if (pKingdom.asset?.id == XIA_ASSET_ID) return true;
                ActorAsset actorAsset = pKingdom.getActorAsset();
                return actorAsset?.id == XIA_ASSET_ID || actorAsset?.banner_id == XIA_ASSET_ID;
            }
            catch { return false; }
        }

        public static bool IsHuman(Actor pActor)
        {
            return pActor?.asset != null && pActor.asset.id == HUMAN_ASSET_ID;
        }

        public static bool IsXiaHumanPair(Actor pA, Actor pB)
        {
            return (IsXia(pA) && IsHuman(pB)) || (IsHuman(pA) && IsXia(pB));
        }

        public static bool HasOriginalClan(Actor pActor)
        {
            return IsXia(pActor) && pActor.hasClan() && pActor.clan?.data != null;
        }

        public static bool HasTraceableFamily(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            return UsesAwLineageSystem(pActor) || (IsXia(pActor) && (lineageId >= 0 || HasOriginalClan(pActor)));
        }

        public static bool UsesAwLineageSystem(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            return ForeignPseudoLineageRules.ShouldUseAwLineageSystem(
                IsXia(pActor),
                XiaizationService.UsesXiaizedInstitutionSystem(pActor.kingdom),
                IsXiaKingdom(pActor.kingdom),
                lineageId >= 0);
        }

        private static bool CanUseXiaizedLineageGovernment(Actor pActor)
        {
            if (pActor?.data == null) return false;
            return IsXia(pActor) || IsXiaKingdom(pActor.kingdom) ||
                   XiaizationService.UsesXiaizedInstitutionSystem(pActor.kingdom);
        }

        public static void EnsureOriginalClanArchived(Actor pActor, bool pRecordHistory = true)
        {
            if (!HasOriginalClan(pActor)) return;
            ArchiveActor(pActor, pAlive: true);
            if (pRecordHistory)
                ChronicleEvents.OnJoinedOriginalClan(pActor, pActor.clan);
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
            if (!ShouldUseLineageBirth(pBaby, pParent1, pParent2)) return;

            string originalForeignName = IsXia(pBaby) ? null : pBaby.getName();
            if (IsXia(pBaby)) EnsureGivenName(pBaby);
            InheritFromParents(pBaby, pParent1, pParent2);
            if (!IsXia(pBaby)) EnsureGivenName(pBaby, originalForeignName);
            SlaveService.EnsureSlaveChild(pBaby, pParent1, pParent2);
            PropagateNobleBloodFromParents(pBaby, pParent1, pParent2);
            RecordFamilyEdges(pBaby, pParent1, pParent2);
            ApplyDisplayName(pBaby);
            ArchiveActor(pBaby, pAlive: true);

            // 编年史:仅入谱贵族(有 lineage_id)记出生事件。
            RecordBirthEvent(pBaby);
        }

        public static void OnMixedAncestryBorn(Actor pBaby, Actor pParent1, Actor pParent2)
        {
            if (pBaby?.data == null) return;
            if (!IsMixedXiaHumanFamily(pBaby, pParent1, pParent2)) return;

            ArchiveTraceableActor(pParent1, pAlive: true);
            ArchiveTraceableActor(pParent2, pAlive: true);
            RecordFamilyEdges(pBaby, pParent1, pParent2);
            ArchiveTraceableActor(pBaby, pAlive: true);
        }

        public static bool HasTraceableArchive(Actor pActor)
        {
            return pActor?.data != null && LineageArchiveReader.ReadRow(pActor.data.id) != null;
        }

        public static void ArchiveTraceableActor(Actor pActor, bool pAlive)
        {
            if (!IsXia(pActor) && !IsHuman(pActor)) return;
            LineageArchiveWriter.Upsert(pActor, pAlive, pTraceOnly: true);
        }

        private static bool IsMixedXiaHumanFamily(Actor pBaby, Actor pParent1, Actor pParent2)
        {
            bool hasXia = IsXia(pBaby) || IsXia(pParent1) || IsXia(pParent2);
            bool hasHuman = IsHuman(pBaby) || IsHuman(pParent1) || IsHuman(pParent2);
            return hasXia && hasHuman;
        }

        private static bool ShouldUseLineageBirth(Actor pBaby, Actor pParent1, Actor pParent2)
        {
            if (pBaby?.data == null) return false;
            bool parentHasLineage = HasLineageData(pParent1) || HasLineageData(pParent2) ||
                                    UsesAwLineageSystem(pParent1) || UsesAwLineageSystem(pParent2);
            return ForeignPseudoLineageRules.ShouldUseLineageBirth(
                IsXia(pBaby), pBaby.asset?.civ == true, parentHasLineage);
        }

        /// <summary>入谱贵族出生 → PersonBiography 记一条 birth 事件(无谱系者不记)。</summary>
        private static void RecordBirthEvent(Actor pActor)
        {
            if (pActor?.data == null || !UsesAwLineageSystem(pActor)) return;
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lid, -1L);
            if (lid < 0) return; // 仅入谱贵族家系

            string name = pActor.getName();
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name, "birth",
                HistoryText.Actor(pActor, name) + " 出生");
        }

        /// <summary>
        ///     确保 aw_given_name。Xia 仍取首字单名；外族使用结构化姓氏剥离后的原个人名。
        ///     已写过的数据不覆盖，避免重复晋升或出生流程堆叠姓名前缀。
        /// </summary>
        private static void EnsureGivenName(Actor pActor, string pOriginalName = null)
        {
            pActor.data.get(LineageKeys.GIVEN_NAME, out string given, "");
            if (!string.IsNullOrEmpty(given)) return;

            if (!IsXia(pActor))
            {
                pActor.data.get(LineageKeys.FAMILY_NAME, out string family, "");
                pActor.data.get(LineageKeys.CHINESE_FAMILY_NAME, out string chineseFamily, "");
                pActor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
                ForeignPseudoNameParts parts = ForeignPseudoLineageRules.ResolveNameParts(
                    pOriginalName ?? pActor.getName(), pActor.clan?.data?.name, "", family, chineseFamily,
                    clan, pActor.kingdom?.name);
                pActor.data.set(LineageKeys.GIVEN_NAME, parts.GivenName);
                return;
            }

            // Clan members use one-character given names; people without a clan retain both generated characters.
            string raw = pActor.getName();
            pActor.data.set(LineageKeys.GIVEN_NAME,
                XiaGivenNameRules.NormalizeGenerated(raw, HasXiaClanIdentity(pActor)));
        }

        private static bool HasXiaClanIdentity(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            pActor.data.get(LineageKeys.CLAN_NAME, out string clanName, "");
            if (shiId >= 0 || !string.IsNullOrEmpty(clanName)) return true;
            try { return pActor.hasClan(); }
            catch { return false; }
        }

        private static void NormalizeXiaGivenNameForClan(Actor pActor)
        {
            if (!IsXia(pActor) || pActor?.data == null) return;
            pActor.data.get(LineageKeys.GIVEN_NAME, out string given, "");
            string normalized = XiaGivenNameRules.NormalizeGenerated(given, HasXiaClanIdentity(pActor));
            if (!string.IsNullOrEmpty(normalized) && normalized != given)
                pActor.data.set(LineageKeys.GIVEN_NAME, normalized);
        }

        /// <summary>
        ///     谱系继承:**双系**——优先有谱系的父系,父系无谱系则退母系(用户要"贵族生的孩子自动变贵族",
        ///     不再严格父系丢母系血统)。继承 lineage/shi/family/clan,noble_distance=源+1;
        ///     继承后调 RefreshNobleStatus 按距离统一加/移 guizu 贵族特质。
        /// </summary>
        private static void InheritFromParents(Actor pActor, Actor pParent1, Actor pParent2)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.LINEAGE_ID, out long existingLineageId, -1);
            if (existingLineageId >= 0) return;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return;

            Actor source = PickCompletePatrilinealSource(pParent1, pParent2);
            if (source != null && TryInheritLineageFromSource(pActor, source, pRequireClan: true)) return;

            if (TryInheritLooseClanFromFather(pActor, pParent1, pParent2)) return;

            source = PickPatrilinealSource(pParent1, pParent2);
            if (source != null) TryInheritLineageFromSource(pActor, source, pRequireClan: false);
        }

        /// <summary>
        ///     选谱系继承来源(双系):优先"有谱系的父系"(男性),父系无谱系退"有谱系的母系",都无则任一有谱系方,否则 null。
        ///     旧 PickFather 因"父亲是男性就选中"会让无谱系父亲挡住有谱系母亲 → 母系贵族孩子丢血统,故改双系。
        /// </summary>
        private static Actor PickLineageSource(Actor pParent1, Actor pParent2)
        {
            return PickPatrilinealSource(pParent1, pParent2);
        }

        private static Actor PickCompleteLineageSource(Actor pParent1, Actor pParent2)
        {
            return PickCompletePatrilinealSource(pParent1, pParent2);
        }

        private static Actor PickPatrilinealSource(Actor pParent1, Actor pParent2)
        {
            Actor father = PickFather(pParent1, pParent2);
            if (father != null && HasLineageData(father)) return father;
            return null;
        }

        private static Actor PickCompletePatrilinealSource(Actor pParent1, Actor pParent2)
        {
            Actor father = PickFather(pParent1, pParent2);
            if (father != null && HasCompleteLineageData(father)) return father;
            return null;
        }

        private static Actor PickFather(Actor pParent1, Actor pParent2)
        {
            if (pParent1 != null && pParent1.isSexMale()) return pParent1;
            if (pParent2 != null && pParent2.isSexMale()) return pParent2;
            return null;
        }

        private static bool HasLineageData(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lid, -1);
            return lid >= 0;
        }

        private static bool HasCompleteLineageData(Actor pActor)
        {
            if (!HasLineageData(pActor)) return false;
            pActor.data.get(LineageKeys.SHI_ID, out long sid, -1);
            pActor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            return sid >= 0 && !string.IsNullOrEmpty(clan);
        }

        private static bool TryInheritLineageFromSource(Actor pChild, Actor pSource, bool pRequireClan)
        {
            if (pChild?.data == null || pSource?.data == null) return false;
            pSource.data.get(LineageKeys.LINEAGE_ID, out long lid, -1);
            if (lid < 0) return false;

            pSource.data.get(LineageKeys.SHI_ID, out long sid, -1);
            pSource.data.get(LineageKeys.FAMILY_NAME, out string fam, "");
            pSource.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            pSource.data.get(LineageKeys.NOBLE_DISTANCE, out int dist, 99);

            if (pRequireClan && (sid < 0 || string.IsNullOrEmpty(clan))) return false;

            pChild.data.set(LineageKeys.LINEAGE_ID, lid);
            if (sid >= 0) pChild.data.set(LineageKeys.SHI_ID, sid);
            if (!string.IsNullOrEmpty(fam))
            {
                pChild.data.set(LineageKeys.FAMILY_NAME, fam);
                pChild.data.set(LineageKeys.CHINESE_FAMILY_NAME, fam);
            }
            if (!string.IsNullOrEmpty(clan)) pChild.data.set(LineageKeys.CLAN_NAME, clan);
            if (pSource.clan?.data != null && pChild.clan != pSource.clan)
                pChild.setClan(pSource.clan);
            pChild.data.set(LineageKeys.NOBLE_DISTANCE, dist + 1);
            pChild.data.set(LineageKeys.LINEAGE_STATUS,
                dist + 1 >= LineageKeys.NOBLE_DECAY_DISTANCE ? LineageStatus.COMMON : LineageStatus.NOBLE);
            PropagateNobleBloodFromSource(pChild, pSource);

            // 按 noble_distance 统一加/移 guizu 贵族特质(继承的贵族子代也享生育加成,与晋升路径对齐)。
            RefreshNobleStatus(pChild);
            return true;
        }

        private static bool TryInheritLooseClanFromParents(Actor pChild, Actor pParent1, Actor pParent2)
        {
            return TryInheritLooseClanFromFather(pChild, pParent1, pParent2);
        }

        private static bool TryInheritLooseClanFromFather(Actor pChild, Actor pParent1, Actor pParent2)
        {
            Actor source = PickLooseClanFather(pParent1, pParent2);
            if (source?.data == null || pChild?.data == null) return false;

            source.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            if (string.IsNullOrEmpty(clan)) return false;
            source.data.get(LineageKeys.FAMILY_NAME, out string fam, "");
            source.data.get(LineageKeys.LINEAGE_ID, out long lid, -1);
            source.data.get(LineageKeys.SHI_ID, out long sid, -1);
            source.data.get(LineageKeys.NOBLE_DISTANCE, out int dist, 99);

            if (lid >= 0) pChild.data.set(LineageKeys.LINEAGE_ID, lid);
            if (sid >= 0) pChild.data.set(LineageKeys.SHI_ID, sid);
            if (!string.IsNullOrEmpty(fam))
            {
                pChild.data.set(LineageKeys.FAMILY_NAME, fam);
                pChild.data.set(LineageKeys.CHINESE_FAMILY_NAME, fam);
            }
            pChild.data.set(LineageKeys.CLAN_NAME, clan);
            if (source.clan?.data != null && pChild.clan != source.clan)
                pChild.setClan(source.clan);
            if (lid >= 0)
            {
                pChild.data.set(LineageKeys.NOBLE_DISTANCE, dist + 1);
                pChild.data.set(LineageKeys.LINEAGE_STATUS,
                    dist + 1 >= LineageKeys.NOBLE_DECAY_DISTANCE ? LineageStatus.COMMON : LineageStatus.NOBLE);
                PropagateNobleBloodFromSource(pChild, source);
                RefreshNobleStatus(pChild);
            }
            source.data.get(LineageKeys.NAME_INTEGRATED, out bool integrated, false);
            if (integrated) pChild.data.set(LineageKeys.NAME_INTEGRATED, true);
            return true;
        }

        private static Actor PickLooseClanSource(Actor pParent1, Actor pParent2)
        {
            return PickLooseClanFather(pParent1, pParent2);
        }

        private static Actor PickLooseClanFather(Actor pParent1, Actor pParent2)
        {
            Actor father = PickFather(pParent1, pParent2);
            if (father != null && HasClanName(father)) return father;
            return null;
        }

        private static bool HasClanName(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            return !string.IsNullOrEmpty(clan);
        }

        private static void PropagateNobleBloodFromParents(Actor pChild, Actor pParent1, Actor pParent2)
        {
            if (pChild?.data == null) return;

            bool hasBest = false;
            long bestOriginId = -1L;
            string bestOriginName = "";
            int bestDistance = 99;

            if (TryReadNobleBloodSource(pParent1, out long id1, out string name1, out int distance1))
            {
                hasBest = true;
                bestOriginId = id1;
                bestOriginName = name1;
                bestDistance = distance1 + 1;
            }

            if (TryReadNobleBloodSource(pParent2, out long id2, out string name2, out int distance2))
            {
                int childDistance = distance2 + 1;
                if (!hasBest || childDistance < bestDistance)
                {
                    hasBest = true;
                    bestOriginId = id2;
                    bestOriginName = name2;
                    bestDistance = childDistance;
                }
            }

            if (hasBest)
                SetNobleBloodSnapshot(pChild, bestOriginId, bestOriginName, bestDistance);
        }

        private static void PropagateNobleBloodFromSource(Actor pChild, Actor pSource)
        {
            if (pChild?.data == null) return;
            if (TryReadNobleBloodSource(pSource, out long originId, out string originName, out int distance))
                SetNobleBloodSnapshot(pChild, originId, originName, distance + 1);
        }

        private static bool TryReadNobleBloodSource(Actor pSource, out long pOriginId,
            out string pOriginName, out int pDistance)
        {
            pOriginId = -1L;
            pOriginName = "";
            pDistance = 99;
            if (pSource?.data == null) return false;

            pSource.data.get(LineageKeys.EVER_NOBLE_BLOOD, out bool ever, false);
            if (ever)
            {
                pSource.data.get(LineageKeys.NOBLE_ORIGIN_ACTOR_ID, out pOriginId, -1L);
                pSource.data.get(LineageKeys.NOBLE_ORIGIN_NAME, out pOriginName, "");
                pSource.data.get(LineageKeys.NOBLE_ORIGIN_DISTANCE, out pDistance, 99);
                return pOriginId >= 0 || !string.IsNullOrEmpty(pOriginName);
            }

            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pSource.data.id);
            if (row != null && row.ever_noble_blood != 0)
            {
                pOriginId = row.noble_origin_actor_id;
                pOriginName = row.noble_origin_name ?? "";
                pDistance = row.noble_origin_distance;
                return pOriginId >= 0 || !string.IsNullOrEmpty(pOriginName);
            }

            pSource.data.get(LineageKeys.NOBLE_DISTANCE, out int nobleDistance, 99);
            pSource.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
            if (nobleDistance == 0 && status == LineageStatus.NOBLE)
            {
                pOriginId = pSource.data.id;
                pOriginName = pSource.getName() ?? "";
                pDistance = 0;
                return true;
            }

            return false;
        }

        private static void SetNobleBloodSnapshot(Actor pActor, long pOriginId, string pOriginName, int pDistance)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.EVER_NOBLE_BLOOD, true);
            pActor.data.set(LineageKeys.NOBLE_ORIGIN_ACTOR_ID, pOriginId);
            pActor.data.set(LineageKeys.NOBLE_ORIGIN_NAME, pOriginName ?? "");
            pActor.data.set(LineageKeys.NOBLE_ORIGIN_DISTANCE, pDistance);
        }

        /// <summary>把 parent_id_1/2 写入 FamilyEdge 持久亲子边表(死后家族树仍可绘制)。</summary>
        private static void RecordFamilyEdges(Actor pActor, Actor pParent1 = null, Actor pParent2 = null)
        {
            if (pActor?.data == null) return;
            long childId = pActor.data.id;
            pActor.data.get(LineageKeys.LINEAGE_ID, out long childLineage, -1);

            long explicitParent1 = pParent1?.data?.id ?? -1L;
            long explicitParent2 = pParent2?.data?.id ?? -1L;
            var parents = FamilyTreeRelationRules.MergeParentSlots(
                pActor.data.parent_id_1, pActor.data.parent_id_2,
                explicitParent1, explicitParent2);
            pActor.data.parent_id_1 = parents.slot1;
            pActor.data.parent_id_2 = parents.slot2;

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
        public static void OnCityLeaderAppointed(Actor pActor, string pOfficeId = CourtOfficeId.Governor)
        {
            if (!IsXia(pActor) && !UsesAwLineageSystem(pActor))
            {
                if (CanUseXiaizedLineageGovernment(pActor))
                    OnActorPromoted(pActor, NobleTrigger.Official, pOfficeId);
                return;
            }

            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1);
            if (lineageId < 0)
            {
                OnActorPromoted(pActor, NobleTrigger.Official, pOfficeId);
                return;
            }

            // 已有谱系:尝试分封(内部会判长子/继承人则不分)。分封同时刷新贵族身份。
            OnNobleChildFounding(pActor);
        }

        /// <summary>成为国王/城主/成名者时赋予或刷新贵族身份。由晋升 Hook 调用。</summary>
        public static void OnActorPromoted(Actor pActor, NobleTrigger pTrigger, string pOfficeId = null)
        {
            if (HistoricalSchoolDescentService.IsCanonicalMaster(pActor)) return;
            if (!CanUseXiaizedLineageGovernment(pActor)) return;
            if (SlaveService.IsSlave(pActor))
                SlaveService.FreeSlave(pActor, "promoted");

            if (IsXia(pActor))
                EnsureLineageForNoble(pActor, pTrigger, pOfficeId);
            else
                EnsureForeignPseudoOfficialLineage(pActor, pTrigger, pOfficeId: pOfficeId);

            // 本人即贵族:距离归零、加 guizu、状态 noble。
            pActor.data.set(LineageKeys.NOBLE_DISTANCE, 0);
            pActor.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.NOBLE);
            if (!pActor.hasTrait(LineageKeys.TRAIT_GUIZU)) pActor.addTrait(LineageKeys.TRAIT_GUIZU);

            ApplyDisplayName(pActor);
            ArchiveActor(pActor, pAlive: true);
            SyncExistingChildrenAfterLineageChange(pActor);
        }

        /// <summary>无谱系贵族:随机古姓建姓族,按封地/城/国生成氏建氏支;已有谱系则沿用。</summary>
        public static void EnsureLineageForNoble(Actor pActor, NobleTrigger pTrigger,
            string pOfficeId = null)
        {
            pActor.data.get(LineageKeys.LINEAGE_ID, out long existing, -1);
            pActor.data.get(LineageKeys.SHI_ID, out long existingShi, -1L);
            if (existing >= 0)
            {
                if (pTrigger == NobleTrigger.Official &&
                    OfficialShiRules.ShouldGrantOfficialShi(existingShi >= 0))
                    GrantOfficialShiBranch(pActor, existing, pOfficeId);
                return;
            }

            // 1) 姓族:随机古姓
            string familyName = LineageNamePool.RandomSurname();
            long lineageId = LineageIdAllocator.NextLineageId();
            InsertLineageGroup(lineageId, familyName, pActor);

            // 2) 氏支:合流前 50% 随机氏 / 50% 城名首字(见 GenerateShiName)
            (string clanName, string sourceType) = pTrigger == NobleTrigger.Official
                ? GenerateOfficialShiName(pActor, pOfficeId)
                : GenerateShiName(pActor);
            if (pTrigger == NobleTrigger.Figure) sourceType = ShiSourceType.SPECIAL_FIGURE;
            if (pTrigger == NobleTrigger.Official) sourceType = ShiSourceType.OFFICIAL_GRANT;

            long shiId = LineageIdAllocator.NextShiId();
            InsertShiBranch(shiId, lineageId, clanName, pActor, sourceType);

            // 3) 回写 actor.data
            pActor.data.set(LineageKeys.LINEAGE_ID, lineageId);
            pActor.data.set(LineageKeys.SHI_ID, shiId);
            pActor.data.set(LineageKeys.FAMILY_NAME, familyName);
            pActor.data.set(LineageKeys.CLAN_NAME, clanName);
            pActor.data.set(LineageKeys.CHINESE_FAMILY_NAME, familyName);
        }

        public static void EnsureOfficialShiAndClan(Actor pActor, string pOfficeId)
        {
            if (HistoricalSchoolDescentService.IsCanonicalMaster(pActor)) return;
            if (pActor?.data == null || pActor.isRekt() || !CanUseXiaizedLineageGovernment(pActor)) return;
            OnActorPromoted(pActor, NobleTrigger.Official, pOfficeId);

            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            if (shiId < 0) return;
            Actor father = FindFatherOfChild(pActor);
            bool parentSameShi = false;
            if (father?.data != null)
            {
                father.data.get(LineageKeys.SHI_ID, out long fatherShi, -1L);
                parentSameShi = fatherShi == shiId;
            }

            if (OfficialShiRules.ShouldReuseParentVisibleClan(
                    hasValidShi: true, parentSameShi, father?.clan?.data != null))
            {
                if (pActor.clan != father.clan) pActor.setClan(father.clan);
            }
            else if (pActor.clan?.data == null)
            {
                try { World.world?.clans?.newClan(pActor, pAddDefaultTraits: true); }
                catch { }
            }

            RenameClanByLeader(pActor.clan, pActor);
            SyncExistingChildrenAfterLineageChange(pActor);
            ArchiveActor(pActor, pAlive: true);
        }

        public static void EnsureForeignPseudoDynastyLineage(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || IsXiaKingdom(pKingdom)) return;
            if (!XiaizationService.UsesXiaizedInstitutionSystem(pKingdom)) return;

            var seen = new HashSet<long>();
            EnsureForeignPseudoOfficialLineage(pKingdom.king, NobleTrigger.King, seen);

            foreach (City city in pKingdom.getCities())
                EnsureForeignPseudoOfficialLineage(city?.leader, NobleTrigger.CityLeader, seen);

            foreach (Actor actor in pKingdom.getUnits())
            {
                if (actor?.data == null || actor.isRekt()) continue;
                bool armyLeader = false;
                try { armyLeader = actor.is_army_captain; } catch { }
                if (!ForeignPseudoLineageRules.ShouldIntegrateOfficial(
                        actor.isKing(), actor.isCityLeader(), armyLeader))
                    continue;
                EnsureForeignPseudoOfficialLineage(actor,
                    actor.isKing() ? NobleTrigger.King : NobleTrigger.CityLeader,
                    seen);
            }
        }

        private static void EnsureForeignPseudoOfficialLineage(Actor pActor, NobleTrigger pTrigger,
            HashSet<long> pSeen = null, string pOfficeId = null)
        {
            if (pActor?.data == null || pActor.isRekt()) return;
            if (IsXia(pActor)) return;
            if (!CanUseXiaizedLineageGovernment(pActor)) return;
            if (pSeen != null && !pSeen.Add(pActor.data.id)) return;

            string rawName = pActor.getName() ?? "";
            pActor.data.get(LineageKeys.GIVEN_NAME, out string existingGiven, "");
            pActor.data.get(LineageKeys.FAMILY_NAME, out string existingFamily, "");
            pActor.data.get(LineageKeys.CHINESE_FAMILY_NAME, out string chineseFamily, "");
            pActor.data.get(LineageKeys.CLAN_NAME, out string existingClan, "");
            ForeignPseudoNameParts parts = ForeignPseudoLineageRules.ResolveNameParts(
                rawName, pActor.clan?.data?.name, existingGiven, existingFamily,
                chineseFamily, existingClan, pActor.kingdom?.name);

            pActor.data.get(LineageKeys.LINEAGE_ID, out long existingLineageId, -1L);
            pActor.data.get(LineageKeys.SHI_ID, out long existingShiId, -1L);
            if (existingLineageId >= 0 && existingShiId < 0 && pTrigger == NobleTrigger.Official)
            {
                GrantOfficialShiBranch(pActor, existingLineageId, pOfficeId);
                pActor.data.get(LineageKeys.SHI_ID, out existingShiId, -1L);
                pActor.data.get(LineageKeys.CLAN_NAME, out string grantedClan, "");
                if (existingShiId < 0) return;
                parts = new ForeignPseudoNameParts(parts.GivenName, parts.FamilyName, grantedClan);
            }
            if (existingLineageId < 0 || existingShiId < 0)
            {
                Actor father = FindFatherOfChild(pActor);
                bool inherited = father?.data != null && TryInheritLineageFromSource(pActor, father, pRequireClan: true);
                if (!inherited)
                {
                    long lineageId = LineageIdAllocator.NextLineageId();
                    long shiId = LineageIdAllocator.NextShiId();
                    if (lineageId < 0 || shiId < 0) return;

                    InsertLineageGroup(lineageId, parts.FamilyName, pActor);
                    if (pTrigger == NobleTrigger.Official)
                    {
                        string officialShi = GenerateOfficialShiName(pActor, pOfficeId).clanName;
                        parts = new ForeignPseudoNameParts(parts.GivenName, parts.FamilyName, officialShi);
                    }
                    string source = pTrigger == NobleTrigger.Official
                        ? ShiSourceType.OFFICIAL_GRANT
                        : "pseudo_foreign";
                    InsertShiBranch(shiId, lineageId, parts.ClanName, pActor, source);
                    pActor.data.set(LineageKeys.LINEAGE_ID, lineageId);
                    pActor.data.set(LineageKeys.SHI_ID, shiId);
                }
                else
                {
                    pActor.data.get(LineageKeys.FAMILY_NAME, out existingFamily, "");
                    pActor.data.get(LineageKeys.CHINESE_FAMILY_NAME, out chineseFamily, "");
                    pActor.data.get(LineageKeys.CLAN_NAME, out existingClan, "");
                    parts = ForeignPseudoLineageRules.ResolveNameParts(
                        rawName, pActor.clan?.data?.name, existingGiven, existingFamily,
                        chineseFamily, existingClan, pActor.kingdom?.name);
                }
            }

            if (string.IsNullOrEmpty(existingGiven))
                pActor.data.set(LineageKeys.GIVEN_NAME, parts.GivenName);
            pActor.data.get(LineageKeys.FAMILY_NAME, out existingFamily, "");
            if (string.IsNullOrEmpty(existingFamily))
                pActor.data.set(LineageKeys.FAMILY_NAME, parts.FamilyName);
            pActor.data.get(LineageKeys.CHINESE_FAMILY_NAME, out chineseFamily, "");
            if (string.IsNullOrEmpty(chineseFamily))
                pActor.data.set(LineageKeys.CHINESE_FAMILY_NAME, parts.FamilyName);
            pActor.data.get(LineageKeys.CLAN_NAME, out existingClan, "");
            if (string.IsNullOrEmpty(existingClan))
                pActor.data.set(LineageKeys.CLAN_NAME, parts.ClanName);
            pActor.data.set(LineageKeys.NAME_INTEGRATED, true);
            pActor.data.set(LineageKeys.NOBLE_DISTANCE, 0);
            pActor.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.NOBLE);
            if (!pActor.hasTrait(LineageKeys.TRAIT_GUIZU)) pActor.addTrait(LineageKeys.TRAIT_GUIZU);

            ApplyDisplayName(pActor);
            RenameClanByLeader(pActor.clan, pActor);
            ArchiveActor(pActor, pAlive: true);
            try { pActor.clearGraphicsFully(); } catch { }
        }

        /// <summary>
        ///     氏支分封:符合分封条件的子嗣去新 city 当 leader 时,从父姓族**分出新氏支**
        ///     (同姓不同氏,source=enfeoffed)。分封条件见 IsEnfeoffmentCandidate(严格:仅 king 子辈、
        ///     本宗有冗余、非长子)。不符合者留原氏,但当了城主仍刷新贵族身份。
        ///     由 AW_PromotionPatch.SetLeader_Postfix 在"已有谱系者再当 leader"时调用。
        /// </summary>
        public static void OnNobleChildFounding(Actor pChild)
        {
            if (!IsXia(pChild) && !UsesAwLineageSystem(pChild)) return;

            pChild.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1);
            if (lineageId < 0) return;
            pChild.data.get(LineageKeys.SHI_ID, out long previousShiId, -1);

            if (IsEnfeoffmentCandidate(pChild))
            {
                // 从父姓族分出新氏支(同姓不同氏)
                (string clanName, string sourceType) = GenerateShiName(pChild);
                long shiId = LineageIdAllocator.NextShiId();
                InsertShiBranch(shiId, lineageId, clanName, pChild, sourceType);

                pChild.data.set(LineageKeys.SHI_ID, shiId);
                pChild.data.set(LineageKeys.CLAN_NAME, clanName);
                MoveExistingDescendantsToBranch(pChild, lineageId, previousShiId, shiId, clanName);
            }

            // 无论是否分封,城主本人都是当代贵族:距离归零、加 guizu。
            pChild.data.set(LineageKeys.NOBLE_DISTANCE, 0);
            pChild.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.NOBLE);
            if (!pChild.hasTrait(LineageKeys.TRAIT_GUIZU)) pChild.addTrait(LineageKeys.TRAIT_GUIZU);

            ApplyDisplayName(pChild);
            ArchiveActor(pChild, pAlive: true);
            SyncExistingChildrenAfterLineageChange(pChild);
        }

        /// <summary>
        ///     称王分封:某人称王且**建新国/夺别国**(新王所在国 ≠ 其当前氏支的 origin_kingdom_id)时,
        ///     从原氏支 + 原版 clan 脱离,**开一个新氏支**(KING_FOUNDED)成为新支始祖;子嗣此后继承新 SHI_ID 只进新支。
        ///     原氏族树保留他的位置 + 标记 FOUNDED_BRANCH_SHI_ID(供"建立分支X氏"提示 + 点击跳转新支)。
        ///     **本国内继位不触发**(新王国 == origin_kingdom_id,大宗本国传承)。
        ///     由 setKing Postfix 调用(AW_HeirPatch / 新 patch)。
        /// </summary>
        public static void OnKingFoundBranch(Kingdom pKingdom, Actor pKing)
        {
            OnKingFoundBranch(pKingdom, pKing, null, false, -1);
        }

        public static void OnKingFoundBranch(Kingdom pKingdom, Actor pKing, Actor pPreviousKing,
            bool pWasRegisteredHeir)
        {
            OnKingFoundBranch(pKingdom, pKing, pPreviousKing, pWasRegisteredHeir, -1);
        }

        public static void OnKingFoundBranch(Kingdom pKingdom, Actor pKing, Actor pPreviousKing,
            bool pWasRegisteredHeir, int pPreNobleDistance)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            // 共和推举的平民首领不建氏支(非世袭君主,不开创宗族分支)。
            if (RepublicGovernmentService.IsRepublicLeader(pKing) || RepublicGovernmentService.IsRepublic(pKingdom))
                return;
            if (!IsXia(pKing) && !UsesAwLineageSystem(pKing)) return;
            pKing.data.get(LineageKeys.IS_HEIR, out bool wasHeir, false);
            bool registeredHeir = wasHeir || pWasRegisteredHeir;
            bool currentHeir = HeirService.IsCurrentHeir(pKingdom, pKing);
            long previousKingId = pPreviousKing?.data?.id ?? -1L;
            pKingdom.data.get(LineageKeys.KINGDOM_PRE_SUCCESSION_KING_ID, out long recordedPreviousKingId, -1L);
            bool directSuccession = IsDirectSuccessionFromPreviousKing(pPreviousKing, pKing) ||
                                    LineageBranchRules.IsDirectSuccessionFromKnownKing(
                                        pKing.data.parent_id_1,
                                        pKing.data.parent_id_2,
                                        previousKingId,
                                        recordedPreviousKingId);
            pKing.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1);
            pKing.data.get(LineageKeys.SHI_ID, out long curShiId, -1);
            pKingdom.data.get(LineageKeys.KINGDOM_SUCCESSION_MODE, out string successionMode, SuccessionMode.NONE);
            if (LineageBranchRules.ShouldApplyCollateralRestoration(
                    successionMode,
                    registeredHeir,
                    currentHeir,
                    directSuccession))
            {
                ApplyCollateralRestoration(pKingdom, pKing, pPreviousKing);
                return;
            }

            long originKingdom = curShiId < 0 ? -1L : LineageQuery.GetShiOriginKingdom(curShiId);
            pKing.data.get(LineageKeys.FOUNDED_BRANCH_SHI_ID, out long foundedShi, -1);
            bool alreadyFoundedForKingdom = foundedShi >= 0 && LineageQuery.GetShiOriginKingdom(foundedShi) == pKingdom.id;
            // 成王前的"非嫡系代际距离"(距最近贵族祖先的代数)。setKing 前捕获;未知则回退现读(已归零→保守不建支)。
            int cadetDistance = pPreNobleDistance;
            if (cadetDistance < 0)
                pKing.data.get(LineageKeys.NOBLE_DISTANCE, out cadetDistance, 0);
            bool shouldFound = LineageBranchRules.ShouldFoundKingBranch(
                validKingdom: pKingdom.data != null,
                isXiaKing: IsXia(pKing) || UsesAwLineageSystem(pKing),
                wasHeir: registeredHeir,
                isCurrentHeir: currentHeir,
                isDirectSuccessionFromPreviousKing: directSuccession,
                isCollateralRestoration: successionMode == SuccessionMode.COLLATERAL_RESTORE,
                hasLineage: lineageId >= 0,
                hasShi: curShiId >= 0,
                isHistoricalFigure: IsHistoricalFigure(pKing),
                isLineageRootFounder: IsLineageRootFounder(pKing, lineageId),
                aliveInCurrentShi: curShiId < 0 ? 0 : LineageQuery.CountAliveInShi(curShiId),
                minAliveForNewBranch: MIN_SHI_ALIVE_FOR_NEW_BRANCH,
                currentKingdomId: pKingdom.id,
                originKingdomId: originKingdom,
                alreadyFoundedForKingdom: alreadyFoundedForKingdom,
                cadetGenerationDistance: cadetDistance,
                minCadetDistanceForBranch: MIN_CADET_DISTANCE_FOR_NEW_BRANCH);
            if (!shouldFound) return;

            // 1) 原版 clan:从旧 clan 脱离,新建以国王为创始人的 clan。
            try { World.world.clans.newClan(pKing, pAddDefaultTraits: true); } catch { /* clan 新建失败不致命 */ }

            // 2) 开新氏支(同姓族 lineageId,新氏名按国/城生成)。
            (string clanName, _) = GenerateShiName(pKing);
            long newShiId = LineageIdAllocator.NextShiId();
            InsertShiBranch(newShiId, lineageId, clanName, pKing, ShiSourceType.KING_FOUNDED);

            // 3) 国王改挂新氏支,成为新支始祖(距离归零、贵族、改氏名)。
            pKing.data.set(LineageKeys.SHI_ID, newShiId);
            pKing.data.set(LineageKeys.CLAN_NAME, clanName);
            pKing.data.set(LineageKeys.NOBLE_DISTANCE, 0);
            pKing.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.NOBLE);
            if (!pKing.hasTrait(LineageKeys.TRAIT_GUIZU)) pKing.addTrait(LineageKeys.TRAIT_GUIZU);

            // 4) 在国王 data 上标记"建立的新支 id"(原氏族树在他位置显示"建立分支X氏"+点击跳转用)。
            pKing.data.set(LineageKeys.FOUNDED_BRANCH_SHI_ID, newShiId);

            // 5) 重命名新建的原版 clan(此时 CLAN_NAME 已就绪,王族命名=国名+氏+"氏")。
            //    newClan Postfix 时氏未就绪已跳过,这里补上。
            RenameClanByLeader(pKing.clan, pKing);

            ApplyDisplayName(pKing);          // 氏变 → 重拼显示名
            ArchiveActor(pKing, pAlive: true);
            pKing.clearGraphicsFully();
            int movedDescendants = MoveExistingDescendantsToBranch(pKing, lineageId, curShiId, newShiId, clanName);
            SyncExistingChildrenAfterLineageChange(pKing);
            if (movedDescendants > 0)
                ModClass.LogInfo($"Moved {movedDescendants} existing descendants to shi={newShiId}.");

            ModClass.LogInfo($"称王分封:{pKing.getName()} 在 {pKingdom.name}(id={pKingdom.id})建立新氏支「{clanName}」(shi={newShiId},脱离旧支 {curShiId})。");
        }

        private static void ApplyCollateralRestoration(Kingdom pKingdom, Actor pKing, Actor pPreviousKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID, out long legitimateLineage, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID, out long legitimateShi, -1L);
            if (legitimateLineage < 0 || legitimateShi < 0) return;

            // 男系(同姓父系)才完整恢复本姓王统;否则以异姓入继处理,不伪造父系/姓氏。
            bool agnatic = LineageQuery.IsAgnaticDescendant(pKing.data.id, legitimateLineage);
            if (!agnatic ||
                !CollateralRestorationTraceService.CanRestoreToLegitimateShi(pKing, legitimateLineage, legitimateShi))
            {
                pKing.data.set(LineageKeys.COLLATERAL_NONAGNATIC, true);
                pKingdom.data.set(LineageKeys.KINGDOM_SUCCESSION_MODE, SuccessionMode.NONE);
                ChronicleEvents.OnNonAgnaticSuccession(pKingdom, pPreviousKing, pKing);
                return;
            }

            ShiBranchInfo branch = LineageQuery.GetShiBranchInfo(legitimateShi);
            string clanName = branch?.clan_name ?? "";
            if (string.IsNullOrEmpty(clanName))
                pKing.data.get(LineageKeys.CLAN_NAME, out clanName, "");

            pKing.data.set(LineageKeys.LINEAGE_ID, legitimateLineage);
            pKing.data.set(LineageKeys.SHI_ID, legitimateShi);
            if (!string.IsNullOrEmpty(clanName)) pKing.data.set(LineageKeys.CLAN_NAME, clanName);
            pKing.data.set(LineageKeys.NOBLE_DISTANCE, 0);
            pKing.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.NOBLE);
            pKing.data.set(LineageKeys.FOUNDED_BRANCH_SHI_ID, -1L);
            pKing.data.set(LineageKeys.RESTORED_SHI_ID, legitimateShi);
            pKing.data.set(LineageKeys.COLLATERAL_NONAGNATIC, false);
            pKingdom.data.set(LineageKeys.KINGDOM_RESTORED_SHI_ID, legitimateShi);
            if (!pKing.hasTrait(LineageKeys.TRAIT_GUIZU)) pKing.addTrait(LineageKeys.TRAIT_GUIZU);

            ApplyDisplayName(pKing);
            ArchiveActor(pKing, pAlive: true);
            try { pKing.clearGraphicsFully(); } catch { }
            SyncExistingChildrenAfterLineageChange(pKing);
            ChronicleEvents.OnCollateralRestoration(pKingdom, pPreviousKing, pKing, branch);
        }

        private static bool IsDirectSuccessionFromPreviousKing(Actor pPreviousKing, Actor pNewKing)
        {
            if (pPreviousKing?.data == null || pNewKing?.data == null) return false;
            if (pPreviousKing.data.id == pNewKing.data.id) return true;
            long previousId = pPreviousKing.data.id;
            return pNewKing.data.parent_id_1 == previousId || pNewKing.data.parent_id_2 == previousId;
        }

        internal static void SyncExistingChildrenAfterLineageChange(Actor pParent)
        {
            if (pParent?.data == null || (!IsXia(pParent) && !UsesAwLineageSystem(pParent))) return;
            if (!HasLineageData(pParent)) return;

            var childIds = new HashSet<long>(LineageQuery.GetChildIds(pParent.data.id));
            try
            {
                foreach (var child in pParent.getChildren(pOnlyCurrentFamily: false))
                    if (child?.data != null) childIds.Add(child.data.id);
            }
            catch { }

            int synced = 0;
            var visited = new HashSet<long>();
            foreach (long childId in childIds)
            {
                if (synced >= MAX_PROMOTION_DESCENDANT_SYNC) break;
                SyncDescendantFromParentRecursive(pParent, childId, visited, ref synced);
            }
        }

        private static void SyncDescendantFromParentRecursive(Actor pParent, long pChildId, HashSet<long> pVisited,
            ref int pSynced)
        {
            if (pParent?.data == null || pChildId < 0 || !pVisited.Add(pChildId)) return;
            if (pSynced >= MAX_PROMOTION_DESCENDANT_SYNC) return;

            Actor child = World.world?.units?.get(pChildId);
            if (child?.data != null && (IsXia(child) || IsHuman(child) || UsesAwLineageSystem(child)) && !child.isRekt())
            {
                if (!ShouldChildFollowParentLine(child, pParent)) return;

                bool sameLine = IsSameLineage(child, pParent);
                bool changed = TrySyncLiveChildFromParent(child, pParent);
                if (changed) pSynced++;

                if (changed || sameLine || IsSameLineage(child, pParent))
                {
                    foreach (long grandChildId in LineageQuery.GetChildIds(child.data.id))
                    {
                        if (pSynced >= MAX_PROMOTION_DESCENDANT_SYNC) break;
                        SyncDescendantFromParentRecursive(child, grandChildId, pVisited, ref pSynced);
                    }
                }
                return;
            }

            var row = LineageArchiveReader.ReadRow(pChildId);
            if (row == null) return;
            if (!ShouldArchivedChildFollowParentLine(row, pParent)) return;
            if (TrySyncArchivedChildFromParent(row, pParent)) pSynced++;
        }

        private static bool ShouldChildFollowParentLine(Actor pChild, Actor pParent)
        {
            if (pChild?.data == null || pParent?.data == null) return false;
            long parentId = pParent.data.id;
            bool listedParent = pChild.data.parent_id_1 == parentId || pChild.data.parent_id_2 == parentId;
            if (!listedParent) return false;
            if (pParent.isSexMale()) return true;

            Actor father = FindFatherOfChild(pChild);
            if (father?.data == null) return false;
            father.data.get(LineageKeys.MATRILOCAL_IN_LAW, out bool inLaw, false);
            father.data.get(LineageKeys.MATRILOCAL_WIFE_ID, out long wifeId, -1L);
            return inLaw && wifeId == parentId;
        }

        private static bool ShouldArchivedChildFollowParentLine(ActorArchiveTableItem pChild, Actor pParent)
        {
            if (pChild == null || pParent?.data == null) return false;
            long parentId = pParent.data.id;
            bool listedParent = pChild.parent_id_1 == parentId || pChild.parent_id_2 == parentId;
            return listedParent && pParent.isSexMale();
        }

        private static Actor FindFatherOfChild(Actor pChild)
        {
            if (pChild?.data == null) return null;
            foreach (long pid in new[] { pChild.data.parent_id_1, pChild.data.parent_id_2 })
            {
                if (pid < 0) continue;
                Actor parent = World.world?.units?.get(pid);
                if (parent != null && parent.isSexMale()) return parent;
            }
            return null;
        }

        private static bool TrySyncLiveChildFromParent(Actor pChild, Actor pParent)
        {
            if (pChild?.data == null || pParent?.data == null) return false;
            if (pChild.hasTrait("figure") || pChild.hasTrait("first")) return false;

            pParent.data.get(LineageKeys.LINEAGE_ID, out long parentLineage, -1L);
            pParent.data.get(LineageKeys.SHI_ID, out long parentShi, -1L);
            if (parentLineage < 0 || parentShi < 0) return false;

            pChild.data.get(LineageKeys.LINEAGE_ID, out long childLineage, -1L);
            if (childLineage >= 0 && childLineage != parentLineage) return false;

            pParent.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            pParent.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            pParent.data.get(LineageKeys.NOBLE_DISTANCE, out int parentDist, 0);

            pChild.data.get(LineageKeys.SHI_ID, out long oldShi, -1L);
            pChild.data.get(LineageKeys.CLAN_NAME, out string oldClan, "");
            if (!OfficialShiRules.ShouldSyncDescendant(
                    parentLineage, parentShi, childLineage, oldShi)) return false;
            bool visibleClanChanged = pParent.clan?.data != null && pChild.clan != pParent.clan;
            bool changed = childLineage != parentLineage || oldShi != parentShi ||
                           oldClan != clan || visibleClanChanged;

            string originalForeignName = IsXia(pChild) ? null : pChild.getName();
            pChild.data.set(LineageKeys.LINEAGE_ID, parentLineage);
            pChild.data.set(LineageKeys.SHI_ID, parentShi);
            if (!string.IsNullOrEmpty(family))
            {
                pChild.data.set(LineageKeys.FAMILY_NAME, family);
                pChild.data.set(LineageKeys.CHINESE_FAMILY_NAME, family);
            }
            if (!string.IsNullOrEmpty(clan)) pChild.data.set(LineageKeys.CLAN_NAME, clan);
            if (visibleClanChanged) pChild.setClan(pParent.clan);
            EnsureGivenName(pChild, originalForeignName);
            pChild.data.set(LineageKeys.NOBLE_DISTANCE, parentDist + 1);
            pChild.data.set(LineageKeys.LINEAGE_STATUS,
                parentDist + 1 >= LineageKeys.NOBLE_DECAY_DISTANCE ? LineageStatus.COMMON : LineageStatus.NOBLE);
            PropagateNobleBloodFromSource(pChild, pParent);
            RefreshNobleStatus(pChild);
            ApplyDisplayName(pChild);
            RecordFamilyEdges(pChild);
            ArchiveActor(pChild, pAlive: true);
            try { pChild.clearGraphicsFully(); } catch { }
            return changed;
        }

        private static bool TrySyncArchivedChildFromParent(ActorArchiveTableItem pChild, Actor pParent)
        {
            if (pChild == null || pParent?.data == null) return false;
            pParent.data.get(LineageKeys.LINEAGE_ID, out long parentLineage, -1L);
            pParent.data.get(LineageKeys.SHI_ID, out long parentShi, -1L);
            if (parentLineage < 0 || parentShi < 0) return false;
            if (pChild.lineage_id >= 0 && pChild.lineage_id != parentLineage) return false;

            pParent.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            pParent.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            pParent.data.get(LineageKeys.NOBLE_DISTANCE, out int parentDist, 0);
            if (pChild.lineage_id == parentLineage && pChild.shi_id == parentShi && pChild.clan_name == clan)
                return false;

            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return false;

            pChild.family_name = family ?? "";
            pChild.clan_name = clan ?? "";
            pChild.lineage_id = parentLineage;
            pChild.shi_id = parentShi;
            pChild.noble_distance = parentDist + 1;
            pChild.status = parentDist + 1 >= LineageKeys.NOBLE_DECAY_DISTANCE
                ? LineageStatus.COMMON
                : LineageStatus.NOBLE;

            db.UpdateValue(ActorArchiveTableItem.GetTableName(),
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ID", pChild.id) },
                ColumnVal.Create("LINEAGE_ID", pChild.lineage_id),
                ColumnVal.Create("SHI_ID", pChild.shi_id),
                ColumnVal.Create("FAMILY_NAME", pChild.family_name),
                ColumnVal.Create("CLAN_NAME", pChild.clan_name),
                ColumnVal.Create("NOBLE_DISTANCE", pChild.noble_distance),
                ColumnVal.Create("STATUS", pChild.status),
                ColumnVal.Create("DISPLAY_NAME", BuildArchivedDisplayName(pChild, pChild.clan_name)));
            return true;
        }

        private static bool IsSameLineage(Actor pActor, Actor pSource)
        {
            if (pActor?.data == null || pSource?.data == null) return false;
            pActor.data.get(LineageKeys.LINEAGE_ID, out long actorLineage, -1L);
            pSource.data.get(LineageKeys.LINEAGE_ID, out long sourceLineage, -1L);
            return actorLineage >= 0 && actorLineage == sourceLineage;
        }

        private static int MoveExistingDescendantsToBranch(Actor pFounder, long pLineageId, long pOldShiId,
            long pNewShiId, string pClanName)
        {
            if (pFounder?.data == null || pLineageId < 0 || pOldShiId < 0 || pNewShiId < 0) return 0;
            if (pOldShiId == pNewShiId || string.IsNullOrEmpty(pClanName)) return 0;

            int moved = 0;
            var visited = new HashSet<long>();
            foreach (long childId in LineageQuery.GetChildIds(pFounder.data.id))
                moved += MoveDescendantToBranchRecursive(childId, pLineageId, pOldShiId, pNewShiId, pClanName, visited);
            return moved;
        }

        private static int MoveDescendantToBranchRecursive(long pActorId, long pLineageId, long pOldShiId,
            long pNewShiId, string pClanName, HashSet<long> pVisited)
        {
            if (pActorId < 0 || !pVisited.Add(pActorId)) return 0;

            int moved = 0;
            var live = World.world?.units?.get(pActorId);
            if (live?.data != null && (IsXia(live) || IsHuman(live) || UsesAwLineageSystem(live)) && !live.isRekt())
            {
                live.data.get(LineageKeys.LINEAGE_ID, out long liveLineageId, -1L);
                live.data.get(LineageKeys.SHI_ID, out long liveShiId, -1L);
                if (liveLineageId != pLineageId || liveShiId != pOldShiId) return 0;
                live.data.get(LineageKeys.FOUNDED_BRANCH_SHI_ID, out long liveFoundedBranch, -1L);
                if (liveFoundedBranch >= 0) return 0;

                live.data.set(LineageKeys.SHI_ID, pNewShiId);
                live.data.set(LineageKeys.CLAN_NAME, pClanName);
                ApplyDisplayName(live);
                ArchiveActor(live, pAlive: true);
                try { live.clearGraphicsFully(); } catch { }
                moved++;
            }
            else
            {
                var row = LineageArchiveReader.ReadRow(pActorId);
                if (row == null || row.lineage_id != pLineageId || row.shi_id != pOldShiId) return 0;
                if (row.founded_branch_shi_id >= 0) return 0;

                UpdateArchivedActorBranch(row, pNewShiId, pClanName);
                moved++;
            }

            foreach (long childId in LineageQuery.GetChildIds(pActorId))
                moved += MoveDescendantToBranchRecursive(childId, pLineageId, pOldShiId, pNewShiId, pClanName, pVisited);
            return moved;
        }

        private static void UpdateArchivedActorBranch(ActorArchiveTableItem pRow, long pNewShiId, string pClanName)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null || pRow == null) return;

            db.UpdateValue(ActorArchiveTableItem.GetTableName(),
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ID", pRow.id) },
                ColumnVal.Create("SHI_ID", pNewShiId),
                ColumnVal.Create("CLAN_NAME", pClanName),
                ColumnVal.Create("DISPLAY_NAME", BuildArchivedDisplayName(pRow, pClanName)));
        }

        private static string BuildArchivedDisplayName(ActorArchiveTableItem pRow, string pClanName)
        {
            string given = pRow.given_name ?? "";
            if (string.IsNullOrEmpty(given)) given = pRow.display_name ?? "";
            if (string.IsNullOrEmpty(given)) return "";

            string family = pRow.family_name ?? "";
            string status = pRow.status ?? LineageStatus.NONE;
            if (status == LineageStatus.NOBLE)
            {
                if (pRow.sex == 0)
                {
                    string prefix = !string.IsNullOrEmpty(pClanName) ? pClanName : family;
                    return !string.IsNullOrEmpty(prefix) ? prefix + given : given;
                }

                return !string.IsNullOrEmpty(family) ? given + family : given;
            }

            return !string.IsNullOrEmpty(pClanName) ? pClanName + given : given;
        }

        private static bool IsHistoricalFigure(Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (FigureStateStore.IndexOfActor(pActor.data.id) >= 0) return true;
            return pActor.hasTrait("figure");
        }

        private static bool IsLineageRootFounder(Actor pActor, long pLineageId)
        {
            if (pActor?.data == null || pLineageId < 0) return false;
            return LineageQuery.GetLineageFounderId(pLineageId) == pActor.data.id;
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
            if (!IsXia(pActor) && !UsesAwLineageSystem(pActor)) return false;
            if (!pActor.isAdult() || !pActor.isSexMale()) return false;

            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1);
            if (lineageId < 0) return false;
            pActor.data.get(LineageKeys.SHI_ID, out long actorShiId, -1);
            if (actorShiId < 0) return false;
            if (!AncientWarfare3.core.policy.KingdomPolicyService.IsEnfeoffmentActive(pActor.kingdom)) return false;
            if (LineageQuery.CountAliveInShi(actorShiId) < MIN_SHI_ALIVE_FOR_NEW_BRANCH) return false;

            Actor father = FindNobleFather(pActor);
            if (father == null) return false;

            // ② 只分当前国王的子辈
            if (!IsCurrentKing(father)) return false;

            // 当前继承人留本宗,不能被外派分封。继承人并不总等于最早出生的男嗣:
            // 未成年/疯狂/失格子嗣会被 HeirService 排除,所以必须按继承系统的当前结果判断。
            if (HeirService.IsCurrentHeir(father.kingdom, pActor)) return false;

            // ③ 长子留本宗
            if (IsEldestSon(father, pActor)) return false;

            // ④ 本宗冗余保护:除申请人 pActor 外,本宗(与父同氏支)还须留 ≥1 个成年活 male
            //    (长子即可),才允许把 pActor 分出去 —— 防本宗绝嗣。
            //    原阈值 ≥2(长子+备胎)过严,王有2子时第二子永远无法分封,改为 ≥1。
            if (CountHomeBranchAdultMales(father, pExclude: pActor) < MIN_HOME_BRANCH_ADULT_MALES_AFTER_BRANCH) return false;

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

            string shi = RandomShiDifferentFromFamily(family);
            return (shi, ShiSourceType.RANDOM);
        }

        private static (string clanName, string sourceType) GenerateOfficialShiName(
            Actor pActor, string pOfficeId)
        {
            int roll = LineageNamePool.Rng.Next(100);
            if (OfficialShiRules.ShouldUseHistoricalOfficeShi(roll, pOfficeId))
                return (OfficialShiRules.HistoricalOfficeShi(pOfficeId), ShiSourceType.OFFICIAL_GRANT);

            string family = "";
            if (pActor?.data != null)
                pActor.data.get(LineageKeys.FAMILY_NAME, out family, "");
            return (RandomShiDifferentFromFamily(family), ShiSourceType.OFFICIAL_GRANT);
        }

        private static void GrantOfficialShiBranch(Actor pActor, long pLineageId, string pOfficeId)
        {
            if (pActor?.data == null || pLineageId < 0) return;
            pActor.data.get(LineageKeys.SHI_ID, out long existingShi, -1L);
            if (!OfficialShiRules.ShouldGrantOfficialShi(existingShi >= 0)) return;

            (string clanName, _) = GenerateOfficialShiName(pActor, pOfficeId);
            long shiId = LineageIdAllocator.NextShiId();
            if (shiId < 0 || string.IsNullOrEmpty(clanName)) return;
            InsertShiBranch(shiId, pLineageId, clanName, pActor, ShiSourceType.OFFICIAL_GRANT);
            pActor.data.set(LineageKeys.SHI_ID, shiId);
            pActor.data.set(LineageKeys.CLAN_NAME, clanName);
        }

        private static string RandomShiDifferentFromFamily(string pFamily)
        {
            string shi = LineageNamePool.RandomShi();
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(pFamily) && shi == pFamily; i++)
                shi = LineageNamePool.RandomShi();
            return shi;
        }

        private static string FirstChar(string pName)
        {
            return string.IsNullOrEmpty(pName) ? null : pName.Substring(0, 1);
        }

        // ──────────────────────────── 原版 clan 命名 ────────────────────────────

        /// <summary>
        ///     按领袖身份重命名一个原版 Clan 的显示名(覆盖游戏默认随机名):
        ///     - 领袖是国王 → 国名(全名) + 氏 + "氏"(如"周幸氏")。
        ///     - 领袖非国王 → 城市名(全名) + 氏 + "氏"(如"某城幸氏")。
        ///     氏取领袖的 CLAN_NAME 字段;地名取领袖当前 kingdom/city 全名。
        ///     领袖不属于 Xia 制度、或氏/地名任一取不到 → 不改名(保留原版名,避免拼出残缺名)。
        ///     幂等:同名不重复 setName。
        /// </summary>
        public static void RenameClanByLeader(Clan pClan, Actor pLeader)
        {
            if (pClan?.data == null || pLeader?.data == null) return;

            if (HistoricalSchoolDescentService.IsCanonicalMaster(pLeader))
            {
                HistoricalSchoolMasterDefinition definition =
                    HistoricalSchoolDescentService.DefinitionFor(pLeader);
                string canonicalName = HistoricalMasterIdentityRules.EnsureSingleShiSuffix(
                    definition?.CanonicalShiName);
                if (!string.IsNullOrEmpty(canonicalName) &&
                    pClan.data.name != canonicalName)
                    try { pClan.setName(canonicalName); } catch { }
                return;
            }

            pLeader.data.get(LineageKeys.CLAN_NAME, out string shi, "");
            bool isKing = pLeader.isKing();
            string place = isKing ? pLeader.kingdom?.name : pLeader.city?.data?.name;
            bool institutional = XiaizationService.UsesXiaizedInstitutionSystem(pLeader.kingdom);
            if (!ForeignPseudoLineageRules.ShouldRenameInstitutionalClan(
                    leaderIsXia: IsXia(pLeader), kingdomUsesXiaizedInstitutions: institutional,
                    hasClan: true, hasBranch: !string.IsNullOrEmpty(shi),
                    hasPlace: !string.IsNullOrEmpty(place))) return;

            string newName = place + shi + "氏";
            if (pClan.data.name == newName) return;   // 幂等
            try { pClan.setName(newName); } catch { /* 改名失败不致命 */ }
        }

        // ──────────────────────────── 身份衰落 ────────────────────────────

        /// <summary>按 noble_distance 添加/移除 guizu。距离≥3 且本人非当前贵族 → 退回平民。</summary>
        public static void RefreshNobleStatus(Actor pActor)
        {
            if (!CanUseXiaizedLineageGovernment(pActor) && !UsesAwLineageSystem(pActor)) return;

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
            if (HistoricalSchoolDescentService.IsCanonicalMaster(pActor))
            {
                var definition = HistoricalSchoolDescentService.DefinitionFor(pActor);
                if (definition != null)
                {
                    pActor.data.set("display_name", definition.CanonicalName);
                    if (pActor.data.name != definition.CanonicalName)
                        pActor.setName(definition.CanonicalName);
                }
                return;
            }
            if (!IsXia(pActor) && !UsesAwLineageSystem(pActor) &&
                !XiaizationService.IsForeignPseudoDynasty(pActor?.kingdom)) return;

            NormalizeXiaGivenNameForClan(pActor);
            pActor.data.get(LineageKeys.GIVEN_NAME, out string given, "");
            pActor.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            pActor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            pActor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);

            if (string.IsNullOrEmpty(given)) given = pActor.getName();

            string display;

            if (status == LineageStatus.NOBLE)
            {
                // 贵族沿用旧规则:男用氏(无氏则姓)+名,女用名+姓。
                if (pActor.isSexMale())
                {
                    string prefix = !string.IsNullOrEmpty(clan) ? clan : family;
                    display = !string.IsNullOrEmpty(prefix) ? prefix + given : given;
                }
                else
                {
                    display = !string.IsNullOrEmpty(family) ? given + family : given;
                }
            }
            else if (!string.IsNullOrEmpty(clan))
            {
                // 有氏的平民/奴隶:男女一致 氏+名。
                display = clan + given;
            }
            else
            {
                // 无氏平民 / 奴隶 / 无谱系:单名
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
                bool pseudoActor = XiaizationService.UsesXiaizedInstitutionSystem(pKingdom) &&
                                   UsesAwLineageSystem(actor);
                if (!IsXia(actor) && !pseudoActor) continue;

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
            bool aUsesLineage = IsXia(pA) || UsesAwLineageSystem(pA);
            bool bUsesLineage = IsXia(pB) || UsesAwLineageSystem(pB);
            if (!aUsesLineage || !bUsesLineage) return true;
            if (!SlaveService.CanFallInLoveByStatus(pA, pB)) return false;
            if (SlaveService.AreBothSlaves(pA, pB)) return true;

            // 任一方所在国已合流 → 不再限制
            if (IsKingdomIntegrated(pA.kingdom) || IsKingdomIntegrated(pB.kingdom)) return true;

            pA.data.get(LineageKeys.FAMILY_NAME, out string fa, "");
            pB.data.get(LineageKeys.FAMILY_NAME, out string fb, "");
            if (string.IsNullOrEmpty(fa) || string.IsNullOrEmpty(fb)) return true;

            return fa != fb; // 同姓 → false(不可)
        }

        public static void OnBecameLovers(Actor pA, Actor pB)
        {
            TryApplyMatrilocalInLaw(pA, pB);
            TryApplyMatrilocalInLaw(pB, pA);
        }

        private static void TryApplyMatrilocalInLaw(Actor pHusband, Actor pWife)
        {
            if (!IsXia(pHusband) || !IsXia(pWife)) return;
            if (!pHusband.isSexMale() || pWife.isSexMale()) return;
            if (!HasCompleteLineageData(pWife)) return;

            pHusband.data.get(LineageKeys.LINEAGE_ID, out long husbandLineage, -1L);
            if (husbandLineage >= 0) return;

            if (!TryInheritLineageFromSource(pHusband, pWife, pRequireClan: true)) return;
            pHusband.data.set(LineageKeys.MATRILOCAL_IN_LAW, true);
            pHusband.data.set(LineageKeys.MATRILOCAL_WIFE_ID, pWife.data.id);
            ApplyDisplayName(pHusband);
            ArchiveActor(pHusband, pAlive: true);
            try { pHusband.clearGraphicsFully(); } catch { }
        }

        public static bool CanFallInLoveByXiaHuman(Actor pA, Actor pB)
        {
            if (!IsXiaHumanPair(pA, pB)) return false;
            if (pA?.data == null || pB?.data == null) return false;
            if (!SlaveService.CanFallInLoveByStatus(pA, pB)) return false;
            if (pA.hasLover() || pB.hasLover()) return false;
            if (!pA.isAdult() || !pB.isAdult()) return false;
            if (!pA.isBreedingAge() || !pB.isBreedingAge()) return false;
            if (pA.subspecies == null || pB.subspecies == null) return false;
            if (!pA.subspecies.needs_mate || !pB.subspecies.needs_mate) return false;
            if (!pA.subspecies.isPartnerSuitableForReproduction(pA, pB)) return false;
            if (pA.isRelatedTo(pB)) return false;
            return true;
        }

        // ──────────────────────── 内部:写姓族/氏支/国家状态 ────────────────────────

        internal static void InsertLineageGroup(long pLineageId, string pFamilyName, Actor pFounder)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return;
            var origin = ResolveOriginIds(pFounder);
            db.Insert(LineageGroupTableItem.GetTableName(),
                ColumnVal.Create("LINEAGE_ID", pLineageId),
                ColumnVal.Create("FAMILY_NAME", pFamilyName),
                ColumnVal.Create("FOUNDER_ACTOR_ID", pFounder.data.id),
                ColumnVal.Create("FOUNDER_NAME", pFounder.getName()),
                ColumnVal.Create("CREATED_TIME", CurTime()),
                ColumnVal.Create("ORIGIN_KINGDOM_ID", origin.kingdomId),
                ColumnVal.Create("ORIGIN_CITY_ID", origin.cityId),
                ColumnVal.Create("IS_EXTINCT", 0));
        }

        internal static void InsertShiBranch(long pShiId, long pLineageId, string pClanName, Actor pFounder,
            string pSourceType)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) return;
            var origin = ResolveOriginIds(pFounder);
            db.Insert(ShiBranchTableItem.GetTableName(),
                ColumnVal.Create("SHI_ID", pShiId),
                ColumnVal.Create("LINEAGE_ID", pLineageId),
                ColumnVal.Create("CLAN_NAME", pClanName),
                ColumnVal.Create("FOUNDER_ACTOR_ID", pFounder.data.id),
                ColumnVal.Create("SOURCE_TYPE", pSourceType),
                ColumnVal.Create("ORIGIN_KINGDOM_ID", origin.kingdomId),
                ColumnVal.Create("ORIGIN_CITY_ID", origin.cityId),
                ColumnVal.Create("ORIGIN_ORIGINAL_CLAN_ID", pFounder.clan?.data?.id ?? -1),
                ColumnVal.Create("CREATED_TIME", CurTime()),
                ColumnVal.Create("IS_EXTINCT", 0));
        }

        private static (long kingdomId, long cityId) ResolveOriginIds(Actor pFounder)
        {
            long cityId = pFounder?.city?.data?.id ?? -1;
            Kingdom kingdom = pFounder?.kingdom ?? pFounder?.city?.kingdom;
            if (cityId < 0 && kingdom?.capital?.data != null) cityId = kingdom.capital.data.id;
            long kingdomId = kingdom?.id ?? -1;
            return (kingdomId, cityId);
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
