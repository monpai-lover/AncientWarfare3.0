using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("RoyalClaim")]
    public class RoyalClaimTableItem : AbstractTableItem<RoyalClaimTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long claim_id;

        public long claimant_actor_id = -1;
        public string claimant_name = "";
        public long original_kingdom_id = -1;
        public string original_kingdom_name = "";
        public string original_kingdom_color = "";
        public long host_kingdom_id = -1;
        public string host_kingdom_name = "";
        public long lineage_id = -1;
        public long shi_id = -1;
        public string clan_name = "";
        public int claim_strength = 0;
        public int active = 1;
        public double created_time = 0;
        [TableItemDef(pDefaultValue: "-1")] public double resolved_time = -1;
        public string resolved_reason = "";
    }
}
