namespace AncientWarfare3.core.pathfinding
{
    internal enum CultiwayPathFeature
    {
        Session,
        ActorHooks,
        DockTransport,
        Teleport,
        Train,
        Cultivation
    }

    internal static class AWPathMigrationRules
    {
        internal static bool ShouldImport(CultiwayPathFeature pFeature)
        {
            return pFeature == CultiwayPathFeature.Session ||
                   pFeature == CultiwayPathFeature.ActorHooks ||
                   pFeature == CultiwayPathFeature.DockTransport;
        }
    }
}
