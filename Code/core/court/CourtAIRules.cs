namespace AncientWarfare3.core.court
{
    public static class CourtAIRules
    {
        public static int ScoreResearch(string dominantSchool, string nodeId, bool atWar, bool mandateExists,
            float livelihood = 0.5f, float commerce = 0.5f, float technology = 0.5f,
            float order = 0.5f)
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
            score += CourtDirectionRules.LivelihoodResearchBonus(
                livelihood, IsLivelihoodResearch(nodeId));
            if (IsCommerceResearch(nodeId)) score += AxisPreferenceBonus(commerce);
            if (IsTechnologyResearch(nodeId)) score += AxisPreferenceBonus(technology);
            if (IsOrderResearch(nodeId)) score += AxisPreferenceBonus(order);
            return score;
        }

        public static int ScoreDecision(string dominantSchool, string decisionId, int cities, bool atWar, bool unstable,
            float livelihood = 0.5f, float aggression = 0.5f, float peace = 0.5f,
            float war = 0.5f, float order = 0.5f)
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
                case CourtSchoolId.Diplomat:
                    if (decisionId == "aw_decision_seek_suzerain" || decisionId == "aw_decision_absorb_vassal") score += 85;
                    break;
                case CourtSchoolId.Ru:
                case CourtSchoolId.YinYang:
                    if (decisionId == "aw_decision_claim_mandate") score += 85;
                    break;
            }
            if (decisionId == "aw_decision_fabricate_core" ||
                decisionId == "aw_decision_fabricate_weak_claim" ||
                decisionId == "aw_decision_fabricate_strong_claim")
            {
                float multiplier = CourtDirectionRules.OffensiveWarMultiplier(
                    aggression, peace, livelihood, war, protectedWar: false);
                score += (int)((multiplier - 1f) * 120f);
            }
            else if (decisionId == "aw_decision_seek_suzerain")
            {
                score += (int)((CourtDirectionRules.VoluntaryDiplomacyMultiplier(peace) - 1f) * 100f);
            }
            else if (decisionId == "aw_decision_absorb_vassal")
            {
                score += (int)((CourtDirectionRules.ForcedVassalMultiplier(aggression, order) - 1f) * 80f);
            }
            return score;
        }

        public static bool IsLivelihoodResearch(string nodeId)
        {
            switch (nodeId ?? "")
            {
                case "aw_tech_iron_plow":
                case "aw_tech_granary_accounting":
                case "aw_tech_well_field_survey":
                case "aw_tech_pottery_casting":
                case "aw_tech_bronze_casting":
                case "aw_tech_city_defense":
                case "aw_policy_household_registry":
                case "aw_policy_corvee_labor":
                case "aw_policy_abolish_slavery":
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsCommerceResearch(string nodeId)
        {
            switch (nodeId ?? "")
            {
                case "aw_tech_granary_accounting":
                case "aw_policy_household_registry":
                case "aw_policy_imperial_court":
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsTechnologyResearch(string nodeId)
        {
            switch (nodeId ?? "")
            {
                case "aw_tech_writing":
                case "aw_tech_bronze_casting":
                case "aw_tech_pottery_casting":
                case "aw_tech_chariot_training":
                case "aw_tech_city_defense":
                case "aw_tech_official_court":
                case "aw_tech_three_departments":
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsOrderResearch(string nodeId)
        {
            switch (nodeId ?? "")
            {
                case "aw_policy_early_law":
                case "aw_policy_household_registry":
                case "aw_policy_xia_law_institutions":
                case "aw_tech_official_court":
                    return true;
                default:
                    return false;
            }
        }

        private static int AxisPreferenceBonus(float pValue)
        {
            float value = pValue < 0f ? 0f : pValue > 1f ? 1f : pValue;
            return (int)System.Math.Round((value - 0.5f) * 60f);
        }
    }
}
