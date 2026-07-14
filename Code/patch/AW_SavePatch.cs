using System;
using AncientWarfare3.core.db;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using AncientWarfare3.content;
using AncientWarfare3.ui.windows;
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
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.saveWorldToDirectory))]
        public static void SaveWorldToDirectory_Prefix()
        {
            core.lineage.DeferredRuntimeWorkService.FlushPersistent();
            bool descentsResolved =
                HistoricalSchoolDescentService.FlushPendingDescentsForSave();
            bool deathsResolved = SchoolMembershipService.FlushDeathRetriesForSave();
            bool runtimeStateResolved = HistoricalSchoolRuntime.FlushPendingStateForSave();
            if (!descentsResolved || !deathsResolved || !runtimeStateResolved)
                throw new InvalidOperationException(
                    "World save blocked: unresolved school persistence");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.saveWorldToDirectory))]
        public static void SaveWorldToDirectory_Postfix(string pFolder)
        {
            if (string.IsNullOrEmpty(pFolder)) return;
            LineageArchiveManager.Instance.SaveToSaveDirectory(pFolder);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadWorld), new[] { typeof(string), typeof(bool) })]
        public static void LoadWorld_Postfix(string pPath)
        {
            if (string.IsNullOrEmpty(pPath)) return;
            LineageArchiveManager.Instance.LoadFromSaveDirectory(pPath);
            XiaSubspeciesRepair.EnsureWorldTraits();
            FigureStateStore.Load();
            core.lineage.KingdomArchiveWriter.BackfillAll();
            ResetHistoryWindowsAfterArchiveSwitch();
            SchoolMembershipService.LoadIndexes();
            HistoricalSchoolRuntime.LoadState();
            core.lineage.AWArmyService.RepairSpecialArmiesAfterLoad();
            core.lineage.WarPlotRedirectService.SweepExistingPlots();
            core.lineage.WarRecordWriter.BackfillActive(); // 重建进行中战争的内存缓存
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.generateNewMap))]
        public static void GenerateNewMap_Postfix()
        {
            LineageArchiveManager.Instance.CreateDataBase();
            core.lineage.WarPlotRedirectService.SweepExistingPlots();
            XiaSubspeciesRepair.EnsureWorldTraits();
            FigureStateStore.Load(); // 新世界:空库 → 全部重置为未生成
            ResetHistoryWindowsAfterArchiveSwitch();
            SchoolMembershipService.LoadIndexes();
            HistoricalSchoolRuntime.LoadState();
        }

        private static void ResetHistoryWindowsAfterArchiveSwitch()
        {
            try { core.lineage.DeferredRuntimeWorkService.ClearRuntimeState(); } catch { }
            try { core.lineage.SlaveCaptureScanService.Clear(); } catch { }
            try { core.lineage.RoyalGuardService.ClearRuntimeCaches(); } catch { }
            try { core.lineage.SlaveService.ClearRuntimeCaches(); } catch { }
            try { SchoolMapBottomBarController.Hide(); } catch { }
            try { AWMapModeMetaLibrary.ClearRuntimeCaches(); } catch { }
            try { SchoolWindow.ResetWorldCache(); } catch { }
            try { SchoolRosterWindow.ResetWorldCache(); } catch { }
            try { SchoolMembershipService.ClearRuntime(); } catch { }
            try { HistoricalSchoolRuntime.ClearRuntime(); } catch { }
            try { HistoryListWindow.ResetWorldCache(); } catch { }
            try { KingdomRosterWindow.ResetWorldCache(pRefreshIfCurrent: true); } catch { }
        }
    }
}
