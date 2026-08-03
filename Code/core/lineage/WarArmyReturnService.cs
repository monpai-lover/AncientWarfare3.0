using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class WarArmyReturnService
    {
        private const int MaximumArmiesPerFrame = 4;

        private sealed class ReturnOrder
        {
            internal long ArmyId;
            internal long KingdomId;
            internal long TargetCityId;
        }

        private static readonly Dictionary<long, ReturnOrder> Orders =
            new Dictionary<long, ReturnOrder>();
        private static readonly Queue<long> Work = new Queue<long>();

        public static bool TryBegin(Army pArmy)
        {
            if (!IsLiveArmy(pArmy)) return false;
            Kingdom kingdom = AWArmyService.GetIntendedKingdom(pArmy);
            if (!IsLiveKingdom(kingdom)) return false;
            Actor captain = SafeCaptain(pArmy);
            if (!IsAlive(captain) || captain.kingdom != kingdom) return false;
            if (IsInsideFriendlySafeCity(captain, kingdom))
            {
                Orders.Remove(pArmy.id);
                return true;
            }

            City target = ResolveTargetCity(pArmy, kingdom);
            if (!IsFriendlySafeCity(target, kingdom)) return false;
            if (!Orders.TryGetValue(pArmy.id, out ReturnOrder order))
            {
                order = new ReturnOrder { ArmyId = pArmy.id };
                Orders[pArmy.id] = order;
                Work.Enqueue(pArmy.id);
            }
            order.KingdomId = kingdom.id;
            order.TargetCityId = target.id;
            return true;
        }

        public static void ProcessFrame()
        {
            int count = Math.Min(MaximumArmiesPerFrame, Work.Count);
            for (int i = 0; i < count; i++)
            {
                long armyId = Work.Dequeue();
                if (!Orders.TryGetValue(armyId, out ReturnOrder order))
                    continue;
                Army army = ResolveArmy(order.ArmyId);
                Kingdom kingdom = ResolveKingdom(order.KingdomId);
                Actor captain = SafeCaptain(army);
                if (!IsLiveArmy(army) || !IsLiveKingdom(kingdom) ||
                    !IsAlive(captain) || captain.kingdom != kingdom ||
                    AWArmyService.GetIntendedKingdom(army) != kingdom)
                {
                    Orders.Remove(armyId);
                    continue;
                }
                WarArmyReturnOrderDecision decision = WarArmyReturnRules.
                    ResolveOrder(armyAlive: true,
                        insideFriendlySafeCity: IsInsideFriendlySafeCity(
                            captain, kingdom),
                        hasValidMission: ArmyRtsControllerService.
                            HasValidMission(army));
                if (decision != WarArmyReturnOrderDecision.Continue)
                {
                    Orders.Remove(armyId);
                    continue;
                }

                City target = ResolveCity(order.TargetCityId);
                if (!IsFriendlySafeCity(target, kingdom))
                {
                    target = ResolveTargetCity(army, kingdom);
                    if (!IsFriendlySafeCity(target, kingdom))
                    {
                        Orders.Remove(armyId);
                        continue;
                    }
                    order.TargetCityId = target.id;
                }
                IssueMovement(captain, target);
                Work.Enqueue(armyId);
            }
        }

        public static void Cancel(long pArmyId)
        {
            if (pArmyId >= 0L) Orders.Remove(pArmyId);
        }

        public static void ClearRuntime()
        {
            Orders.Clear();
            Work.Clear();
        }

        private static void IssueMovement(Actor pCaptain, City pTargetCity)
        {
            WorldTile target = SafeCityTile(pTargetCity);
            if (!IsAlive(pCaptain) || pCaptain.current_tile?.data == null ||
                target?.data == null) return;
            if (!SameIsland(pCaptain.current_tile, target))
            {
                ArmyRtsTransportService.TryHandleActor(pCaptain, target,
                    pMayBegin: true);
                return;
            }
            if (pCaptain.is_moving) return;
            try { pCaptain.goTo(target, pLimitPathfindingRegions: 6); }
            catch { }
        }

        private static City ResolveTargetCity(Army pArmy, Kingdom pKingdom)
        {
            City anchor = AWArmyService.FindAnchorCity(pArmy);
            if (IsFriendlySafeCity(anchor, pKingdom)) return anchor;
            if (IsFriendlySafeCity(pKingdom?.capital, pKingdom))
                return pKingdom.capital;
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (IsFriendlySafeCity(city, pKingdom)) return city;
            }
            catch { }
            return null;
        }

        private static bool IsInsideFriendlySafeCity(Actor pActor,
            Kingdom pKingdom)
        {
            City city;
            try { city = pActor?.current_tile?.zone?.city; }
            catch { city = null; }
            return IsFriendlySafeCity(city, pKingdom);
        }

        private static bool IsFriendlySafeCity(City pCity,
            Kingdom pKingdom)
        {
            if (!IsLiveCity(pCity) || !IsLiveKingdom(pKingdom) ||
                pCity.kingdom != pKingdom) return false;
            try
            {
                return !pCity.isGettingCaptured() &&
                       OccupiedCitySupplyService.CanProvideToRealm(
                           pCity, pKingdom);
            }
            catch { return false; }
        }

        private static WorldTile SafeCityTile(City pCity)
        {
            try { return pCity?.getTile(); }
            catch { return null; }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try { return pArmy?.getCaptain(); }
            catch { return null; }
        }

        private static bool SameIsland(WorldTile pFirst, WorldTile pSecond)
        {
            if (pFirst?.data == null || pSecond?.data == null) return false;
            try { return pFirst.isSameIsland(pSecond); }
            catch { return false; }
        }

        private static bool IsLiveArmy(Army pArmy)
        {
            try
            {
                return pArmy?.data != null && pArmy.isAlive() &&
                       pArmy.countUnits() > 0;
            }
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

        private static Army ResolveArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
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
