using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.schools;
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
        public static bool Upsert(Actor pActor, bool pAlive,
            bool pTraceOnly = false, bool pFinalizeProjection = true)
        {
            return Upsert(pActor, pAlive, pTraceOnly,
                pForceSynchronous: false,
                pAllowSynchronousFallback: true,
                pFinalizeProjection: pFinalizeProjection,
                pIdentityOnlyProjection: false);
        }

        public static bool QueueDeath(Actor pActor, bool pTraceOnly)
        {
            return Upsert(pActor, pAlive: false, pTraceOnly,
                pForceSynchronous: false,
                pAllowSynchronousFallback: false,
                pFinalizeProjection: true,
                pIdentityOnlyProjection: false);
        }

        private static bool Upsert(Actor pActor, bool pAlive,
            bool pTraceOnly, bool pForceSynchronous,
            bool pAllowSynchronousFallback,
            bool pFinalizeProjection, bool pIdentityOnlyProjection)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null ||
                !LineageArchiveManager.Instance.InitializeSuccessful ||
                pActor?.data == null)
                return false;
            long id = pActor.data.id;
            ActorArchiveTableItem previous = LineageArchiveReader.ReadRow(id);
            bool traceableSpecies = LineageService.IsHuman(pActor) ||
                                    LineageService.IsNativeXiaCultureActor(pActor);
            if (!LineageService.UsesAwLineageSystem(pActor) &&
                !LineageService.HasOriginalClan(pActor) &&
                (!pTraceOnly || !traceableSpecies) &&
                (!pIdentityOnlyProjection || previous == null))
                return false;

            string table = ActorArchiveTableItem.GetTableName();
            ActorArchiveTableItem snapshot = CaptureRelationshipSnapshot(
                pActor, pAlive, previous);
            if (snapshot == null) return false;
            FamilyTreeProjectionChange projectionChange =
                !pFinalizeProjection
                    ? FamilyTreeProjectionChange.None
                    : pIdentityOnlyProjection
                        ? ResolveIdentityProjectionChange(previous, snapshot)
                        : ResolveProjectionChange(previous, snapshot);
            if (ActorDeathArchiveRules.ShouldQueueDeathInMemory(pAlive,
                    pForceSynchronous, pAllowSynchronousFallback))
                return ActorDeathArchiveService.EnqueueLineage(snapshot,
                    projectionChange, pFinalizeProjection);
            HistoricalSqlColumn[] inserts = SnapshotColumns(snapshot,
                pIncludeId: true);
            HistoricalSqlColumn[] updates = SnapshotColumns(snapshot,
                pIncludeId: false);
            if (!pForceSynchronous &&
                HistoricalWriteService.TryUpsertState(
                    "actor-archive:" + id, table,
                    new[] { new HistoricalSqlColumn("ID", id) }, updates,
                    inserts, (sequence, replacedSequence) =>
                    {
                        ActorArchivePendingStore.Publish(id, sequence,
                            snapshot);
                        FamilyTreeProjectionPendingStore.TransferOwnership(
                            id, replacedSequence, sequence);
                        if (pFinalizeProjection)
                            FamilyTreeProjectionPendingStore.Publish(id,
                                sequence, projectionChange);
                        else
                            FamilyTreeProjectionPendingStore.PublishDeferred(
                                id, sequence, writeAccepted: true);
                    }, sequence =>
                    {
                        ActorArchivePendingStore.Complete(id, sequence);
                        if (FamilyTreeProjectionPendingStore.TryComplete(
                                id, sequence,
                                out FamilyTreeProjectionChange committedChange))
                            AdvanceProjectionAfterCommit(committedChange);
                    }, (sequence, error) =>
                        OnAsyncWriteFailed(id, sequence),
                    out long queuedSequence, out _))
            {
                return true;
            }

            if (!pForceSynchronous && !pAllowSynchronousFallback &&
                ActorDeathArchiveService.EnqueueLineage(snapshot,
                    projectionChange, pFinalizeProjection))
                return true;

            if (!HistoricalWriteService.FlushForSynchronousFallback(
                    System.TimeSpan.FromSeconds(5), out string flushError))
            {
                ModClass.LogWarning("Actor archive ordering barrier failed: " +
                                    flushError);
                return false;
            }
            bool exists = previous != null || db.CheckKeyExist(table,
                SimpleColumnConstraint.CreateEq("ID", id));
            ColumnVal[] values = SnapshotColumnValues(snapshot,
                pIncludeId: !exists);
            HistoricalContentRevision.AdvanceAfterSuccessfulSynchronousWrite(
                () =>
                {
                    if (exists)
                        db.UpdateValue(table,
                            new List<SimpleColumnConstraint>
                            {
                                SimpleColumnConstraint.CreateEq("ID", id)
                            }, values);
                    else
                        db.Insert(table, values);
                });
            if (pFinalizeProjection)
            {
                FamilyTreeProjectionChange committedChange =
                    FamilyTreeProjectionPendingStore.FinalizeSynchronous(
                        id, projectionChange, finalWriteSucceeded: true);
                AdvanceProjectionAfterCommit(committedChange);
            }
            return true;
        }

        internal static ActorArchiveTableItem CaptureRelationshipSnapshot(
            Actor pActor, bool pAlive)
        {
            if (LineageArchiveManager.Instance.OperatingDB == null ||
                !LineageArchiveManager.Instance.InitializeSuccessful ||
                pActor?.data == null)
                return null;
            ActorArchiveTableItem previous = LineageArchiveReader.ReadRow(
                pActor.data.id);
            return CaptureRelationshipSnapshot(pActor, pAlive, previous);
        }

        internal static bool RefreshIdentity(Actor pActor)
        {
            if (pActor?.data == null ||
                LineageArchiveReader.ReadRow(pActor.data.id) == null)
                return false;
            return Upsert(pActor, pAlive: true, pTraceOnly: true,
                pForceSynchronous: false,
                pAllowSynchronousFallback: true,
                pFinalizeProjection: true,
                pIdentityOnlyProjection: true);
        }

        internal static ActorArchiveTableItem CaptureUnarchivedRelationshipSnapshot(
            Actor pActor, bool pAlive)
        {
            return CaptureRelationshipSnapshot(pActor, pAlive, null);
        }

        private static ActorArchiveTableItem CaptureRelationshipSnapshot(
            Actor pActor, bool pAlive, ActorArchiveTableItem pPrevious)
        {
            if (pActor?.data == null ||
                (pAlive && IsArchivedDead(pPrevious))) return null;

            pActor.data.get(LineageKeys.GIVEN_NAME, out string given, "");
            pActor.data.get("display_name", out string display, "");
            pActor.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            pActor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1);
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1);
            pActor.data.get(LineageKeys.NOBLE_DISTANCE, out int nobleDist, 99);
            pActor.data.get(LineageKeys.LINEAGE_STATUS, out string status,
                LineageStatus.NONE);
            pActor.data.get(LineageKeys.NAME_INTEGRATED, out bool integrated,
                false);
            pActor.data.get(LineageKeys.FOUNDED_BRANCH_SHI_ID,
                out long foundedBranchShi, -1);
            pActor.data.get(LineageKeys.DEATH_CAUSE, out string deathCause, "");
            var nobleBlood = ResolveNobleBloodSnapshot(pActor, pPrevious,
                nobleDist);

            string name = pActor.data.name ?? pActor.getName();
            if (string.IsNullOrEmpty(given)) given = name;
            if (string.IsNullOrEmpty(display)) display = name;
            NamingProfileId namingProfile = NamingProfileId.None;
            try
            {
                namingProfile = AWCultureNamingTraditionService
                    .ResolveForActorReadOnly(pActor).Profile;
            }
            catch { }
            if (namingProfile != NamingProfileId.Western &&
                namingProfile != NamingProfileId.OrcNomadic)
            {
                given = LineageGivenNameNormalizationRules.Normalize(given,
                    family, clan, status == LineageStatus.NOBLE,
                    pActor.isSexMale(), integrated);
            }
            var kingdom = ResolveActorKingdomSnapshot(pActor, pPrevious);
            var city = ResolveActorCitySnapshot(pActor, pPrevious);
            var social = ResolveSocialTitleSnapshot(pActor,
                kingdom.kingdomName, kingdom.kingdomColor, city.cityName);
            string primaryCeremonial = CeremonialTitleResolver.ResolveArchive(
                pActor, pPrevious);
            long clanId = pActor.clan?.data?.id ?? -1L;
            bool currentClan = clanId >= 0L;
            double deathTime = pAlive
                ? pPrevious?.death_time ?? -1d
                : LineageService.CurTime();

            return new ActorArchiveTableItem
            {
                id = pActor.data.id,
                given_name = given ?? "",
                display_name = display ?? "",
                family_name = family ?? "",
                clan_name = clan ?? "",
                lineage_id = lineageId,
                shi_id = shiId,
                asset_id = pActor.asset?.id ?? pPrevious?.asset_id ?? "",
                archive_resolution =
                    LineageFamilyArchiveMigration.Resolved,
                subspecies_id = pActor.subspecies?.getID() ?? -1L,
                subspecies_name = pActor.subspecies?.data?.name ?? "",
                sex = pActor.isSexMale() ? 0 : 1,
                status = status ?? LineageStatus.NONE,
                noble_distance = nobleDist,
                ever_noble_blood = nobleBlood.ever,
                noble_origin_actor_id = nobleBlood.originId,
                noble_origin_name = nobleBlood.originName ?? "",
                noble_origin_distance = nobleBlood.distance,
                name_integrated = integrated ? 1 : 0,
                kingdom_id = kingdom.kingdomId,
                kingdom_name = kingdom.kingdomName ?? "",
                kingdom_color = !string.IsNullOrEmpty(kingdom.kingdomColor)
                    ? kingdom.kingdomColor
                    : pPrevious?.kingdom_color ?? "",
                city_id = city.cityId,
                city_name = city.cityName ?? "",
                social_title = social.title ?? "",
                social_title_color = social.color ?? "",
                primary_ceremonial_title = primaryCeremonial,
                original_clan_id = currentClan
                    ? clanId
                    : pPrevious?.original_clan_id ?? -1L,
                clan_color_text = currentClan
                    ? pActor.clan?.getColor()?.color_text ?? ""
                    : pPrevious?.clan_color_text ?? "",
                clan_color_id = currentClan
                    ? pActor.clan.data.color_id
                    : pPrevious?.clan_color_id ?? -1,
                clan_banner_icon_id = currentClan
                    ? pActor.clan.data.banner_icon_id
                    : pPrevious?.clan_banner_icon_id ?? -1,
                clan_banner_background_id = currentClan
                    ? pActor.clan.data.banner_background_id
                    : pPrevious?.clan_banner_background_id ?? -1,
                parent_id_1 = pActor.data.parent_id_1,
                parent_id_2 = pActor.data.parent_id_2,
                generation = pActor.data.generation,
                birth_time = pPrevious?.birth_time ?? pActor.data.created_time,
                death_time = deathTime,
                death_cause = pAlive
                    ? pPrevious?.death_cause ?? ""
                    : deathCause ?? "",
                is_alive = pAlive ? 1 : 0,
                head = ResolveArchivedHead(pActor, pPrevious),
                skin = FamilyTreePortraitIdentityRules.ResolveArchivedSkinId(
                    currentSkinId: pActor.subspecies?.data?.skin_id ?? 0,
                    hasCurrentSubspecies: pActor.subspecies?.data != null,
                    previousSkinId: pPrevious?.skin ?? 0),
                skin_set = FamilyTreePortraitIdentityRules.
                    ResolveArchivedSkinSet(
                        hasCurrentSubspecies:
                            pActor.subspecies?.data != null,
                        previousSkinSet: pPrevious?.skin_set ?? 0),
                age_overgrowth = pActor.data.age_overgrowth,
                phenotype_index = pActor.data.phenotype_index,
                phenotype_shade = pActor.data.phenotype_shade,
                founded_branch_shi_id = foundedBranchShi
            };
        }

        internal static bool TryQueueCapturedDeath(
            ActorArchiveTableItem pSnapshot,
            FamilyTreeProjectionChange pProjectionChange,
            bool pFinalizeProjection)
        {
            if (pSnapshot == null) return false;
            long id = pSnapshot.id;
            string table = ActorArchiveTableItem.GetTableName();
            HistoricalSqlColumn[] inserts = SnapshotColumns(pSnapshot,
                pIncludeId: true);
            HistoricalSqlColumn[] updates = SnapshotColumns(pSnapshot,
                pIncludeId: false);
            if (!HistoricalWriteService.TryUpsertState(
                    "actor-archive:" + id, table,
                    new[] { new HistoricalSqlColumn("ID", id) }, updates,
                    inserts, (sequence, replacedSequence) =>
                    {
                        ActorArchivePendingStore.Publish(id, sequence,
                            pSnapshot);
                        FamilyTreeProjectionPendingStore.TransferOwnership(
                            id, replacedSequence, sequence);
                        if (pFinalizeProjection)
                            FamilyTreeProjectionPendingStore.Publish(id,
                                sequence, pProjectionChange);
                        else
                            FamilyTreeProjectionPendingStore.PublishDeferred(
                                id, sequence, writeAccepted: true);
                    }, sequence =>
                    {
                        ActorArchivePendingStore.Complete(id, sequence);
                        if (FamilyTreeProjectionPendingStore.TryComplete(
                                id, sequence,
                                out FamilyTreeProjectionChange committed))
                            AdvanceProjectionAfterCommit(committed);
                    }, (sequence, error) =>
                        OnAsyncWriteFailed(id, sequence),
                    out long queuedSequence, out _)) return false;
            return true;
        }

        internal static bool WriteCapturedDeathSynchronously(
            ActorArchiveTableItem pSnapshot,
            FamilyTreeProjectionChange pProjectionChange,
            bool pFinalizeProjection,
            System.TimeSpan? pOrderingTimeout = null)
        {
            if (pSnapshot == null) return false;
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null ||
                !LineageArchiveManager.Instance.InitializeSuccessful)
                return false;
            if (!HistoricalWriteService.FlushForSynchronousFallback(
                    pOrderingTimeout ?? System.TimeSpan.FromSeconds(5),
                    out _)) return false;

            string table = ActorArchiveTableItem.GetTableName();
            bool exists = db.CheckKeyExist(table,
                SimpleColumnConstraint.CreateEq("ID", pSnapshot.id));
            ColumnVal[] values = SnapshotColumnValues(pSnapshot,
                pIncludeId: !exists);
            HistoricalContentRevision.AdvanceAfterSuccessfulSynchronousWrite(
                () =>
                {
                    if (exists)
                        db.UpdateValue(table,
                            new List<SimpleColumnConstraint>
                            {
                                SimpleColumnConstraint.CreateEq("ID",
                                    pSnapshot.id)
                            }, values);
                    else
                        db.Insert(table, values);
                });
            if (pFinalizeProjection)
            {
                FamilyTreeProjectionChange committed =
                    FamilyTreeProjectionPendingStore.FinalizeSynchronous(
                        pSnapshot.id, pProjectionChange,
                        finalWriteSucceeded: true);
                AdvanceProjectionAfterCommit(committed);
            }
            return true;
        }

        private static FamilyTreeProjectionChange ResolveIdentityProjectionChange(
            ActorArchiveTableItem pPrevious,
            ActorArchiveTableItem pCurrent)
        {
            if (pPrevious == null || pCurrent == null)
                return FamilyTreeProjectionChange.None;
            bool changed =
                !Same(pPrevious.given_name, pCurrent.given_name) ||
                !Same(pPrevious.display_name, pCurrent.display_name) ||
                !Same(pPrevious.social_title, pCurrent.social_title) ||
                !Same(pPrevious.social_title_color,
                    pCurrent.social_title_color) ||
                !Same(pPrevious.primary_ceremonial_title,
                    pCurrent.primary_ceremonial_title) ||
                pPrevious.status != pCurrent.status ||
                pPrevious.noble_distance != pCurrent.noble_distance ||
                !Same(pPrevious.kingdom_name, pCurrent.kingdom_name) ||
                !Same(pPrevious.kingdom_color, pCurrent.kingdom_color) ||
                !Same(pPrevious.city_name, pCurrent.city_name) ||
                !Same(pPrevious.asset_id, pCurrent.asset_id) ||
                pPrevious.subspecies_id != pCurrent.subspecies_id ||
                pPrevious.sex != pCurrent.sex ||
                pPrevious.head != pCurrent.head ||
                pPrevious.skin != pCurrent.skin ||
                pPrevious.skin_set != pCurrent.skin_set ||
                pPrevious.age_overgrowth != pCurrent.age_overgrowth ||
                pPrevious.phenotype_index != pCurrent.phenotype_index ||
                pPrevious.phenotype_shade != pCurrent.phenotype_shade;
            return changed
                ? FamilyTreeProjectionChange.IdentityOrTitle
                : FamilyTreeProjectionChange.None;
        }

        private static FamilyTreeProjectionChange ResolveProjectionChange(
            ActorArchiveTableItem pPrevious,
            ActorArchiveTableItem pCurrent)
        {
            if (pCurrent == null) return FamilyTreeProjectionChange.None;
            bool firstArchive = pPrevious == null;
            bool familyStructureChanged = !firstArchive &&
                (pPrevious.lineage_id != pCurrent.lineage_id ||
                 pPrevious.shi_id != pCurrent.shi_id ||
                 pPrevious.parent_id_1 != pCurrent.parent_id_1 ||
                 pPrevious.parent_id_2 != pCurrent.parent_id_2 ||
                 pPrevious.generation != pCurrent.generation ||
                 pPrevious.founded_branch_shi_id !=
                     pCurrent.founded_branch_shi_id ||
                 pPrevious.original_clan_id != pCurrent.original_clan_id ||
                 !Same(pPrevious.family_name, pCurrent.family_name) ||
                 !Same(pPrevious.clan_name, pCurrent.clan_name));
            bool lifeStatusChanged = !firstArchive &&
                (pPrevious.is_alive != pCurrent.is_alive ||
                 pPrevious.death_time != pCurrent.death_time ||
                 !Same(pPrevious.death_cause, pCurrent.death_cause));
            bool identityOrTitleChanged = !firstArchive &&
                (!Same(pPrevious.given_name, pCurrent.given_name) ||
                 !Same(pPrevious.display_name, pCurrent.display_name) ||
                 !Same(pPrevious.social_title, pCurrent.social_title) ||
                 !Same(pPrevious.social_title_color,
                     pCurrent.social_title_color) ||
                 !Same(pPrevious.primary_ceremonial_title,
                     pCurrent.primary_ceremonial_title) ||
                 pPrevious.status != pCurrent.status ||
                 pPrevious.noble_distance != pCurrent.noble_distance ||
                 !Same(pPrevious.kingdom_name, pCurrent.kingdom_name) ||
                 !Same(pPrevious.kingdom_color, pCurrent.kingdom_color) ||
                 !Same(pPrevious.city_name, pCurrent.city_name) ||
                 !Same(pPrevious.asset_id, pCurrent.asset_id) ||
                 pPrevious.subspecies_id != pCurrent.subspecies_id ||
                 pPrevious.sex != pCurrent.sex ||
                 pPrevious.head != pCurrent.head ||
                 pPrevious.skin != pCurrent.skin ||
                 pPrevious.skin_set != pCurrent.skin_set ||
                 pPrevious.age_overgrowth != pCurrent.age_overgrowth ||
                 pPrevious.phenotype_index != pCurrent.phenotype_index ||
                 pPrevious.phenotype_shade != pCurrent.phenotype_shade);
            return FamilyTreeProjectionRevisionRules.ResolveArchiveChange(
                firstArchive, familyStructureChanged, lifeStatusChanged,
                identityOrTitleChanged);
        }

        private static bool Same(string pLeft, string pRight)
        {
            return string.Equals(pLeft ?? "", pRight ?? "",
                System.StringComparison.Ordinal);
        }

        private static void AdvanceProjectionAfterCommit(
            FamilyTreeProjectionChange pChange)
        {
            if (!FamilyTreeProjectionRevisionRules.ShouldAdvance(pChange))
                return;
            FamilyTreeProjectionRevision.Advance(pChange);
        }

        private static void OnAsyncWriteFailed(long pActorId,
            long pSequence)
        {
            ActorArchivePendingStore.Complete(pActorId, pSequence);
            FamilyTreeProjectionPendingStore.Fail(pActorId, pSequence);
        }

        private static int ResolveArchivedHead(Actor pActor,
            ActorArchiveTableItem pPrevious)
        {
            if (pActor?.data == null) return pPrevious?.head ?? -1;
            int headCount = 0;
            try
            {
                AnimationContainerUnit container = pActor.animation_container ??
                    DynamicActorSpriteCreatorUI.getContainerForUI(pActor);
                var heads = pActor.isSexMale()
                    ? container?.heads_male
                    : container?.heads_female;
                headCount = heads?.Length ?? 0;
            }
            catch { }
            if (headCount <= 0 && pActor.data.head < 0 &&
                pPrevious?.head >= 0) return pPrevious.head;
            return FamilyTreePortraitIdentityRules.ResolveArchivedHeadId(
                pActor.data.head, pActor.data.id, headCount);
        }

        private static HistoricalSqlColumn[] SnapshotColumns(
            ActorArchiveTableItem pRow, bool pIncludeId)
        {
            ColumnVal[] values = SnapshotColumnValues(pRow, pIncludeId);
            var result = new HistoricalSqlColumn[values.Length];
            for (int index = 0; index < values.Length; index++)
                result[index] = new HistoricalSqlColumn(values[index].Name,
                    values[index].Value);
            return result;
        }

        private static ColumnVal[] SnapshotColumnValues(
            ActorArchiveTableItem pRow, bool pIncludeId)
        {
            var values = new List<ColumnVal>();
            if (pIncludeId) values.Add(ColumnVal.Create("ID", pRow.id));
            values.Add(ColumnVal.Create("GIVEN_NAME", pRow.given_name));
            values.Add(ColumnVal.Create("DISPLAY_NAME", pRow.display_name));
            values.Add(ColumnVal.Create("FAMILY_NAME", pRow.family_name));
            values.Add(ColumnVal.Create("CLAN_NAME", pRow.clan_name));
            values.Add(ColumnVal.Create("LINEAGE_ID", pRow.lineage_id));
            values.Add(ColumnVal.Create("SHI_ID", pRow.shi_id));
            values.Add(ColumnVal.Create("ASSET_ID", pRow.asset_id));
            values.Add(ColumnVal.Create("ARCHIVE_RESOLUTION", pRow.archive_resolution));
            values.Add(ColumnVal.Create("SUBSPECIES_ID", pRow.subspecies_id));
            values.Add(ColumnVal.Create("SUBSPECIES_NAME", pRow.subspecies_name));
            values.Add(ColumnVal.Create("SEX", pRow.sex));
            values.Add(ColumnVal.Create("STATUS", pRow.status));
            values.Add(ColumnVal.Create("NOBLE_DISTANCE", pRow.noble_distance));
            values.Add(ColumnVal.Create("EVER_NOBLE_BLOOD", pRow.ever_noble_blood));
            values.Add(ColumnVal.Create("NOBLE_ORIGIN_ACTOR_ID", pRow.noble_origin_actor_id));
            values.Add(ColumnVal.Create("NOBLE_ORIGIN_NAME", pRow.noble_origin_name));
            values.Add(ColumnVal.Create("NOBLE_ORIGIN_DISTANCE", pRow.noble_origin_distance));
            values.Add(ColumnVal.Create("NAME_INTEGRATED", pRow.name_integrated));
            values.Add(ColumnVal.Create("KINGDOM_ID", pRow.kingdom_id));
            values.Add(ColumnVal.Create("KINGDOM_NAME", pRow.kingdom_name));
            values.Add(ColumnVal.Create("KINGDOM_COLOR", pRow.kingdom_color));
            values.Add(ColumnVal.Create("CITY_ID", pRow.city_id));
            values.Add(ColumnVal.Create("CITY_NAME", pRow.city_name));
            values.Add(ColumnVal.Create("SOCIAL_TITLE", pRow.social_title));
            values.Add(ColumnVal.Create("SOCIAL_TITLE_COLOR", pRow.social_title_color));
            values.Add(ColumnVal.Create("PRIMARY_CEREMONIAL_TITLE",
                pRow.primary_ceremonial_title));
            values.Add(ColumnVal.Create("ORIGINAL_CLAN_ID", pRow.original_clan_id));
            values.Add(ColumnVal.Create("CLAN_COLOR_TEXT", pRow.clan_color_text));
            values.Add(ColumnVal.Create("CLAN_COLOR_ID", pRow.clan_color_id));
            values.Add(ColumnVal.Create("CLAN_BANNER_ICON_ID", pRow.clan_banner_icon_id));
            values.Add(ColumnVal.Create("CLAN_BANNER_BACKGROUND_ID", pRow.clan_banner_background_id));
            values.Add(ColumnVal.Create("PARENT_ID_1", pRow.parent_id_1));
            values.Add(ColumnVal.Create("PARENT_ID_2", pRow.parent_id_2));
            values.Add(ColumnVal.Create("GENERATION", pRow.generation));
            values.Add(ColumnVal.Create("BIRTH_TIME", pRow.birth_time));
            values.Add(ColumnVal.Create("DEATH_TIME", pRow.death_time));
            values.Add(ColumnVal.Create("DEATH_CAUSE", pRow.death_cause));
            values.Add(ColumnVal.Create("IS_ALIVE", pRow.is_alive));
            values.Add(ColumnVal.Create("HEAD", pRow.head));
            values.Add(ColumnVal.Create("SKIN", pRow.skin));
            values.Add(ColumnVal.Create("SKIN_SET", pRow.skin_set));
            values.Add(ColumnVal.Create("AGE_OVERGROWTH", pRow.age_overgrowth));
            values.Add(ColumnVal.Create("PHENOTYPE_INDEX", pRow.phenotype_index));
            values.Add(ColumnVal.Create("PHENOTYPE_SHADE", pRow.phenotype_shade));
            values.Add(ColumnVal.Create("FOUNDED_BRANCH_SHI_ID", pRow.founded_branch_shi_id));
            return values.ToArray();
        }

        internal static bool ReplaceHistoricalMasterIdentity(Actor pActor,
            HistoricalMasterLineageCommitIdentity pIdentity)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            Clan clan = pActor?.clan;
            if (db == null || !LineageArchiveManager.Instance.InitializeSuccessful ||
                pActor?.data == null || pIdentity == null || !pIdentity.IsValid ||
                !pIdentity.IdsFrozen || pActor.data.id != pIdentity.ActorId ||
                clan?.data == null)
                return false;

            if (!Upsert(pActor, pAlive: true, pTraceOnly: false,
                    pForceSynchronous: true,
                    pAllowSynchronousFallback: true,
                    pFinalizeProjection: false,
                    pIdentityOnlyProjection: false))
                return false;
            if (!HistoricalWriteService.FlushForSynchronousFallback(
                    System.TimeSpan.FromSeconds(5), out string flushError))
            {
                ModClass.LogWarning(
                    "Historical master identity ordering barrier failed: " +
                    flushError);
                return false;
            }
            long clanId = clan.data.id;
            string clanColorText = clan.getColor()?.color_text ?? "";
            int clanColorId = clan.data.color_id;
            int clanBannerIconId = clan.data.banner_icon_id;
            int clanBannerBackgroundId = clan.data.banner_background_id;
            HistoricalContentRevision.AdvanceAfterSuccessfulSynchronousWrite(
                () => db.UpdateValue(ActorArchiveTableItem.GetTableName(),
                new List<SimpleColumnConstraint>
                {
                    SimpleColumnConstraint.CreateEq("ID", pActor.data.id)
                },
                ColumnVal.Create("GIVEN_NAME", pIdentity.GivenName),
                ColumnVal.Create("DISPLAY_NAME", pIdentity.CanonicalName),
                ColumnVal.Create("FAMILY_NAME", pIdentity.FamilyName),
                ColumnVal.Create("CLAN_NAME", pIdentity.ShiName),
                ColumnVal.Create("LINEAGE_ID", pIdentity.LineageId),
                ColumnVal.Create("SHI_ID", pIdentity.ShiId),
                ColumnVal.Create("ORIGINAL_CLAN_ID", clanId),
                ColumnVal.Create("CLAN_COLOR_TEXT", clanColorText),
                ColumnVal.Create("CLAN_COLOR_ID", clanColorId),
                ColumnVal.Create("CLAN_BANNER_ICON_ID", clanBannerIconId),
                ColumnVal.Create("CLAN_BANNER_BACKGROUND_ID", clanBannerBackgroundId),
                ColumnVal.Create("FOUNDED_BRANCH_SHI_ID", -1L),
                ColumnVal.Create("IS_ALIVE", 1),
                ColumnVal.Create("DEATH_TIME", -1d),
                ColumnVal.Create("DEATH_CAUSE", "")));
            FamilyTreeProjectionChange committedChange =
                FamilyTreeProjectionPendingStore.FinalizeSynchronous(
                    pActor.data.id,
                    FamilyTreeProjectionChange.FamilyStructure,
                    finalWriteSucceeded: true);
            AdvanceProjectionAfterCommit(committedChange);

            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActor.data.id);
            return row != null && row.given_name == pIdentity.GivenName &&
                   row.display_name == pIdentity.CanonicalName &&
                   row.family_name == pIdentity.FamilyName &&
                   row.clan_name == pIdentity.ShiName &&
                   row.lineage_id == pIdentity.LineageId && row.shi_id == pIdentity.ShiId &&
                   row.original_clan_id == clanId &&
                   (row.clan_color_text ?? "") == clanColorText &&
                   row.clan_color_id == clanColorId &&
                   row.clan_banner_icon_id == clanBannerIconId &&
                   row.clan_banner_background_id == clanBannerBackgroundId &&
                   row.founded_branch_shi_id == -1L && row.is_alive == 1 &&
                   row.death_time < 0d && string.IsNullOrEmpty(row.death_cause);
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

            try
            {
                pActor.data.get(LineageKeys.FORMER_KING_TITLE, out string formerTitle, "");
                if (!string.IsNullOrEmpty(formerTitle))
                {
                    pActor.data.get(LineageKeys.FORMER_KINGDOM_COLOR, out string formerColor, "");
                    return (formerTitle, string.IsNullOrEmpty(formerColor) ? color : formerColor);
                }

            }
            catch { }

            var roles = new List<string>();
            string rolesColor = color;
            try
            {
                string dynasticTitle =
                    DynasticTitleService.ResolveLivingTitle(pActor);
                if (!string.IsNullOrEmpty(dynasticTitle))
                    roles.Add(dynasticTitle);
            }
            catch { }
            try
            {
                pActor.data.get(LineageKeys.FORMER_HEIR_TITLE, out string formerHeirTitle, "");
                if (!string.IsNullOrEmpty(formerHeirTitle))
                {
                    roles.Add(formerHeirTitle);
                    pActor.data.get(LineageKeys.FORMER_HEIR_KINGDOM_COLOR,
                        out string formerHeirColor, "");
                    if (!string.IsNullOrEmpty(formerHeirColor)) rolesColor = formerHeirColor;
                }
            }
            catch { }
            try
            {
                pActor.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
                if (isHeir || HeirService.IsCurrentHeir(pActor.kingdom, pActor))
                {
                    roles.Add(HeirTitleRules.BuildSocialTitle(pKingdomName, pActor.kingdom));
                    rolesColor = color;
                }
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
            {
                pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                    out long courtKingdomId, -1L);
                Kingdom courtKingdom = World.world?.kingdoms?.get(courtKingdomId) ??
                                        pActor.kingdom;
                roles.Add(CourtInstitutionService.OfficeName(
                    courtKingdom, office));
            }

            string combined = CourtTitleRules.Combine(roles.ToArray());
            if (!string.IsNullOrEmpty(combined)) return (combined, rolesColor);

            try
            {
                pActor.data.get(LineageKeys.CAPTIVE_NOBLE_TITLE, out string captiveTitle, "");
                if (!string.IsNullOrEmpty(captiveTitle))
                {
                    pActor.data.get(LineageKeys.CAPTIVE_NOBLE_COLOR, out string captiveColor, "");
                    return (captiveTitle, string.IsNullOrEmpty(captiveColor) ? color : captiveColor);
                }
            }
            catch { }

            return ("", "");
        }
    }
}
