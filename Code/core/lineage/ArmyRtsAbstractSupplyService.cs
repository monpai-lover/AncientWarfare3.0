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
            long actorId = pActor?.data?.id ?? -1L;
            double now;
            try { now = Time.realtimeSinceStartupAsDouble; }
            catch { now = 0d; }
            double nextAllowed = actorId >= 0L &&
                                 NextScheduledCheck.TryGetValue(actorId,
                                     out double next)
                ? next
                : 0d;
            // ShouldRunScheduledCheck 是纯 AND,先判哪一项都等价。这里被 P0
            // 每帧按 actor 调用(约 6000 次/采样区间),而绝大多数调用都处在
            // 冷却窗口里。把最便宜的时间闸门提到最前,冷却中的调用就不必再付
            // HasActiveMilitaryP0Owner / IsRoyalGuard / needsFood / isHungry
            // 这几项原版查询的代价。
            if (now < nextAllowed) return false;
            bool actorAlive = pActor?.data != null && pActor.isAlive() &&
                              !pActor.isRekt();
            bool activeMilitaryOwner = actorAlive &&
                (ArmyRtsControllerService.HasActiveMilitaryP0Owner(pActor) ||
                 RoyalGuardService.IsRoyalGuard(pActor));
            bool hungry = false;
            try { hungry = actorAlive && activeMilitaryOwner &&
                           pActor.needsFood() && pActor.isHungry(); }
            catch { }
            bool shouldRun = ArmyRtsAbstractSupplyRules.
                ShouldRunScheduledCheck(actorAlive, activeMilitaryOwner,
                    hungry, now, nextAllowed);
            // 冷却戳原本只在 shouldRun 为真时才写,而 shouldRun 要求 hungry。
            // 于是不饿的 actor(绝大多数)永远拿不到戳,nextAllowed 恒为 0,
            // 上面那道时间闸门对它们永远不成立 —— 每帧照付四项原版查询。
            // 改成"评估过就盖戳":代价是刚变饿的 actor 最多多等一个冷却周期,
            // 而这本来就是 ScheduledCheckIntervalSeconds 定义的粒度。
            if (actorId >= 0L)
            {
                if (NextScheduledCheck.Count >= MaximumTrackedActors)
                    NextScheduledCheck.Clear();
                NextScheduledCheck[actorId] = now +
                                              ScheduledCheckIntervalSeconds;
            }
            if (!shouldRun) return false;
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
