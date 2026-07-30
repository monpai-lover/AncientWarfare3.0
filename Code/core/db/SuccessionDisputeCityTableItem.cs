using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SuccessionDisputeCity")]
    public sealed class SuccessionDisputeCityTableItem :
        AbstractTableItem<SuccessionDisputeCityTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long entry_id;
        public long dispute_id = -1;
        public long city_id = -1;
        public long original_kingdom_id = -1;
        public int side;
        public int ordinal;
        public int active = 1;
        public double assigned_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double end_time = -1;
        public string end_reason = "";
    }
}
