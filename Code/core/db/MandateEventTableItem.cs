using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("MandateEvent")]
    public class MandateEventTableItem : AbstractTableItem<MandateEventTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long event_id;

        public long period_id = -1;
        public string event_type = "";
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public long actor_id = -1;
        public string actor_name = "";
        public long city_id = -1;
        public string city_name = "";
        public double world_time = -1;
        public string year_prefix = "";
        public int value_delta = 0;
        public int mandate_value = 0;
        public int imperial_authority = 0;
        public string content = "";
    }
}
