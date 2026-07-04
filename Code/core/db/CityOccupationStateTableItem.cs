using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("CityOccupationState")]
    public class CityOccupationStateTableItem : AbstractTableItem<CityOccupationStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long occupation_id;

        public long city_id = -1;
        public string city_name = "";
        public long original_kingdom_id = -1;
        public string original_kingdom_name = "";
        public string original_kingdom_color = "";
        public long occupier_kingdom_id = -1;
        public string occupier_kingdom_name = "";
        public string occupier_kingdom_color = "";
        public string original_culture_id = "";
        public string original_language_id = "";
        public string occupier_culture_id = "";
        public string occupier_language_id = "";
        public string occupation_type = "";
        public double assimilation_progress = 0;
        public double resentment = 0;
        public int slave_converted_count = 0;
        public int leader_replaced = 0;
        public double start_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double end_time = -1;
        public double updated_time = -1;
    }
}
