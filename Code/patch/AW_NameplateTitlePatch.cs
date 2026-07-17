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
            string cached = RulerAppellationService.GetCompactLivingAppellation(kingdom);
            if (!string.IsNullOrEmpty(cached)) pName = cached;
        }
    }
}
