using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("PeaceSettlement")]
    public class PeaceSettlementTableItem : AbstractTableItem<PeaceSettlementTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long settlement_id;

        public long war_id = -1;
        public long war_goal_id = -1;
        public string action = "";
        public long winner_kingdom_id = -1;
        public string winner_name = "";
        public long loser_kingdom_id = -1;
        public string loser_name = "";
        public long target_city_id = -1;
        public string target_city_name = "";
        public long claimant_actor_id = -1;
        public string claimant_name = "";
        public string terms_text = "";
        public double world_time = -1;
    }
}
