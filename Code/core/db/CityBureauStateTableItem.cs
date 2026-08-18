using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("CityBureauState")]
    public class CityBureauStateTableItem : AbstractTableItem<CityBureauStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long city_id;
        public long kingdom_id;
        public string city_name;
        public int office_slots;
        public string local_school;
        public double bureau_efficiency;
        public string officer_actor_ids;
        public string local_template_id;
        public int local_template_manual;
        public int last_refresh_year;
        public double updated_time;
    }
}
