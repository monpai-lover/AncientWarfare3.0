using System;
using System.Collections.Generic;
using life.taxi;

namespace AncientWarfare3.core.lineage
{
    internal static class TemporaryMilitaryReturnService
    {
        private const int MaximumActorsPerFrame = 12;

        private sealed class ReturnOrder
        {
            internal long ActorId;
            internal long KingdomId;
            internal long TargetCityId;
        }

        private static readonly Dictionary<long, ReturnOrder> Orders =
            new Dictionary<long, ReturnOrder>();
        private static readonly Queue<long> Work = new Queue<long>();

        public static bool TryBeginOrComplete(Actor pActor)
        {
            if (!IsAlive(pActor))
            {
                Remove(pActor?.data?.id ?? -1L);
                return true;
            }
            if (ActiveMilitaryLifecycleService.
                    HasWartimeMilitaryLock(pActor)) return false;

            Kingdom kingdom = pActor.kingdom;
            if (!IsLiveKingdom(kingdom)) return true;
            bool safe = IsInsideFriendlySafeCity(pActor, kingdom);
            if (ActiveMilitaryLifecycleRules.CanDemobilizeAtLocation(
                    actorAlive: true, hasActiveWar: false,
                    insideHomeKingdom: safe, inFriendlySafeCity: safe))
            {
                Remove(pActor.data.id);
                return true;
            }

            City target = ResolveTargetCity(pActor, kingdom);
            if (!IsLiveCity(target)) return false;
            if (!Orders.TryGetValue(pActor.data.id, out ReturnOrder order))
            {
                order = new ReturnOrder
                {
                    ActorId = pActor.data.id,
                    KingdomId = kingdom.id,
                    TargetCityId = target.id
                };
                Orders[pActor.data.id] = order;
                Work.Enqueue(pActor.data.id);
            }
            else
            {
                order.KingdomId = kingdom.id;
                order.TargetCityId = target.id;
            }
            IssueMovement(pActor, target);
            return false;
        }

        public static void ProcessFrame()
        {
            int count = Math.Min(MaximumActorsPerFrame, Work.Count);
            for (int i = 0; i < count; i++)
            {
                long actorId = Work.Dequeue();
                if (!Orders.TryGetValue(actorId, out ReturnOrder order))
                    continue;
                Actor actor = ResolveActor(order.ActorId);
                Kingdom kingdom = ResolveKingdom(order.KingdomId);
                if (!IsAlive(actor) || !IsLiveKingdom(kingdom) ||
                    actor.kingdom != kingdom)
                {
                    Orders.Remove(actorId);
                    continue;
                }
                if (ActiveMilitaryLifecycleService.
                        HasWartimeMilitaryLock(actor))
                {
                    Work.Enqueue(actorId);
                    continue;
                }
                if (IsInsideFriendlySafeCity(actor, kingdom))
                {
                    Orders.Remove(actorId);
                    TemporaryMilitaryDemobilizationService.
                        RestoreCivilian(actor);
                    continue;
                }
                City target = ResolveCity(order.TargetCityId);
                if (!IsLiveCity(target) || target.kingdom != kingdom)
                {
                    target = ResolveTargetCity(actor, kingdom);
                    if (IsLiveCity(target)) order.TargetCityId = target.id;
                }
                if (IsLiveCity(target)) IssueMovement(actor, target);
                Work.Enqueue(actorId);
            }
        }

        public static void ClearRuntime()
        {
            Orders.Clear();
            Work.Clear();
        }

        private static void IssueMovement(Actor pActor, City pTargetCity)
        {
            WorldTile target = null;
            try { target = pTargetCity?.getTile(); }
            catch { }
            if (pActor?.current_tile?.data == null || target?.data == null)
                return;
            if (!SameIsland(pActor.current_tile, target))
            {
                if (pActor.army?.data != null &&
                    ArmyRtsTransportService.TryHandleActor(pActor, target,
                        pMayBegin: true)) return;
                EnsureDetachedTaxiRequest(pActor, target);
                return;
            }
            if (pActor.is_moving) return;
            try { pActor.goTo(target, pLimitPathfindingRegions: 6); }
            catch { }
        }

        private static void EnsureDetachedTaxiRequest(Actor pActor,
            WorldTile pTarget)
        {
            if (pActor?.data == null || pTarget?.data == null ||
                pActor.is_inside_boat) return;
            try
            {
                TaxiRequest request = TaxiManager.getRequestForActor(pActor);
                if (request?.getTileTarget()?.data?.tile_id ==
                    pTarget.data.tile_id) return;
                TaxiManager.newRequest(pActor, pTarget);
            }
            catch { }
        }

        private static bool IsInsideFriendlySafeCity(Actor pActor,
            Kingdom pKingdom)
        {
            if (!IsAlive(pActor) || !IsLiveKingdom(pKingdom)) return false;
            City city;
            try { city = pActor.current_tile?.zone?.city; }
            catch { city = null; }
            if (!IsLiveCity(city) || city.kingdom != pKingdom) return false;
            try
            {
                return !city.isGettingCaptured() &&
                       OccupiedCitySupplyService.CanProvideToRealm(city,
                           pKingdom);
            }
            catch { return false; }
        }

        private static City ResolveTargetCity(Actor pActor,
            Kingdom pKingdom)
        {
            City city = pActor?.city;
            if (IsLiveCity(city) && city.kingdom == pKingdom) return city;
            if (IsLiveCity(pKingdom?.capital) &&
                pKingdom.capital.kingdom == pKingdom)
                return pKingdom.capital;
            try
            {
                foreach (City candidate in pKingdom.getCities())
                    if (IsLiveCity(candidate)) return candidate;
            }
            catch { }
            return null;
        }

        private static void Remove(long pActorId)
        {
            if (pActorId >= 0L) Orders.Remove(pActorId);
        }

        private static bool SameIsland(WorldTile pFirst, WorldTile pSecond)
        {
            if (pFirst?.data == null || pSecond?.data == null) return false;
            try { return pFirst.isSameIsland(pSecond); }
            catch { return false; }
        }

        private static bool IsAlive(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.isAlive() &&
                       !pActor.isRekt();
            }
            catch { return false; }
        }

        private static bool IsLiveKingdom(Kingdom pKingdom)
        {
            try
            {
                return pKingdom?.data != null && pKingdom.isAlive() &&
                       !pKingdom.isRekt();
            }
            catch { return false; }
        }

        private static bool IsLiveCity(City pCity)
        {
            try
            {
                return pCity?.data != null && pCity.isAlive() &&
                       !pCity.isRekt();
            }
            catch { return false; }
        }

        private static Actor ResolveActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static City ResolveCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }
    }
}
