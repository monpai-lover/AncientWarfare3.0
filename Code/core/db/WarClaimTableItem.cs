using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("WarClaim")]
    public class WarClaimTableItem : AbstractTableItem<WarClaimTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long claim_id;

        public long source_kingdom_id = -1;
        public string source_kingdom_name = "";
        public string source_kingdom_color = "";
        public long target_kingdom_id = -1;
        public string target_kingdom_name = "";
        public string target_kingdom_color = "";
        public long target_city_id = -1;
        public string target_city_name = "";
        public string claim_type = "";
        public string war_type = "";
        public string reason_key = "";
        public double created_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double expires_time = -1;
        public int active = 1;
        public int consumed = 0;
        public long created_by_actor_id = -1;
        public string created_by_name = "";
    }
}
