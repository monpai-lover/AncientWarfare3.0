using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Reflection;
using AncientWarfare3.core.db;
using AncientWarfare3.core.court;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using AncientWarfare3.utils;
using db;
using HarmonyLib;
using VanillaSQLiteConnectionWithLock = SQLite.SQLiteConnectionWithLock;

namespace AncientWarfare3.core.lineage
{
    internal sealed class KingdomRestorationRequest
    {
        public long claim_id = -1;
        public long original_kingdom_id = -1;
        public string original_kingdom_name = "";
        public long original_capital_city_id = -1;
        public long original_mandate_period_id = -1;
        public long lineage_id = -1;
        public long shi_id = -1;
        public string clan_name = "";
        public string state_name = "";
        public string mode = "";
    }

    internal static class KingdomIdentityContinuityService
    {
        [ThreadStatic] private static CreationContext _current;

        private static readonly FieldInfo DeadKingdomsField =
            AccessTools.Field(typeof(KingdomManager), "_dead_kingdoms");

        private static System.Data.SQLite.SQLiteConnection ArchiveDB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool ArchiveReady => ArchiveDB != null &&
                                            LineageArchiveManager.Instance.InitializeSuccessful;

        private sealed class CreationContext : IDisposable
        {
            public KingdomRestorationRequest Request;
            public RestorationKingdomIdLease Lease;
            public KingdomData DeadStatsData;

            public void Dispose()
            {
                Lease?.Dispose();
                if (ReferenceEquals(_current, this)) _current = null;
            }
        }

        private sealed class ContinuitySnapshot
        {
            public long kingdomId = -1;
            public string name = "";
            public double foundedTime = -1;
            public string originalActorAsset = "";
            public long cultureId = -1;
            public long languageId = -1;
            public long religionId = -1;
            public long capitalCityId = -1;
            public long legitimateLineageId = -1;
            public long legitimateShiId = -1;
            public int title;
            public bool integrated;
            public bool policyEnabled;
            public bool policyAiEnabled;
            public bool slaveryEnabled;
            public bool slaveArmyEnabled;
            public float xiaContactProgress;
            public bool wasMandate;
            public long mandatePeriodId = -1;
        }

        private sealed class ArchiveSnapshot
        {
            public string name = "";
            public int colorId;
            public int bannerIconId;
            public int bannerBackgroundId;
            public bool alive;
            public double foundedTime = -1;
        }

        public static bool IsCreatingRestoration => _current != null;

        public static bool ShouldSuppressNewKingdomEffects(Kingdom pKingdom)
        {
            long actualId = pKingdom?.data?.id ?? -1L;
            return RoyalRestorationRules.ShouldSuppressNewKingdomEffects(
                _current != null,
                _current?.Request?.original_kingdom_id ?? -1L,
                actualId);
        }

        public static bool TryConsumeKingdomId(string pType, out long pKingdomId)
        {
            pKingdomId = -1L;
            return _current?.Lease != null && _current.Lease.TryConsume(pType, out pKingdomId);
        }

        public static void CaptureBeforeDestruction(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || !ArchiveReady || !KingdomArchiveWriter.IsArchivable(pKingdom)) return;
            Actor king = pKingdom.king;
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                out long legitimateLineageId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                out long legitimateShiId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_INTEGRATED, out bool integrated, false);
            pKingdom.data.get(LineageKeys.POLICY_ENABLED, out bool policyEnabled,
                XiaizationService.DefaultPolicyEnabled(pKingdom));
            pKingdom.data.get(LineageKeys.POLICY_AI_ENABLED, out bool policyAiEnabled,
                XiaizationService.DefaultPolicyAIEnabled(pKingdom));
            pKingdom.data.get(LineageKeys.SLAVERY_ENABLED, out bool slaveryEnabled, false);
            pKingdom.data.get(LineageKeys.SLAVE_ARMY_ENABLED, out bool slaveArmyEnabled, false);
            pKingdom.data.get(LineageKeys.XIA_CONTACT_PROGRESS, out float xiaContactProgress, 0f);
            pKingdom.data.get(LineageKeys.MANDATE_PERIOD_ID, out long mandatePeriodId, -1L);
            pKingdom.data.get(LineageKeys.RESTORATION_ORIGINAL_MANDATE_PERIOD_ID,
                out long restorationMandatePeriodId, -1L);
            bool wasMandate = MandateService.IsMandateKingdom(pKingdom) ||
                              restorationMandatePeriodId >= 0;
            if (mandatePeriodId < 0 && restorationMandatePeriodId >= 0)
                mandatePeriodId = restorationMandatePeriodId;
            City capital = pKingdom.capital;
            long capitalId = capital?.data?.id ??
                             (pKingdom.data.last_capital_id >= 0
                                 ? pKingdom.data.last_capital_id
                                 : pKingdom.data.capitalID);
            string capitalName = capital?.data?.name ?? "";
            int restorationCount = ReadRestorationCount(pKingdom.id);
            string table = KingdomContinuityTableItem.GetTableName();
            var values = new[]
            {
                ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                ColumnVal.Create("FOUNDED_TIME", pKingdom.data.created_time),
                ColumnVal.Create("DESTROYED_TIME", LineageService.CurTime()),
                ColumnVal.Create("ORIGINAL_ACTOR_ASSET", pKingdom.data.original_actor_asset ?? ""),
                ColumnVal.Create("CULTURE_ID", pKingdom.culture?.id ?? -1L),
                ColumnVal.Create("LANGUAGE_ID", pKingdom.language?.id ?? -1L),
                ColumnVal.Create("RELIGION_ID", pKingdom.religion?.id ?? -1L),
                ColumnVal.Create("ROYAL_CLAN_ID", pKingdom.data.royal_clan_id),
                ColumnVal.Create("CAPITAL_CITY_ID", capitalId),
                ColumnVal.Create("CAPITAL_CITY_NAME", capitalName),
                ColumnVal.Create("LAST_KING_ACTOR_ID", king?.data?.id ?? -1L),
                ColumnVal.Create("LAST_KING_NAME", king?.getName() ?? ""),
                ColumnVal.Create("LEGITIMATE_LINEAGE_ID", legitimateLineageId),
                ColumnVal.Create("LEGITIMATE_SHI_ID", legitimateShiId),
                ColumnVal.Create("KINGDOM_TITLE", (int)KingdomTitleService.GetTitle(pKingdom)),
                ColumnVal.Create("NAME_INTEGRATED", integrated ? 1 : 0),
                ColumnVal.Create("POLICY_ENABLED", policyEnabled ? 1 : 0),
                ColumnVal.Create("POLICY_AI_ENABLED", policyAiEnabled ? 1 : 0),
                ColumnVal.Create("SLAVERY_ENABLED", slaveryEnabled ? 1 : 0),
                ColumnVal.Create("SLAVE_ARMY_ENABLED", slaveArmyEnabled ? 1 : 0),
                ColumnVal.Create("XIA_CONTACT_PROGRESS", (double)xiaContactProgress),
                ColumnVal.Create("WAS_MANDATE", wasMandate ? 1 : 0),
                ColumnVal.Create("MANDATE_PERIOD_ID", wasMandate ? mandatePeriodId : -1L),
                ColumnVal.Create("RESTORATION_COUNT", restorationCount),
                ColumnVal.Create("LAST_RESTORED_TIME", -1.0)
            };

            if (ArchiveDB.CheckKeyExist(table,
                    SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdom.id)))
            {
                ArchiveDB.UpdateValue(table,
                    new List<SimpleColumnConstraint>
                    {
                        SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdom.id)
                    }, values);
            }
            else
            {
                var insert = new List<ColumnVal> { ColumnVal.Create("KINGDOM_ID", pKingdom.id) };
                insert.AddRange(values);
                ArchiveDB.Insert(table, insert.ToArray());
            }
        }

        public static Kingdom RestoreFromCity(City pTargetCity, Actor pClaimant,
            KingdomRestorationRequest pRequest, out string pError)
        {
            pError = "";
            if (pTargetCity?.data == null || pTargetCity.isRekt() ||
                pClaimant?.data == null || pRequest == null || pRequest.original_kingdom_id < 0)
            {
                pError = "invalid_restoration_request";
                return null;
            }
            if (!RoyalGuardOfficeRules.CanReplaceLifetimeGuardIdentity(
                    RoyalGuardService.IsRoyalGuard(pClaimant)))
            {
                pError = "claimant_is_royal_guard";
                return null;
            }
            if (_current != null)
            {
                pError = "nested_restoration_creation";
                return null;
            }

            ArchiveSnapshot archive = ReadArchive(pRequest.original_kingdom_id);
            bool liveExists = World.world?.kingdoms?.get(pRequest.original_kingdom_id)?.data != null;
            if (!RoyalRestorationRules.CanLeaseOriginalKingdomId(
                    pRequest.original_kingdom_id, liveExists, archive != null && !archive.alive))
            {
                pError = liveExists ? "original_kingdom_alive" : "missing_dead_archive";
                return null;
            }

            ContinuitySnapshot continuity = ReadContinuity(pRequest.original_kingdom_id);
            Kingdom previousHost = PrepareClaimantForRestorationAccession(
                pClaimant, out bool clearedHostHeir);
            PrepareClaimantLineage(pClaimant, pRequest);
            Kingdom targetOwner = pTargetCity.kingdom;
            try
            {
                using (FormalAffiliationTransferScope.Open(
                           pClaimant.data.id, targetOwner?.id ?? -1L, pTargetCity.id))
                    if (pClaimant.city != pTargetCity) pClaimant.joinCity(pTargetCity);
            }
            catch (Exception e)
            {
                RefreshPreviousHostHeir(previousHost, clearedHostHeir);
                pError = "claimant_move_failed:" + e.Message;
                return null;
            }
            RefreshPreviousHostHeir(previousHost, clearedHostHeir);

            KingdomData deadStats = DetachDeadStatsRow(pRequest.original_kingdom_id);
            ClearDeadKingdomCache(pRequest.original_kingdom_id);
            KingdomPolicyInheritanceService.PrepareForIdentityRestoration(
                pRequest.original_kingdom_id, pClaimant.data.id);
            var context = new CreationContext
            {
                Request = pRequest,
                Lease = new RestorationKingdomIdLease(pRequest.original_kingdom_id),
                DeadStatsData = deadStats
            };
            _current = context;
            Kingdom restored = null;
            try
            {
                using (FormalAffiliationTransferScope.Open(
                           pClaimant.data.id, pRequest.original_kingdom_id, pTargetCity.id))
                    restored = pTargetCity.makeOwnKingdom(pClaimant,
                        pRebellion: true, pFellApart: false);
            }
            catch (Exception e)
            {
                pError = "make_own_kingdom_failed:" + e.Message;
            }
            finally
            {
                context.Dispose();
            }

            if (restored?.data == null || restored.id != pRequest.original_kingdom_id)
            {
                if (deadStats != null) RestoreDetachedStatsRow(deadStats);
                if (string.IsNullOrEmpty(pError)) pError = "original_id_not_consumed";
                return null;
            }

            try
            {
                ApplyIdentity(restored, pTargetCity, pClaimant, pRequest,
                    archive, continuity, deadStats);
                // 复国沿用旧国的合法氏支作为政治正统，但复国领袖必须另立
                // 一支，避免把新一轮王朝直接写回旧氏，导致族谱看不到分支。
                LineageService.EnsureRestorationFounderBranch(restored, pClaimant);
                XiaizationService.RestoreIdentityContinuity(restored);
                KingdomPolicyService.RestoreIdentityContinuity(restored);
                CourtService.RestoreIdentityContinuity(restored);
                RestorePersistentRuntimeFlags(restored, continuity);
                WarTerritoryService.ResetTransientStateForIdentityRestoration(restored.id);
                KingdomArchiveWriter.ReviveContinuity(restored);
                MarkContinuityRestored(restored.id);
                ChronicleEvents.OnKingChanged(restored, pClaimant);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Restored kingdom identity recovery failed: " + e);
                pError = "identity_recovery_partial:" + e.Message;
            }
            return restored;
        }

        private static Kingdom PrepareClaimantForRestorationAccession(
            Actor pClaimant, out bool pClearedHostHeir)
        {
            pClearedHostHeir = false;
            if (pClaimant?.data == null) return null;
            Kingdom previousHost = pClaimant.kingdom;
            if (RoyalGuardService.IsRoyalGuard(pClaimant))
                RoyalGuardService.DismissGuard(pClaimant, "restoration_accession");
            if (GeneralService.IsGeneral(pClaimant) ||
                FiefService.GetFiefCityId(pClaimant) >= 0)
                GeneralService.RetireForSuccession(pClaimant);
            CourtService.ClearOfficeForReignTransition(
                pClaimant, "restoration_accession");
            if (HeirService.IsCurrentHeir(previousHost, pClaimant))
            {
                HeirService.ClearHeir(previousHost);
                pClearedHostHeir = true;
            }
            City previousCity = pClaimant.city;
            if (previousCity?.leader == pClaimant) previousCity.removeLeader();
            if (pClaimant.hasArmy()) pClaimant.removeFromArmy();
            return previousHost;
        }

        private static void RefreshPreviousHostHeir(Kingdom pPreviousHost,
            bool pClearedHostHeir)
        {
            if (!pClearedHostHeir || pPreviousHost?.data == null ||
                pPreviousHost.isRekt()) return;
            try { HeirService.RefreshHeir(pPreviousHost); }
            catch { }
        }

        private static void PrepareClaimantLineage(Actor pClaimant, KingdomRestorationRequest pRequest)
        {
            if (pRequest.lineage_id >= 0) pClaimant.data.set(LineageKeys.LINEAGE_ID, pRequest.lineage_id);
            if (pRequest.shi_id >= 0) pClaimant.data.set(LineageKeys.SHI_ID, pRequest.shi_id);
            if (!string.IsNullOrEmpty(pRequest.clan_name))
                pClaimant.data.set(LineageKeys.CLAN_NAME, pRequest.clan_name);
            pClaimant.data.set(LineageKeys.NOBLE_DISTANCE, 0);
            pClaimant.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.NOBLE);
            if (!pClaimant.hasTrait(LineageKeys.TRAIT_GUIZU))
                pClaimant.addTrait(LineageKeys.TRAIT_GUIZU);
        }

        private static void ApplyIdentity(Kingdom pKingdom, City pCapital, Actor pClaimant,
            KingdomRestorationRequest pRequest, ArchiveSnapshot pArchive,
            ContinuitySnapshot pContinuity, KingdomData pDeadStats)
        {
            MergeVanillaHistory(pKingdom.data, pDeadStats);
            string name = RoyalRestorationRules.ResolveRestoredKingdomName(
                pContinuity?.name,
                pArchive?.name,
                pRequest.original_kingdom_name,
                StateNameService.GetBoundStateName(pRequest.shi_id),
                pRequest.state_name);
            if (!string.IsNullOrEmpty(name)) pKingdom.setName(name, pTrack: false);

            if (pArchive != null)
            {
                pKingdom.data.setColorID(pArchive.colorId);
                pKingdom.data.banner_icon_id = pArchive.bannerIconId;
                pKingdom.data.banner_background_id = pArchive.bannerBackgroundId;
            }
            if (pContinuity != null)
            {
                if (pContinuity.foundedTime >= 0) pKingdom.data.created_time = pContinuity.foundedTime;
                if (!string.IsNullOrEmpty(pContinuity.originalActorAsset))
                    pKingdom.data.original_actor_asset = pContinuity.originalActorAsset;
                RestoreMetas(pKingdom, pContinuity);
                KingdomTitleService.SetTitle(pKingdom, (KingdomTitle)pContinuity.title);
                pKingdom.data.set(LineageKeys.KINGDOM_INTEGRATED, pContinuity.integrated);
            }
            else if (pArchive?.foundedTime >= 0)
            {
                pKingdom.data.created_time = pArchive.foundedTime;
            }

            long legitimateLineage = pRequest.lineage_id >= 0
                ? pRequest.lineage_id
                : pContinuity?.legitimateLineageId ?? -1L;
            long legitimateShi = pRequest.shi_id >= 0
                ? pRequest.shi_id
                : pContinuity?.legitimateShiId ?? -1L;
            if (legitimateLineage >= 0)
                pKingdom.data.set(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID, legitimateLineage);
            if (legitimateShi >= 0)
                pKingdom.data.set(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID, legitimateShi);
            pKingdom.data.set(LineageKeys.KINGDOM_MONARCHY_ESTABLISHED, true);
            pKingdom.data.set(LineageKeys.KINGDOM_SUCCESSION_MODE, SuccessionMode.DIRECT);
            pKingdom.data.set(LineageKeys.CHRONICLE_LAST_KING_ID, -1L);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_ID, -1L);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_RELATION_ACTOR_ID, -1L);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_RELATION_KING_ID, -1L);
            pKingdom.data.kingID = pClaimant.data.id;
            pKingdom.data.royal_clan_id = pClaimant.clan?.data?.id ?? -1L;
            pKingdom.setCapital(pCapital);
            if (pClaimant.city != pCapital)
            {
                using (FormalAffiliationTransferScope.Open(
                           pClaimant.data.id, pKingdom.id, pCapital.id))
                    pClaimant.joinCity(pCapital);
            }
            pCapital.setLeader(pClaimant, pNew: true);
            MetaColorCacheService.RefreshKingdomAfterGeneratedColor(pKingdom);
            HeirService.EnsureLegitimateLine(pKingdom, pClaimant);
            HeirService.RefreshHeir(pKingdom);
        }

        private static void MergeVanillaHistory(KingdomData pCurrent, KingdomData pDead)
        {
            if (pCurrent == null || pDead == null) return;
            List<LeaderEntry> newReign = pCurrent.past_rulers;
            pCurrent.past_rulers = pDead.past_rulers ?? new List<LeaderEntry>();
            pDead.past_rulers = null;
            if (newReign != null)
            {
                foreach (LeaderEntry entry in newReign)
                    if (entry != null) pCurrent.past_rulers.Add(entry);
            }
            while (pCurrent.past_rulers.Count > 30) pCurrent.past_rulers.RemoveAt(0);
            pCurrent.total_kings = Math.Max(pDead.total_kings, 0) + Math.Max(newReign?.Count ?? 0, 1);
            pCurrent.created_time = pDead.created_time;
            pCurrent.custom_name = pDead.custom_name;
            pCurrent.name_culture_id = pDead.name_culture_id;
            pCurrent.motto = pDead.motto;
            AWLocalizedMottoService.CopyIdentity(pDead, pCurrent);
            pCurrent.left = pDead.left;
            pCurrent.joined = pDead.joined;
            pCurrent.moved = pDead.moved;
            pCurrent.migrated = pDead.migrated;
            pCurrent.past_names = pDead.past_names;
            pDead.past_names = null;
            pCurrent.saved_traits = pDead.saved_traits;
            pDead.saved_traits = null;
        }

        private static void RestoreMetas(Kingdom pKingdom, ContinuitySnapshot pSnapshot)
        {
            if (pSnapshot.cultureId >= 0)
            {
                Culture culture = World.world?.cultures?.get(pSnapshot.cultureId);
                if (culture != null) pKingdom.setCulture(culture);
            }
            if (pSnapshot.languageId >= 0)
            {
                Language language = World.world?.languages?.get(pSnapshot.languageId);
                if (language != null) pKingdom.setLanguage(language);
            }
            if (pSnapshot.religionId >= 0)
            {
                Religion religion = World.world?.religions?.get(pSnapshot.religionId);
                if (religion != null) pKingdom.setReligion(religion);
            }
        }

        private static void RestorePersistentRuntimeFlags(Kingdom pKingdom,
            ContinuitySnapshot pSnapshot)
        {
            if (pKingdom?.data == null || pSnapshot == null) return;
            pKingdom.data.set(LineageKeys.POLICY_ENABLED, pSnapshot.policyEnabled);
            pKingdom.data.set(LineageKeys.POLICY_AI_ENABLED,
                pSnapshot.policyEnabled && pSnapshot.policyAiEnabled);
            pKingdom.data.set(LineageKeys.SLAVERY_ENABLED, pSnapshot.slaveryEnabled);
            pKingdom.data.set(LineageKeys.SLAVE_ARMY_ENABLED, pSnapshot.slaveArmyEnabled);
            pKingdom.data.set(LineageKeys.XIA_CONTACT_PROGRESS, pSnapshot.xiaContactProgress);
        }

        private static KingdomData DetachDeadStatsRow(long pKingdomId)
        {
            if (Config.disable_db) return null;
            try
            {
                DBInserter.executeCommands();
                VanillaSQLiteConnectionWithLock connection = DBManager.getSyncConnection();
                using (connection.Lock())
                {
                    KingdomData snapshot = connection.Find<KingdomData>(pKingdomId);
                    if (snapshot != null) connection.Delete<KingdomData>(pKingdomId);
                    return snapshot;
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Dead KingdomData detach failed: " + e.Message);
                return null;
            }
        }

        private static void RestoreDetachedStatsRow(KingdomData pSnapshot)
        {
            if (pSnapshot == null || Config.disable_db) return;
            try
            {
                VanillaSQLiteConnectionWithLock connection =
                    DBManager.getSyncConnection();
                using (connection.Lock())
                    connection.InsertOrReplace(pSnapshot);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Dead KingdomData rollback failed: " +
                                    e.Message);
            }
        }

        private static void ClearDeadKingdomCache(long pKingdomId)
        {
            try
            {
                var cache = DeadKingdomsField?.GetValue(World.world?.kingdoms) as Dictionary<long, DeadKingdom>;
                if (cache == null || !cache.TryGetValue(pKingdomId, out DeadKingdom dead)) return;
                cache.Remove(pKingdomId);
                dead?.Dispose();
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Dead kingdom cache clear failed: " + e.Message);
            }
        }

        private static ArchiveSnapshot ReadArchive(long pKingdomId)
        {
            if (!ArchiveReady || pKingdomId < 0) return null;
            try
            {
                using var cmd = new SQLiteCommand(ArchiveDB);
                cmd.CommandText =
                    $"SELECT KINGDOM_NAME, COLOR_ID, BANNER_ICON_ID, BANNER_BACKGROUND_ID, " +
                    $"IS_ALIVE, FOUNDED_TIME FROM {KingdomArchiveTableItem.GetTableName()} " +
                    "WHERE KINGDOM_ID=@k LIMIT 1";
                cmd.Parameters.AddWithValue("@k", pKingdomId);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                if (!reader.Read()) return null;
                return new ArchiveSnapshot
                {
                    name = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    colorId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    bannerIconId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    bannerBackgroundId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    alive = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                    foundedTime = reader.IsDBNull(5) ? -1.0 : reader.GetDouble(5)
                };
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Kingdom archive identity read failed: " + e.Message);
                return null;
            }
        }

        private static ContinuitySnapshot ReadContinuity(long pKingdomId)
        {
            if (!ArchiveReady || pKingdomId < 0) return null;
            try
            {
                using var cmd = new SQLiteCommand(ArchiveDB);
                cmd.CommandText =
                    $"SELECT KINGDOM_ID, KINGDOM_NAME, FOUNDED_TIME, ORIGINAL_ACTOR_ASSET, " +
                    $"CULTURE_ID, LANGUAGE_ID, RELIGION_ID, CAPITAL_CITY_ID, LEGITIMATE_LINEAGE_ID, " +
                    $"LEGITIMATE_SHI_ID, KINGDOM_TITLE, NAME_INTEGRATED, POLICY_ENABLED, " +
                    $"POLICY_AI_ENABLED, SLAVERY_ENABLED, SLAVE_ARMY_ENABLED, XIA_CONTACT_PROGRESS, " +
                    $"WAS_MANDATE, MANDATE_PERIOD_ID " +
                    $"FROM {KingdomContinuityTableItem.GetTableName()} WHERE KINGDOM_ID=@k LIMIT 1";
                cmd.Parameters.AddWithValue("@k", pKingdomId);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                if (!reader.Read()) return null;
                return new ContinuitySnapshot
                {
                    kingdomId = reader.GetInt64(0),
                    name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    foundedTime = reader.IsDBNull(2) ? -1.0 : reader.GetDouble(2),
                    originalActorAsset = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    cultureId = reader.IsDBNull(4) ? -1L : reader.GetInt64(4),
                    languageId = reader.IsDBNull(5) ? -1L : reader.GetInt64(5),
                    religionId = reader.IsDBNull(6) ? -1L : reader.GetInt64(6),
                    capitalCityId = reader.IsDBNull(7) ? -1L : reader.GetInt64(7),
                    legitimateLineageId = reader.IsDBNull(8) ? -1L : reader.GetInt64(8),
                    legitimateShiId = reader.IsDBNull(9) ? -1L : reader.GetInt64(9),
                    title = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                    integrated = !reader.IsDBNull(11) && reader.GetInt32(11) != 0,
                    policyEnabled = !reader.IsDBNull(12) && reader.GetInt32(12) != 0,
                    policyAiEnabled = !reader.IsDBNull(13) && reader.GetInt32(13) != 0,
                    slaveryEnabled = !reader.IsDBNull(14) && reader.GetInt32(14) != 0,
                    slaveArmyEnabled = !reader.IsDBNull(15) && reader.GetInt32(15) != 0,
                    xiaContactProgress = reader.IsDBNull(16) ? 0f : Convert.ToSingle(reader.GetValue(16)),
                    wasMandate = !reader.IsDBNull(17) && reader.GetInt32(17) != 0,
                    mandatePeriodId = reader.IsDBNull(18) ? -1L : reader.GetInt64(18)
                };
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Kingdom continuity read failed: " + e.Message);
                return null;
            }
        }

        private static int ReadRestorationCount(long pKingdomId)
        {
            if (!ArchiveReady || pKingdomId < 0) return 0;
            try
            {
                using var cmd = new SQLiteCommand(ArchiveDB);
                cmd.CommandText = $"SELECT RESTORATION_COUNT FROM {KingdomContinuityTableItem.GetTableName()} " +
                                  "WHERE KINGDOM_ID=@k LIMIT 1";
                cmd.Parameters.AddWithValue("@k", pKingdomId);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
            }
            catch { return 0; }
        }

        private static void MarkContinuityRestored(long pKingdomId)
        {
            if (!ArchiveReady || pKingdomId < 0) return;
            int count = ReadRestorationCount(pKingdomId);
            ArchiveDB.UpdateValue(KingdomContinuityTableItem.GetTableName(),
                new List<SimpleColumnConstraint>
                {
                    SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdomId)
                },
                ColumnVal.Create("RESTORATION_COUNT", count + 1),
                ColumnVal.Create("LAST_RESTORED_TIME", LineageService.CurTime()));
        }
    }
}
