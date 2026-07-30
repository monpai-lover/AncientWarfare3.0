using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SpyNetwork")]
    public sealed class SpyNetworkTableItem :
        AbstractTableItem<SpyNetworkTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long network_id;
        public long source_kingdom_id = -1;
        public long target_kingdom_id = -1;
        public int points;
        public int last_accrual_year = -1;
        public double last_accrual_time = -1;
        public int active = 1;
    }
}
