using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("RoyalGuardState")]
    public class RoyalGuardStateTableItem : AbstractTableItem<RoyalGuardStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long actor_id;

        public string actor_name;
        public long kingdom_id = -1;
        public string kingdom_name;
        public long city_id = -1;
        public string city_name;
        public string guard_name;

        public int active = 1;
        public int captain;
        public int noble;
        public double appointed_time;
        [TableItemDef(pDefaultValue: "-1")] public double dismissed_time = -1;
        public string dismiss_reason;
    }
}
