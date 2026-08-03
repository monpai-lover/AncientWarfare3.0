using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("MandateCoreCity")]
    public class MandateCoreCityTableItem : AbstractTableItem<MandateCoreCityTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long core_id;

        public long period_id = -1;
        public long city_id = -1;
        public string city_name = "";
        public long original_kingdom_id = -1;
        public string original_kingdom_name = "";
        public string original_kingdom_color = "";
        public string core_type = "founding";
        public double added_time = -1;
        public int active = 1;
        public string projection_key = "";
    }
}
