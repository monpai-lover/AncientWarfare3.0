using System;

namespace AncientWarfare3.core.lineage
{
    public static class DiplomacyFailureReasonRules
    {
        public const int MaximumKeyLength = 128;

        public static string StableKey(string pReason)
        {
            if (string.IsNullOrWhiteSpace(pReason)) return "unavailable";
            string value = pReason.Trim();
            int suffix = value.IndexOf(':');
            if (suffix >= 0) value = value.Substring(0, suffix);
            if (value.Length == 0 || value.Length > MaximumKeyLength)
                return "execution_failed";
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 'a' && c <= 'z' ||
                    c >= '0' && c <= '9' || c == '_')
                    continue;
                return "execution_failed";
            }
            return value;
        }
    }
}
