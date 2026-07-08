namespace AncientWarfare3.core.lineage
{
    public static class SoldierRetirementRules
    {
        public static bool CanConsiderForRetirement(bool isSupportedActor, bool isRekt, bool isWarrior,
            bool alreadyRetired, bool isGeneral, bool isFiefHolder, bool isRoyalGuard = false)
        {
            if (!isSupportedActor) return false;
            if (isRekt || !isWarrior) return false;
            if (alreadyRetired) return false;
            if (isRoyalGuard) return false;
            return !isGeneral && !isFiefHolder;
        }

        public static bool ShouldRunExpensiveRetirementChecks(bool isSupportedActor, bool isRekt, bool isWarrior,
            bool alreadyRetired, float age, float lifespan, float retirementAgeRatio)
        {
            if (!isSupportedActor) return false;
            if (isRekt || !isWarrior) return false;
            if (alreadyRetired) return false;
            if (lifespan <= 0f) return false;
            return age >= lifespan * retirementAgeRatio;
        }

        public static bool ShouldRunCityRetirementScan(bool pActorUpdateAgeRetirementEnabled, bool pMaintenanceDue)
        {
            if (!pMaintenanceDue) return false;
            return !pActorUpdateAgeRetirementEnabled;
        }
    }
}
