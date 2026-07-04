using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("CityEconomyState")]
    public class CityEconomyStateTableItem : AbstractTableItem<CityEconomyStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long city_id;

        public string city_name = "";
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public string role = "";
        public string previous_role = "";
        public float policy_points = 0;
        public float tech_points = 0;
        public float tax_value = 0;
        public float manpower = 0;
        public float food_stability = 0;
        public float unrest_risk = 0;
        public int last_year = -1;
        public double updated_time = -1;
    }
}
