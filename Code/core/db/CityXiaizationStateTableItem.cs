using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("CityXiaizationState")]
    public class CityXiaizationStateTableItem : AbstractTableItem<CityXiaizationStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long city_id;

        public string city_name = "";
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public string mode = "";
        public double xia_progress = 0;
        public double foreign_elite_progress = 0;
        public double resentment = 0;
        public string original_culture_id = "";
        public string original_language_id = "";
        public string court_culture_id = "";
        public string court_language_id = "";
        public double start_time = -1;
        public double updated_time = -1;
    }
}
