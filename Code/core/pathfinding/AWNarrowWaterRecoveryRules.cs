namespace AncientWarfare3.core.pathfinding
{
    public static class AWNarrowWaterRecoveryRules
    {
        public const int MaximumConsecutiveWaterTiles = 5;

        public static bool ShouldTryBoundedCrossingBeforeTransport(
            bool military, bool boundedCrossingAttempt)
        {
            return military && !boundedCrossingAttempt;
        }

        public static bool CanStart(bool military, bool isBoat,
            bool isWaterCreature, bool damagedByOcean, bool alreadyRetried)
        {
            _ = damagedByOcean;
            return military && !isBoat && !isWaterCreature &&
                   !alreadyRetried;
        }

        public static bool CanAdvance(int currentWaterRun,
            bool enteringWater, bool predictedLethal, bool lava)
        {
            if (predictedLethal || lava) return false;
            return !enteringWater ||
                   currentWaterRun < MaximumConsecutiveWaterTiles;
        }

        public static bool CanEnterDamagingWater(bool damagedByOcean,
            bool alreadyInLiquid, bool boundedMilitaryWater,
            bool plannedSwimStep)
        {
            return !damagedByOcean || alreadyInLiquid ||
                   boundedMilitaryWater && plannedSwimStep;
        }

        public static int NextWaterRun(int currentWaterRun,
            bool enteringWater)
        {
            return enteringWater ? currentWaterRun + 1 : 0;
        }
    }
}
