using System;

namespace AncientWarfare3.core.policy
{
    public static class CityTechUpdateRules
    {
        private const double EPSILON = 0.001;

        public static bool ShouldSkipStableAdoptedUpdate(bool existingAdopted, bool nextAdopted,
            double existingAdoption, double nextAdoption, double existingExposure, double nextExposure,
            bool sameOwner)
        {
            if (!sameOwner) return false;
            if (!existingAdopted || !nextAdopted) return false;
            return Math.Abs(existingAdoption - nextAdoption) <= EPSILON &&
                   Math.Abs(existingExposure - nextExposure) <= EPSILON;
        }
    }

    public static class CityTechSpreadRules
    {
        public static bool ShouldSkipFullyAdoptedSpread(int pCityCount, int pAdoptedCityCount)
        {
            return pCityCount > 0 && pAdoptedCityCount >= pCityCount;
        }
    }

    public static class CityTechNeighborRules
    {
        public static bool ShouldConsiderNeighborKingdom(bool pHasKingdom, bool pSameKingdom,
            bool pIsRekt, bool pIsNeutral)
        {
            if (!pHasKingdom) return false;
            if (pSameKingdom) return false;
            return !pIsRekt && !pIsNeutral;
        }
    }
}
