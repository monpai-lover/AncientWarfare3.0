using System;

namespace AncientWarfare3.core.grandstrategy
{
    public enum GrandStrategyMovementState
    {
        Land = 0,
        Fleet = 1,
        Retreat = 2,
        Engaged = 3
    }

    public static class GrandStrategyPathRules
    {
        public static bool IsLowerCost(int mountainCost, int forestCost,
            int roadBonus)
        {
            return Math.Max(0, forestCost - Math.Max(0, roadBonus)) < mountainCost;
        }

        public static GrandStrategyMovementState NextMovementState(
            GrandStrategyMovementState state, bool reachedCoast,
            bool validLanding)
        {
            if (state == GrandStrategyMovementState.Land && reachedCoast && !validLanding)
                return GrandStrategyMovementState.Fleet;
            if (state == GrandStrategyMovementState.Fleet && validLanding)
                return GrandStrategyMovementState.Land;
            return state;
        }

        public static bool CanIssueOrder(GrandStrategyMovementState state)
        {
            return state != GrandStrategyMovementState.Retreat &&
                state != GrandStrategyMovementState.Engaged;
        }

        public static int StrategicCost(int terrain, int roadBonus,
            int hostileRisk, int supplyCost)
        {
            return Math.Max(1, terrain + Math.Max(0, hostileRisk) +
                Math.Max(0, supplyCost) - Math.Max(0, roadBonus));
        }
    }
}
