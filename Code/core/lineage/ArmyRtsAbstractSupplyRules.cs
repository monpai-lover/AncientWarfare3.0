namespace AncientWarfare3.core.lineage
{
    public static class ArmyRtsAbstractSupplyRules
    {
        public static bool CanAttempt(bool actorAlive,
            bool ownsLiveRtsActor, bool hasAnchorCity,
            bool anchorInActorKingdom)
        {
            return actorAlive && ownsLiveRtsActor && hasAnchorCity &&
                   anchorInActorKingdom;
        }

        public static bool ShouldSuppressVanillaFoodTask(bool supplied)
        {
            return supplied;
        }

        public static bool CanConsume(bool eligible,
            bool hasSuitableFood, bool hasFoodItem)
        {
            return eligible && hasSuitableFood && hasFoodItem;
        }
    }
}
