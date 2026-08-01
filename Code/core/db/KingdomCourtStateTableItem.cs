using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("KingdomCourtState")]
    public class KingdomCourtStateTableItem : AbstractTableItem<KingdomCourtStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long kingdom_id;
        public string kingdom_name;
        [TableItemDef(pDefaultValue: "")] public string court_profile_id;
        [TableItemDef(pDefaultValue: "")] public string institution_id;
        public string court_mode;
        public string dominant_school;
        public string secondary_school;
        public double court_efficiency;
        public double faction_concentration;
        public string faction_cache;
        public string aristocratic_group_cache;
        public double direction_livelihood;
        public double direction_war;
        public double direction_aggression;
        public double direction_peace;
        public double direction_order;
        public double direction_commerce;
        public double direction_technology;
        public int last_refresh_year;
        public int last_candidate_refresh_year;
        public int last_strong_event_year;
        public double updated_time;
    }
}
