using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("KingdomContinuity")]
    public class KingdomContinuityTableItem : AbstractTableItem<KingdomContinuityTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long kingdom_id;

        public string kingdom_name = "";
        public double founded_time = -1;
        public double destroyed_time = -1;
        public string original_actor_asset = "";
        public long culture_id = -1;
        public long language_id = -1;
        public long religion_id = -1;
        public long royal_clan_id = -1;
        public long capital_city_id = -1;
        public string capital_city_name = "";
        public long last_king_actor_id = -1;
        public string last_king_name = "";
        public long legitimate_lineage_id = -1;
        public long legitimate_shi_id = -1;
        public int kingdom_title = 0;
        public int name_integrated = 0;
        public int was_mandate = 0;
        public long mandate_period_id = -1;
        public int restoration_count = 0;
        public double last_restored_time = -1;
    }
}
