namespace AncientWarfare3.core.pathfinding
{
    internal readonly struct AWDockEndpoint
    {
        internal AWDockEndpoint(long pId, int pTileId, int pWaterComponent,
            int pLegacyWaterComponent = -1)
        {
            Id = pId;
            TileId = pTileId;
            WaterComponent = pWaterComponent;
            LegacyWaterComponent = pLegacyWaterComponent;
        }

        internal long Id { get; }
        internal int TileId { get; }
        internal int WaterComponent { get; }
        internal int LegacyWaterComponent { get; }
        internal bool IsValid => Id > 0 && TileId >= 0 && WaterComponent >= 0;
    }

    internal readonly struct AWDockRouteCandidate
    {
        internal AWDockRouteCandidate(AWDockEndpoint pEntry,
            AWDockEndpoint pExit)
        {
            Entry = pEntry;
            Exit = pExit;
        }

        internal AWDockEndpoint Entry { get; }
        internal AWDockEndpoint Exit { get; }
        internal bool IsValid => Entry.IsValid && Exit.IsValid &&
                                 Entry.Id != Exit.Id &&
                                 Entry.WaterComponent == Exit.WaterComponent;
    }
}
