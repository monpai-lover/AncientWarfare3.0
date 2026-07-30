namespace AncientWarfare3.core.lineage
{
    public static class OccupiedCitySupplyRules
    {
        public static bool CanProvideToRealm(bool cityValid,
            bool formalOwnerMatches, bool enemyFrozenControl)
        {
            return cityValid && formalOwnerMatches &&
                   !enemyFrozenControl;
        }

        public static bool CanRunLocalProduction(bool cityValid,
            bool enemyFrozenControl)
        {
            _ = enemyFrozenControl;
            return cityValid;
        }

        public static float RealmContributionMultiplier(
            bool enemyFrozenControl)
        {
            return enemyFrozenControl ? 0f : 1f;
        }
    }
}
