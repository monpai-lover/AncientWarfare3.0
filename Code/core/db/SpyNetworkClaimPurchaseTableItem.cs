using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SpyNetworkClaimPurchase")]
    public sealed class SpyNetworkClaimPurchaseTableItem :
        AbstractTableItem<SpyNetworkClaimPurchaseTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long purchase_id;
        public long source_kingdom_id = -1;
        public long target_kingdom_id = -1;
        public string purchase_key = "";
        public int purchase_year = -1;
        public int cost;
    }
}
