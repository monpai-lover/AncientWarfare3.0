using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_NameplateTitlePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(NameplateText), "getStringForNameplate")]
        public static void GetStringForNameplate_Prefix(NameplateText __instance, ref string pName)
        {
            if (__instance == null || __instance.is_mini ||
                !(__instance.nano_object is Kingdom kingdom) || kingdom.data == null) return;

            bool rebel = MandateRebelService.IsRebelKingdom(kingdom);
            bool republic = RepublicGovernmentService.IsRepublic(kingdom);
            if (kingdom.data.original_actor_asset != LineageService.XIA_ASSET_ID &&
                !rebel && !republic) return;

            string suffix = KingdomTitleDisplayRules.GetNameplateTitleSuffix(
                (int)KingdomTitleService.GetTitle(kingdom),
                MandateService.IsRuntimeMandateKingdom(kingdom), rebel, republic);
            if (!string.IsNullOrEmpty(suffix)) pName += suffix;
        }
    }
}
