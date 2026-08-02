namespace AncientWarfare3.core.lineage
{
    public static class WarTargetSelectionRules
    {
        public static int CountStrongClaimsForDisplay(int pExplicitStrongClaims, int pCoreTargets)
        {
            return CountEffectiveStrongClaims(pExplicitStrongClaims, pCoreTargets);
        }

        public static int CountEffectiveStrongClaims(int pExplicitStrongClaims, int pCoreTargets)
        {
            return System.Math.Max(0, pExplicitStrongClaims) + System.Math.Max(0, pCoreTargets);
        }

        public static bool HasClaimLikeCasusBelli(int pWeakClaims, int pExplicitStrongClaims, int pCoreTargets)
        {
            return System.Math.Max(0, pWeakClaims) + CountEffectiveStrongClaims(pExplicitStrongClaims, pCoreTargets) > 0;
        }

        public static int ScoreTarget(string pGoalType, bool pHasCore, bool pHasStrongClaim,
            bool pHasWeakClaim, int pRestorationStrength, int pPopulation)
        {
            int score = pPopulation / 2;
            switch (pGoalType)
            {
                case ZhuluWarRules.GoalTypeId:
                    score += 240;
                    break;
                case WarTerritoryService.GOAL_TAKE_MANDATE:
                    score += 180;
                    break;
                case WarTerritoryService.GOAL_MANDATE_CONQUEST:
                    score += 140;
                    if (pHasStrongClaim) score += 40;
                    break;
                case WarTerritoryService.GOAL_TAKE_CORE_CITY:
                    if (pHasCore) score += 120;
                    break;
                case WarTerritoryService.GOAL_PRESS_CLAIM_CITY:
                    if (pHasStrongClaim) score += 90;
                    if (pHasWeakClaim) score += 55;
                    break;
                case WarTerritoryService.GOAL_RESTORE_KINGDOM:
                    score += pRestorationStrength + 35;
                    break;
                case WarTerritoryService.GOAL_REUNIFY_SUCCESSION:
                    score += 210;
                    break;
                case WarTerritoryService.GOAL_FORCE_VASSAL:
                    score += 60;
                    break;
                case WarTerritoryService.GOAL_FORCE_TRIBUTARY:
                    score += 55;
                    break;
                case WarTerritoryService.GOAL_NO_CB:
                    score -= 35;
                    break;
            }
            return score;
        }
    }
}
