using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     Postfix KingdomWindow.showStatsRows:注入年号 / 头衔 / 继承人三行(后端已算好,UI 只读)。
    ///     继承人行可点击 → 选中并打开该继承人单位窗。
    ///
    ///     当前 kingdom 取 SelectedMetas.selected_kingdom(=原版 KingdomWindow.meta_object 来源;
    ///     新版无 Config.selectedKingdom)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_KingdomWindowPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomWindow), nameof(KingdomWindow.showStatsRows))]
        public static void ShowStatsRows_Postfix(KingdomWindow __instance)
        {
            var kingdom = SelectedMetas.selected_kingdom;
            if (kingdom == null || kingdom.data == null) return;

            // 年号
            string yearName = YearNameService.GetYearName(kingdom);
            if (!string.IsNullOrEmpty(yearName))
                __instance.showStatRow("aw_year_name", yearName);

            // 头衔(GetTitleString 接收 enum,先 GetTitle 取等级)
            string title = KingdomTitleService.GetTitleString(KingdomTitleService.GetTitle(kingdom));
            if (!string.IsNullOrEmpty(title))
                __instance.showStatRow("aw_kingdom_title", title);

            // 继承人(可点击 → 选中并 inspect)
            var heir = HeirService.GetHeir(kingdom);
            if (heir != null && !heir.isRekt())
            {
                var kvf = __instance.showStatRow("aw_heir", heir.getName());
                if (kvf != null)
                {
                    var h = heir;
                    kvf.on_click_value = () =>
                    {
                        SelectedUnit.makeMainSelected(h);
                        ScrollWindow.showWindow("unit");
                    };
                }
            }
        }
    }
}
