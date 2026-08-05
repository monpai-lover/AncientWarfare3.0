using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    /// Immutable city-zone geometry captured at a territorial event.  Atlas
    /// generation reads this table only; it never samples the live map.
    /// </summary>
    [TableDef("KingdomAtlasZoneArchive")]
    public class KingdomAtlasZoneArchiveTableItem : AbstractTableItem<KingdomAtlasZoneArchiveTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long snapshot_id;
        public long city_id = -1;
        public double world_time;
        public string event_type = "";
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public int x;
        public int y;
        public int water;
        public int neighbor_mask;
        [TableItemDef(pIsUnique: true)] public string snapshot_key = "";
    }
}
