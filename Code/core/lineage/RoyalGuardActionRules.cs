namespace AncientWarfare3.core.lineage
{
    public static class RoyalGuardActionRules
    {
        public const float FollowIdleWaitMin = 3f;
        public const float FollowIdleWaitMax = 6f;
        public const float NoThreatWaitMin = 2f;
        public const float NoThreatWaitMax = 5f;

        public static bool ShouldIssueFollowMove(bool pHasTarget, bool pTargetIsCurrentTile)
        {
            return pHasTarget && !pTargetIsCurrentTile;
        }

        public static bool ShouldSearchThreats(bool pHasEnemyWar, bool pKingOrGuardUnderAttack)
        {
            return pHasEnemyWar || pKingOrGuardUnderAttack;
        }

        public static float WaitAfterFollowIdle(float pMin, float pMax)
        {
            return ClampMin(pMin, pMax);
        }

        public static float WaitAfterNoThreat(float pMin, float pMax)
        {
            return ClampMin(pMin, pMax);
        }

        private static float ClampMin(float pMin, float pMax)
        {
            if (pMax < pMin) return pMax;
            return pMin;
        }
    }
}
