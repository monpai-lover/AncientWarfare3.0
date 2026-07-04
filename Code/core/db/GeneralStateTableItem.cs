using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("GeneralState")]
    public class GeneralStateTableItem : AbstractTableItem<GeneralStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long actor_id;

        public string actor_name = "";
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public long home_city_id = -1;
        public string home_city_name = "";
        public long fief_city_id = -1;
        public string fief_city_name = "";
        public long personal_army_id = -1;
        public int merit_score = 0;
        public int loyalty_score = 50;
        public int ambition_score = 20;
        public int troop_power_snapshot = 0;
        public double appointed_time = -1;
        public double granted_time = -1;
        public double last_reward_time = -1;
        public double last_risk_check_time = -1;
        public int active = 1;
        public int rebelled = 0;
        public string end_reason = "";
    }
}
