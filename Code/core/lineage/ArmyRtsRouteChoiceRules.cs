using System;

namespace AncientWarfare3.core.lineage
{
    public enum ArmyRtsTravelChoice
    {
        Land = 0,
        Transport = 1
    }

    public static class ArmyRtsRouteChoiceRules
    {
        public const float TransportHysteresis = 6f;

        public static ArmyRtsTravelChoice Resolve(float landCost,
            bool transportAvailable, float pickupCost, float queueCost,
            float seaCost, float landingCost)
        {
            if (!transportAvailable) return ArmyRtsTravelChoice.Land;
            float transportCost = Normalize(pickupCost) +
                                  Normalize(queueCost) +
                                  Normalize(seaCost) +
                                  Normalize(landingCost);
            if (!IsFinitePositive(transportCost))
                return ArmyRtsTravelChoice.Land;
            if (!IsFinitePositive(landCost))
                return ArmyRtsTravelChoice.Transport;
            return transportCost + TransportHysteresis < landCost
                ? ArmyRtsTravelChoice.Transport
                : ArmyRtsTravelChoice.Land;
        }

        public static bool IsFinitePositive(float pValue)
        {
            return !float.IsNaN(pValue) && !float.IsInfinity(pValue) &&
                   pValue >= 0f;
        }

        private static float Normalize(float pValue)
        {
            return IsFinitePositive(pValue) ? pValue : 0f;
        }
    }
}
