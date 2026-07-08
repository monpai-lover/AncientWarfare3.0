namespace AncientWarfare3.core.lineage
{
    public static class WarPlotRedirectRules
    {
        public const string NewWarPlotId = "new_war";

        public static bool ShouldRedirectNewWarPlot(string pPlotId,
            bool pCivilKingdom,
            bool pCanUseAwDecision,
            bool pAw3AllowedWarStart)
        {
            if (pAw3AllowedWarStart) return false;
            if ((pPlotId ?? "") != NewWarPlotId) return false;
            return pCivilKingdom;
        }

        public static bool ShouldInterceptActiveNewWarPlot(string pPlotId,
            bool pCivilKingdom,
            bool pCanUseAwDecision,
            bool pAw3AllowedWarStart)
        {
            return (pPlotId ?? "") == NewWarPlotId;
        }
    }
}
