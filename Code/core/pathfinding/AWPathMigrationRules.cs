namespace AncientWarfare3.core.pathfinding
{
    internal enum CultiwayPathFeature
    {
        Session,
        ActorHooks,
        DockTransport,
        Dock,
        Boat,
        Passenger,
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
                   pFeature == CultiwayPathFeature.DockTransport ||
                   pFeature == CultiwayPathFeature.Dock ||
                   pFeature == CultiwayPathFeature.Boat ||
                   pFeature == CultiwayPathFeature.Passenger;
        }
    }
}
