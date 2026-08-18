namespace AncientWarfare3.core.pathfinding
{
    internal enum AWTransportRouteSource
    {
        DockPortal = 0,
        ShoreFallback = 1
    }

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
        internal AWDockEndpoint(long pId, int pLandTileId,
            int pOceanTileId, int pWaterComponent)
        {
            Id = pId;
            LandTileId = pLandTileId;
            OceanTileId = pOceanTileId;
            WaterComponent = pWaterComponent;
        }

        // Legacy rule tests and save adapters only have one physical tile.
        // Treat it as both sides of the endpoint; runtime dock routes use the
        // explicit land/ocean constructor above.
        internal AWDockEndpoint(long pId, int pTileId,
            int pWaterComponent)
            : this(pId, pTileId, pTileId, pWaterComponent)
        {
        }

        internal long Id { get; }
        internal int LandTileId { get; }
        internal int OceanTileId { get; }
        internal int TileId => LandTileId;
        internal int WaterComponent { get; }
        internal int LegacyWaterComponent => -1;
        internal bool HasPhysicalTiles => LandTileId >= 0 &&
                                          OceanTileId >= 0 &&
                                          WaterComponent >= 0;
        internal bool IsDockPortal => Id > 0 && HasPhysicalTiles;
        internal AWDockEndpointKey Key => new AWDockEndpointKey(Id,
            WaterComponent);
        internal bool IsValid => HasPhysicalTiles;
    }

    internal readonly struct AWDockRouteCandidate
    {
        internal AWDockRouteCandidate(AWTransportRouteSource pSource,
            AWDockEndpoint pEntry, AWDockEndpoint pExit,
            float pEstimatedRouteTiles)
        {
            Source = pSource;
            Entry = pEntry;
            Exit = pExit;
            EstimatedRouteTiles = pEstimatedRouteTiles;
        }

        internal AWTransportRouteSource Source { get; }
        internal AWDockEndpoint Entry { get; }
        internal AWDockEndpoint Exit { get; }
        internal float EstimatedRouteTiles { get; }
        internal bool IsValid => Entry.HasPhysicalTiles &&
                                 Exit.HasPhysicalTiles &&
                                 (Source == AWTransportRouteSource.
                                      ShoreFallback ||
                                  Entry.Id > 0 && Exit.Id > 0 &&
                                  Entry.Id != Exit.Id) &&
                                 Entry.WaterComponent == Exit.WaterComponent &&
                                 !float.IsNaN(EstimatedRouteTiles) &&
                                 !float.IsInfinity(EstimatedRouteTiles) &&
                                 EstimatedRouteTiles >= 0f;
    }
}
