namespace AncientWarfare3.core.pathfinding
{
    internal static class AWDockEndpointRules
    {
        internal static int ResolveWaterComponent(int pSnapshotComponent,
            int pLegacyFallbackComponent)
        {
            return pSnapshotComponent >= 0
                ? pSnapshotComponent
                : pLegacyFallbackComponent;
        }

        internal static bool SameWaterComponent(int pFirstSnapshotComponent,
            int pSecondSnapshotComponent, int pFirstLegacyComponent,
            int pSecondLegacyComponent)
        {
            return ResolveWaterComponent(pFirstSnapshotComponent,
                       pFirstLegacyComponent) ==
                   ResolveWaterComponent(pSecondSnapshotComponent,
                       pSecondLegacyComponent) &&
                   ResolveWaterComponent(pFirstSnapshotComponent,
                       pFirstLegacyComponent) >= 0;
        }
    }
}
