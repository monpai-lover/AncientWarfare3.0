using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.windows;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     Injects AW social identity and lineage rows into the unit stats window.
    ///     Identity is independent from lineage: slaves can exist without LINEAGE_ID.
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

            actor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
            if (SlaveService.IsSlave(actor))
                status = LineageStatus.SLAVE;

            actor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            bool hasLineage = lineageId >= 0;
            if (!hasLineage && status == LineageStatus.NONE) return;

            actor.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            actor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            actor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);

            bool integrated = IsKingdomIntegrated(actor);
            ShowRawRow(__instance, "aw_identity", IdentityText(status));

            bool isNoble = status == LineageStatus.NOBLE;
            if (hasLineage && !integrated && isNoble && !string.IsNullOrEmpty(family))
            {
                var kvf = ShowRawRow(__instance, "aw_family_name", family);
                if (kvf != null)
                {
                    string f = family;
                    kvf.on_click_value = () => ShiBranchListWindow.OpenFor(f);
                }
            }

            if (hasLineage && (isNoble || integrated) && !string.IsNullOrEmpty(clan) && shiId >= 0)
            {
                var kvf = ShowRawRow(__instance, "aw_clan_name", clan);
                if (kvf != null)
                {
                    long s = shiId;
                    kvf.on_click_value = () => FamilyTreeWindow.OpenBigTree(s);
                }
            }
        }

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
            if (pStatus == LineageStatus.NOBLE) return "\u8D35\u65CF";
            if (pStatus == LineageStatus.COMMON) return "\u5E73\u6C11";
            if (pStatus == LineageStatus.SLAVE) return "\u5974\u96B6";
            return "\u65E0";
        }
    }
}
