using System;
using System.Collections.Generic;
using NeoModLoader.General;

namespace AncientWarfare3.core.court
{
    internal static class CourtLocalizationRepair
    {
        private static readonly Dictionary<string, string> Simplified =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aw_court_statistics_button"] = "\u7edf\u8ba1",
                ["aw_court_statistics"] = "\u7edf\u8ba1",
                ["aw_court_statistics Title"] = "\u7edf\u8ba1",
                ["aw_court_statistics_title"] = "\u4eba\u53e3\u4e0e\u7ecf\u6d4e\u7edf\u8ba1",
                ["aw_court_statistics_national"] = "\u5168\u56fd",
                ["aw_court_statistics_region"] = "\u5dde",
                ["aw_court_statistics_city"] = "\u90e1",
                ["aw_back_to_court"] = "\u8fd4\u56de\u5b98\u573a",
                ["aw_court_statistics_population"] = "\u4eba\u53e3",
                ["aw_court_statistics_city_count"] = "\u57ce\u5e02\u6570",
                ["aw_court_statistics_tax"] = "\u7a0e\u503c",
                ["aw_court_statistics_policy"] = "\u653f\u7b56\u70b9",
                ["aw_court_statistics_technology"] = "\u79d1\u6280\u70b9",
                ["aw_court_statistics_manpower"] = "\u4eba\u529b",
                ["aw_court_statistics_food"] = "\u7cae\u98df\u7a33\u5b9a",
                ["aw_court_statistics_unrest"] = "\u6cbb\u5b89\u98ce\u9669",
                ["aw_court_statistics_unavailable"] = "\u7edf\u8ba1\u4e0d\u53ef\u7528",
                ["aw_court_statistics_records"] = "\u7ecf\u6d4e\u8bb0\u5f55\uff1a{0}/{1}",
                ["aw_court_statistics_no_record"] = "\u6682\u65e0\u5e74\u5ea6\u7ecf\u6d4e\u8bb0\u5f55",
                ["aw_court_statistics_fallback"] = "\u5dde\u8303\u56f4\u4e0d\u53ef\u7528\uff0c\u5f53\u524d\u663e\u793a\u672c\u90e1",
                ["aw_de_jure_region_retire_description"] = "\u64a4\u9500\u9009\u4e2d\u5dde\u6cd5\u7406\uff1b\u64a4\u9500\u540e\u8be5\u5dde\u4e0d\u518d\u663e\u793a\u6cd5\u7406\u8fb9\u754c\uff0c\u57ce\u5e02\u3001\u4eba\u53e3\u548c\u5b98\u573a\u6570\u636e\u4e0d\u4f1a\u88ab\u5220\u9664\u3002\u82e5\u8981\u6062\u590d\uff0c\u9700\u624b\u52a8\u91cd\u65b0\u5efa\u7acb\u6216\u5212\u5206\u5dde\u6cd5\u7406\u3002",
                ["aw_court_vacancy"] = "空缺",
                ["aw_court_history_end_missing"] = "原因不明",
                ["aw_court_history_end_invalid"] = "任职失效",
                ["aw_court_history_end_local_office_reformed"] = "官署改制",
                ["aw_court_history_end_guest_term_expired"] = "客卿任期届满",
                ["aw_court_office_heir"] = "世子",
                ["aw_court_command"] = "统帅",
                ["aw_court_regional_governor"] = "区域长官"
                , ["aw_bandit_amnesty_settlement_title"] = "土匪招安条件"
                , ["aw_bandit_amnesty_reward"] = "许诺赏格"
                , ["aw_bandit_amnesty_reward_none"] = "不许赏格"
                , ["aw_bandit_amnesty_reward_office"] = "授予官职"
                , ["aw_bandit_amnesty_reward_title"] = "授予虚爵"
                , ["aw_bandit_amnesty_confirm"] = "颁诏招安"
                , ["aw_bandit_amnesty_title_placeholder"] = "输入招安所授虚爵名"
            };

        private static readonly Dictionary<string, string> Traditional =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aw_court_vacancy"] = "空缺",
                ["aw_court_history_end_missing"] = "原因不明",
                ["aw_court_history_end_invalid"] = "任職失效",
                ["aw_court_history_end_local_office_reformed"] = "官署改制",
                ["aw_court_history_end_guest_term_expired"] = "客卿任期屆滿",
                ["aw_court_office_heir"] = "世子",
                ["aw_court_command"] = "統帥",
                ["aw_court_regional_governor"] = "區域長官",
                ["aw_de_jure_region_retire_description"] = "\u64a4\u9500\u9078\u4e2d\u7684\u5dde\u6cd5\u7406\uff1b\u64a4\u92b7\u5f8c\u8a72\u5dde\u4e0d\u518d\u986f\u793a\u6cd5\u7406\u908a\u754c\uff0c\u57ce\u5e02\u3001\u4eba\u53e3\u548c\u5b98\u5834\u8cc7\u6599\u4e0d\u6703\u88ab\u522a\u9664\u3002\u82e5\u8981\u6062\u5fa9\uff0c\u9700\u624b\u52d5\u91cd\u65b0\u5efa\u7acb\u6216\u5283\u5206\u5dde\u6cd5\u7406\u3002"
            };

        private static readonly Dictionary<string, string> English =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aw_court_vacancy"] = "Vacant",
                ["aw_court_history_end_missing"] = "Reason unavailable",
                ["aw_court_history_end_invalid"] = "Appointment invalid",
                ["aw_court_history_end_local_office_reformed"] = "Local office reformed",
                ["aw_court_history_end_guest_term_expired"] = "Guest term expired",
                ["aw_court_office_heir"] = "Heir Apparent",
                ["aw_court_command"] = "Command",
                ["aw_court_regional_governor"] = "Regional Governor",
                ["aw_court_statistics_button"] = "Statistics",
                ["aw_court_statistics"] = "Statistics",
                ["aw_court_statistics Title"] = "Statistics",
                ["aw_court_statistics_title"] = "Population and Economy Statistics",
                ["aw_court_statistics_national"] = "Nation",
                ["aw_court_statistics_region"] = "State",
                ["aw_court_statistics_city"] = "Prefecture",
                ["aw_back_to_court"] = "Back to Court",
                ["aw_court_statistics_population"] = "Population",
                ["aw_court_statistics_city_count"] = "Cities",
                ["aw_court_statistics_tax"] = "Tax value",
                ["aw_court_statistics_policy"] = "Policy points",
                ["aw_court_statistics_technology"] = "Technology points",
                ["aw_court_statistics_manpower"] = "Manpower",
                ["aw_court_statistics_food"] = "Food stability",
                ["aw_court_statistics_unrest"] = "Unrest risk",
                ["aw_court_statistics_unavailable"] = "Statistics unavailable",
                ["aw_court_statistics_records"] = "Economy records: {0}/{1}",
                ["aw_court_statistics_no_record"] = "No annual economy record is available",
                ["aw_court_statistics_fallback"] = "Regional scope unavailable; showing the current prefecture."
                , ["aw_de_jure_region_retire_description"] = "Retire the selected de jure state. Its legal boundaries disappear without deleting cities, population, or court records. Recreate or reassign it manually to restore it."
                , ["aw_bandit_amnesty_settlement_title"] = "Bandit Amnesty Terms"
                , ["aw_bandit_amnesty_reward"] = "Promised reward"
                , ["aw_bandit_amnesty_reward_none"] = "No reward"
                , ["aw_bandit_amnesty_reward_office"] = "Grant office"
                , ["aw_bandit_amnesty_reward_title"] = "Grant titular rank"
                , ["aw_bandit_amnesty_confirm"] = "Grant amnesty"
                , ["aw_bandit_amnesty_title_placeholder"] = "Enter the promised titular rank"
            };

        internal static void Ensure()
        {
            try
            {
                string language = LocalizedTextManager.current_language?.id ?? "ch";
                Dictionary<string, string> values = string.Equals(language, "en",
                    StringComparison.OrdinalIgnoreCase) ? English :
                    string.Equals(language, "cz", StringComparison.OrdinalIgnoreCase)
                        ? Traditional : Simplified;
                foreach (KeyValuePair<string, string> entry in values)
                    if (!LocalizedTextManager.stringExists(entry.Key))
                        LM.AddToCurrentLocale(entry.Key, entry.Value);
                LM.ApplyLocale();
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Court localization repair failed: " +
                                    error.Message);
            }
        }
    }
}
