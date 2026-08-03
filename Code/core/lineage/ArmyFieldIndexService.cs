using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyFieldIndexService
    {
        private static readonly ArmyFieldIdentityIndex Index =
            new ArmyFieldIdentityIndex();
        private static readonly Dictionary<long, long> CanonicalArmyByCity =
            new Dictionary<long, long>();
        private static readonly Dictionary<long, long> CityByArmy =
            new Dictionary<long, long>();

        public static void OnArmyChanged(Army pArmy)
        {
            Kingdom changedKingdom = SafeKingdom(pArmy);
            if (!IsFieldArmy(pArmy, out Kingdom kingdom))
            {
                if (pArmy != null)
                {
                    Index.Remove(pArmy.id);
                    RemoveCityMapping(pArmy.id);
                }
                StandingArmyService.OnFieldArmyChanged(changedKingdom);
                return;
            }
            Index.Register(pArmy.id, kingdom.id);
            RegisterCityArmy(pArmy, kingdom);
            StandingArmyService.OnFieldArmyChanged(kingdom);
        }

        public static void OnArmyDisposed(Army pArmy)
        {
            Kingdom kingdom = SafeKingdom(pArmy);
            if (pArmy != null)
            {
                Index.Remove(pArmy.id);
                RemoveCityMapping(pArmy.id);
            }
            StandingArmyService.OnFieldArmyChanged(kingdom);
        }

        public static int Count(Kingdom pKingdom)
        {
            return pKingdom?.data == null ? 0 : Index.Count(pKingdom.id);
        }

        public static ArmyStrategicIdCursor CreateSnapshotCursor(
            Kingdom pKingdom)
        {
            return Index.CreateCursor(pKingdom?.data == null
                ? -1L
                : pKingdom.id);
        }

        public static Army ResolveIndexedArmy(long pArmyId,
            long pKingdomId)
        {
            Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                pArmyId, pKingdomId);
            if (IsFieldArmy(army, out Kingdom kingdom) &&
                kingdom.id == pKingdomId) return army;
            Index.Remove(pArmyId);
            return null;
        }

        public static bool TryGetCityArmy(City pCity, out Army pArmy)
        {
            pArmy = null;
            Kingdom kingdom = pCity?.kingdom;
            if (pCity?.data == null || kingdom?.data == null) return false;

            if (CanonicalArmyByCity.TryGetValue(pCity.id, out long armyId))
            {
                Army indexed = ResolveIndexedArmy(armyId, kingdom.id);
                if (IsCityArmy(indexed, pCity))
                {
                    pArmy = indexed;
                    return true;
                }
                CanonicalArmyByCity.Remove(pCity.id);
                CityByArmy.Remove(armyId);
            }

            Army cityArmy = null;
            try { if (pCity.hasArmy()) cityArmy = pCity.getArmy(); }
            catch { }
            if (IsCityArmy(cityArmy, pCity))
            {
                RegisterCityArmy(cityArmy, kingdom);
                if (CanonicalArmyByCity.TryGetValue(pCity.id,
                        out armyId))
                    pArmy = ResolveIndexedArmy(armyId, kingdom.id);
            }
            if (pArmy?.data == null)
            {
                ArmyStrategicIdCursor cursor = CreateSnapshotCursor(kingdom);
                while (!cursor.IsComplete)
                {
                    IReadOnlyList<long> ids = cursor.Take(
                        ArmyEstablishmentRules.MaximumFieldArmies);
                    if (ids.Count == 0) break;
                    for (int i = 0; i < ids.Count; i++)
                    {
                        Army candidate = ResolveIndexedArmy(ids[i],
                            kingdom.id);
                        if (IsCityArmy(candidate, pCity))
                            RegisterCityArmy(candidate, kingdom);
                    }
                }
                if (CanonicalArmyByCity.TryGetValue(pCity.id,
                        out armyId))
                    pArmy = ResolveIndexedArmy(armyId, kingdom.id);
            }
            return pArmy?.data != null;
        }

        public static bool TryRouteStandingCandidate(Actor pActor,
            City pCity, out Army pArmy)
        {
            pArmy = null;
            Kingdom kingdom = pCity?.kingdom;
            if (pActor?.data == null || pCity?.data == null ||
                kingdom?.data == null || pActor.kingdom != kingdom ||
                pActor.army != null) return false;

            if (!TryGetCityArmy(pCity, out pArmy)) return false;
            AWArmyService.AddToArmy(pActor, pArmy);
            return pActor.army == pArmy;
        }

        public static bool IsFieldCreationExempt(Actor pActor, City pCity)
        {
            if (pCity?.data == null) return true;
            MilitaryRecruitmentKind kind = MilitaryRecruitmentScope.Current;
            if (kind != MilitaryRecruitmentKind.None &&
                kind != MilitaryRecruitmentKind.StandingArmy &&
                kind != MilitaryRecruitmentKind.TemporaryLevy)
                return true;
            if (pActor?.army?.data != null &&
                AWArmyService.IsSpecialArmy(pActor.army)) return true;
            return pActor?.data != null &&
                   TemporarySlaveVanguardService.IsMember(pActor);
        }

        public static void ClearRuntime()
        {
            Index.Clear();
            CanonicalArmyByCity.Clear();
            CityByArmy.Clear();
        }

        private static void RegisterCityArmy(Army pArmy, Kingdom pKingdom)
        {
            long cityId = AWArmyService.GetAnchorCityId(pArmy);
            RemoveCityMapping(pArmy.id);
            if (cityId < 0L) return;
            CityByArmy[pArmy.id] = cityId;
            if (!CanonicalArmyByCity.TryGetValue(cityId,
                    out long existingId) || existingId == pArmy.id)
            {
                CanonicalArmyByCity[cityId] = pArmy.id;
                return;
            }

            Army existing = ResolveIndexedArmy(existingId, pKingdom.id);
            if (existing?.data == null)
            {
                CanonicalArmyByCity[cityId] = pArmy.id;
                return;
            }
            long canonicalId = ArmyEstablishmentRules.SelectCanonicalCityArmy(
                existing.id, HasStableCaptain(existing), pArmy.id,
                HasStableCaptain(pArmy));
            Army canonical = canonicalId == existing.id ? existing : pArmy;
            Army duplicate = canonicalId == existing.id ? pArmy : existing;
            CanonicalArmyByCity[cityId] = canonical.id;
            StandingArmyService.ScheduleCityDuplicateMerge(pKingdom,
                duplicate, canonical);
        }

        private static void RemoveCityMapping(long pArmyId)
        {
            if (!CityByArmy.TryGetValue(pArmyId, out long cityId)) return;
            CityByArmy.Remove(pArmyId);
            if (CanonicalArmyByCity.TryGetValue(cityId, out long canonicalId) &&
                canonicalId == pArmyId)
                CanonicalArmyByCity.Remove(cityId);
        }

        private static bool IsCityArmy(Army pArmy, City pCity)
        {
            if (pArmy?.data == null || pCity?.kingdom?.data == null ||
                AWArmyService.GetAnchorCityId(pArmy) != pCity.id)
                return false;
            return IsFieldArmy(pArmy, out Kingdom kingdom) &&
                   kingdom == pCity.kingdom;
        }

        private static bool HasStableCaptain(Army pArmy)
        {
            Actor captain = null;
            try { captain = pArmy?.getCaptain(); }
            catch { }
            try
            {
                return captain?.data != null && captain.army == pArmy &&
                       !captain.isRekt() && captain.isAlive();
            }
            catch { return false; }
        }

        private static bool IsFieldArmy(Army pArmy, out Kingdom pKingdom)
        {
            pKingdom = null;
            bool hasData = pArmy?.data != null;
            bool restorationArmy = false;
            bool nonReplacingShell = false;
            if (hasData)
            {
                pArmy.data.get(LineageKeys.RESTORATION_UPRISING_ARMY,
                    out restorationArmy, false);
                pArmy.data.get(LineageKeys.AW_ARMY_NON_REPLACING_SHELL,
                    out nonReplacingShell, false);
            }
            bool alive = false;
            try
            {
                alive = pArmy != null && pArmy.isAlive();
                if (alive) pKingdom = pArmy.getKingdom();
            }
            catch { alive = false; }
            bool hasKingdom = pKingdom?.data != null && !pKingdom.isRekt();
            return !nonReplacingShell &&
                   ArmyEstablishmentRules.IsFieldArmyClassification(
                hasData, alive, hasKingdom,
                markedSpecial: AWArmyService.IsSpecialArmy(pArmy),
                sortie: GarrisonSortieService.IsSortieArmy(pArmy),
                controlledCreationShell:
                    AWArmyService.IsSpecialArmyCreationInProgress(pArmy),
                restorationArmy: restorationArmy);
        }

        private static int SafeUnitCount(Army pArmy)
        {
            try { return Math.Max(0, pArmy?.units?.Count ?? 0); }
            catch { return 0; }
        }

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return pArmy?.getKingdom(); }
            catch { return null; }
        }
    }
}
