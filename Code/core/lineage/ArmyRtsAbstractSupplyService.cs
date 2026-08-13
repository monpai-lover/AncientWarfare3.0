namespace AncientWarfare3.core.lineage
{
    internal static class ArmyRtsAbstractSupplyService
    {
        public static bool TryConsumeHomeRation(Actor pActor)
        {
            try
            {
                bool actorAlive = pActor?.data != null &&
                                  pActor.isAlive() && !pActor.isRekt();
                bool ownsLiveRtsActor = actorAlive &&
                    ArmyRtsControllerService.OwnsLiveActor(pActor);
                City home = ownsLiveRtsActor
                    ? AWArmyService.FindAnchorCity(pActor.army)
                    : null;
                bool hasAnchorCity = home?.data != null && !home.isRekt();
                bool anchorInActorKingdom = hasAnchorCity &&
                    ReferenceEquals(home.kingdom, pActor.kingdom);
                bool eligible = ArmyRtsAbstractSupplyRules.CanAttempt(
                    actorAlive, ownsLiveRtsActor, hasAnchorCity,
                    anchorInActorKingdom);
                if (!eligible) return false;

                bool hasSuitableFood = home.hasSuitableFood(pActor.subspecies);
                ResourceAsset food = hasSuitableFood
                    ? home.getFoodItem(pActor.subspecies,
                        pActor.data.favorite_food)
                    : null;
                if (!ArmyRtsAbstractSupplyRules.CanConsume(eligible,
                        hasSuitableFood, food != null)) return false;

                home.eatFoodItem(food.id);
                pActor.consumeFoodResource(food);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
