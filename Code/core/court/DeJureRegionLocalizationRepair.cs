using System;
using System.Collections.Generic;
using NeoModLoader.General;

namespace AncientWarfare3.core.court
{
    internal static class DeJureRegionLocalizationRepair
    {
        private static readonly Dictionary<string, string> Simplified =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aw_de_jure_region_create"] = "建立州法理",
                ["aw_de_jure_region_create_description"] =
                    "使用神力在选定城市建立新的州法理",
                ["aw_de_jure_region_assign"] = "划入州法理",
                ["aw_de_jure_region_assign_description"] =
                    "使用神力先选择州法理首府，再将城市划入该州法理"
            };

        private static readonly Dictionary<string, string> Traditional =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aw_de_jure_region_create"] = "建立州法理",
                ["aw_de_jure_region_create_description"] =
                    "使用神力在選定城市建立新的州法理",
                ["aw_de_jure_region_assign"] = "劃入州法理",
                ["aw_de_jure_region_assign_description"] =
                    "使用神力先選擇州法理首府，再將城市劃入該州法理"
            };

        private static readonly Dictionary<string, string> English =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aw_de_jure_region_create"] = "Create de jure state",
                ["aw_de_jure_region_create_description"] =
                    "Use divine power to create a new de jure state at the selected city",
                ["aw_de_jure_region_assign"] = "Assign to de jure state",
                ["aw_de_jure_region_assign_description"] =
                    "Select a de jure capital, then assign cities to that state"
            };

        internal static void Ensure()
        {
            try
            {
                string language = LocalizedTextManager.current_language?.id ??
                                   "ch";
                Dictionary<string, string> values =
                    string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
                        ? English
                        : string.Equals(language, "cz",
                            StringComparison.OrdinalIgnoreCase)
                            ? Traditional
                            : Simplified;
                foreach (KeyValuePair<string, string> entry in values)
                    LM.AddToCurrentLocale(entry.Key, entry.Value);
                LM.ApplyLocale();
            }
            catch (Exception error)
            {
                ModClass.LogError("De jure localization repair failed: " +
                                  error.Message);
            }
        }
    }
}
