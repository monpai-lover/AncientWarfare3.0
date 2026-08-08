using System;

namespace AncientWarfare3.core.performance
{
    public static class AWEnemySearchBackoffRules
    {
        public static bool ShouldApply(bool pHasAttackTarget,
            float pTimeout, bool pMoving, bool pUsingPath)
        {
            return !pHasAttackTarget && pTimeout <= 0f && !pMoving &&
                   !pUsingPath;
        }

        public static float ResolveTimeout(float pCurrentTimeout,
            float pTimeScale)
        {
            if (pCurrentTimeout <= 0f) return pCurrentTimeout;
            float scale = Math.Max(1f, Math.Min(5f,
                Math.Max(0f, pTimeScale) * 0.25f));
            return pCurrentTimeout * scale;
        }
    }
}
