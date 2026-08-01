using System;

namespace AncientWarfare3.core.policy
{
    public static class WesternRoyalAuthorityRules
    {
        public const int ConsolidationGain = 10;
        public const int MaximumConsolidatedAuthority = 30;
        public const int RitualOrderSuccessionBonus = 10;
        public const int RoyalDirectRuleSuccessionBonus = 5;
        public const int MinimumCourtInfluence = -60;
        public const int MaximumCourtInfluenceWithInstitutions = 70;

        public static int ApplyConsolidation(int pCurrentAuthority)
        {
            int current = Clamp(pCurrentAuthority, 0,
                MaximumConsolidatedAuthority);
            return Math.Min(MaximumConsolidatedAuthority,
                current + ConsolidationGain);
        }

        public static int ResolveSuccessionBonus(
            KingdomPolicyProfileId pProfile, bool ritualOrderCompleted,
            bool royalDirectRuleActive, int consolidatedAuthority)
        {
            if (pProfile != KingdomPolicyProfileId.WesternGeneral)
                return 0;

            int bonus = ritualOrderCompleted
                ? RitualOrderSuccessionBonus
                : 0;
            if (royalDirectRuleActive)
                bonus += RoyalDirectRuleSuccessionBonus +
                         Clamp(consolidatedAuthority, 0,
                             MaximumConsolidatedAuthority);
            return bonus;
        }

        public static int ApplyToCourtInfluence(int pBaseInfluence,
            int pInstitutionalBonus)
        {
            long combined = (long)pBaseInfluence +
                            Math.Max(0, pInstitutionalBonus);
            if (combined <= MinimumCourtInfluence)
                return MinimumCourtInfluence;
            if (combined >= MaximumCourtInfluenceWithInstitutions)
                return MaximumCourtInfluenceWithInstitutions;
            return (int)combined;
        }

        private static int Clamp(int pValue, int pMinimum, int pMaximum)
        {
            return Math.Max(pMinimum, Math.Min(pMaximum, pValue));
        }
    }
}
