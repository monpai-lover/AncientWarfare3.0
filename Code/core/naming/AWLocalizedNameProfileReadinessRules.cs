using System;

namespace AncientWarfare3.core.naming
{
    public static class AWLocalizedNameProfileReadinessRules
    {
        public static bool IsReady(string generatorId,
            string persistedProfileId)
        {
            string generator = (generatorId ?? string.Empty).Trim();
            string profile = (persistedProfileId ?? string.Empty).Trim();
            if (generator.StartsWith("western_", StringComparison.Ordinal))
                return string.Equals(profile, "western",
                    StringComparison.Ordinal);
            if (generator.StartsWith("orc_nomadic_",
                    StringComparison.Ordinal))
                return string.Equals(profile, "orc_nomadic",
                    StringComparison.Ordinal);
            if (generator.StartsWith("Xia_", StringComparison.Ordinal))
                return string.Equals(profile, "xia",
                    StringComparison.Ordinal);
            if (generator.StartsWith("civ_monkey_",
                    StringComparison.Ordinal))
                return string.Equals(profile, "monkey",
                    StringComparison.Ordinal);
            return generator.Length > 0;
        }
    }
}
