using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    internal static class XiaExpansionDecisionRules
    {
        internal const int ClaimLandTaskIncompatible = -1;
        internal const int ClaimLandGuardAlreadyInstalled = -2;
        private const string ClaimSelectorType =
            "BehActorCheckZoneTarget";
        private const string ClaimMovementType = "BehGoToTileTarget";
        private const string ClaimArrivalGuardType =
            "BehCivicLeaderClaimArrival";
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

        public static bool IsCivicLeader(bool actorIsKing,
            bool actorIsCityLeader)
        {
            return actorIsKing || actorIsCityLeader;
        }

        public static bool IsExternalClaimZoneValid(bool zoneExists,
            bool centerTileExists, bool zoneHasCity, bool touchesOwnCity,
            bool sameIsland, bool nativeClaimAllowed,
            bool isCitySeedZone = false)
        {
            return zoneExists && centerTileExists && !zoneHasCity &&
                   (touchesOwnCity || isCitySeedZone) && sameIsland &&
                   nativeClaimAllowed;
        }

        public static bool CanBeginExternalClaimAnimation(
            bool currentZoneMatchesSelectedTarget,
            bool externalZoneStillValid)
        {
            return currentZoneMatchesSelectedTarget &&
                   externalZoneStillValid;
        }

        public static bool ShouldUseExternalClaimSelector(
            bool pipelineReady, bool civicLeader)
        {
            return pipelineReady && civicLeader;
        }

        public static int ClaimLandGuardInsertionIndex(
            IReadOnlyList<string> actionTypeNames)
        {
            if (actionTypeNames == null)
                return ClaimLandTaskIncompatible;
            if (actionTypeNames.Count < 2 ||
                actionTypeNames[0] != ClaimSelectorType ||
                actionTypeNames[1] != ClaimMovementType)
                return ClaimLandTaskIncompatible;
            int guardCount = 0;
            int guardIndex = -1;
            for (int i = 0; i < actionTypeNames.Count; i++)
                if (actionTypeNames[i] == ClaimArrivalGuardType)
                {
                    guardCount++;
                    guardIndex = i;
                }
            if (guardCount == 1 && guardIndex == 2)
                return ClaimLandGuardAlreadyInstalled;
            if (guardCount != 0)
                return ClaimLandTaskIncompatible;
            return 2;
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
