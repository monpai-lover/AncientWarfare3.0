namespace AncientWarfare3.core.lineage
{
    public enum ActorKingdomRepairSource
    {
        None = 0,
        City = 1,
        Wild = 2
    }

    public static class ActorKingdomSafetyRules
    {
        public static bool CanRunEnemyCheck(bool actorExists,
            bool actorAssetExists, bool kingdomAssetExists)
        {
            return actorExists && actorAssetExists && kingdomAssetExists;
        }

        public static bool CanEnterVanillaZoneProcessing(bool actorExists,
            bool actorAssetExists, bool tileExists,
            bool professionAssetExists, bool kingdomAssetExists)
        {
            return actorExists && actorAssetExists && tileExists &&
                   professionAssetExists && kingdomAssetExists;
        }

        public static bool CanRenderUnit(bool actorExists,
            bool actorAssetExists, bool tileExists)
        {
            return actorExists && actorAssetExists && tileExists;
        }

        public static bool IsCityKingdomRepairable(
            bool cityKingdomAssetExists, bool cityKingdomIsRekt)
        {
            return cityKingdomAssetExists && !cityKingdomIsRekt;
        }

        public static bool ShouldQueueDeferredRepair(
            bool immediateRepairSucceeded)
        {
            return !immediateRepairSucceeded;
        }

        public static bool ShouldDetachInvalidKingdomBeforeRepair(
            bool kingdomObjectExists, bool kingdomAssetExists)
        {
            return kingdomObjectExists && !kingdomAssetExists;
        }

        public static ActorKingdomRepairSource SelectRepairSource(
            bool actorExists, bool actorAssetExists,
            bool kingdomAssetExists, bool cityKingdomAssetExists,
            bool cityKingdomIsRekt, bool wildKingdomIdExists)
        {
            if (!actorExists || !actorAssetExists || kingdomAssetExists)
                return ActorKingdomRepairSource.None;
            if (IsCityKingdomRepairable(cityKingdomAssetExists,
                    cityKingdomIsRekt))
                return ActorKingdomRepairSource.City;
            return wildKingdomIdExists
                ? ActorKingdomRepairSource.Wild
                : ActorKingdomRepairSource.None;
        }
    }
}
