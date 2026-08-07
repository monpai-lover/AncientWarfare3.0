using System;

namespace AncientWarfare3.core.lineage
{
    internal static class PosthumousStateNameRules
    {
        public static string Resolve(string pHistoricalStateName,
            string pCurrentKingdomName, string pNobleTitleName,
            int pNobleRank, int pKingdomTitle)
        {
            string noble = (pNobleTitleName ?? "").Trim();
            if (MatchesRulingTitle(pNobleRank, pKingdomTitle) &&
                noble.Length > 0) return noble;

            string current = (pCurrentKingdomName ?? "").Trim();
            if (current.Length > 0) return current;
            return (pHistoricalStateName ?? "").Trim();
        }

        private static bool MatchesRulingTitle(int pNobleRank,
            int pKingdomTitle)
        {
            int expectedRank = pKingdomTitle switch
            {
                0 => NobleRankRules.RankCountyMale,
                1 => NobleRankRules.RankCountyMarquis,
                2 => NobleRankRules.RankCountyDuke,
                3 => NobleRankRules.RankStateDuke,
                4 => NobleRankRules.RankPrince,
                _ => NobleRankRules.RankNone
            };
            return expectedRank > NobleRankRules.RankNone &&
                   NobleRankRules.ClampRank(pNobleRank) == expectedRank;
        }
    }
}
