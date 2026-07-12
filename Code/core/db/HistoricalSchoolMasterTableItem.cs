using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("HistoricalSchoolMaster")]
    public sealed class HistoricalSchoolMasterTableItem :
        AbstractTableItem<HistoricalSchoolMasterTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public string master_id;
        public long actor_id = -1;
        public string school_id;
        public string canonical_name;
        public int spawned;
        public int dead;
        public long home_kingdom_id = -1;
        public string home_kingdom_name;
        public long hometown_city_id = -1;
        public int spawn_year = -1;
        public int death_year = -1;
        public string lifecycle_state;
        public string death_cause;
        public long death_city_id = -1;
        public double updated_time;
    }
}
