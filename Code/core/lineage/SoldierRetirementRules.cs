namespace AncientWarfare3.core.lineage
{
    public static class SoldierRetirementRules
    {
        public const float HardRetirementAge = 65f;

        public static bool IsOrdinaryServiceAgeAllowed(float age)
        {
            return age >= 0f && age < HardRetirementAge;
        }

        public static bool HasReachedHardRetirementAge(float age)
        {
            return age >= HardRetirementAge;
        }

        public static bool ShouldDeferTemporaryServiceRetirement(
            bool temporaryService, float age)
        {
            return temporaryService && !HasReachedHardRetirementAge(age);
        }

        public static bool CanRecallReserve(bool wartimeMobilization,
            bool isRetired, float age, float maximumAge)
        {
            return wartimeMobilization && isRetired && age >= 0f &&
                   maximumAge > 0f && age < maximumAge;
        }

        public static bool CanConsiderForRetirement(bool isSupportedActor, bool isRekt, bool isWarrior,
            bool alreadyRetired, bool isGeneral, bool isFiefHolder,
            bool isRoyalGuard = false, bool hardRetirement = false)
        {
            if (!isSupportedActor) return false;
            if (isRekt || !isWarrior) return false;
            if (alreadyRetired) return false;
            if (isGeneral || isRoyalGuard) return false;
            if (hardRetirement) return true;
            return !isFiefHolder;
        }

        public static bool ShouldRunExpensiveRetirementChecks(bool isSupportedActor, bool isRekt, bool isWarrior,
            bool alreadyRetired, float age, float lifespan, float retirementAgeRatio)
        {
            if (!isSupportedActor) return false;
            if (isRekt || !isWarrior) return false;
            if (alreadyRetired) return false;
            if (HasReachedHardRetirementAge(age)) return true;
            if (lifespan <= 0f) return false;
            return age >= lifespan * retirementAgeRatio;
        }

        public static bool ShouldReadRetirementState(bool isSupportedActor, bool isRekt, bool isWarrior)
        {
            if (!isSupportedActor) return false;
            if (isRekt) return false;
            return isWarrior;
        }

        public static bool ShouldEnterActorUpdateAgeRetirement(bool isSupportedActor, bool isRekt, bool isWarrior)
        {
            return ShouldReadRetirementState(isSupportedActor, isRekt, isWarrior);
        }

        public static bool ShouldRunCityRetirementScan(bool pActorUpdateAgeRetirementEnabled, bool pMaintenanceDue)
        {
            if (!pMaintenanceDue) return false;
            return !pActorUpdateAgeRetirementEnabled;
        }
    }
}
