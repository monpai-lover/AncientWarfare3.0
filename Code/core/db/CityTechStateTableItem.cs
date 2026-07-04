using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("CityTechState")]
    public class CityTechStateTableItem : AbstractTableItem<CityTechStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long record_id;

        public long city_id = -1;
        public string city_name = "";
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string tech_id = "";
        public int adopted = 0;
        public double adoption_progress = 0;
        public double exposure_progress = 0;
        public string source_type = "";
        public long source_city_id = -1;
        public long source_kingdom_id = -1;
        public double first_seen_time = -1;
        public double adopted_time = -1;
        public double updated_time = -1;
    }
}
