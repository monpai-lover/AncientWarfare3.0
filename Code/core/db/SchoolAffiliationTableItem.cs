using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SchoolAffiliation")]
    public sealed class SchoolAffiliationTableItem : AbstractTableItem<SchoolAffiliationTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long actor_id;
        public long home_kingdom_id = -1;
        public string home_kingdom_name;
        public long hometown_city_id = -1;
        public long residence_city_id = -1;
        public long previous_residence_city_id = -1;
        public long destination_city_id = -1;
        public long service_kingdom_id = -1;
        public string lifecycle_state;
        public int service_start_year = -1;
        public int service_end_year = -1;
        public int last_travel_year = -1;
        public int travel_wait_start_year = -1;
        public int voyage_start_year = -1;
        public int voyage_arrival_year = -1;
        public int transport_failures;
        public double updated_time;
    }
}
