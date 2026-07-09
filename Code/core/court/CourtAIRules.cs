namespace AncientWarfare3.core.court
{
    public static class CourtAIRules
    {
        public static int ScoreResearch(string dominantSchool, string nodeId, bool atWar, bool mandateExists)
        {
            int score = 0;
            switch (dominantSchool ?? "")
            {
                case CourtSchoolId.Ru:
                    if (nodeId == "aw_tech_rites_music" || nodeId == "aw_policy_ancestral_rites" || nodeId == "aw_policy_mandate_rites") score += 90;
                    break;
                case CourtSchoolId.Legalist:
                    if (nodeId == "aw_policy_early_law" || nodeId == "aw_policy_abolish_slavery" || nodeId == "aw_policy_xia_law_institutions") score += 95;
                    break;
                case CourtSchoolId.Dao:
                    if (nodeId == "aw_policy_abolish_slavery") score += 35;
                    if (atWar) score -= 20;
                    break;
                case CourtSchoolId.Mohist:
                    if (nodeId == "aw_tech_city_defense" || nodeId == "aw_policy_border_enfeoffment") score += 85;
                    break;
                case CourtSchoolId.Military:
                    if (nodeId == "aw_tech_chariot_training" || nodeId == "aw_policy_military_merit" || nodeId == "aw_policy_slave_army") score += atWar ? 120 : 70;
                    break;
                case CourtSchoolId.Diplomat:
                    if (nodeId == "aw_policy_imperial_court" || nodeId == "aw_policy_mandate_rites") score += mandateExists ? 80 : 45;
                    break;
                case CourtSchoolId.Agrarian:
                    if (nodeId == "aw_tech_iron_plow" || nodeId == "aw_tech_granary_accounting" || nodeId == "aw_policy_corvee_labor") score += 75;
                    break;
                case CourtSchoolId.YinYang:
                    if (nodeId == "aw_policy_mandate_rites" || nodeId == "aw_decision_year_name") score += 70;
                    break;
                case CourtSchoolId.Logician:
                    if (nodeId == "aw_decision_fabricate_weak_claim" || nodeId == "aw_decision_fabricate_strong_claim") score += 60;
                    break;
            }
            return score;
        }

        public static int ScoreDecision(string dominantSchool, string decisionId, int cities, bool atWar, bool unstable)
        {
            int score = 0;
            switch (dominantSchool ?? "")
            {
                case CourtSchoolId.Legalist:
                    if (decisionId == "aw_decision_control_slaves" || decisionId == "aw_decision_fabricate_core") score += 80;
                    break;
                case CourtSchoolId.Mohist:
                    if (decisionId == "aw_decision_change_capital" && unstable) score += 30;
                    break;
                case CourtSchoolId.Military:
                    if (decisionId == "aw_decision_declare_war" && !atWar && cities >= 2) score += 95;
                    break;
                case CourtSchoolId.Diplomat:
                    if (decisionId == "aw_decision_seek_suzerain" || decisionId == "aw_decision_absorb_vassal") score += 85;
                    if (decisionId == "aw_decision_declare_war") score += 30;
                    break;
                case CourtSchoolId.Ru:
                case CourtSchoolId.YinYang:
                    if (decisionId == "aw_decision_claim_mandate" || decisionId == "aw_decision_mandate_ritual") score += 85;
                    break;
                case CourtSchoolId.Dao:
                    if (decisionId == "aw_decision_declare_war") score -= 60;
                    break;
            }
            return score;
        }
    }
}
