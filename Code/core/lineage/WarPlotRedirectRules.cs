namespace AncientWarfare3.core.lineage
{
    public static class WarPlotRedirectRules
    {
        public const string NewWarPlotId = "new_war";
        public const string AllianceCreatePlotId = "alliance_create";
        public const string AllianceJoinPlotId = "alliance_join";
        public const string AllianceDestroyPlotId = "alliance_destroy";
        public const string StopWarPlotId = "attacker_stop_war";

        public static bool IsManagedDiplomacyPlot(string pPlotId)
        {
            return pPlotId == NewWarPlotId ||
                   pPlotId == AllianceCreatePlotId ||
                   pPlotId == AllianceJoinPlotId ||
                   pPlotId == AllianceDestroyPlotId ||
                   pPlotId == StopWarPlotId;
        }

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
