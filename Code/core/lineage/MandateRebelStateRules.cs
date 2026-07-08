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
    }
}
