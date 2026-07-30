using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("DiplomaticMarriage")]
    public sealed class DiplomaticMarriageTableItem :
        AbstractTableItem<DiplomaticMarriageTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long marriage_id;
        public long kingdom_a_id = -1;
        public long kingdom_b_id = -1;
        public long actor_a_id = -1;
        public long actor_b_id = -1;
        public int start_year = -1;
        public double start_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double end_time = -1;
        public int status;
        public long source_proposal_id = -1;
    }
}
