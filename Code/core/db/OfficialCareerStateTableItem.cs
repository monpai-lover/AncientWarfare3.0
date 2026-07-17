using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("OfficialCareerState")]
    public class OfficialCareerStateTableItem : AbstractTableItem<OfficialCareerStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long actor_id;

        public string actor_name = "";
        public long kingdom_id = -1;
        public long city_id = -1;
        [TableItemDef(pDefaultValue: "1")] public int rank = 1;
        public int track = 0;
        public string office_id = "";
        public float merit = 0f;
        [TableItemDef(pDefaultValue: "1")] public int merit_cap = 1;
        public int term_end_year = -1;
        [TableItemDef(pDefaultValue: "2")] public int last_kaoke = 2;
        public int kaoke_mod_until = -1;
        public int seniority = 0;
        public int last_pop_snapshot = -1;
        public double updated_time = -1d;
    }
}
