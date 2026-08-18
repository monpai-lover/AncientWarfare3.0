namespace AncientWarfare3.core.lineage
{
    public static class PeasantRebelBanditStrongholdPopulationRules
    {
        public static bool IsLivingResident(bool actorExists, bool alive,
            bool rekt, bool boat, bool belongsToCity)
        {
            return actorExists && alive && !rekt && !boat && belongsToCity;
        }

        public static bool ShouldQueueFall(bool activeStronghold,
            int livingResidents)
        {
            return activeStronghold && livingResidents <= 0;
        }
    }
}
