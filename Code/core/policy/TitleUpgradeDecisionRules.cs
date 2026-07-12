using System;

namespace AncientWarfare3.core.policy
{
    public static class TitleUpgradeDecisionRules
    {
        public const string DecisionId = "aw_decision_title_upgrade";

        public static bool ShouldCompleteImmediately(string pDecisionId, bool pHasValidSuzerain)
        {
            return !pHasValidSuzerain &&
                   string.Equals(pDecisionId, DecisionId, StringComparison.Ordinal);
        }
    }
}
