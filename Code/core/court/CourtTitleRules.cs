using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public static class CourtTitleRules
    {
        public static string Combine(params string[] pTitles)
        {
            if (pTitles == null || pTitles.Length == 0) return "";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();
            foreach (string title in pTitles)
            {
                if (string.IsNullOrWhiteSpace(title)) continue;
                string trimmed = title.Trim();
                if (seen.Add(trimmed)) ordered.Add(trimmed);
            }
            return string.Join(" · ", ordered);
        }
    }
}
