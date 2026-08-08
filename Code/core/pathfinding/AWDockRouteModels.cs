namespace AncientWarfare3.core.pathfinding
{
    internal readonly struct AWDockEndpointKey :
        System.IEquatable<AWDockEndpointKey>
    {
        internal AWDockEndpointKey(long pDockId, int pWaterComponent)
        {
            DockId = pDockId;
            WaterComponent = pWaterComponent;
        }

        internal long DockId { get; }
        internal int WaterComponent { get; }

        public bool Equals(AWDockEndpointKey pOther)
        {
            return DockId == pOther.DockId &&
                   WaterComponent == pOther.WaterComponent;
        }

        public override bool Equals(object pObject)
        {
            return pObject is AWDockEndpointKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return DockId.GetHashCode() * 397 ^ WaterComponent;
            }
        }
    }

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
        internal AWDockEndpointKey Key => new AWDockEndpointKey(Id,
            WaterComponent);
    }

    internal readonly struct AWDockRouteCandidate
    {
        internal AWDockRouteCandidate(AWDockEndpoint pEntry,
            AWDockEndpoint pExit, float pEstimatedRouteTiles)
        {
            Entry = pEntry;
            Exit = pExit;
            EstimatedRouteTiles = pEstimatedRouteTiles;
        }

        internal AWDockEndpoint Entry { get; }
        internal AWDockEndpoint Exit { get; }
        internal float EstimatedRouteTiles { get; }
        internal bool IsValid => Entry.IsValid && Exit.IsValid &&
                                 Entry.Id != Exit.Id &&
                                 Entry.WaterComponent == Exit.WaterComponent &&
                                 !float.IsNaN(EstimatedRouteTiles) &&
                                 !float.IsInfinity(EstimatedRouteTiles) &&
                                 EstimatedRouteTiles >= 0f;
    }
}
