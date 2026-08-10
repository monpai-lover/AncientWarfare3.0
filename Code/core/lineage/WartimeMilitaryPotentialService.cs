using System;

namespace AncientWarfare3.core.lineage
{
    internal static class WartimeMilitaryPotentialService
    {
        public static int CountPotentialWarriors(Kingdom pKingdom)
        {
            TryCountPotentialWarriors(pKingdom, out int potential);
            return potential;
        }

        public static bool TryCountPotentialWarriors(Kingdom pKingdom,
            out int potential)
        {
            potential = 0;
            if (pKingdom?.data == null || pKingdom.isRekt()) return true;
            int active = 0;
            try
            {
                active = Math.Max(0, pKingdom.countTotalWarriors());
            }
            catch { }
            bool reservesReady = CityReservePoolService
                .TryCountAvailable(pKingdom, out int reserves);
            potential = WarForceEliminationRules.AddPotential(active,
                reserves);
            return reservesReady;
        }

        public static int CountPotentialWarriorsBounded(Kingdom pKingdom,
            int pMaximumCityScans)
        {
            return CountPotentialWarriors(pKingdom);
        }

        public static bool TryCountPotentialWarriorsBounded(
            Kingdom pKingdom, int pMaximumCityScans, out int potential)
        {
            return TryCountPotentialWarriors(pKingdom, out potential);
        }

        public static void ClearRuntime() { }

        public static void RemoveKingdom(long pKingdomId) { }

    }
}
