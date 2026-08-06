namespace AncientWarfare3.core.pathfinding
{
    internal readonly struct AWDockEndpoint
    {
        internal AWDockEndpoint(long pId, int pTileId, int pWaterComponent)
        {
            Id = pId;
            TileId = pTileId;
            WaterComponent = pWaterComponent;
        }

        internal long Id { get; }
        internal int TileId { get; }
        internal int WaterComponent { get; }
        internal bool IsValid => Id > 0 && TileId >= 0 && WaterComponent >= 0;
    }

    internal readonly struct AWDockRouteCandidate
    {
        internal AWDockRouteCandidate(AWDockEndpoint pEntry,
            AWDockEndpoint pExit, float pCost)
        {
            Entry = pEntry;
            Exit = pExit;
            Cost = pCost < 0f ? 0f : pCost;
        }

        internal AWDockEndpoint Entry { get; }
        internal AWDockEndpoint Exit { get; }
        internal float Cost { get; }
        internal bool IsValid => Entry.IsValid && Exit.IsValid &&
                                  Entry.Id != Exit.Id &&
                                  Entry.WaterComponent == Exit.WaterComponent;
    }
}
