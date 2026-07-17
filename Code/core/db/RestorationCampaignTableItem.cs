using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("RestorationCampaign")]
    public class RestorationCampaignTableItem : AbstractTableItem<RestorationCampaignTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long campaign_id;

        public long claim_id = -1;
        public long original_kingdom_id = -1;
        public long claimant_actor_id = -1;
        public string claimant_name = "";
        public long seed_city_id = -1;
        public string seed_city_name = "";
        public long original_mandate_period_id = -1;
        public string state = "uprising";
        public int controlled_core_count = 0;
        public int total_core_count = 0;
        public int started_year = -1;
        public int last_attempt_year = -1;
        public double started_time = -1;
        public double completed_time = -1;
        public string result = "";
    }
}
