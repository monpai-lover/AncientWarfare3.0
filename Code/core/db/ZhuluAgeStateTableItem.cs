using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("ZhuluAgeState")]
    public sealed class ZhuluAgeStateTableItem :
        AbstractTableItem<ZhuluAgeStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long state_id;
        public int entry_active;
        public double updated_time;
    }
}
