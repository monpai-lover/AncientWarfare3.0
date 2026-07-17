namespace AncientWarfare3.core.lineage
{
    public static class WarDisplayLabelRules
    {
        private static readonly string[] EmbeddedKeys =
        {
            "strong_claim_decision",
            "weak_claim_decision",
            "fabricate_strong_claim",
            "fabricate_weak_claim",
            "fabricate_core",
            "core_reclaim",
            "claim_war",
            "force_vassal",
            "vassal_war",
            "independence_war",
            "restoration_war",
            "restoration",
            "mandate_conquest",
            "tianmingrebel",
            "tianming",
            "general_rebellion_war",
            "fief_independence_war"
        };

        public static string Label(string pKey)
        {
            return Label(pKey, HistoryLocalizationRules.CurrentLanguage());
        }

        public static string Label(string pKey, string pLanguage)
        {
            return TryWarOrDecisionLabel(pKey, pLanguage, out string label)
                ? label
                : EventLabel(pKey, pLanguage);
        }

        public static string NormalizeEmbeddedKeys(string pText)
        {
            return NormalizeEmbeddedKeys(pText, HistoryLocalizationRules.CurrentLanguage());
        }

        public static string NormalizeEmbeddedKeys(string pText, string pLanguage)
        {
            if (string.IsNullOrEmpty(pText)) return pText ?? "";
            string result = pText;
            foreach (string key in EmbeddedKeys)
            {
                if (result.IndexOf(key, System.StringComparison.Ordinal) < 0) continue;
                result = result.Replace(key, Label(key, pLanguage));
            }
            return result;
        }

        public static string EventLabel(string pKey)
        {
            return EventLabel(pKey, HistoryLocalizationRules.CurrentLanguage());
        }

        public static string EventLabel(string pKey, string pLanguage)
        {
            switch (pKey ?? "")
            {
                case "war_claim_created": return T("aw_hist_event_war_claim_created", pLanguage);
                case "war_project_started": return T("aw_hist_event_war_project_started", pLanguage);
                case "war_project_completed": return T("aw_hist_event_war_project_completed", pLanguage);
                case "war_goal_set": return T("aw_hist_event_war_goal_set", pLanguage);
                case "war_decision": return T("aw_hist_event_war_decision", pLanguage);
                case "war_start": return T("aw_hist_event_war_start", pLanguage);
                case "war_end": return T("aw_hist_event_war_end", pLanguage);
                case "mandate_start": return T("aw_hist_event_mandate_start", pLanguage);
                case "mandate_declared_orthodox": return T("aw_hist_event_mandate_declared_orthodox", pLanguage);
                case "mandate_declared_rebel": return T("aw_hist_event_mandate_declared_rebel", pLanguage);
                case "mandate_declared_foreign_pseudo": return T("aw_hist_event_mandate_declared_foreign_pseudo", pLanguage);
                case "mandate_declared_player_grant": return T("aw_hist_event_mandate_declared_player_grant", pLanguage);
                case "mandate_end": return T("aw_hist_event_mandate_end", pLanguage);
                case "mandate_yearly": return T("aw_hist_event_mandate_yearly", pLanguage);
                case "mandate_war_start": return T("aw_hist_event_mandate_war_start", pLanguage);
                case "mandate_war_won": return T("aw_hist_event_mandate_war_won", pLanguage);
                case "mandate_ritual": return T("aw_hist_event_mandate_ritual", pLanguage);
                case "mandate_sacrifice_auspicious": return T("aw_hist_event_mandate_sacrifice_auspicious", pLanguage);
                case "mandate_sacrifice_neutral": return T("aw_hist_event_mandate_sacrifice_neutral", pLanguage);
                case "mandate_sacrifice_ominous": return T("aw_hist_event_mandate_sacrifice_ominous", pLanguage);
                case "mandate_year_name": return T("aw_hist_event_mandate_year_name", pLanguage);
                case "mandate_ruler_title": return T("aw_hist_event_mandate_ruler_title", pLanguage);
                case "former_king": return T("aw_hist_event_former_king", pLanguage);
                case "captive_executed": return T("aw_hist_event_captive_executed", pLanguage);
                case "mandate_succession_crisis": return T("aw_hist_event_mandate_succession_crisis", pLanguage);
                case "succession_collateral_restore": return T("aw_hist_event_succession_collateral_restore", pLanguage);
                case "person_collateral_restore": return T("aw_hist_event_person_collateral_restore", pLanguage);
                case "mandate_collapse": return T("aw_hist_event_mandate_collapse", pLanguage);
                case "centralization_reformed": return T("aw_hist_event_centralization_reformed", pLanguage);
                case "centralization_chaos_downgrade": return T("aw_hist_event_centralization_chaos_downgrade", pLanguage);
                case "vassal_tribute": return T("aw_hist_event_vassal_tribute", pLanguage);
                case "city_economy_role": return T("aw_hist_event_city_economy_role", pLanguage);
                case "city_economy_tax": return T("aw_hist_event_city_economy_tax", pLanguage);
                case "royal_asylum_started": return T("aw_hist_event_royal_asylum_started", pLanguage);
                case "royal_asylum_relocated": return T("aw_hist_event_royal_asylum_relocated", pLanguage);
                case "royal_asylum_returned": return T("aw_hist_event_royal_asylum_returned", pLanguage);
                case "royal_asylum_naturalized": return T("aw_hist_event_royal_asylum_naturalized", pLanguage);
                default:
                    if (TryWarOrDecisionLabel(pKey, pLanguage, out string label)) return label;
                    return string.IsNullOrEmpty(pKey) ? T("aw_hist_event_unknown", pLanguage) : pKey;
            }
        }

        private static bool TryWarOrDecisionLabel(string pKey, string pLanguage, out string pLabel)
        {
            switch (pKey ?? "")
            {
                case "fabricate_core":
                case "core_decision":
                case "aw_decision_fabricate_core":
                    pLabel = T("aw_hist_label_fabricate_core", pLanguage);
                    return true;
                case "weak_claim":
                case "weak_claim_decision":
                case "aw_decision_fabricate_weak_claim":
                case "fabricate_weak_claim":
                    pLabel = T("aw_hist_label_fabricate_weak_claim", pLanguage);
                    return true;
                case "strong_claim":
                case "strong_claim_decision":
                case "aw_decision_fabricate_strong_claim":
                case "fabricate_strong_claim":
                    pLabel = T("aw_hist_label_fabricate_strong_claim", pLanguage);
                    return true;
                case "core_reclaim":
                case "reclaim":
                    pLabel = T("aw_hist_label_core_reclaim", pLanguage);
                    return true;
                case "claim_war":
                case "aw_normal_war":
                    pLabel = T("aw_hist_label_claim_war", pLanguage);
                    return true;
                case "force_vassal":
                    pLabel = T("aw_hist_label_force_vassal", pLanguage);
                    return true;
                case "vassal_war":
                    pLabel = T("aw_hist_label_vassal_war", pLanguage);
                    return true;
                case "independence_war":
                    pLabel = T("aw_hist_label_independence_war", pLanguage);
                    return true;
                case "restoration":
                case "restoration_war":
                    pLabel = T("aw_hist_label_restoration_war", pLanguage);
                    return true;
                case "no_cb":
                    pLabel = T("aw_hist_label_no_cb", pLanguage);
                    return true;
                case "tianming":
                    pLabel = T("aw_hist_label_tianming", pLanguage);
                    return true;
                case "mandate_conquest":
                    pLabel = T("aw_hist_label_mandate_conquest", pLanguage);
                    return true;
                case "tianmingrebel":
                    pLabel = T("aw_hist_label_tianmingrebel", pLanguage);
                    return true;
                case "general_rebellion_war":
                    pLabel = T("aw_hist_label_general_rebellion_war", pLanguage);
                    return true;
                case "fief_independence_war":
                    pLabel = T("aw_hist_label_fief_independence_war", pLanguage);
                    return true;
                default:
                    pLabel = "";
                    return false;
            }
        }

        private static string T(string pKey, string pLanguage)
        {
            return HistoryLocalizationRules.Text(pKey, pLanguage);
        }
    }
}
