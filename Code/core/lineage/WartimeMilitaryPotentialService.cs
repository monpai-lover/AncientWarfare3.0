using System;

namespace AncientWarfare3.core.lineage
{
    internal static class WartimeMilitaryPotentialService
    {
        public static int CountPotentialWarriors(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return 0;
            int active = CountLivingOrdinaryMilitary(pKingdom);
            int reserve = CityReservePoolService.CountAvailable(pKingdom);
            int recruitable = CountForceRecruitablePopulation(pKingdom);
            return AddClamped(AddClamped(active, reserve), recruitable);
        }

        /// <summary>
        /// Population that can still be mobilized without importing soldiers
        /// from another city.  This is intentionally based on each city's
        /// population and effective warrior slots, matching wartime levy
        /// admission instead of the current Army count alone.
        /// </summary>
        public static int CountForceRecruitablePopulation(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return 0;
            long total = 0L;
            try
            {
                if (pKingdom.cities == null) return 0;
                for (int index = 0; index < pKingdom.cities.Count; index++)
                {
                    City city = pKingdom.cities[index];
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom) continue;
                    int population = Math.Max(0, city.getPopulationPeople());
                    int current = Math.Max(0,
                        StandingArmyService.CountOrdinaryMilitary(city));
                    int slots = Math.Max(0,
                        MandateMilitaryPhaseService.EffectiveWarriorSlots(
                            pKingdom, city.status?.warrior_slots ?? 0));
                    total += WartimeRecruitmentPopulationRules.
                        AdditionalMobilizationCapacity(population, current,
                            slots);
                    if (total >= int.MaxValue) return int.MaxValue;
                }
            }
            catch { }
            return (int)total;
        }

        public static int CountPotentialWarriorsBounded(Kingdom pKingdom,
            int pMaximumCityScans)
        {
            return CountPotentialWarriors(pKingdom);
        }

        public static void ClearRuntime() { }

        public static void RemoveKingdom(long pKingdomId) { }

        private static int CountLivingOrdinaryMilitary(Kingdom pKingdom)
        {
            long total = 0L;
            ArmyStrategicIdCursor cursor = ArmyFieldIndexService.
                CreateSnapshotCursor(pKingdom);
            while (!cursor.IsComplete)
            {
                var armyIds = cursor.Take(
                    ArmyEstablishmentRules.MaximumFieldArmies);
                for (int i = 0; i < armyIds.Count; i++)
                {
                    Army army = ArmyFieldIndexService.ResolveIndexedArmy(
                        armyIds[i], pKingdom.id);
                    try { total += Math.Max(0, army?.countUnits() ?? 0); }
                    catch { }
                    if (total >= int.MaxValue) return int.MaxValue;
                }
                if (armyIds.Count == 0) break;
            }
            return (int)total;
        }

        private static int AddClamped(int first, int second)
        {
            long total = (long)Math.Max(0, first) + Math.Max(0, second);
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }
    }
}
