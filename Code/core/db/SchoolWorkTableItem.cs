using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SchoolWork")]
    public sealed class SchoolWorkTableItem : AbstractTableItem<SchoolWorkTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long work_id;
        public string work_key;
        public string display_name;
        public string school_id;
        public long author_actor_id = -1;
        public long city_id = -1;
        public long institution_id = -1;
        public int written_year;
        public int preserved = 1;
        public double condition = 100d;
        public double updated_time;
    }
}
