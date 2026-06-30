using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.windows;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     Postfix UnitWindow.showStatsRows:Xia 有谱系者注入身份/姓/氏/家族树入口。
    ///     - 合流前贵族:身份「贵族」+ 姓(点→氏支列表) + 氏(点→氏族大树)
    ///     - 合流前平民/奴隶谱系:只身份(不显姓氏)
    ///     - 合流后:身份 + 氏(点→氏族大树),姓隐藏
    ///     - 一律加「家族树」按钮(点→本人家族树)
    ///
    ///     当前 actor 取 __instance.actor(=SelectedUnit.unit;新版无 Config.selectedUnit)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_UnitWindowPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitWindow), nameof(UnitWindow.showStatsRows))]
        public static void ShowStatsRows_Postfix(UnitWindow __instance)
        {
            var actor = __instance.actor;
            if (actor == null || actor.data == null) return;
            if (!LineageService.IsXia(actor)) return;

            actor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            if (lineageId < 0) return; // 无谱系记录不注入

            actor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
            actor.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            actor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            actor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);

            bool integrated = IsKingdomIntegrated(actor);

            // 身份行(label 经本地化键"身份"显示,value 原样中文;showStatRow 只本地化 label)
            ShowRawRow(__instance, "aw_identity", IdentityText(status));

            bool isNoble = status == LineageStatus.NOBLE;

            // 姓行(合流前贵族显示,点→该姓氏支列表)
            if (!integrated && isNoble && !string.IsNullOrEmpty(family))
            {
                var kvf = ShowRawRow(__instance, "aw_family_name", family);
                if (kvf != null)
                {
                    string f = family;
                    kvf.on_click_value = () => ShiBranchListWindow.OpenFor(f);
                }
            }

            // 氏行(合流前贵族 或 合流后所有人;点→氏族大树)
            if ((isNoble || integrated) && !string.IsNullOrEmpty(clan) && shiId >= 0)
            {
                var kvf = ShowRawRow(__instance, "aw_clan_name", clan);
                if (kvf != null)
                {
                    long s = shiId;
                    kvf.on_click_value = () => FamilyTreeWindow.OpenBigTree(s);
                }
            }

            // 家族树入口已移到 unit 窗侧栏按钮(AW_UnitTabPatch),此处不再加 stats 行。
        }

        /// <summary>
        ///     加一行:label(pId)经本地化键显示(年号/氏 等),value 原样(中文名)。
        ///     ⚠ StatsRowsContainer.showStatRow:70 只对 **pId(label)** 做 LM.getText,value 永远原样
        ///     (`pValue.ToString()`,:89)。故 pLocalize **必须 true**(否则 label 显示成 aw_clan_name 原始键),
        ///     value 不会因此被当本地化键(不会 missing text)。
        /// </summary>
        private static KeyValueField ShowRawRow(UnitWindow pWindow, string pId, string pValue)
        {
            return pWindow.showStatRow(pId, pValue);
        }

        private static bool IsKingdomIntegrated(Actor pActor)
        {
            var kingdom = pActor.kingdom;
            if (kingdom == null || kingdom.data == null) return false;
            kingdom.data.get(LineageKeys.KINGDOM_INTEGRATED, out bool integrated, false);
            return integrated;
        }

        private static string IdentityText(string pStatus)
        {
            if (pStatus == LineageStatus.NOBLE) return "贵族";
            if (pStatus == LineageStatus.COMMON) return "平民谱系";
            if (pStatus == LineageStatus.SLAVE) return "奴隶谱系";
            return "无";
        }
    }
}
