using AncientWarfare3.core.lineage;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_RepublicGovernmentPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomBehCheckKing), nameof(KingdomBehCheckKing.execute))]
        public static void CheckKing_Postfix(Kingdom pKingdom)
        {
            RepublicGovernmentService.RefreshAfterKingCheck(pKingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad)
        {
            if (pFromLoad || pActor?.data == null) return;
            RepublicGovernmentService.ClearRepublic(__instance, "king_restored");
        }
    }
}
