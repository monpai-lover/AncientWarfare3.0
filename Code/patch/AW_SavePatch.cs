using System;
using AncientWarfare3.core.db;
using AncientWarfare3.core.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.county;
using AncientWarfare3.core.schools;
using AncientWarfare3.core.lineage;
using AncientWarfare3.content;
using AncientWarfare3.ui.windows;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.presentation;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     档案库随游戏存档持久化(AW2 没做完的部分):
    ///     - Postfix SaveManager.saveWorldToDirectory:存档时把运行时档案库复制进存档目录。
    ///     - Postfix SaveManager.loadWorld(string):读档时从存档目录恢复档案库(无则建空库)。
    ///     - Postfix MapBox.generateNewMap:新世界时清空运行时库重建。
    ///
    ///     均为 Postfix 注入,不接管原流程。
    /// </summary>
    [HarmonyPatch]
    public static class AW_SavePatch
    {
        [ThreadStatic]
        private static int _multiplayerSnapshotSaveDepth;
        [ThreadStatic]
        private static bool _ownsAsyncSaveBarrier;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.saveWorldToDirectory))]
        public static void SaveWorldToDirectory_Prefix()
        {
            if (_multiplayerSnapshotSaveDepth > 0) return;
            // 存档前先恢复可推断的城市箭塔归属，再清理无法恢复的 null 建筑，
            // 防止脏数据写入存档文件。
            AW_XiaWatchTowerKingdomPatch.RestoreCityWatchTowerKingdoms();
            // 成因：watch_tower_Xia 之前版本 asset.kingdom="nomads_Xia" 但
            // kingdoms_wild 实例不存在，setBuilding 路径①把 null 赋给 building.kingdom。
            PurgeNullKingdomBuildings();
            RepairNullKingdomAssets();
            try
            {
                EnterOwnedSaveBoundary();
                if (!TryPrepareForSave(out string error,
                        out Exception cause))
                    throw AWSaveBoundaryException.CreateBlocked(
                        error, cause);
            }
            catch
            {
                ExitOwnedSaveBarrier();
                throw;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.saveWorldToDirectory))]
        public static void SaveWorldToDirectory_Postfix(string pFolder)
        {
            if (_multiplayerSnapshotSaveDepth > 0) return;
            try
            {
                if (string.IsNullOrEmpty(pFolder)) return;
                SyntheticMobilizationLedgerService.TryWriteSnapshot(pFolder,
                    out string mobilizationSnapshotError);
                if (!string.IsNullOrEmpty(mobilizationSnapshotError))
                {
                    ModClass.LogWarning(
                        "Synthetic mobilization snapshot write failed");
                    ModClass.LogWarning(mobilizationSnapshotError);
                }
                if (!LineageArchiveManager.Instance.TryExportLineageArchive(
                        pFolder, out string error))
                {
                    ModClass.LogWarning(
                        "LineageArchiveManager: lineage export failed");
                    ModClass.LogWarning(error);
                }
                ArmyRtsPlanSnapshotService.PublishToSave(pFolder);
                AW3SaveDirectoryRegistry.Observe(pFolder);
                DeJureRegionStore.PublishToSave(pFolder);
                CountyAdministrationStore.PublishToSave(pFolder);
            }
            finally
            {
                ExitOwnedSaveBarrier();
            }
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.saveWorldToDirectory))]
        private static Exception SaveWorldToDirectory_Finalizer(
            Exception __exception)
        {
            ExitOwnedSaveBarrier();
            return __exception;
        }

        internal static bool TryPrepareForSave(out string pError)
        {
            return TryPrepareForSave(out pError, out _);
        }

        internal static bool TryPrepareForSave(out string pError,
            out Exception pCause)
        {
            pError = string.Empty;
            pCause = null;
            try
            {
                core.lineage.DeferredRuntimeWorkService.FlushPersistent();
                string deathArchiveError = string.Empty;
                string asyncWriteError = string.Empty;
                HistoricalSchoolSavePreparationResult preparation =
                    HistoricalSchoolSavePreparation.Run(
                        HistoricalSchoolDescentService.
                            FlushPendingDescentsForSave,
                        SchoolMembershipService.FlushDeathRetriesForSave,
                        NobleRankService.
                            FlushPendingDeathSuccessionsForSave,
                        HistoricalSchoolWriteBufferService.FlushForSave,
                        HistoricalSchoolActivityQueue.
                            FlushPendingPersistenceForSave,
                        () =>
                        {
                            bool flushed = HistoricalSchoolWriteBufferService.
                                FlushForSave();
                            core.lineage.DeferredRuntimeWorkService.
                                FlushPersistent();
                            return HistoricalSchoolWriteBufferService.
                                FlushForSave() && flushed;
                        },
                        () => ActorDeathArchiveService.FlushForSave(
                            TimeSpan.FromSeconds(ActorDeathArchiveRules.
                                ResolveSaveTimeoutSeconds(5,
                                    ActorDeathArchiveService.PendingCount)),
                            out deathArchiveError),
                        () => HistoricalWriteService.FlushForSave(
                            TimeSpan.FromSeconds(5), out asyncWriteError),
                        HistoricalSchoolRuntime.FlushPendingStateForSave);
                if (!preparation.AllResolved)
                {
                    pError = "unresolved school persistence " +
                             "descents=" + preparation.DescentsResolved +
                             " deaths=" + preparation.DeathsResolved +
                             " noble_deaths=" +
                             preparation.NobleDeathsResolved +
                             " runtime=" + preparation.RuntimeStateResolved +
                             " runtime_attempted=" +
                             preparation.RuntimeStateAttempted +
                             " prior_writes=" +
                             preparation.PriorWritesResolved +
                             " activities=" +
                             preparation.ActivitiesResolved +
                             " writes=" + preparation.WritesResolved +
                             " death_archives=" +
                             preparation.DeathArchivesResolved +
                             " death_archive_error=" + deathArchiveError +
                             " async_writes=" +
                             preparation.AsyncWritesResolved +
                             " async_error=" + asyncWriteError +
                             " buffered=" +
                             HistoricalSchoolWriteBufferService.Count;
                    return false;
                }

                if (!LineageArchivePragmaService.CheckpointForSave(
                        LineageArchiveManager.Instance.OperatingDB))
                {
                    pError = "lineage archive checkpoint failed";
                    return false;
                }

                return true;
            }
            catch (Exception error)
            {
                pError = error.Message;
                pCause = error;
                return false;
            }
        }

        internal static IDisposable EnterMultiplayerSnapshotSave()
        {
            if (_multiplayerSnapshotSaveDepth == 0)
                EnterOwnedSaveBoundary();
            _multiplayerSnapshotSaveDepth++;
            return new MultiplayerSnapshotSaveScope();
        }

        private static void EnterOwnedSaveBoundary()
        {
            if (!AWAsyncWorldLifecycle.TryEnterSaveBarrier(
                    TimeSpan.FromSeconds(5),
                    AW_FramePrioritySchedulerPatch
                        .DrainSimulationToSaveBoundary,
                    out string barrierError))
                throw AWSaveBoundaryException.CreateBlocked(
                    barrierError, null);
            _ownsAsyncSaveBarrier = true;
        }

        private static void ExitOwnedSaveBarrier()
        {
            if (!_ownsAsyncSaveBarrier) return;
            _ownsAsyncSaveBarrier = false;
            AWAsyncWorldLifecycle.ExitSaveBarrier();
        }

        private sealed class MultiplayerSnapshotSaveScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_multiplayerSnapshotSaveDepth > 0)
                    _multiplayerSnapshotSaveDepth--;
                if (_multiplayerSnapshotSaveDepth == 0)
                    ExitOwnedSaveBarrier();
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadWorld), new[] { typeof(string), typeof(bool) })]
        public static void LoadWorld_Prefix(string pPath)
        {
            CityReservePoolService.BeginWorldLoadRestore();
            ArmyRtsPlanSnapshotService.ObserveLoadDirectory(pPath);
            AW3SaveDirectoryRegistry.Observe(pPath);
            DeJureRegionStore.ObserveLoadDirectory(pPath);
            CountyAdministrationStore.ObserveLoadDirectory(pPath);
            AW3WorldLoadCoordinator.ObserveLoadWorldStarted(pPath);
        }

        // 读档结束后清理 kingdom==null 的建筑，防止 ChunkObjectContainer.addBuilding
        // 取 pBuilding.kingdom.id 崩溃。成因见 SaveWorldToDirectory_Prefix 注释。
        internal static void PurgeNullKingdomBuildings()
        {
            if (World.world?.buildings == null) return;
            var toRemove = new System.Collections.Generic.List<Building>();
            foreach (Building b in World.world.buildings.getSimpleList())
            {
                if (b?.data == null || b.isRemoved() || b.isOnRemove()) continue;
                if (b.kingdom == null)
                    toRemove.Add(b);
            }
            foreach (Building b in toRemove)
            {
                try { b.removeBuildingFinal(); }
                catch (System.Exception e)
                {
                    ModClass.LogWarning(
                        "PurgeNullKingdomBuildings: removal failed id=" +
                        (b.data?.id ?? -1L) + " " + e.Message);
                }
            }
            if (toRemove.Count > 0)
                ModClass.LogInfo("[AW3] 清理 kingdom==null 建筑 " +
                    toRemove.Count + " 个");
        }

        /// <summary>
        ///     修复 asset == null 的王国。
        ///
        ///     成因两条:
        ///     ① <c>Kingdom.load2</c> 用
        ///        <c>AssetManager.kingdoms.get(actorAsset.kingdom_id_civilization)</c>
        ///        取 asset,取不到时**静默**赋 null 并永远保持 null —— 存档里的
        ///        王国指向一个当时没注册上的王国资产(改过 id、读档时 AW3 资产还
        ///        没注册完)就会这样。
        ///     ② <c>Kingdom.Dispose</c> 把 asset 置 null,而 chunk 的
        ///        objects.kingdoms 存的是 id,仍能通过 getCivOrWildViaID 反查到。
        ///
        ///     后果不是"少显示一点东西":<c>Kingdom.isCiv()</c> 是裸的
        ///     <c>return asset.civ;</c>,而 EnemyFinderContainer.getData 内联的
        ///     findEnemiesOfKingdomInChunk 会对 chunk 里**每一个**王国调
        ///     isEnemy → isCiv。塔一扫到这种王国就抛 NRE,而且它一直在那儿,
        ///     所以是每帧重复崩(用户反馈里的 "repeated 67 times")。
        /// </summary>
        internal static void RepairNullKingdomAssets()
        {
            int repaired = 0;
            int unresolved = 0;
            RepairKingdomAssets(World.world?.kingdoms, pWild: false,
                ref repaired, ref unresolved);
            RepairKingdomAssets(World.world?.kingdoms_wild, pWild: true,
                ref repaired, ref unresolved);
            if (repaired > 0)
                ModClass.LogInfo("[AW3] 修复 asset==null 王国 " + repaired +
                    " 个");
            if (unresolved > 0)
                ModClass.LogWarning("[AW3] 仍有 " + unresolved +
                    " 个王国无法解析 asset;敌人查找对它们会返回空表");
        }

        private static void RepairKingdomAssets(
            System.Collections.Generic.IEnumerable<Kingdom> pKingdoms,
            bool pWild, ref int pRepaired, ref int pUnresolved)
        {
            if (pKingdoms == null) return;
            var targets = new System.Collections.Generic.List<Kingdom>();
            foreach (Kingdom kingdom in pKingdoms)
            {
                if (kingdom?.data != null && kingdom.asset == null)
                    targets.Add(kingdom);
            }

            foreach (Kingdom kingdom in targets)
            {
                KingdomAsset asset = ResolveKingdomAsset(kingdom, pWild);
                if (asset == null) { pUnresolved++; continue; }
                kingdom.asset = asset;
                pRepaired++;
            }
        }

        private static KingdomAsset ResolveKingdomAsset(Kingdom pKingdom,
            bool pWild)
        {
            KingdomAsset fromFounder = LookupKingdomAsset(
                SafeActorAsset(pKingdom), pWild);
            if (fromFounder != null) return fromFounder;

            // 建国者资产解析不出来时退回到现存成员:王国存在就还有人。
            try
            {
                foreach (Actor unit in pKingdom.units)
                {
                    if (unit?.data == null || !unit.isAlive()) continue;
                    KingdomAsset fromUnit = LookupKingdomAsset(unit.asset,
                        pWild);
                    if (fromUnit != null) return fromUnit;
                }
            }
            catch { }

            return null;
        }

        private static ActorAsset SafeActorAsset(Kingdom pKingdom)
        {
            try { return pKingdom.getActorAsset(); }
            catch { return null; }
        }

        private static KingdomAsset LookupKingdomAsset(ActorAsset pActorAsset,
            bool pWild)
        {
            string id = pWild
                ? pActorAsset?.kingdom_id_wild
                : pActorAsset?.kingdom_id_civilization;
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                return AssetManager.kingdoms.has(id)
                    ? AssetManager.kingdoms.get(id)
                    : null;
            }
            catch { return null; }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadData),
            new[] { typeof(SavedMap), typeof(string) })]
        public static void LoadData_Postfix(string pPath)
        {
            AW3WorldLoadCoordinator.ObserveWorldDataQueued(pPath);
            ArmyRtsPlanSnapshotService.ObserveLoadDirectory(pPath);
            AW3SaveDirectoryRegistry.Observe(pPath);
            DeJureRegionStore.ObserveLoadDirectory(pPath);
            CountyAdministrationStore.ObserveLoadDirectory(pPath);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.generateNewMap))]
        public static void GenerateNewMap_Prefix()
        {
            AW3WorldLoadCoordinator.ObserveGeneratedWorldQueued();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.generateNewMap))]
        public static void GenerateNewMap_Postfix()
        {
            ArmyRtsPlanSnapshotService.OnNewWorldGenerated();
            AW3SaveDirectoryRegistry.ClearForNewWorld();
            DeJureRegionStore.ClearForNewWorld();
            CountyAdministrationStore.ClearForNewWorld();
            SocialIdentityMigrationService.ResetForNewWorld();
            ClanMembershipSyncService.ClearRuntime();
            FamilyIdentitySyncService.ClearRuntime();
        }
    }
}
