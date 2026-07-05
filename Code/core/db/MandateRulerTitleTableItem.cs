using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("MandateRulerTitle")]
    public class MandateRulerTitleTableItem : AbstractTableItem<MandateRulerTitleTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long record_id;

        public long period_id = -1;
        public long reign_id = -1;
        public long actor_id = -1;
        public string actor_name = "";
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public string temple_name = "";
        public string double_posthumous = "";
        public string full_title = "";
        public string reason_key = "";
        public string score_detail = "";
        public double decided_time = -1;
    }
}
