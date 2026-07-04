using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("MandateState")]
    public class MandateStateTableItem : AbstractTableItem<MandateStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long state_id;

        public int active = 0;
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public string dynasty_name = "";
        public long emperor_actor_id = -1;
        public string emperor_name = "";
        public long period_id = -1;
        public int mandate_value = 0;
        public int imperial_authority = 0;
        public int dynasty_prestige = 0;
        public double core_control = 0;
        public double vassal_loyalty = 0;
        public string crisis_level = "";
        public double start_time = -1;
        public double updated_time = -1;
        public int last_year = -999999;
        public string origin_type = "native";
        public int original_core_count = 0;
        public long rebel_origin_kingdom_id = -1;
        public string rebel_origin_kingdom_name = "";
        public string claimant_kind = "orthodox";
        public string map_marker_kind = "moh";
    }
}
