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
                ["aw_court_vacancy"] = "空缺",
                ["aw_court_history_end_missing"] = "原因不明",
                ["aw_court_history_end_invalid"] = "任职失效",
                ["aw_court_history_end_local_office_reformed"] = "官署改制",
                ["aw_court_history_end_guest_term_expired"] = "客卿任期届满",
                ["aw_court_office_heir"] = "世子",
                ["aw_court_command"] = "统帅",
                ["aw_court_regional_governor"] = "区域长官"
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
                ["aw_court_regional_governor"] = "區域長官"
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
                ["aw_court_regional_governor"] = "Regional Governor"
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
