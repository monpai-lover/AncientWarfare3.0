namespace AncientWarfare3.core.lineage
{
    public static class ArmyRetreatRules
    {
        public const int MinimumBaselineUnits = 8;
        public const int RetreatCooldownYears = 1;

        public static bool ShouldRetreat(string pRole, int pBaselineUnits, int pCurrentUnits,
            bool pCaptainAlive, bool pIsAttacking, bool pCooldownActive)
        {
            if (!pIsAttacking) return false;
            if (pCooldownActive) return false;
            if (pRole == AWArmyRole.RoyalGuard) return false;
            if (pBaselineUnits < MinimumBaselineUnits) return false;
            if (pCurrentUnits < 0) pCurrentUnits = 0;

            float ratio = (float)pCurrentUnits / pBaselineUnits;
            float threshold = ThresholdForRole(pRole, pCaptainAlive);
            return ratio < threshold;
        }

        public static bool ShouldSkipAttackWhileRetreating(int pRetreatUntilYear, int pCurrentYear)
        {
            return pRetreatUntilYear > pCurrentYear;
        }

        public static bool ShouldResetBaseline(long pStoredTargetCityId, long pCurrentTargetCityId,
            int pStoredBaselineUnits, int pCurrentUnits)
        {
            if (pCurrentTargetCityId < 0) return false;
            if (pStoredTargetCityId != pCurrentTargetCityId) return true;
            if (pStoredBaselineUnits <= 0) return true;
            return pCurrentUnits > pStoredBaselineUnits;
        }

        private static float ThresholdForRole(string pRole, bool pCaptainAlive)
        {
            if (!pCaptainAlive) return 0.60f;
            if (pRole == AWArmyRole.SlaveArmy) return 0.35f;
            if (pRole == AWArmyRole.BorderArmy) return 0.40f;
            return 0.45f;
        }
    }
}
