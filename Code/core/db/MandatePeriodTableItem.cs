using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("MandatePeriod")]
    public class MandatePeriodTableItem : AbstractTableItem<MandatePeriodTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long period_id;

        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public string dynasty_name = "";
        public long founder_actor_id = -1;
        public string founder_name = "";
        public double start_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double end_time = -1;
        public string end_reason = "";
        public int start_mandate = 30;
        public int end_mandate = 30;
        public int legal_core_count = 0;
        public string origin_type = "native";
        public long rebel_origin_kingdom_id = -1;
        public string rebel_origin_kingdom_name = "";
        public string claimant_kind = "orthodox";
    }
}
