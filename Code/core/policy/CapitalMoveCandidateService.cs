using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class CapitalMoveCandidateService
    {
        public static bool CanConsider(City pCandidate, Kingdom pKingdom, City pCurrentCapital)
        {
            InspectNeighbors(pCandidate, pKingdom, out bool hasOwnNeighbor, out bool touchesForeignBorder);
            return CapitalMoveRules.CanConsiderCandidate(
                pCandidateAlive: pCandidate?.data != null && pCandidate.isAlive() && !pCandidate.isRekt(),
                pIsCurrentCapital: pCandidate == pCurrentCapital,
                pIsCoreCity: WarTerritoryService.HasCore(pKingdom, pCandidate),
                pHasOwnNeighbor: hasOwnNeighbor,
                pTouchesForeignBorder: touchesForeignBorder);
        }

        private static void InspectNeighbors(City pCity, Kingdom pKingdom, out bool pHasOwnNeighbor,
            out bool pTouchesForeignBorder)
        {
            pHasOwnNeighbor = false;
            pTouchesForeignBorder = false;
            if (pCity?.data == null || pKingdom?.data == null) return;

            try
            {
                foreach (City neighbor in pCity.neighbours_cities)
                {
                    if (neighbor?.data == null || neighbor.isRekt() || !neighbor.isAlive()) continue;
                    Kingdom owner = neighbor.kingdom;
                    if (owner == pKingdom)
                    {
                        pHasOwnNeighbor = true;
                        continue;
                    }

                    if (owner?.data != null && !owner.isRekt() && !owner.isNeutral())
                        pTouchesForeignBorder = true;
                }
            }
            catch
            {
                pHasOwnNeighbor = false;
                pTouchesForeignBorder = true;
            }
        }
    }
}
