using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class MapModeTooltipTextRules
    {
        public static string BuildPointedCityStatusBlock(string pCityPrefix, string pStatusPrefix,
            string pProgressPrefix, string pCityName, string pStatusLabel, double pProgress, double pCost)
        {
            if (string.IsNullOrWhiteSpace(pCityName)) return "";

            var lines = new List<string> { (pCityPrefix ?? "") + pCityName };
            if (!string.IsNullOrWhiteSpace(pStatusLabel))
                lines.Add((pStatusPrefix ?? "") + pStatusLabel);

            string progress = BuildProgressLine(pProgressPrefix, pProgress, pCost);
            if (!string.IsNullOrEmpty(progress))
                lines.Add(progress);

            return string.Join("\n", lines.ToArray());
        }

        public static string BuildProgressLine(string pProgressPrefix, double pProgress, double pCost)
        {
            if (pCost <= 0.0 || pProgress < 0.0) return "";
            int percent = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, pProgress / pCost)) * 100.0,
                MidpointRounding.AwayFromZero);
            return (pProgressPrefix ?? "") + percent + "%";
        }
    }
}
