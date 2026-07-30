using System;

namespace AncientWarfare3.core.lineage
{
    internal static class WartimeMilitaryPotentialService
    {
        public static int CountPotentialWarriors(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return 0;
            return AddClamped(CountLivingOrdinaryMilitary(pKingdom),
                CityReservePoolService.CountAvailable(pKingdom));
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
