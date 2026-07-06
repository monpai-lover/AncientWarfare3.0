namespace AncientWarfare3.core.policy
{
    public static class CityTechChronicleRules
    {
        public static bool ShouldRecordNationalCompletionInKingdomHistory()
        {
            return true;
        }

        public static bool ShouldRecordCityAdoptionInKingdomHistory()
        {
            return false;
        }
    }
}
