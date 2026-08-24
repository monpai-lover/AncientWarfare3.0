namespace AncientWarfare3.core.pathfinding
{
    internal static class AWPathOptimizationRules
    {
        internal static bool ShouldUseStraightSegment(
            AWPathWorkClass pWorkClass, bool pBoundedMilitaryWater,
            bool pPhysicalTransportAvailable, bool pIsMilitary,
            bool pIsBoat, bool pIsWaterCreature, bool pCanFly)
        {
            return pWorkClass == AWPathWorkClass.Ambient &&
                   !pBoundedMilitaryWater &&
                   !pPhysicalTransportAvailable &&
                   !pIsMilitary && !pIsBoat && !pIsWaterCreature &&
                   !pCanFly;
        }
    }
}
