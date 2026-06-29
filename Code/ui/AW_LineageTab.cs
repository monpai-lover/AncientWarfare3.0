using System.Collections.Generic;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;

namespace AncientWarfare3.ui
{
    /// <summary>
    ///     自定义「姓族」分栏 tab + 「姓族列表」按钮(照搬 AW2 TabManager 写法,NML 新版 API)。
    ///     由 ModClass.OnModLoad 末尾调用 Init()。
    /// </summary>
    internal static class AW_LineageTab
    {
        private const string TAB_ID = "AW3Lineage";
        private const string GROUP = "lineage";
        private static bool _inited;

        public static void Init()
        {
            if (_inited) return;
            _inited = true;

            PowersTab tab = TabManager.CreateTab(
                TAB_ID,
                "AW3 Lineage",
                "Ancient Warfare 3 lineage / surname archive",
                SpriteTextureLoader.getSprite("ui/icons/iconClan"));

            tab.SetLayout(new List<string> { GROUP });

            PowerButton overviewButton = PowerButtonCreator.CreateSimpleButton(
                "aw_lineage_overview_btn",
                () => OpenOverview(),
                SpriteTextureLoader.getSprite("ui/icons/iconClan"));

            tab.AddPowerButton(GROUP, overviewButton);
            tab.UpdateLayout();
        }

        private static void OpenOverview()
        {
            // Task 3 接好窗口后改为 ScrollWindow.showWindow(AW_LineageWindowIds.OVERVIEW)
            ModClass.LogInfo("[AW3] 姓族列表按钮点击(总览窗待 Task 3 接入)");
        }
    }
}
