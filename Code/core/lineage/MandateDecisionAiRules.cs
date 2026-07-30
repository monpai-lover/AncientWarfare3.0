using System;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateDecisionAiRules
    {
        public const int BorderDefenseCooldownYears = 6;
        public const int GreatEnfeoffmentCooldownYears = 12;
        public const int GreatRoyalGrantCooldownYears = 12;

        public static int CooldownYears(string pDecisionId)
        {
            return pDecisionId switch
            {
                "aw_mandate_decision_border_defense" =>
                    BorderDefenseCooldownYears,
                "aw_mandate_decision_great_enfeoffment" =>
                    GreatEnfeoffmentCooldownYears,
                "aw_mandate_decision_grant_royal_titles" =>
                    GreatRoyalGrantCooldownYears,
                _ => 0
            };
        }

        public static bool IsCooldownReady(int pCurrentYear,
            int pLastSuccessYear, int pCooldownYears)
        {
            return pCooldownYears <= 0 || pLastSuccessYear < 0 ||
                   (long)pCurrentYear - pLastSuccessYear >= pCooldownYears;
        }

        public static int Score(string pDecisionId,
            bool pPreferredSacrifice)
        {
            if (string.IsNullOrEmpty(pDecisionId)) return int.MinValue;
            if (pPreferredSacrifice) return 1400;
            return pDecisionId switch
            {
                "aw_mandate_decision_favor_order" => 1300,
                "aw_mandate_decision_centralize_3" => 1230,
                "aw_mandate_decision_centralize_2" => 1220,
                "aw_mandate_decision_centralize_1" => 1210,
                "aw_mandate_decision_great_enfeoffment" => 1100,
                "aw_mandate_decision_grant_royal_titles" => 1050,
                "aw_mandate_decision_border_defense" => 200,
                "aw_mandate_decision_sacrifice_gamble" => 100,
                "aw_mandate_decision_sacrifice_moderate" => 100,
                "aw_mandate_decision_sacrifice_conservative" => 100,
                _ => 0
            };
        }
    }
}
