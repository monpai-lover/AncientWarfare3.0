using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("Feudatory")]
    public sealed class FeudatoryTableItem : AbstractTableItem<FeudatoryTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long feudatory_id;

        public long empire_kingdom_id = -1;
        public long prince_actor_id = -1;
        public string prince_name = "";
        public string feudatory_name = "";
        public long shi_branch_id = -1;
        public long seat_city_id = -1;
        public int autonomy = 40;
        public int loyalty = 60;
        public long garrison_army_id = -1;
        public long garrison_captain_actor_id = -1;
        public int established_year = -1;
        public int status = 0;
        [TableItemDef(pDefaultValue: "-1")] public long active_war_id = -1;
        [TableItemDef(pDefaultValue: "-1")] public long rebel_kingdom_id = -1;
        public double start_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double end_time = -1;
        public string end_reason = "";
    }
}
