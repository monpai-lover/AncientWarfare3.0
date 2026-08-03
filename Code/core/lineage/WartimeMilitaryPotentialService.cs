using System;

namespace AncientWarfare3.core.lineage
{
    internal static class WartimeMilitaryPotentialService
    {
        public static int CountPotentialWarriors(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return 0;
            int active = 0;
            try
            {
                active = Math.Max(0, pKingdom.countTotalWarriors());
            }
            catch { }
            return WarForceEliminationRules.AddPotential(active,
                CityReservePoolService.CountAvailable(pKingdom));
        }

        public static int CountPotentialWarriorsBounded(Kingdom pKingdom,
            int pMaximumCityScans)
        {
            return CountPotentialWarriors(pKingdom);
        }

        public static void ClearRuntime() { }

        public static void RemoveKingdom(long pKingdomId) { }

    }
}
