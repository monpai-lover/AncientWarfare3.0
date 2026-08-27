namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// Guards the vanilla Actor.loadFromSave path when a persisted city has
    /// not resolved its kingdom yet.  City.isNeutral() dereferences that
    /// kingdom directly, so an unresolved relation must be treated as the
    /// temporary neutral state during loading.
    /// </summary>
    public static class ActorLoadCitySafetyRules
    {
        public static bool ShouldUseNeutralFallback(bool cityExists,
            bool cityKingdomExists, bool cityKingdomAssetExists)
        {
            return !cityExists || !cityKingdomExists ||
                   !cityKingdomAssetExists;
        }

        public static bool CanRestorePersistedCity(bool cityExists,
            bool cityKingdomExists, bool cityKingdomAssetExists,
            bool actorKingdomExists, bool sameKingdom)
        {
            return cityExists && cityKingdomExists &&
                   cityKingdomAssetExists && actorKingdomExists &&
                   sameKingdom;
        }
    }
}
