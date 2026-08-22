using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SuccessionDispute")]
    public sealed class SuccessionDisputeTableItem :
        AbstractTableItem<SuccessionDisputeTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long dispute_id;
        public long original_kingdom_id = -1;
        public long rival_kingdom_id = -1;
        public long predecessor_actor_id = -1;
        public long successor_actor_id = -1;
        public long claimant_actor_id = -1;
        public string original_state_name = "";
        public string original_qualifier = "";
        public string rival_qualifier = "";
        public int accession_law;
        public string successor_mode = "";
        public string claimant_mode = "";
        public int successor_support;
        public int claimant_support;
        [TableItemDef(pDefaultValue: "-1")] public long war_id = -1;
        [TableItemDef(pDefaultValue: "-1")]
        public long original_capital_city_id_at_war_start = -1;
        [TableItemDef(pDefaultValue: "-1")]
        public long rival_capital_city_id_at_war_start = -1;
        public double prepared_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double start_time = -1;
        public int prepared_year = -1;
        public int deadline_year = -1;
        public int status;
        [TableItemDef(pDefaultValue: "-1")] public double end_time = -1;
        public string end_reason = "";
        public long original_lineage_id = -1;
        public long original_shi_id = -1;
        public int claim_generation_boundary = 3;
    }
}
