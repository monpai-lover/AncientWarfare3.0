namespace AncientWarfare3.core.policy
{
    internal static class XiaExpansionDecisionRules
    {
        internal const int InitialZoneAllowance = 55;
        internal const int FirstTechZoneAllowance = 75;
        internal const int FourthTechZoneAllowance = 95;
        internal const int SeventhTechZoneAllowance = 125;
        internal const int TenthTechZoneAllowance = 150;
        internal const int FullyUnlockedTechCount = 14;
        internal const float XiaWeightMultiplier = 4f;
        internal const float OneCityWeightMultiplier = 8f;
        internal const float TwoCityWeightMultiplier = 6f;
        internal const float ClaimLandWeightMultiplier = 2f;
        internal const float CivicLeaderClaimLandWeightMultiplier = 8f;

        public static float ApplyWeight(float pUpstreamWeight, bool pIsXia)
        {
            return pIsXia ? pUpstreamWeight * XiaWeightMultiplier : pUpstreamWeight;
        }

        public static float ApplyWeight(float pUpstreamWeight, bool pIsXia,
            int kingdomCityCount)
        {
            if (!pIsXia) return pUpstreamWeight;
            float multiplier = kingdomCityCount <= 1
                ? OneCityWeightMultiplier
                : kingdomCityCount == 2
                    ? TwoCityWeightMultiplier
                    : XiaWeightMultiplier;
            return pUpstreamWeight * multiplier;
        }

        public static float ApplyClaimLandWeight(float upstreamWeight,
            bool isXia, bool belowZoneAllowance, bool civicLeader)
        {
            if (upstreamWeight <= 0f || !isXia || !belowZoneAllowance)
                return upstreamWeight;
            float multiplier = civicLeader
                ? CivicLeaderClaimLandWeightMultiplier
                : ClaimLandWeightMultiplier;
            return upstreamWeight * multiplier;
        }

        public static int ZoneAllowance(int pAdoptedTechCount)
        {
            int adoptedTechCount = System.Math.Max(0, pAdoptedTechCount);
            if (adoptedTechCount >= FullyUnlockedTechCount)
                return int.MaxValue;
            if (adoptedTechCount >= 10) return TenthTechZoneAllowance;
            if (adoptedTechCount >= 7) return SeventhTechZoneAllowance;
            if (adoptedTechCount >= 4) return FourthTechZoneAllowance;
            if (adoptedTechCount >= 1) return FirstTechZoneAllowance;
            return InitialZoneAllowance;
        }

        public static bool CanClaimZone(bool upstreamAllowed, bool isXia,
            int adoptedTechCount, int zoneCount)
        {
            return CanClaimZone(upstreamAllowed, adoptedTechCount,
                zoneCount);
        }

        public static bool CanClaimZone(bool pUpstreamAllowed,
            int pAdoptedTechCount, int pZoneCount)
        {
            if (!pUpstreamAllowed) return false;
            return System.Math.Max(0, pZoneCount) <
                   ZoneAllowance(pAdoptedTechCount);
        }

        public static int ClaimCountWithinZoneAllowance(
            int pVanillaBatchCount, int pCurrentZoneCount,
            int pZoneAllowance)
        {
            int vanillaBatchCount = System.Math.Max(0, pVanillaBatchCount);
            if (pZoneAllowance == int.MaxValue) return vanillaBatchCount;
            int currentZoneCount = System.Math.Max(0, pCurrentZoneCount);
            int remainingCapacity = System.Math.Max(0,
                pZoneAllowance - currentZoneCount);
            return System.Math.Min(vanillaBatchCount, remainingCapacity);
        }

    }
}
