namespace AncientWarfare3.core.lineage
{
    public static class WarPeaceSubjectTransferRules
    {
        public static bool CanOfferForceVassal(bool participantsValid,
            bool alreadySubjectToRecipient, bool wouldCreateCycle,
            bool hasThirdPartySuzerain = false)
        {
            return participantsValid && !alreadySubjectToRecipient &&
                   !wouldCreateCycle;
        }

        public static bool CanOfferForceTributary(bool participantsValid,
            bool targetIndependent)
        {
            return participantsValid && targetIndependent;
        }
    }
}
