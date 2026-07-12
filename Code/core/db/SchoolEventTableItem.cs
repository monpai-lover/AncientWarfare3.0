using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SchoolEvent")]
    public sealed class SchoolEventTableItem : AbstractTableItem<SchoolEventTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long event_id;
        public string event_type;
        public long actor_id = -1;
        public long target_actor_id = -1;
        public string school_id;
        public long city_id = -1;
        public long kingdom_id = -1;
        public int event_year;
        public string payload;
        public int importance;
        public double world_time;
    }
}
