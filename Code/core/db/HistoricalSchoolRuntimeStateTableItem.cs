using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("HistoricalSchoolRuntimeState")]
    public sealed class HistoricalSchoolRuntimeStateTableItem :
        AbstractTableItem<HistoricalSchoolRuntimeStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long state_id;
        public int eligible_year;
        public int last_world_year = -1;
        public double updated_time;
    }
}
