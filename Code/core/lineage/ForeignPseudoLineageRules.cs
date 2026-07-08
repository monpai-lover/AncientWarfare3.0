using System;

namespace AncientWarfare3.core.lineage
{
    public static class ForeignPseudoLineageRules
    {
        private static readonly char[] NameDelimiters = { ' ', '\t', '\u00b7', '\u2022', '-', '_', '/', '\\' };

        public static string ExtractClanName(string pDisplayName, string pFallback)
        {
            string raw = (pDisplayName ?? "").Trim();
            int index = LastDelimiterIndex(raw);
            string clan = index >= 0 && index + 1 < raw.Length ? raw.Substring(index + 1).Trim() : "";
            if (string.IsNullOrEmpty(clan)) clan = (pFallback ?? "").Trim();
            if (string.IsNullOrEmpty(clan)) clan = raw;
            if (clan.EndsWith("氏", StringComparison.Ordinal) && clan.Length > 1)
                clan = clan.Substring(0, clan.Length - 1);
            return clan;
        }

        public static string ExtractGivenName(string pDisplayName)
        {
            string raw = (pDisplayName ?? "").Trim();
            int index = LastDelimiterIndex(raw);
            string given = index > 0 ? raw.Substring(0, index).Trim() : raw;
            return string.IsNullOrEmpty(given) ? raw : given;
        }

        public static bool ShouldUseAwLineageSystem(bool pIsXiaActor, bool pKingdomIsForeignPseudoDynasty,
            bool pHasLineage)
        {
            return pHasLineage && (pIsXiaActor || pKingdomIsForeignPseudoDynasty);
        }

        public static bool ShouldIntegrateOfficial(bool pIsKing, bool pIsCityLeader, bool pIsArmyLeader)
        {
            return pIsKing || pIsCityLeader || pIsArmyLeader;
        }

        private static int LastDelimiterIndex(string pRaw)
        {
            if (string.IsNullOrEmpty(pRaw)) return -1;
            return pRaw.LastIndexOfAny(NameDelimiters);
        }
    }
}
