using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class WarArmyReturnService
    {
        private const int MaximumArmiesPerFrame = 4;
        private const int MaximumQueueScansPerFrame = 64;

        private static readonly WarArmyReturnQueueCore Queue =
            new WarArmyReturnQueueCore();
        private static bool _rebuildPending;

        public static bool TryBegin(Army pArmy)
        {
            if (!IsLiveArmy(pArmy)) return false;
            Kingdom kingdom = AWArmyService.GetIntendedKingdom(pArmy);
            if (!IsLiveKingdom(kingdom)) return false;
            Actor captain = SafeCaptain(pArmy);
            if (!IsAlive(captain) || captain.kingdom != kingdom) return false;
            if (IsInsideFriendlySafeCity(captain, kingdom))
            {
                Finish(pArmy.id, pArmy);
                return true;
            }

            City target = ResolveTargetCity(pArmy, kingdom);
            if (!IsFriendlySafeCity(target, kingdom)) return false;
            if (!Queue.Begin(pArmy.id, kingdom.id, target.id)) return false;
            Persist(pArmy, kingdom.id, target.id);
            return true;
        }

        public static void ProcessFrame()
        {
            if (_rebuildPending) RebuildRuntime();
            IReadOnlyList<WarArmyReturnQueueOrder> frame = Queue.TakeFrame(
                MaximumArmiesPerFrame, MaximumQueueScansPerFrame);
            for (int i = 0; i < frame.Count; i++)
            {
                WarArmyReturnQueueOrder order = frame[i];
                long armyId = order.ArmyId;
                Army army = ResolveArmy(order.ArmyId);
                Kingdom kingdom = ResolveKingdom(order.KingdomId);
                Actor captain = SafeCaptain(army);
                if (!IsLiveArmy(army) || !IsLiveKingdom(kingdom) ||
                    !IsAlive(captain) || captain.kingdom != kingdom ||
                    AWArmyService.GetIntendedKingdom(army) != kingdom)
                {
                    Finish(armyId, army);
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
                    Finish(armyId, army);
                    continue;
                }

                City target = ResolveCity(order.TargetCityId);
                if (!IsFriendlySafeCity(target, kingdom))
                {
                    target = ResolveTargetCity(army, kingdom);
                    if (!IsFriendlySafeCity(target, kingdom))
                    {
                        Finish(armyId, army);
                        continue;
                    }
                    Queue.UpdateTarget(armyId, target.id);
                    Persist(army, kingdom.id, target.id);
                }
                IssueMovement(captain, target);
                Queue.Requeue(armyId);
            }
        }

        public static void Cancel(long pArmyId)
        {
            if (pArmyId < 0L) return;
            Queue.Cancel(pArmyId);
            ClearPersisted(ResolveArmy(pArmyId));
        }

        public static void OnArmyDisposed(Army pArmy)
        {
            if (pArmy == null) return;
            Queue.RemoveDisposed(pArmy.id);
            ClearPersisted(pArmy);
        }

        public static void ClearRuntime()
        {
            Queue.Clear();
            _rebuildPending = true;
        }

        public static void RebuildRuntime()
        {
            Queue.Clear();
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
            {
                _rebuildPending = true;
                return;
            }
            _rebuildPending = false;
            if (World.world?.armies == null)
            {
                _rebuildPending = true;
                return;
            }
            foreach (Army army in World.world.armies)
                TryRestore(army);
        }

        private static void TryRestore(Army pArmy)
        {
            WarArmyReturnStoredIntent stored = ReadPersisted(pArmy);
            if (stored == null || !stored.Active) return;
            Kingdom kingdom = ResolveKingdom(stored.KingdomId);
            Actor captain = SafeCaptain(pArmy);
            City storedTarget = ResolveCity(stored.TargetCityId);
            City replacement = IsFriendlySafeCity(storedTarget, kingdom)
                ? storedTarget
                : ResolveTargetCity(pArmy, kingdom);
            var facts = new WarArmyReturnRestoreFacts
            {
                ArmyAlive = IsLiveArmy(pArmy) && IsAlive(captain),
                ArmyKingdomMatches = IsLiveKingdom(kingdom) &&
                    captain?.kingdom == kingdom &&
                    AWArmyService.GetIntendedKingdom(pArmy) == kingdom,
                InsideFriendlySafeCity = IsInsideFriendlySafeCity(captain,
                    kingdom),
                HasValidMission = ArmyRtsControllerService.
                    HasValidMission(pArmy),
                StoredTargetFriendlySafe = IsFriendlySafeCity(storedTarget,
                    kingdom),
                ReplacementTargetCityId = replacement?.id ?? -1L
            };
            if (!WarArmyReturnPersistenceRules.TryRestore(stored, facts,
                    out WarArmyReturnStoredIntent restored) ||
                !Queue.Begin(restored.ArmyId, restored.KingdomId,
                    restored.TargetCityId))
            {
                ClearPersisted(pArmy);
                return;
            }
            Persist(pArmy, restored.KingdomId, restored.TargetCityId);
        }

        private static void Finish(long pArmyId, Army pArmy)
        {
            Queue.Complete(pArmyId);
            ClearPersisted(pArmy);
        }

        private static void Persist(Army pArmy, long pKingdomId,
            long pTargetCityId)
        {
            if (pArmy?.data == null) return;
            pArmy.data.set(LineageKeys.AW_ARMY_RETURN_ACTIVE, true);
            pArmy.data.set(LineageKeys.AW_ARMY_RETURN_KINGDOM_ID,
                pKingdomId);
            pArmy.data.set(LineageKeys.AW_ARMY_RETURN_TARGET_CITY_ID,
                pTargetCityId);
        }

        private static WarArmyReturnStoredIntent ReadPersisted(Army pArmy)
        {
            if (pArmy?.data == null) return null;
            pArmy.data.get(LineageKeys.AW_ARMY_RETURN_ACTIVE,
                out bool active, false);
            if (!active) return null;
            pArmy.data.get(LineageKeys.AW_ARMY_RETURN_KINGDOM_ID,
                out long kingdomId, -1L);
            pArmy.data.get(LineageKeys.AW_ARMY_RETURN_TARGET_CITY_ID,
                out long targetCityId, -1L);
            return new WarArmyReturnStoredIntent
            {
                Active = true,
                ArmyId = pArmy.id,
                KingdomId = kingdomId,
                TargetCityId = targetCityId
            };
        }

        private static void ClearPersisted(Army pArmy)
        {
            if (pArmy?.data == null) return;
            pArmy.data.removeBool(LineageKeys.AW_ARMY_RETURN_ACTIVE);
            pArmy.data.removeLong(LineageKeys.AW_ARMY_RETURN_KINGDOM_ID);
            pArmy.data.removeLong(LineageKeys.AW_ARMY_RETURN_TARGET_CITY_ID);
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
