using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("WarScoreControl")]
    public sealed class WarScoreControlTableItem :
        AbstractTableItem<WarScoreControlTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public string control_key = "";
        public long war_id = -1;
        public string control_kind = "";
        public string subject_id = "";
        public long home_kingdom_id = -1;
        public long controller_kingdom_id = -1;
        public int home_side;
        public int controller_side;
        public int value;
        public int contribution;
        public int verified_goal;
        public int occurrence;
        public int home_city_count;
        public double started_time = -1d;
        public double updated_time = -1d;
    }
}
