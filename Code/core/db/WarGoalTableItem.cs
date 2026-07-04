using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("WarGoal")]
    public class WarGoalTableItem : AbstractTableItem<WarGoalTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long war_goal_id;

        public long war_id = -1;
        public long attacker_kingdom_id = -1;
        public string attacker_name = "";
        public string attacker_color = "";
        public long defender_kingdom_id = -1;
        public string defender_name = "";
        public string defender_color = "";
        public string war_type = "";
        public string goal_type = "";
        public long target_city_id = -1;
        public string target_city_name = "";
        public long target_kingdom_id = -1;
        public string target_kingdom_name = "";
        public long source_claim_id = -1;
        public long source_core_id = -1;
        public long source_project_id = -1;
        public long claimant_actor_id = -1;
        public string claimant_name = "";
        public double created_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double resolved_time = -1;
        public int resolved = 0;
        public string result = "";
    }
}
