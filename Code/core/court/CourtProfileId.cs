namespace AncientWarfare3.core.court
{
    public enum CourtProfileId
    {
        None = 0,
        Xia = 1,
        Western = 2
    }

    public static class CourtProfileRules
    {
        public static bool CanHoldTogether(CourtProfileId firstProfile,
            string firstLayer, CourtProfileId secondProfile,
            string secondLayer)
        {
            if (firstProfile == CourtProfileId.None ||
                secondProfile == CourtProfileId.None) return true;
            if (firstProfile == secondProfile) return true;
            return firstLayer != CourtOfficeLayer.Central ||
                   secondLayer != CourtOfficeLayer.Central;
        }
    }
}
