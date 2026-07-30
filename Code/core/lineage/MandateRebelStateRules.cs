using AncientWarfare3.content.policies;

namespace AncientWarfare3.core.lineage
{
    public static class MandateRebelStateRules
    {
        public static bool IsCurrentRebelGovernment(bool pRebelFlag, string pClassState,
            string pOriginType, string pClaimantKind)
        {
            if (pRebelFlag) return true;
            return pClassState == KingdomPolicyDefs.ClassRebel;
        }

        public static string SettledClassAfterRebellion(string pClassState)
        {
            return pClassState == KingdomPolicyDefs.ClassRebel || string.IsNullOrEmpty(pClassState)
                ? KingdomPolicyDefs.ClassDefault
                : pClassState;
        }

        public static bool ShouldUseActiveClaimantCache(int pCachedYear, int pCurrentYear,
            int pCachedKingdomCount, int pCurrentKingdomCount)
        {
            return pCachedYear == pCurrentYear && pCachedKingdomCount == pCurrentKingdomCount;
        }

        public static bool CanClaimFormerDynastyMandate(
            bool pMandateActive, long pPreviousMandateKingdomId,
            long pRebelOriginKingdomId, bool pOriginKingdomAlive,
            bool pActiveRebellionAgainstOrigin, int pLegalCoreCount,
            double pCoreControlRatio, double pClaimThreshold)
        {
            if (pMandateActive || pPreviousMandateKingdomId < 0 ||
                pRebelOriginKingdomId != pPreviousMandateKingdomId)
                return false;
            if (!pOriginKingdomAlive || !pActiveRebellionAgainstOrigin ||
                pLegalCoreCount <= 0)
                return false;
            return pCoreControlRatio + 0.0001d >= pClaimThreshold;
        }
    }
}
