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
        public static bool ShouldRepairActor(bool actorExists,
            bool actorAlive, bool actorWrecked)
        {
            return actorExists && actorAlive && !actorWrecked;
        }

        public static bool CanRunEnemyCheck(bool actorExists,
            bool actorAssetExists, bool kingdomDataExists,
            bool kingdomAssetExists)
        {
            return actorExists && actorAssetExists &&
                   HasUsableKingdom(kingdomDataExists,
                       kingdomAssetExists);
        }

        public static bool CanEnterVanillaZoneProcessing(bool actorExists,
            bool actorAssetExists, bool tileExists,
            bool professionAssetExists, bool kingdomDataExists,
            bool kingdomAssetExists)
        {
            return actorExists && actorAssetExists && tileExists &&
                   professionAssetExists &&
                   HasUsableKingdom(kingdomDataExists,
                       kingdomAssetExists);
        }

        public static bool CanRenderUnit(bool actorExists,
            bool actorAssetExists, bool tileExists)
        {
            return actorExists && actorAssetExists && tileExists;
        }

        public static bool ShouldUseFallbackKingdomColor(
            bool kingdomObjectExists, bool kingdomDataExists,
            bool kingdomAssetExists)
        {
            return kingdomObjectExists &&
                   (!kingdomDataExists || !kingdomAssetExists);
        }

        public static bool ShouldSuppressKingdomDependentPresentation(
            bool actorExists, bool actorAssetExists,
            bool kingdomObjectExists)
        {
            return actorExists && actorAssetExists &&
                   !kingdomObjectExists;
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
            bool kingdomObjectExists, bool kingdomDataExists,
            bool kingdomAssetExists)
        {
            return kingdomObjectExists &&
                   !HasUsableKingdom(kingdomDataExists,
                       kingdomAssetExists);
        }

        public static ActorKingdomRepairSource SelectRepairSource(
            bool actorExists, bool actorAssetExists,
            bool kingdomDataExists, bool kingdomAssetExists,
            bool cityKingdomAssetExists,
            bool cityKingdomIsRekt, bool wildKingdomIdExists)
        {
            if (!actorExists || !actorAssetExists ||
                HasUsableKingdom(kingdomDataExists,
                    kingdomAssetExists))
                return ActorKingdomRepairSource.None;
            if (IsCityKingdomRepairable(cityKingdomAssetExists,
                    cityKingdomIsRekt))
                return ActorKingdomRepairSource.City;
            return wildKingdomIdExists
                ? ActorKingdomRepairSource.Wild
                : ActorKingdomRepairSource.None;
        }

        private static bool HasUsableKingdom(bool kingdomDataExists,
            bool kingdomAssetExists)
        {
            return kingdomDataExists && kingdomAssetExists;
        }
    }
}
