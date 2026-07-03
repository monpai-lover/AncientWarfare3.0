using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SlaveState")]
    public class SlaveStateTableItem : AbstractTableItem<SlaveStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long actor_id;

        public string actor_name;
        public long kingdom_id = -1;
        public string kingdom_name;
        public long city_id = -1;
        public string city_name;

        public double enslaved_time;
        [TableItemDef(pDefaultValue: "-1")] public double freed_time = -1;
        public string reason;
        public long captured_by_actor_id = -1;

        public int merit;
        public int active = 1;
        public int soldier;
        [TableItemDef(pDefaultValue: "-1")] public double soldier_start_time = -1;
        public int freedman;
    }
}
