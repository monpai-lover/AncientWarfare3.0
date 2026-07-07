namespace AncientWarfare3.core.lineage
{
    public static class WarAllianceRules
    {
        public static bool CanStartWar(bool pSameAlliance, bool pSystemWar, bool pIndependenceWar,
            out string pReason)
        {
            pReason = "";
            if (pSystemWar || pIndependenceWar) return true;
            if (!pSameAlliance) return true;

            pReason = "same_alliance";
            return false;
        }
    }
}
