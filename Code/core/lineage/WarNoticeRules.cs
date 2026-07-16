using System;

namespace AncientWarfare3.core.lineage
{
    public enum WarNoticeGate
    {
        PoliticalProgressIncomplete,
        Wait,
        Ready,
        Forced
    }

    public static class WarNoticeRules
    {
        public const int AdoptedRitesLevel = 3;
        public const int MinimumPreparationYears = 1;
        public const int MaximumPreparationYears = 3;

        public static bool RequiresNotice(bool attackerIsXia, int xiaizationLevel,
            bool deliberateDecision, string goalType, string warType,
            bool joiningExistingWar, bool pairAlreadyAtWar)
        {
            if (!deliberateDecision || joiningExistingWar || pairAlreadyAtWar) return false;
            if (!attackerIsXia && xiaizationLevel < AdoptedRitesLevel) return false;
            if (string.Equals(goalType, "independence", StringComparison.Ordinal)) return false;

            string type = warType ?? "";
            if (type == "independence_war" || type == "general_rebellion_war" ||
                type == "fief_independence_war" || type.IndexOf("rebellion", StringComparison.Ordinal) >= 0)
                return false;
            return true;
        }

        public static int EarliestWarYear(int pIssueYear)
        {
            return pIssueYear + MinimumPreparationYears;
        }

        public static int ForcedWarYear(int pIssueYear)
        {
            return pIssueYear + MaximumPreparationYears;
        }

        public static WarNoticeGate EvaluateGate(float pProgress, float pCost, int pCurrentYear,
            int pEarliestWarYear, int pForcedWarYear, bool deploymentsReady)
        {
            if (pProgress + 0.001f < pCost) return WarNoticeGate.PoliticalProgressIncomplete;
            if (pCurrentYear < pEarliestWarYear) return WarNoticeGate.Wait;
            if (deploymentsReady) return WarNoticeGate.Ready;
            return pCurrentYear >= pForcedWarYear ? WarNoticeGate.Forced : WarNoticeGate.Wait;
        }

        public static string BuildSignature(long pAttackerId, long pDefenderId, string pGoalType,
            long pTargetCityId, int pIssueYear)
        {
            return pAttackerId + ":" + pDefenderId + ":" + (pGoalType ?? "") + ":" +
                   pTargetCityId + ":" + pIssueYear;
        }
    }
}
