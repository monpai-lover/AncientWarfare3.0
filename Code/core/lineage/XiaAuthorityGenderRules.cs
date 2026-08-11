namespace AncientWarfare3.core.lineage
{
    public static class XiaAuthorityGenderRules
    {
        public static bool IsSuccessionCandidateSexEligible(bool pIsMale,
            bool pFemaleSuccessionAllowed)
        {
            return pIsMale || pFemaleSuccessionAllowed;
        }

        public static bool ShouldAllowSetLeader(bool pUsesXiaLaw,
            bool pIsMale, bool pIsNewAppointment,
            bool pFemaleSuccessionAllowed)
        {
            return !pIsNewAppointment ||
                   IsSuccessionCandidateSexEligible(pIsMale,
                       pFemaleSuccessionAllowed);
        }

        public static bool ShouldAllowSetKing(bool pCandidateIsMale,
            bool pCandidateIsXia, bool pKingdomIsXia,
            bool pFemaleSuccessionAllowed)
        {
            return IsSuccessionCandidateSexEligible(pCandidateIsMale,
                pFemaleSuccessionAllowed);
        }
    }
}
