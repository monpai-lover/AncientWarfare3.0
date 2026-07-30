namespace AncientWarfare3.core.lineage
{
    public static class CityAttackZoneRules
    {
        public static bool ShouldRepairTargetZone(bool hasTargetCity, bool targetHasZones,
            bool hasCurrentZone, bool currentZoneBelongsToTarget)
        {
            return hasTargetCity && targetHasZones &&
                   (!hasCurrentZone || !currentZoneBelongsToTarget);
        }

        public static bool ShouldInvalidateAttackTarget(
            bool targetOtherwiseValid,
            bool frozenControlledBySource)
        {
            return targetOtherwiseValid && frozenControlledBySource;
        }

        public static bool CanSelectAttackCandidate(bool alive, bool enemy,
            bool reachable, bool frozenControlledBySource)
        {
            return alive && enemy && reachable &&
                   !frozenControlledBySource;
        }

        public static bool ShouldTreatNaturalLimitAsControlled(
            bool persistedFrozenControl, bool naturalCaptureLimit,
            bool physicalControllerOnSameSide, bool activeDefenders)
        {
            return persistedFrozenControl ||
                   naturalCaptureLimit && physicalControllerOnSameSide &&
                   !activeDefenders;
        }

        public static bool ShouldAdvanceAfterFrozenOccupation(
            bool targetMatchesFrozenCity,
            bool hasOffensiveArmy,
            bool hasLiveCaptain,
            bool hasNextEnemyCity,
            bool isRoyalGuard,
            bool isDedicatedGarrison)
        {
            return targetMatchesFrozenCity && hasOffensiveArmy &&
                   hasLiveCaptain && hasNextEnemyCity &&
                   !isRoyalGuard && !isDedicatedGarrison;
        }

        public static bool TargetMatchesFrozenCity(
            bool attackCityMatches, bool attackZoneMatches)
        {
            return attackCityMatches || attackZoneMatches;
        }

        public static bool ShouldReleaseFrozenAttackTarget(
            bool targetMatchesFrozenCity,
            bool hasOffensiveArmy,
            bool hasLiveCaptain,
            bool isRoyalGuard,
            bool isDedicatedGarrison)
        {
            return targetMatchesFrozenCity && hasOffensiveArmy &&
                   hasLiveCaptain && !isRoyalGuard &&
                   !isDedicatedGarrison;
        }
    }
}
