namespace AncientWarfare3.core.lineage
{
    internal static class CityOccupationThreatRules
    {
        internal static bool IsEnemyOccupationActive(
            bool pPhysicalOccupation,
            bool pPhysicalControllerHostile,
            bool pFrozenOccupation,
            bool pFrozenControllerHostile)
        {
            return (pPhysicalOccupation && pPhysicalControllerHostile) ||
                   (pFrozenOccupation && pFrozenControllerHostile);
        }
    }
}
