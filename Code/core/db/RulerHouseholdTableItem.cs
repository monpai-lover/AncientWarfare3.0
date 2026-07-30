using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("RulerHousehold")]
    public sealed class RulerHouseholdTableItem :
        AbstractTableItem<RulerHouseholdTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long relationship_id;
        public long ruler_actor_id = -1;
        public long partner_actor_id = -1;
        public long source_kingdom_id = -1;
        public long recipient_kingdom_id = -1;
        public string relationship_kind = "";
        public string rank_code = "";
        public int start_year = -1;
        public double start_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double end_time = -1;
        public int status;
        public long source_proposal_id = -1;
    }
}
