using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal sealed class PeasantRebelBanditCreationContext
    {
        internal Kingdom Bandit;
        internal Kingdom Origin;
        internal City Mother;
        internal Actor Ruler;
        internal bool RemoveBanditOnFailure;
        internal Func<City, bool> FinalizeGovernment;
        internal Action RollbackGovernment;
    }

    internal sealed class PeasantRebelBanditStrongholdPlan
    {
        internal PeasantRebelBanditCreationContext Context;
        internal TileZone CenterZone;
        internal List<TileZone> InteriorZones = new List<TileZone>();
        internal List<TileZone> ExteriorZones = new List<TileZone>();
        internal List<CultiwayWallPoint> WallPoints =
            new List<CultiwayWallPoint>();
        internal List<CultiwayWallPoint> GateCenters =
            new List<CultiwayWallPoint>();
        internal List<WorldTile> TowerTiles = new List<WorldTile>();
        internal BuildingAsset TowerAsset;
        internal List<string> FixedZoneKeys = new List<string>();
        internal Actor ReserveMotherActor;
        internal WorldTile MotherCoreTile;
        internal bool RequiresMotherCore;
    }
}
