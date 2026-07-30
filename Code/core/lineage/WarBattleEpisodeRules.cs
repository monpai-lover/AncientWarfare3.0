using System;

namespace AncientWarfare3.core.lineage
{
    public static class WarBattleEpisodeRules
    {
        public const int MinimumTotalDeaths = 4;
        public const int MinimumWinningMargin = 2;

        public static bool TryResolve(int pAttackerDeaths,
            int pDefenderDeaths, out WarScoreSide pWinner,
            out int pIntensity)
        {
            pWinner = WarScoreSide.None;
            pIntensity = 0;
            if (pAttackerDeaths <= 0 || pDefenderDeaths <= 0) return false;

            long total = (long)pAttackerDeaths + pDefenderDeaths;
            long margin = Math.Abs((long)pAttackerDeaths - pDefenderDeaths);
            if (total < MinimumTotalDeaths ||
                margin < MinimumWinningMargin) return false;

            pWinner = pAttackerDeaths < pDefenderDeaths
                ? WarScoreSide.Attackers
                : WarScoreSide.Defenders;
            pIntensity = (int)Math.Min(int.MaxValue, total);
            return true;
        }
    }
}
