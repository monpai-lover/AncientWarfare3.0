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
            if (pFromLoad || __instance?.data == null || pActor?.data == null) return;
            bool wasRepublic = RepublicGovernmentService.IsRepublic(__instance);
            bool registeredSuccessor =
                RepublicGovernmentService.IsRegisteredRepublicSuccessor(__instance, pActor);
            bool markedLeader = RepublicGovernmentService.IsRepublicLeader(pActor);
            bool preserveRepublic = RepublicGovernmentRules.ShouldPreserveRepublicOnSetKing(
                wasRepublic, registeredSuccessor, markedLeader);
            if (preserveRepublic)
            {
                RepublicGovernmentService.MarkRepublicLeader(pActor);
                RepublicGovernmentService.RefreshRepublicSuccessor(__instance, pActor);
                return;
            }

            if (RepublicGovernmentRules.ShouldClearRepublicLeaderMarker(
                    preserveRepublic, markedLeader))
                RepublicGovernmentService.ClearRepublicLeader(pActor);
            RepublicGovernmentService.ClearRepublic(__instance, "king_restored");
        }
    }
}
