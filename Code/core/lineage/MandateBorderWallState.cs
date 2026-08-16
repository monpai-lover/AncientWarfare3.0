using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal sealed class MandateBorderWallState
    {
        internal const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public bool Activated { get; set; }
        public long SourceKingdomId { get; set; } = -1L;
        public Dictionary<long, MandateBorderCityWallManifest> Cities
            { get; set; } =
            new Dictionary<long, MandateBorderCityWallManifest>();
    }

    internal sealed class MandateBorderCityWallManifest
    {
        public long CityId { get; set; }
        public string WallTypeId { get; set; } = "";
        public List<MandateBorderWallPointState> Points { get; set; } =
            new List<MandateBorderWallPointState>();
    }

    internal sealed class MandateBorderWallPointState
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string OriginalTopTypeId { get; set; } = "";
    }
}
