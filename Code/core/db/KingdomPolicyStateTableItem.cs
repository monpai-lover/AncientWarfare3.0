using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("KingdomPolicyState")]
    public class KingdomPolicyStateTableItem : AbstractTableItem<KingdomPolicyStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long kingdom_id;

        public string kingdom_name;
        [TableItemDef(pDefaultValue: "")] public string profile_id;
        [TableItemDef(pDefaultValue: "default")] public string government_state;
        [TableItemDef(pDefaultValue: "0")] public int royal_authority;
        [TableItemDef(pDefaultValue: "0")] public int migration_version;
        [TableItemDef(pDefaultValue: "")] public string obsolete_node_ids;
        public string class_state;
        public string army_state;
        public string name_state;
        public string enfeoffment_state;
        public double policy_points;
        public double tech_points;
        public string current_policy;
        public double policy_progress;
        public string current_tech;
        public double tech_progress;
        public string current_decision;
        public double decision_progress;
        public long decision_target_kingdom_id;
        public string decision_target_kingdom_name;
        public string decision_queue;
        public long core_fab_current_city_id;
        public string core_fab_current_city_name;
        public double core_fab_progress;
        public string core_fab_queue;
        public string completed_policies;
        public string completed_techs;
        public string completed_decisions;
        public string locked_nodes;
        public double updated_time;
    }
}
