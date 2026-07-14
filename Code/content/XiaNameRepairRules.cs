using System;

namespace AncientWarfare3.content
{
    public static class XiaNameRepairRules
    {
        public static bool IsInvalidGeneratedMetaName(string pName)
        {
            if (string.IsNullOrWhiteSpace(pName)) return true;

            string trimmed = pName.Trim();
            if (trimmed == "无名" || trimmed == "无名氏") return true;

            string normalized = Normalize(trimmed).Trim('#');
            if (normalized.Length == 0) return true;

            return normalized.StartsWith("NAME", StringComparison.Ordinal) ||
                   normalized == "NO_NAME" ||
                   normalized == "COLLECTIVE_NAME" ||
                   (normalized.StartsWith("NO", StringComparison.Ordinal) &&
                    normalized.IndexOf("NAME", StringComparison.Ordinal) >= 0) ||
                   normalized.StartsWith("NO_NAME ", StringComparison.Ordinal) ||
                   normalized.StartsWith("NO_NAME[", StringComparison.Ordinal) ||
                   normalized.StartsWith("NO_NAME [", StringComparison.Ordinal) ||
                   normalized.IndexOf("$", StringComparison.Ordinal) >= 0;
        }

        public static bool IsInvalidXiaSubspeciesName(string pName)
        {
            if (IsInvalidGeneratedMetaName(pName)) return true;

            string trimmed = pName.Trim();
            return trimmed == "夏" ||
                   trimmed == "夏人" ||
                   trimmed == "夏人人" ||
                   trimmed.EndsWith("人人", StringComparison.Ordinal);
        }

        public static bool IsInvalidXiaReligionName(string pName)
        {
            return IsInvalidGeneratedMetaName(pName);
        }

        private static string Normalize(string pName)
        {
            return pName.Trim()
                .Replace("-", "_")
                .ToUpperInvariant();
        }
    }
}
