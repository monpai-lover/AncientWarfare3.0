using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class CityAttackZoneService
    {
        private const string AttackTaskId =
            "warrior_army_leader_move_to_attack_target";

        public static void OnCityFrozenControlled(City pFrozenCity,
            Kingdom pOccupier)
        {
            if (pFrozenCity?.data == null || pOccupier?.data == null)
                return;
            KingdomWarDirectorService.OnCityControlChanged(pFrozenCity,
                pOccupier);
            ArmyRtsMode mode = ArmyRtsRuntimeMode.Current;
            bool liveRts = ArmyRtsRuntimeModeRules.ShouldCommit(mode);
            bool legacyWrites = ArmyRtsRuntimeModeRules.
                ShouldUseLegacyStrategicWrites(mode);
            if (liveRts)
            {
                CompleteLiveArmyTarget(pFrozenCity);
                return;
            }

            // Capture completion is a low-frequency event. Restrict the work
            // to the occupier's city armies instead of adding an actor tick.
            for (int i = 0; i < pOccupier.cities.Count; i++)
            {
                City source = pOccupier.cities[i];
                if (source?.data == null || source.kingdom != pOccupier)
                    continue;
                bool targetMatches = CityAttackZoneRules.
                    TargetMatchesFrozenCity(
                        source.target_attack_city == pFrozenCity,
                        source.target_attack_zone?.city == pFrozenCity);
                Army army = SafeArmy(source);
                Actor captain = SafeCaptain(army);
                bool hasOffensiveArmy = IsLiveOffensiveArmy(army,
                    captain);
                City nextTarget = legacyWrites && targetMatches &&
                                  hasOffensiveArmy
                    ? FindNextTargetCity(source)
                    : null;
                bool royalGuard = AWArmyService.IsRoleArmy(army,
                    AWArmyRole.RoyalGuard) ||
                    RoyalGuardService.IsRoyalGuard(captain);
                bool dedicatedGarrison =
                    WartimeGarrisonService.IsActive(captain);
                if (!CityAttackZoneRules.
                        ShouldReleaseFrozenAttackTarget(
                            targetMatches,
                            hasOffensiveArmy,
                            captain?.data != null && captain.isAlive() &&
                            !captain.isRekt(),
                            royalGuard,
                            dedicatedGarrison))
                    continue;

                source.target_attack_city = null;
                source.target_attack_zone = null;
                AWArmyMarchService.ClearArmy(army);
                if (!CityAttackZoneRules.
                        ShouldAdvanceAfterFrozenOccupation(
                            targetMatches,
                            hasOffensiveArmy,
                            captain?.data != null && captain.isAlive() &&
                            !captain.isRekt(),
                            nextTarget?.data != null,
                            royalGuard,
                            dedicatedGarrison))
                {
                    ReturnCaptainToSource(captain, source);
                    continue;
                }
                source.target_attack_city = nextTarget;
                RepairAfterTargetSelection(source);
                RestartCaptainAdvance(captain);
            }
        }

        private static void CompleteLiveArmyTarget(City pFrozenCity)
        {
            ArmyRtsControllerService.OnTargetCompleted(pFrozenCity);
        }

        internal static bool IsControlledBySide(War pWar, City pCity,
            Kingdom pKingdom)
        {
            return ResolveControlledSide(pWar, pCity, pKingdom,
                pEnemySide: false);
        }

        internal static bool IsControlledByEnemySide(War pWar, City pCity,
            Kingdom pKingdom)
        {
            return ResolveControlledSide(pWar, pCity, pKingdom,
                pEnemySide: true);
        }

        internal static bool HasHostileMilitaryInside(War pWar,
            City pCity, Kingdom pKingdom)
        {
            if (pWar?.data == null || pCity?.data == null ||
                pKingdom?.data == null || pCity.zones == null) return false;
            if (CityMilitaryThreatFacts.TryGet(pWar, pCity, pKingdom,
                    out bool cached))
                return cached;
            Kingdom[] militaryKingdoms;
            if (!CityMilitaryThreatFacts.TryGetPresence(pWar, pCity,
                    out militaryKingdoms))
            {
                var scan = new MilitaryPresenceScanContext();
                try
                {
                    CityMilitaryThreatFacts.RecordPhysicalScan();
                    for (int zoneIndex = 0;
                         zoneIndex < pCity.zones.Count; zoneIndex++)
                        ScanZone(pCity.zones[zoneIndex], scan);
                    if (pCity.border_zones != null)
                        foreach (TileZone borderZone in pCity.border_zones)
                            ScanZone(borderZone, scan);
                }
                catch { return false; }
                militaryKingdoms = scan.ToArray();
                CityMilitaryThreatFacts.StorePresence(pWar, pCity,
                    militaryKingdoms);
            }
            bool hostile = HasHostileMilitaryKingdom(pWar, pKingdom,
                militaryKingdoms);
            CityMilitaryThreatFacts.Store(pWar, pCity, pKingdom, hostile);
            return hostile;
        }

        private static void ScanZone(TileZone pZone,
            MilitaryPresenceScanContext pScan)
        {
            if (pZone?.chunk == null || pScan == null) return;
            pScan.AddZone(pZone);
        }

        private static bool HasHostileMilitaryKingdom(War pWar,
            Kingdom pObservingKingdom, Kingdom[] pMilitaryKingdoms)
        {
            var HostilityByKingdom =
                new Dictionary<long, bool>();
            if (pMilitaryKingdoms == null) return false;
            for (int index = 0; index < pMilitaryKingdoms.Length; index++)
            {
                Kingdom actorKingdom = pMilitaryKingdoms[index];
                if (actorKingdom?.data == null ||
                    actorKingdom == pObservingKingdom) continue;
                long actorKingdomId;
                try { actorKingdomId = actorKingdom.id; }
                catch { continue; }
                if (!HostilityByKingdom.TryGetValue(actorKingdomId,
                        out bool hostile))
                {
                    try
                    {
                        hostile = !pWar.onTheSameSide(pObservingKingdom,
                                      actorKingdom) &&
                                  pWar.isInWarWith(pObservingKingdom,
                                      actorKingdom);
                    }
                    catch { hostile = false; }
                    HostilityByKingdom[actorKingdomId] = hostile;
                }
                if (hostile) return true;
            }
            return false;
        }

        private sealed class MilitaryPresenceScanContext
        {
            private readonly HashSet<Kingdom> Kingdoms =
                new HashSet<Kingdom>();
            private readonly HashSet<TileZone> AllowedZones =
                new HashSet<TileZone>();
            private readonly HashSet<MapChunk> Chunks =
                new HashSet<MapChunk>();

            internal Kingdom[] ToArray()
            {
                foreach (MapChunk chunk in Chunks) ScanChunk(chunk);
                var result = new Kingdom[Kingdoms.Count];
                Kingdoms.CopyTo(result);
                return result;
            }

            internal void AddZone(TileZone pZone)
            {
                if (pZone?.chunk == null || !AllowedZones.Add(pZone))
                    return;
                Chunks.Add(pZone.chunk);
            }

            private void ScanChunk(MapChunk pChunk)
            {
                if (pChunk?.objects == null ||
                    pChunk.objects.kingdoms == null) return;

                // This is the same index used by vanilla EnemiesFinder.
                for (int kingdomIndex = 0;
                     kingdomIndex < pChunk.objects.kingdoms.Count;
                     kingdomIndex++)
                {
                    long kingdomId = pChunk.objects.kingdoms[kingdomIndex];
                    List<Actor> units;
                    try { units = pChunk.objects.getUnits(kingdomId); }
                    catch { continue; }
                    if (units == null) continue;
                    for (int unitIndex = 0; unitIndex < units.Count;
                         unitIndex++)
                        VisitActor(units[unitIndex]);
                }
            }

            private void VisitActor(Actor pActor)
            {
                Kingdom actorKingdom = pActor?.kingdom;
                if (pActor?.data == null ||
                    !AllowedZones.Contains(pActor.current_tile?.zone) ||
                    !pActor.is_profession_warrior ||
                    actorKingdom?.data == null) return;
                Kingdoms.Add(actorKingdom);
            }
        }

        private static bool ResolveControlledSide(War pWar, City pCity,
            Kingdom pKingdom, bool pEnemySide)
        {
            if (pWar?.data == null || pCity?.data == null ||
                pKingdom?.data == null) return false;
            Kingdom controller = null;
            bool persisted = false;
            try
            {
                if (WarScoreService.TryGetFrozenOccupation(pWar.data.id,
                        pCity.id, out long controllerId))
                {
                    controller = World.world?.kingdoms?.get(controllerId);
                    persisted = ControllerMatchesSide(pWar, pKingdom,
                        controller, pEnemySide);
                }
            }
            catch { }
            Kingdom physicalController = pCity.being_captured_by;
            bool physicalSide = ControllerMatchesSide(pWar, pKingdom,
                physicalController, pEnemySide);
            bool naturalLimit = CityOccupationAccelerationService.
                HasReachedNaturalCaptureLimit(pCity);
            bool activeDefenders = CityOccupationAccelerationService.
                HasActiveDefenders(pCity);
            return CityAttackZoneRules.ShouldTreatNaturalLimitAsControlled(
                persisted, naturalLimit, physicalSide, activeDefenders);
        }

        private static bool ControllerMatchesSide(War pWar,
            Kingdom pKingdom, Kingdom pController, bool pEnemySide)
        {
            if (pController?.data == null) return false;
            try
            {
                bool sameSide = pController == pKingdom ||
                                pWar.onTheSameSide(pKingdom, pController);
                return pEnemySide
                    ? !sameSide && pWar.isInWarWith(pKingdom, pController)
                    : sameSide;
            }
            catch { return !pEnemySide && pController == pKingdom; }
        }

        public static void RepairAfterTargetSelection(City pSourceCity)
        {
            if (pSourceCity?.data == null) return;
            if (!ArmyRtsRuntimeModeRules.ShouldUseLegacyStrategicWrites(
                    ArmyRtsRuntimeMode.Current)) return;
            City targetCity = pSourceCity.target_attack_city ??
                              pSourceCity.target_attack_zone?.city;
            ArmyRetreatService.OnAttackTargetAssigned(
                SafeArmy(pSourceCity), targetCity);
            if (ShouldReplaceFrozenTarget(pSourceCity, targetCity))
            {
                pSourceCity.target_attack_city = null;
                pSourceCity.target_attack_zone = null;
                AWArmyMarchService.ClearArmy(SafeArmy(pSourceCity));
                targetCity = FindNextTargetCity(pSourceCity);
                if (targetCity?.data == null) return;
                pSourceCity.target_attack_city = targetCity;
            }

            TileZone currentZone = pSourceCity.target_attack_zone;
            bool targetHasZones = false;
            try { targetHasZones = targetCity?.data != null && targetCity.hasZones(); }
            catch { }

            if (!CityAttackZoneRules.ShouldRepairTargetZone(
                    targetCity?.data != null,
                    targetHasZones,
                    currentZone != null,
                    currentZone?.city == targetCity))
                return;

            try { pSourceCity.target_attack_zone = targetCity.zones.GetRandom(); }
            catch { }
        }

        private static bool ShouldReplaceFrozenTarget(City pSourceCity,
            City pTargetCity)
        {
            Army army = SafeArmy(pSourceCity);
            if (!IsLiveOffensiveArmy(army, SafeCaptain(army)))
                return false;
            bool targetOtherwiseValid = IsOtherwiseValidTarget(
                pSourceCity, pTargetCity);
            bool frozenControlledBySource = targetOtherwiseValid &&
                WarScoreService.IsCityFrozenControlledBySide(
                    pTargetCity, pSourceCity.kingdom);
            bool frozenLockedAgainstSource = targetOtherwiseValid &&
                WarScoreService.IsCityFrozenOccupationLockedAgainst(
                    pTargetCity, pSourceCity.kingdom);
            return CityAttackZoneRules.ShouldInvalidateAttackTarget(
                targetOtherwiseValid, frozenControlledBySource ||
                                      frozenLockedAgainstSource);
        }

        private static City FindNextTargetCity(City pSourceCity)
        {
            Kingdom sourceKingdom = pSourceCity?.kingdom;
            if (sourceKingdom?.data == null) return null;
            City result = null;
            float bestDistance = float.MaxValue;
            try
            {
                using (var candidates = new ListPool<City>())
                {
                    World.world.wars.getWarCities(sourceKingdom, candidates);
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        City candidate = candidates[i];
                        bool alive = candidate?.data != null &&
                                     candidate.isAlive();
                        bool enemy = alive && candidate.kingdom?.data != null &&
                                     candidate.kingdom.isEnemy(sourceKingdom);
                        bool reachable = enemy && candidate.reachableFrom(
                                             pSourceCity);
                        bool frozenControlledBySource = reachable &&
                            WarScoreService.IsCityFrozenControlledBySide(
                                candidate, sourceKingdom);
                        bool frozenLockedAgainstSource = reachable &&
                            WarScoreService.
                                IsCityFrozenOccupationLockedAgainst(
                                    candidate, sourceKingdom);
                        if (!CityAttackZoneRules.CanSelectAttackCandidate(
                                alive, enemy, reachable,
                                frozenControlledBySource ||
                                frozenLockedAgainstSource)) continue;
                        float distance = Toolbox.SquaredDistVec2Float(
                            candidate.city_center, pSourceCity.city_center);
                        if (distance >= bestDistance) continue;
                        result = candidate;
                        bestDistance = distance;
                    }
                }
            }
            catch { return null; }
            return result;
        }

        private static bool IsOtherwiseValidTarget(City pSourceCity,
            City pTargetCity)
        {
            try
            {
                return pSourceCity?.kingdom?.data != null &&
                       pTargetCity?.data != null &&
                       pTargetCity.isAlive() &&
                       pSourceCity.hasAnyWarriors() &&
                       pTargetCity.kingdom?.data != null &&
                       pTargetCity.kingdom.isEnemy(pSourceCity.kingdom) &&
                       pTargetCity.reachableFrom(pSourceCity);
            }
            catch { return false; }
        }

        private static Army SafeArmy(City pCity)
        {
            try { return pCity?.hasArmy() == true ? pCity.getArmy() : null; }
            catch { return null; }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try { return pArmy?.data != null ? pArmy.getCaptain() : null; }
            catch { return null; }
        }

        private static bool IsLiveOffensiveArmy(Army pArmy,
            Actor pCaptain)
        {
            try
            {
                return pArmy?.data != null && pArmy.isAlive() &&
                       pArmy.countUnits() > 0 &&
                       pCaptain?.data != null && pCaptain.isAlive() &&
                       !pCaptain.isRekt() &&
                       !AWArmyService.IsRoleArmy(pArmy,
                           AWArmyRole.RoyalGuard) &&
                       !RoyalGuardService.IsRoyalGuard(pCaptain) &&
                       !WartimeGarrisonService.IsActive(pCaptain);
            }
            catch { return false; }
        }

        private static void RestartCaptainAdvance(Actor pCaptain)
        {
            if (pCaptain?.data == null || pCaptain.ai == null) return;
            try
            {
                pCaptain.clearOldPath();
                pCaptain.clearTileTarget();
                pCaptain.beh_tile_target = null;
                pCaptain.setTask(AttackTaskId, pClean: true,
                    pCleanJob: false, pForceAction: true);
            }
            catch { }
        }

        private static void ReturnCaptainToSource(Actor pCaptain,
            City pSourceCity)
        {
            if (pCaptain?.data == null || pSourceCity?.data == null)
                return;
            try
            {
                WorldTile target = pSourceCity.getTile();
                if (target == null || pCaptain.current_tile == null) return;
                pCaptain.clearOldPath();
                pCaptain.clearTileTarget();
                pCaptain.beh_tile_target = null;
                pCaptain.cancelAllBeh();
                pCaptain.goTo(target, pLimitPathfindingRegions: 6);
            }
            catch { }
        }
    }
}
