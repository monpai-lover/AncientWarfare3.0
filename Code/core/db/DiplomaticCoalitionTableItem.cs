using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("DiplomaticCoalition")]
    public sealed class DiplomaticCoalitionTableItem :
        AbstractTableItem<DiplomaticCoalitionTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long coalition_id;
        public long member_a_id = -1;
        public long member_b_id = -1;
        public long target_kingdom_id = -1;
        public int start_year = -1;
        public int end_year = -1;
        public double start_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double end_time = -1;
        public int status;
        public long source_proposal_id = -1;
        [TableItemDef(pDefaultValue: "-1")] public long joined_war_id = -1;
    }
}
