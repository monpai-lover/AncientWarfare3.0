using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class VassalAnnexGuardReconciliationService
    {
        public static void Reconcile(Kingdom pSuzerain,
            Kingdom pAbsorbedKingdom, IReadOnlyList<Actor> pFormerGuards,
            bool pCityTransferCommitted, bool pRelationClosed)
        {
            if (!VassalAnnexGuardReconciliationRules.ShouldReconcile(
                    pCityTransferCommitted, pRelationClosed,
                    pAbsorbed: true)) return;

            if (pFormerGuards != null)
            {
                for (int i = 0; i < pFormerGuards.Count; i++)
                    RoyalGuardService.ReleaseAfterVassalAbsorption(
                        pAbsorbedKingdom, pFormerGuards[i]);
            }
            RoyalGuardService.ClearKingdomGuardStateForAbsorption(
                pAbsorbedKingdom);
            KingdomWarDirectorService.QueueArmyChanged(pSuzerain);
        }
    }
}
