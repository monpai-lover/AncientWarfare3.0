using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("FeudatoryCity")]
    public sealed class FeudatoryCityTableItem : AbstractTableItem<FeudatoryCityTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long entry_id;

        public long feudatory_id = -1;
        public long city_id = -1;
        public int active = 1;
        public double assigned_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double end_time = -1;
        public string end_reason = "";
    }
}
