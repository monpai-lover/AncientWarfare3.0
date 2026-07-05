using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class AncestryDisplayRules
    {
        public static float PercentForAncestorDistance(int pDistance)
        {
            if (pDistance <= 0) return 100f;
            return 100f / (float)Math.Pow(2, pDistance);
        }

        public static string FormatNobleAncestorLabel(string pCityName, string pClanName,
            string pActorName, string pSocialTitle, float pPercent)
        {
            var parts = new List<string>();
            string lineage = FormatLineageLabel(pCityName, pClanName);
            if (!string.IsNullOrEmpty(lineage)) parts.Add(lineage);
            parts.Add(pPercent.ToString("0.0") + "%");
            if (!string.IsNullOrEmpty(pActorName)) parts.Add(pActorName.Trim());
            if (!string.IsNullOrEmpty(pSocialTitle)) parts.Add(pSocialTitle.Trim());
            return string.Join(" ", parts.ToArray());
        }

        public static string FormatLineageLabel(string pCityName, string pClanName)
        {
            string city = (pCityName ?? "").Trim();
            string clan = NormalizeClanName(pClanName);
            if (!string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(clan)) return city + clan + "\u6c0f";
            if (!string.IsNullOrEmpty(clan)) return clan + "\u6c0f";
            return city;
        }

        public static string NormalizeClanName(string pClanName)
        {
            string clan = (pClanName ?? "").Trim();
            if (clan.EndsWith("\u6c0f")) clan = clan.Substring(0, clan.Length - 1);
            return clan;
        }

        public static bool ShouldUseNobleAncestorRowsForSocialSection(int pNobleAncestorCount)
        {
            return pNobleAncestorCount > 0;
        }
    }
}
