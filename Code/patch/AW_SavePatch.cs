using AncientWarfare3.core.db;
using AncientWarfare3.content;
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
            FigureStateStore.Load(); // DB 已切到该存档的库 → 刷新历史人物生成状态内存缓存
            core.lineage.KingdomArchiveWriter.BackfillAll(); // 老存档/已有王国立即进万国史名册
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.generateNewMap))]
        public static void GenerateNewMap_Postfix()
        {
            LineageArchiveManager.Instance.CreateDataBase();
            XiaSubspeciesRepair.EnsureWorldTraits();
            FigureStateStore.Load(); // 新世界:空库 → 全部重置为未生成
        }
    }
}
