using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyRtsAbstractSupplyService
    {
        private const double ScheduledCheckIntervalSeconds = 1d;
        private const int MaximumTrackedActors = 8192;
        private static readonly Dictionary<long, double> NextScheduledCheck =
            new Dictionary<long, double>();

        internal static void ClearRuntime()
        {
            NextScheduledCheck.Clear();
        }

        internal static bool TryConsumeHomeRationScheduled(Actor pActor)
        {
            bool actorAlive = pActor?.data != null && pActor.isAlive() &&
                              !pActor.isRekt();
            long actorId = pActor?.data?.id ?? -1L;
            bool activeMilitaryOwner = actorAlive &&
                (ArmyRtsControllerService.HasActiveMilitaryP0Owner(pActor) ||
                 RoyalGuardService.IsRoyalGuard(pActor));
            double now;
            try { now = Time.realtimeSinceStartupAsDouble; }
            catch { now = 0d; }
            double nextAllowed = actorId >= 0L &&
                                 NextScheduledCheck.TryGetValue(actorId,
                                     out double next)
                ? next
                : 0d;
            bool hungry = false;
            try { hungry = actorAlive && pActor.needsFood() && pActor.isHungry(); }
            catch { }
            if (!ArmyRtsAbstractSupplyRules.ShouldRunScheduledCheck(
                    actorAlive, activeMilitaryOwner, hungry, now,
                    nextAllowed)) return false;
            if (NextScheduledCheck.Count >= MaximumTrackedActors)
                NextScheduledCheck.Clear();
            NextScheduledCheck[actorId] = now +
                                          ScheduledCheckIntervalSeconds;
            return TryConsumeHomeRation(pActor);
        }

        public static bool TryConsumeHomeRation(Actor pActor)
        {
            try
            {
                bool actorAlive = pActor?.data != null &&
                                  pActor.isAlive() && !pActor.isRekt();
                bool ownsLiveRtsActor = actorAlive &&
                    (ArmyRtsControllerService.
                         HasActiveMilitaryP0Owner(pActor) ||
                     RoyalGuardService.IsRoyalGuard(pActor));
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
