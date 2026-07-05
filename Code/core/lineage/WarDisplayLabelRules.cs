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
            "tianmingrebel",
            "tianming",
            "general_rebellion_war",
            "fief_independence_war"
        };

        public static string Label(string pKey)
        {
            return TryWarOrDecisionLabel(pKey, out string label)
                ? label
                : EventLabel(pKey);
        }

        public static string NormalizeEmbeddedKeys(string pText)
        {
            if (string.IsNullOrEmpty(pText)) return pText ?? "";
            string result = pText;
            foreach (string key in EmbeddedKeys)
            {
                if (result.IndexOf(key, System.StringComparison.Ordinal) < 0) continue;
                result = result.Replace(key, Label(key));
            }
            return result;
        }

        public static string EventLabel(string pKey)
        {
            switch (pKey ?? "")
            {
                case "war_claim_created": return "\u5236\u9020\u5ba3\u79f0";
                case "war_project_started": return "\u5f00\u59cb\u6218\u4e89\u7b79\u5907";
                case "war_project_completed": return "\u5b8c\u6210\u6218\u4e89\u7b79\u5907";
                case "war_goal_set": return "\u8bbe\u5b9a\u6218\u4e89\u76ee\u6807";
                case "war_decision": return "\u6218\u4e89\u51b3\u7b56";
                case "war_start": return "\u6218\u4e89\u7206\u53d1";
                case "war_end": return "\u6218\u4e89\u7ed3\u675f";
                case "mandate_start": return "\u53d7\u547d\u79f0\u5e1d";
                case "mandate_end": return "\u5931\u53bb\u5929\u547d";
                case "mandate_yearly": return "\u5929\u547d\u5e74\u5ea6\u53d8\u5316";
                case "mandate_war_start": return "\u5929\u547d\u6218\u4e89\u5f00\u59cb";
                case "mandate_war_won": return "\u5929\u547d\u6218\u4e89\u80dc\u5229";
                case "mandate_ritual": return "\u796d\u5929\u6574\u987f";
                case "mandate_year_name": return "\u6539\u5143";
                case "mandate_ruler_title": return "\u8ffd\u4e0a\u5e99\u8c25";
                case "mandate_succession_crisis": return "\u7ee7\u627f\u5371\u673a";
                case "succession_collateral_restore": return "\u6062\u590d\u5b97\u7edf";
                case "person_collateral_restore": return "\u6062\u590d\u5b97\u7edf";
                case "mandate_collapse": return "\u5929\u547d\u5d29\u89e3";
                case "city_economy_role": return "\u57ce\u5e02\u7ecf\u6d4e\u5b9a\u4f4d";
                case "city_economy_tax": return "\u57ce\u5e02\u7a0e\u6536";
                default:
                    if (TryWarOrDecisionLabel(pKey, out string label)) return label;
                    return string.IsNullOrEmpty(pKey) ? "\u672a\u8bb0\u5f55" : pKey;
            }
        }

        private static bool TryWarOrDecisionLabel(string pKey, out string pLabel)
        {
            switch (pKey ?? "")
            {
                case "fabricate_core":
                case "core_decision":
                case "aw_decision_fabricate_core":
                    pLabel = "\u5236\u9020\u6838\u5fc3";
                    return true;
                case "weak_claim":
                case "weak_claim_decision":
                case "aw_decision_fabricate_weak_claim":
                case "fabricate_weak_claim":
                    pLabel = "\u5236\u9020\u5f31\u5ba3\u79f0";
                    return true;
                case "strong_claim":
                case "strong_claim_decision":
                case "aw_decision_fabricate_strong_claim":
                case "fabricate_strong_claim":
                    pLabel = "\u5236\u9020\u5f3a\u5ba3\u79f0";
                    return true;
                case "core_reclaim":
                case "reclaim":
                    pLabel = "\u6536\u590d\u6838\u5fc3";
                    return true;
                case "claim_war":
                case "aw_normal_war":
                    pLabel = "\u6309\u5ba3\u79f0\u5ba3\u6218";
                    return true;
                case "force_vassal":
                    pLabel = "\u5f3a\u5236\u81e3\u670d";
                    return true;
                case "vassal_war":
                    pLabel = "\u9644\u5eb8\u6218\u4e89";
                    return true;
                case "independence_war":
                    pLabel = "\u72ec\u7acb\u6218\u4e89";
                    return true;
                case "restoration":
                case "restoration_war":
                    pLabel = "\u590d\u56fd\u6218\u4e89";
                    return true;
                case "no_cb":
                    pLabel = "\u65e0\u7406\u7531\u5ba3\u6218";
                    return true;
                case "tianming":
                    pLabel = "\u5929\u547d\u6218\u4e89";
                    return true;
                case "tianmingrebel":
                    pLabel = "\u4e49\u519b\u5929\u547d\u6218\u4e89";
                    return true;
                case "general_rebellion_war":
                    pLabel = "\u5927\u5c06\u53db\u4e71";
                    return true;
                case "fief_independence_war":
                    pLabel = "\u5c01\u5730\u72ec\u7acb";
                    return true;
                default:
                    pLabel = "";
                    return false;
            }
        }
    }
}
