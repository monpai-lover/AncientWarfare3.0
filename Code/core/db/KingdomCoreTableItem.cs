using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("KingdomCore")]
    public class KingdomCoreTableItem : AbstractTableItem<KingdomCoreTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long core_id;

        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public long city_id = -1;
        public string city_name = "";
        public long owner_kingdom_id = -1;
        public string owner_kingdom_name = "";
        public string source_type = "";
        public string source_label = "";
        public double created_time = -1;
        public int active = 1;
    }
}
