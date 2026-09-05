using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("BanditAmnestySettlement")]
    public sealed class BanditAmnestySettlementTableItem :
        AbstractTableItem<BanditAmnestySettlementTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long settlement_id;
        public long bandit_kingdom_id = -1L;
        public long origin_kingdom_id = -1L;
        public long leader_actor_id = -1L;
        public long stronghold_city_id = -1L;
        public long mother_city_id = -1L;
        public string reward_kind = "None";
        public string office_id = "";
        public long fief_city_id = -1L;
        public string title_text = "";
        public int hereditary = 1;
        public string phase = "Prepared";
        public int retry_count = 0;
        public string failure_key = "";
        public int created_year = -1;
        public double created_time = -1d;
        public double updated_time = -1d;
    }
}
