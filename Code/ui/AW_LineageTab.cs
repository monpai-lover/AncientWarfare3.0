using System.Collections.Generic;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;

namespace AncientWarfare3.ui
{
    /// <summary>
    ///     自定义「姓族」分栏 tab。两个 group:
    ///     - lineage:「姓族总览」按钮(Click,打开 LineageOverviewWindow)
    ///     - creature:「生成 Xia」神力按钮(GodPower,点地图生成夏人,照搬 AW2 spawn_xia)
    ///     tab 顶图标用夏朝专属 iconXias(对齐 AW2)。由 ModClass.OnModLoad 调用 Init()。
    /// </summary>
    internal static class AW_LineageTab
    {
        private const string TAB_ID = "AW3Lineage";
        private const string GROUP_LINEAGE = "lineage";
        private const string GROUP_CREATURE = "creature";
        private static bool _inited;

        public static void Init()
        {
            if (_inited) return;
            _inited = true;

            PowersTab tab = TabManager.CreateTab(
                TAB_ID,
                "AW3 Lineage",               // titleKey:本地化键(others.csv 注册→姓族档案)
                "AW3 Lineage Description",   // descKey:hover 描述键(others.csv 注册)
                SpriteTextureLoader.getSprite("ui/Icons/iconXias")); // tab 图标:夏朝专属(对齐 AW2)

            tab.SetLayout(new List<string> { GROUP_LINEAGE, GROUP_CREATURE });

            // 姓族总览(Click 按钮,图标 iconClan 区分于生成按钮)
            PowerButton overviewButton = PowerButtonCreator.CreateSimpleButton(
                "aw_lineage_overview_btn",
                () => OpenOverview(),
                SpriteTextureLoader.getSprite("ui/icons/iconClan"));
            tab.AddPowerButton(GROUP_LINEAGE, overviewButton);

            // 全王国列表(含亡国)按钮 —— 用户指定放本自定义 tab(有神力按钮的这个),非 kingdom 窗。
            PowerButton rosterButton = PowerButtonCreator.CreateSimpleButton(
                "aw_kingdom_roster_btn",
                () => OpenRoster(),
                SpriteTextureLoader.getSprite("ui/icons/iconKingdomList")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconClan"));
            tab.AddPowerButton(GROUP_LINEAGE, rosterButton);

            // 生成 Xia 神力(GodPower 按钮,id 必须 = power id "spawn_xia";power 已在 GodPowerLibrary.Init 注册)
            PowerButton spawnButton = PowerButtonCreator.CreateGodPowerButton(
                content.GodPowerLibrary.SPAWN_XIA,
                SpriteTextureLoader.getSprite("ui/Icons/iconXias"));
            tab.AddPowerButton(GROUP_CREATURE, spawnButton);

            // 历史人物开关(toggle 按钮,绑定 aw_toggle_figure power;默认开,见 HistoricalFigureService)。
            // 关闭后 TrySpawnOn 早退,不再生成历史人物。
            PowerButton figureToggle = PowerButtonCreator.CreateToggleButton(
                content.figures.HistoricalFigureService.TOGGLE_POWER_ID,
                SpriteTextureLoader.getSprite("ui/Icons/iconKings"));
            if (figureToggle != null) tab.AddPowerButton(GROUP_CREATURE, figureToggle);

            tab.UpdateLayout();
        }

        private static void OpenOverview()
        {
            windows.LineageOverviewWindow.Open();
        }

        private static void OpenRoster()
        {
            windows.KingdomRosterWindow.Open();
        }
    }
}
