using AncientWarfare3.core.lineage;
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
            __state = __instance is Kingdom kingdom ? kingdom.name : null;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NanoObject), nameof(NanoObject.setName))]
        public static void SetName_Postfix(NanoObject __instance, string pName, bool pTrack, string __state)
        {
            if (__instance is not Kingdom kingdom) return;
            KingdomRenameSyncService.OnKingdomNameChanged(kingdom, __state, pName, pTrack);
            RulerAppellationService.RefreshLivingProjection(kingdom);
        }
    }
}
