using System;

namespace AncientWarfare3.core.lineage
{
    public static class ArmyConquestReserveRules
    {
        public const int PopulationShareDenominator = 5;

        public static int GrantForPopulation(int pPopulation)
        {
            return Math.Max(0, pPopulation) / PopulationShareDenominator;
        }

        public static int Add(int pCurrent, int pGrant)
        {
            long total = (long)Math.Max(0, pCurrent) + Math.Max(0, pGrant);
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }

        public static int Consume(int pCurrent, int pRequested,
            out int pConsumed)
        {
            int current = Math.Max(0, pCurrent);
            int requested = Math.Max(0, pRequested);
            pConsumed = Math.Min(current, requested);
            return current - pConsumed;
        }
    }
}
