using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("DiplomacyProposal")]
    public sealed class DiplomacyProposalTableItem :
        AbstractTableItem<DiplomacyProposalTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long proposal_id;
        public long requester_kingdom_id = -1;
        public string requester_name = "";
        public long responder_kingdom_id = -1;
        public string responder_name = "";
        public string proposal_type = "";
        public string status = "pending";
        [TableItemDef(pDefaultValue: "-1")] public long war_id = -1;
        public int player_initiated;
        public int created_year = -1;
        public int expiry_year = -1;
        [TableItemDef(pDefaultValue: "-1")] public int response_year = -1;
        [TableItemDef(pDefaultValue: "-1")] public int treaty_until_year = -1;
        public double created_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double response_due_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double response_time = -1;
        public string response_reason = "";
        public string requester_title = "";
        public string responder_title = "";
        public string request_year_prefix = "";
        public string response_year_prefix = "";
        public string request_style = "peer";
        public string request_tone = "neutral";
        public string response_style = "peer";
        public string response_tone = "neutral";
        [TableItemDef(pDefaultValue: "-1")] public long target_kingdom_id = -1;
        [TableItemDef(pDefaultValue: "-1")] public long requester_actor_id = -1;
        [TableItemDef(pDefaultValue: "-1")] public long responder_actor_id = -1;
        [TableItemDef(pDefaultValue: "-1")] public long target_city_id = -1;
        public string detail_id = "";
    }
}
