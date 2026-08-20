namespace AncientWarfare3.core.court
{
    public static class DeJureRegionEligibilityRules
    {
        public static bool CanParticipate(bool liveCity, bool banditStronghold)
        {
            return liveCity && !banditStronghold;
        }
    }
}
