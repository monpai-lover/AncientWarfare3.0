using AncientWarfare3.core.naming;
using HarmonyLib;

namespace AncientWarfare3.patch.naming
{
    [HarmonyPatch(typeof(LocalizedTextManager),
        nameof(LocalizedTextManager.setLanguage))]
    internal static class AW_LanguageChangeNamePatch
    {
        [HarmonyPostfix]
        private static void SetLanguage_Postfix()
        {
            AWLocalizedNameRefreshService.Request();
        }
    }
}
