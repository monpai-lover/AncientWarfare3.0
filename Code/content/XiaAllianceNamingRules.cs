using System;
using System.Collections.Generic;
using System.Globalization;

namespace AncientWarfare3.content
{
    public static class XiaAllianceNamingRules
    {
        public static bool ShouldUseXiaName(bool pFounder1IsXia, bool pFounder2IsXia)
        {
            return pFounder1IsXia || pFounder2IsXia;
        }

        public static bool ShouldRenameAfterCreation(bool pUsesXiaNaming, bool pValidName)
        {
            return pUsesXiaNaming && pValidName;
        }

        public static bool ShouldFinalizeCreation(bool usesXiaNaming, bool customName)
        {
            return usesXiaNaming && !customName;
        }

        public static string ResolveMeetingCity(params string[] pCandidates)
        {
            if (pCandidates == null) return "";
            foreach (string candidate in pCandidates)
            {
                string value = candidate?.Trim();
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return "";
        }

        public static string MeetingName(string pCityName)
        {
            string city = ResolveMeetingCity(pCityName);
            return string.IsNullOrEmpty(city) ? "" : city + "之盟";
        }

        public static string ResolveUniqueName(string pCandidate, long pAllianceId,
            IEnumerable<string> pUsedNames)
        {
            string candidate = pCandidate?.Trim() ?? "";
            if (string.IsNullOrEmpty(candidate)) return "";

            var used = new HashSet<string>(StringComparer.Ordinal);
            if (pUsedNames != null)
            {
                foreach (string name in pUsedNames)
                    if (!string.IsNullOrEmpty(name)) used.Add(name);
            }
            if (!used.Contains(candidate)) return candidate;

            string id = Math.Abs(pAllianceId).ToString(CultureInfo.InvariantCulture);
            string resolved = candidate + "·" + id;
            int collision = 2;
            while (used.Contains(resolved)) resolved = candidate + "·" + id + "-" + collision++;
            return resolved;
        }
    }
}
