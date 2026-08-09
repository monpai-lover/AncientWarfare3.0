namespace AncientWarfare3.core.lineage
{
    public static class MilitaryGovernorateWarRules
    {
        public static bool CanExternalRealmTargetGovernorate(
            VassalSubjectKind pKind)
        {
            return pKind == VassalSubjectKind.MilitaryGovernorate;
        }

        public static long ResolveMainDefender(long pTargetKingdomId,
            long pRootSuzerainId, VassalSubjectKind pKind)
        {
            return pKind == VassalSubjectKind.MilitaryGovernorate &&
                   pRootSuzerainId >= 0
                ? pRootSuzerainId
                : pTargetKingdomId;
        }

        public static long ResolvePeaceController(long pTargetKingdomId,
            long pRootSuzerainId, VassalSubjectKind pKind)
        {
            return ResolveMainDefender(pTargetKingdomId, pRootSuzerainId,
                pKind);
        }

        public static bool CanUseStateProposal(
            VassalSubjectKind pRequesterKind,
            VassalSubjectKind pResponderKind)
        {
            return pRequesterKind != VassalSubjectKind.MilitaryGovernorate &&
                   pResponderKind != VassalSubjectKind.MilitaryGovernorate;
        }
    }
}
