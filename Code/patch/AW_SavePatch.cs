using System;
using AncientWarfare3.core.db;
using AncientWarfare3.core.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
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
            AW3WorldLoadCoordinator.ObserveLoadWorldStarted(pPath);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadData),
            new[] { typeof(SavedMap), typeof(string) })]
        public static void LoadData_Postfix(string pPath)
        {
            AW3WorldLoadCoordinator.ObserveWorldDataQueued(pPath);
            ArmyRtsPlanSnapshotService.ObserveLoadDirectory(pPath);
            AW3SaveDirectoryRegistry.Observe(pPath);
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
        }
    }
}
