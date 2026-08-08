using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("WarPeaceSettlementProposal")]
    public sealed class WarPeaceSettlementProposalTableItem :
        AbstractTableItem<WarPeaceSettlementProposalTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long proposal_id;
        public long war_id = -1;
        public long requester_kingdom_id = -1;
        public long responder_kingdom_id = -1;
        [TableItemDef(pDefaultValue: "coalition")] public string scope_kind =
            "coalition";
        [TableItemDef(pDefaultValue: "-1")] public long exit_root_kingdom_id =
            -1;
        public int signed_war_score;
        public int total_cost;
        public int player_initiated;
        [TableItemDef(pDefaultValue: "0")]
        public int automatic_exhaustion_settlement;
        public string status = "pending";
        public string response_reason = "";
        [TableItemDef(pDefaultValue: "0")] public int recovery_attempts;
        public int created_year = -1;
        [TableItemDef(pDefaultValue: "-1")] public int response_year = -1;
        public double created_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double response_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double executed_time = -1;
    }
}
