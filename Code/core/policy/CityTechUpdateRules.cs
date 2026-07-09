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
}
