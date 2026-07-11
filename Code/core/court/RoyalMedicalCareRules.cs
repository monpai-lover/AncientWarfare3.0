namespace AncientWarfare3.core.court
{
    public static class RoyalMedicalCareRules
    {
        public static bool ShouldTreat(bool physicianAlive, bool physicianActive,
            bool sameKingdom, bool patientAlive)
        {
            return physicianAlive && physicianActive && sameKingdom && patientAlive;
        }

        public static bool ShouldRecordCure(int removedCurableTraits)
        {
            return removedCurableTraits > 0;
        }
    }
}
