using AncientWarfare3.core.lineage;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_NameplateTitlePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(NameplateText), "showTextKingdom")]
        public static void ShowTextKingdom_Postfix(NameplateText __instance, Kingdom pMetaObject, Vector2 pPosition)
        {
            if (pMetaObject?.data == null) return;

            bool isRebel = MandateRebelService.IsRebelKingdom(pMetaObject);
            bool isRepublic = RepublicGovernmentService.IsRepublic(pMetaObject);
            if (pMetaObject.data.original_actor_asset != LineageService.XIA_ASSET_ID && !isRebel && !isRepublic) return;
            if (!__instance.is_full) return;

            var title = KingdomTitleService.GetTitle(pMetaObject);
            bool isMandate = MandateService.IsMandateKingdom(pMetaObject);
            string titleSuffix = KingdomTitleDisplayRules.GetNameplateTitleSuffix((int)title, isMandate, isRebel,
                isRepublic);
            if (string.IsNullOrEmpty(titleSuffix)) return;

            int pop = pMetaObject.getPopulationPeople();
            string baseStr = __instance.getStringForNameplate(pMetaObject.name + titleSuffix, pop);
            __instance.setText(baseStr, pPosition);
        }
    }
}
