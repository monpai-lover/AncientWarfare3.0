namespace AncientWarfare3.core.lineage
{
    public static class RoyalGuardActionRules
    {
        public const float FollowIdleWaitMin = 0.1f;
        public const float FollowIdleWaitMax = 0.1f;
        public const float NoThreatWaitMin = 0.1f;
        public const float NoThreatWaitMax = 0.1f;

        public static bool ShouldIssueFollowMove(bool pHasTarget, bool pTargetIsCurrentTile)
        {
            return pHasTarget && !pTargetIsCurrentTile;
        }

        public static bool ShouldIssueFollowMoveAfterCooldown(
            bool pHasTarget, bool pTargetIsCurrentTile,
            bool pCooldownActive)
        {
            return pHasTarget && (!pTargetIsCurrentTile || !pCooldownActive);
        }

        public static bool ShouldProbeProtectionInP0(bool isProtectTask,
            bool hasThreatTarget)
        {
            return isProtectTask && !hasThreatTarget;
        }

        public static bool ShouldUseFollowP0(bool isFollowTask,
            bool isProtectTask, bool hasThreatTarget)
        {
            return isFollowTask && !isProtectTask && !hasThreatTarget;
        }

        public static bool ShouldKeepMilitaryP0(bool canFollowKingOnLand,
            bool hasThreatTarget, bool immediateCombat)
        {
            return canFollowKingOnLand &&
                   (!hasThreatTarget || immediateCombat);
        }

        public static bool ShouldSearchThreats(bool pHasEnemyWar, bool pKingOrGuardUnderAttack)
        {
            return pHasEnemyWar || pKingOrGuardUnderAttack;
        }

        public static int ResolveFollowOffsetIndex(long pActorId,
            int pOffsetCount)
        {
            if (pOffsetCount <= 0) return -1;
            ulong stableId = unchecked((ulong)pActorId);
            return (int)(stableId % (ulong)pOffsetCount);
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
