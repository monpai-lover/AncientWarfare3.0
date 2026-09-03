using System;
using System.Data.SQLite;
using AncientWarfare3.content.figures;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     历史人物(开国君主 / 学派宗师)的双亲处理:
    ///
    ///     1. **清掉引擎双亲** —— 降临/化身时随机落到某对夫妇名下的 `parent_id_1/2`
    ///        一律清成 -1,历史人物不做任何人的儿子。
    ///     2. **写入史载真实双亲** —— 从内容表取姬昌/太姒这类确有记载的名字,落成
    ///        一对「合成祖先」档案行 + 亲子边,家族树里就能上溯到他们。
    ///
    ///     合成祖先没有 live Actor,且 `LINEAGE_ID = SHI_ID = -1`,所以继承逻辑
    ///     (`SuccessionRelationshipIndex` 只由活 actor 构建)、氏支大树、姓族成员、
    ///     绝嗣判定一概看不见他们 —— 隔离论证见 <see cref="HistoricalAncestorRules"/>。
    ///
    ///     全流程幂等:三源已是目标状态时一条 SQL 都不发,可以在读档时对全部历史
    ///     人物无脑跑一遍(旧存档修档)。
    /// </summary>
    internal static class HistoricalAncestorService
    {
        internal static bool EnsureCardParentage(Actor pActor,
            HistoricalFigureCardDefinition pDefinition, string pDeploymentId)
        {
            if (pActor?.data == null || pDefinition == null ||
                string.IsNullOrWhiteSpace(pDeploymentId)) return false;
            var parentage = new HistoricalAncestorParentage(
                pDefinition.FatherDisplayName, "", pDefinition.MotherDisplayName,
                "", false);
            return Ensure(pActor, parentage,
                CardParentId(pDeploymentId, HistoricalFigureCardParentSlot.Father),
                CardParentId(pDeploymentId, HistoricalFigureCardParentSlot.Mother));
        }

        internal static bool EnsureFigureParentage(Actor pActor,
            int pRegistryIndex, string pFigureId)
        {
            if (!content.figures.HistoricalFigureParentage.TryGet(pFigureId,
                    out HistoricalAncestorParentage parentage))
                parentage = default;
            return Ensure(pActor, parentage,
                HistoricalAncestorRules.FigureAncestorId(pRegistryIndex,
                    HistoricalAncestorRules.FatherSlot),
                HistoricalAncestorRules.FigureAncestorId(pRegistryIndex,
                    HistoricalAncestorRules.MotherSlot));
        }

        internal static bool EnsureMasterParentage(Actor pActor,
            int pRegistryIndex, string pCanonicalName)
        {
            if (!content.schools.HistoricalMasterParentage.TryGet(
                    pCanonicalName, out HistoricalAncestorParentage parentage))
                parentage = default;
            return Ensure(pActor, parentage,
                HistoricalAncestorRules.MasterAncestorId(pRegistryIndex,
                    HistoricalAncestorRules.FatherSlot),
                HistoricalAncestorRules.MasterAncestorId(pRegistryIndex,
                    HistoricalAncestorRules.MotherSlot));
        }

        /// <returns>true = 本次真的改了状态(含清双亲/建祖先);false = 无需改动或失败。</returns>
        private static bool Ensure(Actor pActor,
            HistoricalAncestorParentage pParentage, long pFatherSlotId,
            long pMotherSlotId)
        {
            if (pActor?.data == null) return false;

            // FatherDisplayOnly:父亲本人也在名册里(司马迁之父司马谈),名字照实
            // 显示,但不建合成祖先 —— 否则家族树上会同时出现真假两个司马谈。
            long fatherId = pParentage.FatherDisplayOnly
                ? -1L
                : HistoricalAncestorRules.ShouldCreateAncestor(pFatherSlotId,
                    pParentage.FatherName) ? pFatherSlotId : -1L;
            long motherId = HistoricalAncestorRules.ShouldCreateAncestor(
                pMotherSlotId, pParentage.MotherName) ? pMotherSlotId : -1L;

            pActor.data.get(LineageKeys.HISTORICAL_FATHER_ACTOR_ID,
                out long storedFatherId, -1L);
            pActor.data.get(LineageKeys.HISTORICAL_MOTHER_ACTOR_ID,
                out long storedMotherId, -1L);
            pActor.data.get(LineageKeys.HISTORICAL_FATHER_NAME,
                out string storedFatherName, "");
            pActor.data.get(LineageKeys.HISTORICAL_MOTHER_NAME,
                out string storedMotherName, "");
            string expectedFatherName = pParentage.HasFather
                ? pParentage.FatherName
                : string.Empty;
            string expectedMotherName = pParentage.HasMother
                ? pParentage.MotherName
                : string.Empty;
            if (HistoricalAncestorRules.IsAlreadyApplied(
                    pActor.data.parent_id_1, pActor.data.parent_id_2,
                    new HistoricalParentageState(storedFatherId,
                        storedMotherId, storedFatherName, storedMotherName),
                    new HistoricalParentageState(fatherId, motherId,
                        expectedFatherName, expectedMotherName)))
                return false;

            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null ||
                LineageArchiveManager.Instance?.InitializeSuccessful != true)
                return false;

            long previousParent1 = pActor.data.parent_id_1;
            long previousParent2 = pActor.data.parent_id_2;
            double createdTime = LineageService.CurTime();
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            // 画像字段(head/skin/skin_set/表现型)的取值规则不简单,直接借本人的
            // 一份未落库快照,免得在这里重算一套而与档案写入分叉。
            ActorArchiveTableItem portrait = LineageArchiveWriter
                .CaptureUnarchivedRelationshipSnapshot(pActor, pAlive: true);

            try
            {
                using SQLiteTransaction transaction = db.BeginTransaction();
                try
                {
                    if (fatherId >= 0L)
                        HistoricalAncestorPersistence.UpsertAncestor(db,
                            transaction,
                            BuildAncestorRow(pActor, portrait, fatherId,
                                pParentage.FatherName, pMale: true,
                                pFamilyName: ResolveFatherFamilyName(pActor)),
                            createdTime);
                    if (motherId >= 0L)
                        HistoricalAncestorPersistence.UpsertAncestor(db,
                            transaction,
                            BuildAncestorRow(pActor, portrait, motherId,
                                pParentage.MotherName, pMale: false,
                                pFamilyName: pParentage.MotherFamilyName),
                            createdTime);
                    HistoricalAncestorPersistence.ApplyChildParents(db,
                        transaction, pActor.data.id, fatherId, motherId,
                        lineageId, createdTime);
                    transaction.Commit();
                }
                catch
                {
                    try { transaction.Rollback(); }
                    catch { }
                    throw;
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Historical parentage write failed for actor " +
                    pActor.data.id + ": " + error.Message);
                return false;
            }

            // DB 已经是权威状态,再动 live 侧,顺序保证中途失败不会留下
            // 「引擎双亲已清、档案还指着旧双亲」的空档。
            DetachEngineParents(pActor, previousParent1, previousParent2);
            pActor.data.set(LineageKeys.HISTORICAL_FATHER_ACTOR_ID, fatherId);
            pActor.data.set(LineageKeys.HISTORICAL_MOTHER_ACTOR_ID, motherId);
            // 名字按「是否可考」写,不按「是否建了祖先」—— 仅显示的父亲也要显示。
            pActor.data.set(LineageKeys.HISTORICAL_FATHER_NAME,
                expectedFatherName);
            pActor.data.set(LineageKeys.HISTORICAL_MOTHER_NAME,
                expectedMotherName);

            ActorArchivePresenceIndex.Mark(pActor.data.id);
            SuccessionRelationshipIndex.Refresh(pActor);
            FamilyTreeProjectionRevision.Advance(
                FamilyTreeProjectionChange.FamilyStructure);
            return true;
        }

        /// <summary>
        ///     清掉引擎双亲槽。原版没有 decreaseChildren,`_current_children` 只增不减,
        ///     所以对前任父母尽力递减一次 —— 它是运行时字段,读档时
        ///     `Actor.loadFromSave` 会按 `getParents()` 重建,所以偏差最多存活一局,
        ///     且没有任何逻辑依赖它(`getChildren` 靠孩子身上的 parent_id 判定)。
        /// </summary>
        private static void DetachEngineParents(Actor pActor,
            long pPreviousParent1, long pPreviousParent2)
        {
            pActor.data.parent_id_1 = -1L;
            pActor.data.parent_id_2 = -1L;
            ReleaseChildSlot(pPreviousParent1);
            if (pPreviousParent2 != pPreviousParent1)
                ReleaseChildSlot(pPreviousParent2);
        }

        private static void ReleaseChildSlot(long pParentId)
        {
            if (pParentId < 0L) return;
            try
            {
                Actor parent = World.world?.units?.get(pParentId);
                if (parent?.data == null || parent._current_children <= 0)
                    return;
                parent._current_children--;
            }
            catch { }
        }

        private static long CardParentId(string pDeploymentId,
            HistoricalFigureCardParentSlot pSlot)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                string key = pDeploymentId.Trim() + ":" +
                    (pSlot == HistoricalFigureCardParentSlot.Father
                        ? "father" : "mother");
                for (int i = 0; i < key.Length; i++)
                {
                    hash ^= key[i];
                    hash *= 1099511628211UL;
                }
                return HistoricalAncestorRules.SyntheticBase + 200000L +
                    (long)(hash % 1000000000UL);
            }
        }

        /// <summary>父与本人同姓同氏,所以姓沿用本人的(内容表因此不必重复填)。</summary>
        private static string ResolveFatherFamilyName(Actor pActor)
        {
            pActor.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            if (!string.IsNullOrWhiteSpace(family)) return family;
            pActor.data.get(LineageKeys.CHINESE_FAMILY_NAME, out family, "");
            return family ?? string.Empty;
        }

        private static ActorArchiveTableItem BuildAncestorRow(Actor pActor,
            ActorArchiveTableItem pPortrait, long pAncestorId,
            string pDisplayName, bool pMale, string pFamilyName)
        {
            return new ActorArchiveTableItem
            {
                id = pAncestorId,
                // given_name 刻意留空:LineageDisplayNameRules.ProjectStored 在
                // given 为空时直接返回 stored display,史载名(姬昌/周曷朱/太姒)
                // 才不会被姓氏拼接规则改写。
                given_name = string.Empty,
                display_name = pDisplayName ?? string.Empty,
                family_name = pFamilyName ?? string.Empty,
                clan_name = string.Empty,
                // 关键隔离点:不入姓族、不入氏支 —— 历史人物本人仍是自己宗族的始祖。
                lineage_id = -1L,
                shi_id = -1L,
                asset_id = pActor.asset?.id ?? pPortrait?.asset_id ??
                    string.Empty,
                subspecies_id = -1L,
                subspecies_name = string.Empty,
                sex = pMale ? 0 : 1,
                status = LineageStatus.NOBLE,
                kingdom_id = -1L,
                kingdom_name = string.Empty,
                kingdom_color = string.Empty,
                city_id = -1L,
                city_name = string.Empty,
                social_title = string.Empty,
                social_title_color = string.Empty,
                original_clan_id = -1L,
                clan_color_text = string.Empty,
                parent_id_1 = -1L,
                parent_id_2 = -1L,
                generation = 0,
                noble_distance = 0,
                ever_noble_blood = 1,
                noble_origin_actor_id = -1L,
                noble_origin_name = string.Empty,
                noble_origin_distance = 0,
                // 生卒不可考:沿用 LineageFamilyArchiveMigration 占位行的取值。
                birth_time = 0d,
                death_time = -1d,
                death_cause = string.Empty,
                is_alive = 0,
                name_integrated = 1,
                // 画像用本人的表现型:同族同种,且必然是合法取值。
                head = pPortrait?.head ?? pActor.data.head,
                skin = pPortrait?.skin ?? 0,
                skin_set = pPortrait?.skin_set ?? 0,
                age_overgrowth = 1,
                phenotype_index = pPortrait?.phenotype_index ??
                    pActor.data.phenotype_index,
                phenotype_shade = pPortrait?.phenotype_shade ??
                    pActor.data.phenotype_shade,
                founded_branch_shi_id = -1L
            };
        }
    }
}
