namespace AncientWarfare3.core.court
{
    public static class MilitaryGovernorateCourtRules
    {
        public const int GeneralRank = 40;
        public const int GovernorRank = GeneralRank;
        public const int SuccessorRank = 45;
        public const int LocalGovernmentRank = 50;
        public const int StableOrderBase = 1000000;

        public static bool ShouldInclude(bool pIsDirectVassal,
            bool pIsMilitaryGovernorate, bool pProjectionActive)
        {
            return pIsDirectVassal && pIsMilitaryGovernorate &&
                   pProjectionActive;
        }

        public static bool IsSubjectActor(bool pActorValid,
            long pActorKingdomId, long pSubjectKingdomId)
        {
            return pActorValid && pActorKingdomId >= 0 &&
                   pActorKingdomId == pSubjectKingdomId;
        }
    }
}
