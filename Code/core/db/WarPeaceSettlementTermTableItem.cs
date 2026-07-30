using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("WarPeaceSettlementTerm")]
    public sealed class WarPeaceSettlementTermTableItem :
        AbstractTableItem<WarPeaceSettlementTermTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long term_id;
        public long proposal_id = -1;
        public int position;
        public string term_kind = "";
        public int cost;
        public long from_kingdom_id = -1;
        public long to_kingdom_id = -1;
        public string resource_id = "";
        public int amount;
        public int duration_years;
        public long city_id = -1;
        public long captive_actor_id = -1;
        public long claim_id = -1;
        [TableItemDef(pDefaultValue: "-1")] public long war_goal_id = -1;
        public int frozen_occupation;
        public int core_or_claim_basis;
        public string apply_status = "pending";
        public string apply_reason = "";
        [TableItemDef(pDefaultValue: "-1")] public double applied_time = -1;
        public int baseline_captured;
        [TableItemDef(pDefaultValue: "-1")] public int source_amount_before = -1;
        [TableItemDef(pDefaultValue: "-1")] public int target_amount_before = -1;
        [TableItemDef(pDefaultValue: "-1")] public long source_city_id = -1;
        [TableItemDef(pDefaultValue: "-1")] public long target_city_id = -1;
    }
}
