using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("MilitaryGovernorateState")]
    public class MilitaryGovernorateStateTableItem :
        AbstractTableItem<MilitaryGovernorateStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long state_id;

        public long relation_id = -1;
        public long subject_kingdom_id = -1;
        public long suzerain_kingdom_id = -1;
        public long seat_city_id = -1;
        public long governor_actor_id = -1;
        public long successor_actor_id = -1;
        public long expeditionary_army_id = -1;
        public string command_name = "";
        public int created_year = -1;
        public int succession_state = 0;
        [TableItemDef(pDefaultValue: "0")]
        public int replacement_allowed = 0;
        public int active = 1;
        public double end_time = -1;
        public string end_reason = "";
    }
}
