namespace AncientWarfare3.core.lineage
{
    public static class XiaAuthorityGenderRules
    {
        public static bool ShouldAllowSetLeader(bool pIsXiaActor, bool pIsMale, bool pIsNewAppointment)
        {
            if (!pIsXiaActor) return true;
            return pIsMale;
        }

        public static bool ShouldAllowSetKing(bool pFromLoad, bool pCandidateIsMale, bool pCandidateIsXia,
            bool pKingdomIsXia)
        {
            if (!pCandidateIsXia && !pKingdomIsXia) return true;
            return pCandidateIsMale;
        }
    }
}
