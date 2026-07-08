using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class SlaveKingAbdicationService
    {
        private static readonly Dictionary<long, string> PendingReasons = new Dictionary<long, string>();

        public static bool TryForceAbdicate(Actor pActor, string pReason, bool pWasKing, bool pWasSlave,
            Kingdom pKingdom)
        {
            if (pActor?.data == null) return false;
            bool isSlaveNow = SlaveService.IsSlave(pActor);
            if (!SlaveKingAbdicationRules.ShouldForceAbdicate(pWasKing, pWasSlave, isSlaveNow,
                    pKingdom?.data != null))
                return false;
            if (pKingdom.king != pActor) return false;

            PendingReasons[pActor.data.id] = pReason ?? "";
            pKingdom.kingLeftEvent();
            return true;
        }

        public static bool TryForceCurrentSlaveKing(Kingdom pKingdom, Actor pKing, string pReason)
        {
            bool isKing = pKingdom?.data != null && pKing?.data != null && pKingdom.king == pKing;
            return TryForceAbdicate(pKing, pReason, isKing, pWasSlave: true, pKingdom);
        }

        public static bool TryConsumeReason(long pActorId, out string pReason)
        {
            if (PendingReasons.TryGetValue(pActorId, out pReason))
            {
                PendingReasons.Remove(pActorId);
                return true;
            }

            pReason = "";
            return false;
        }
    }
}
