namespace AncientWarfare3.core.lineage
{
    public enum FeudatoryRevocationAction
    {
        None = 0,
        Relocate = 1,
        ReclaimCity = 2,
        Abolish = 3
    }

    public static class FeudatoryRevocationRules
    {
        public const int MaximumRelocationCities = 3;
        public const int RelocationIntensity = 20;
        public const int CityReclamationIntensity = 35;
        public const int AbolitionIntensity = 60;

        public static int IntensityFor(FeudatoryRevocationAction action)
        {
            return action switch
            {
                FeudatoryRevocationAction.Relocate => RelocationIntensity,
                FeudatoryRevocationAction.ReclaimCity =>
                    CityReclamationIntensity,
                FeudatoryRevocationAction.Abolish => AbolitionIntensity,
                _ => 0
            };
        }

        public static int RelocationTargetCityCount(int currentCityCount)
        {
            if (currentCityCount <= 0) return 0;
            return currentCityCount < MaximumRelocationCities
                ? currentCityCount
                : MaximumRelocationCities;
        }

        public static bool CanUseRelocationCity(bool baseEligible,
            bool currentMember, int distanceToCapital,
            int oldSeatDistanceToCapital)
        {
            return baseEligible && !currentMember && distanceToCapital >= 0 &&
                   oldSeatDistanceToCapital >= 0 &&
                   distanceToCapital < oldSeatDistanceToCapital;
        }

        public static bool CanReclaimCity(bool isMember,
            int activeCityCount)
        {
            return isMember && activeCityCount > 1;
        }
    }
}
