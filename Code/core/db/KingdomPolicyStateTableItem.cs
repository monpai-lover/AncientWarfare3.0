using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("KingdomPolicyState")]
    public class KingdomPolicyStateTableItem : AbstractTableItem<KingdomPolicyStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long kingdom_id;

        public string kingdom_name;
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
        public string completed_policies;
        public string completed_techs;
        public string completed_decisions;
        public double updated_time;
    }
}
