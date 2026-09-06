namespace AncientWarfare3.core.lineage
{
    public static class MilitaryGovernorateWarRules
    {
        public static bool CanExternalRealmTargetGovernorate(
            VassalSubjectKind pKind)
        {
            return pKind == VassalSubjectKind.MilitaryGovernorate;
        }

        public static long ResolveMainDefender(long pAttackerKingdomId,
            long pTargetKingdomId, long pRootSuzerainId,
            VassalSubjectKind pKind)
        {
            return pKind == VassalSubjectKind.MilitaryGovernorate &&
                   pRootSuzerainId >= 0 &&
                   pRootSuzerainId != pAttackerKingdomId
                ? pRootSuzerainId
                : pTargetKingdomId;
        }

        public static long ResolveMainDefender(long pTargetKingdomId,
            long pRootSuzerainId, VassalSubjectKind pKind)
        {
            return ResolveMainDefender(-1L, pTargetKingdomId,
                pRootSuzerainId, pKind);
        }

        public static long ResolvePeaceController(long pTargetKingdomId,
            long pRootSuzerainId, VassalSubjectKind pKind)
        {
            return ResolveMainDefender(-1L, pTargetKingdomId,
                pRootSuzerainId, pKind);
        }

        public static bool ShouldBreakDirectRelationForWar(
            bool pIndependenceWar,
            bool pSuzerainAttacksMilitaryGovernorate)
        {
            return !pIndependenceWar && !pSuzerainAttacksMilitaryGovernorate;
        }

        public static bool ShouldAllowGovernorReplacement(
            bool independenceWar, bool governorateIsAttacker,
            bool attackersWon, bool defendersWon)
        {
            return independenceWar &&
                   (governorateIsAttacker ? defendersWon : attackersWon);
        }

        public static bool ShouldExecuteGovernorForFailedIndependenceWar(
            bool independenceWar, bool governorateIsAttacker,
            bool defendersWon)
        {
            return independenceWar && governorateIsAttacker && defendersWon;
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
