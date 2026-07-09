namespace AncientWarfare3.core.lineage
{
    public static class SlaveChronicleRules
    {
        public static bool ShouldRecordIndividualCityEnslavement(string pReason)
        {
            switch (pReason ?? "")
            {
                case "city_fall":
                case "battlefield_capture":
                case "captured":
                case "foreign_occupation":
                    return false;
                default:
                    return true;
            }
        }
    }
}
