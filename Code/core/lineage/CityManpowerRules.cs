using System;

namespace AncientWarfare3.core.lineage
{
    public static class CityManpowerRules
    {
        public static int Capacity(int authenticPopulation)
        {
            return Math.Max(0, authenticPopulation) / 2;
        }

        public static int AuthenticPopulation(int authenticResidents,
            int authenticMobilized)
        {
            long total = (long)Math.Max(0, authenticResidents) +
                         Math.Max(0, authenticMobilized);
            return (int)Math.Min(int.MaxValue, total);
        }

        public static int NoticeHeadroom(int authenticPopulation,
            int activeCitySourcedMilitary)
        {
            return Math.Max(0, Capacity(authenticPopulation) -
                               Math.Max(0, activeCitySourcedMilitary));
        }

        public static int OpenWarReserve(int authenticPopulation,
            int livingCitySoldiers)
        {
            return NoticeHeadroom(authenticPopulation, livingCitySoldiers);
        }

        public static int WarReserveAvailable(int reserveCapacity,
            int consumed)
        {
            return Math.Max(0, Math.Max(0, reserveCapacity) -
                               Math.Max(0, consumed));
        }

        public static int RequiredSynthetic(int approvedShortage,
            int availableWarReserve)
        {
            return Math.Min(Math.Max(0, approvedShortage),
                Math.Max(0, availableWarReserve));
        }
    }
}
