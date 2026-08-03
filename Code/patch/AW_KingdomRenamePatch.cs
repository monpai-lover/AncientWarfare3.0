using System;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_KingdomRenamePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(NanoObject), nameof(NanoObject.setName))]
        public static void SetName_Prefix(NanoObject __instance, out string __state)
        {
            __state = __instance is Kingdom kingdom
                ? kingdom.name
                : (__instance is City city ? city.data?.name : null);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NanoObject), nameof(NanoObject.setName))]
        public static void SetName_Postfix(NanoObject __instance, string pName, bool pTrack, string __state)
        {
            if (__instance is City city)
            {
                if (!string.Equals(__state, city.data?.name,
                        StringComparison.Ordinal))
                    HierarchicalVassalMapModeService.MarkCityDirty(city);
                return;
            }
            if (__instance is not Kingdom kingdom) return;
            string committedName = kingdom.name ?? kingdom.data?.name ??
                                   pName ?? "";
            KingdomRenameSyncService.OnKingdomNameChanged(kingdom, __state,
                committedName, pTrack);
            if (AWLocalizedNameProjectionChangeRules.ShouldInvalidate(
                    __state, committedName) &&
                AWLocalizedNameProjectionRefreshScope.
                    ShouldRefreshAutomatically(
                        AWLocalizedKingdomNameService.IsEditing(kingdom)))
                KingdomRenameProjectionService.Refresh(kingdom);
        }
    }
}
