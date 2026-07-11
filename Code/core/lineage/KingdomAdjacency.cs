namespace AncientWarfare3.core.lineage
{
    internal static class KingdomAdjacency
    {
        public static bool AreDirectNeighbors(Kingdom pA, Kingdom pB)
        {
            if (pA?.data == null || pB?.data == null || pA == pB || pA.isRekt() || pB.isRekt())
                return false;

            try
            {
                foreach (City city in pA.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    foreach (Kingdom neighbor in city.neighbours_kingdoms)
                        if (neighbor == pB) return true;
                }
            }
            catch { }

            return false;
        }
    }
}
