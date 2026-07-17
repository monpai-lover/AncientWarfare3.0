using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("DynastyTitleRegistry")]
    public class DynastyTitleRegistryTableItem : AbstractTableItem<DynastyTitleRegistryTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long registry_id;

        public long shi_id = -1;
        public string title_type = "";
        public string title_value = "";
        public int cycle_no;
        public long actor_id = -1;
        public long reign_id = -1;
        public double used_time;
    }
}
