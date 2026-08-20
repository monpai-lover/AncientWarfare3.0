using System;
using System.Collections.Generic;
using NeoModLoader.General;

namespace AncientWarfare3.core.court
{
    internal static class CourtImmediateVacancyLocalization
    {
        private static readonly Dictionary<string, string> Simplified =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aw_court_fill_vacancies"] = "立即补缺",
                ["aw_court_fill_vacancies_desc"] = "校验中央官员并立即补足空缺；西式制度会进入选举队列。",
                ["aw_court_fill_vacancies_success"] = "中央官场补缺完成",
                ["aw_court_fill_vacancies_queued"] = "空缺已加入西式选举队列",
                ["aw_court_fill_vacancies_no_change"] = "中央官场暂无新的空缺",
                ["aw_court_fill_vacancies_invalid"] = "国家不存在或已灭亡",
                ["aw_court_fill_vacancies_unavailable"] = "中央官场当前不可用"
            };

        private static readonly Dictionary<string, string> Traditional =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aw_court_fill_vacancies"] = "立即補缺",
                ["aw_court_fill_vacancies_desc"] = "校驗中央官員並立即補足空缺；西式制度會進入選舉隊列。",
                ["aw_court_fill_vacancies_success"] = "中央官場補缺完成",
                ["aw_court_fill_vacancies_queued"] = "空缺已加入西式選舉隊列",
                ["aw_court_fill_vacancies_no_change"] = "中央官場暫無新的空缺",
                ["aw_court_fill_vacancies_invalid"] = "國家不存在或已滅亡",
                ["aw_court_fill_vacancies_unavailable"] = "中央官場目前不可用"
            };

        private static readonly Dictionary<string, string> English =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aw_court_fill_vacancies"] = "Fill vacancies",
                ["aw_court_fill_vacancies_desc"] = "Validate central officers and immediately fill vacancies; western institutions enter the election queue.",
                ["aw_court_fill_vacancies_success"] = "Central court vacancy repair completed",
                ["aw_court_fill_vacancies_queued"] = "Vacancies added to the western election queue",
                ["aw_court_fill_vacancies_no_change"] = "The central court has no new vacancies",
                ["aw_court_fill_vacancies_invalid"] = "The kingdom is missing or extinct",
                ["aw_court_fill_vacancies_unavailable"] = "The central court is currently unavailable"
            };

        internal static void Ensure()
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
    }
}
