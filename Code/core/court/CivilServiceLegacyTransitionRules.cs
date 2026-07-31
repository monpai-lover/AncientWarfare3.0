namespace AncientWarfare3.core.court
{
    public static class CivilServiceLegacyTransitionRules
    {
        public const int TransitionVersion = 1;

        public static bool ShouldIssueCredential(bool transitionAlreadyApplied,
            bool candidateEligibleBeforeExam, bool hasFormalQualification,
            bool alreadyHoldingFormalOffice)
        {
            return !transitionAlreadyApplied &&
                   candidateEligibleBeforeExam &&
                   !hasFormalQualification &&
                   !alreadyHoldingFormalOffice;
        }

        public static bool CanUseCredential(long issuerKingdomId,
            long appointmentKingdomId, bool credentialRemaining,
            bool isFormalAppointment)
        {
            return credentialRemaining && isFormalAppointment &&
                   issuerKingdomId >= 0L &&
                   issuerKingdomId == appointmentKingdomId;
        }
    }
}
