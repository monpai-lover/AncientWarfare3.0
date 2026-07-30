using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("Enfeoffment")]
    public sealed class EnfeoffmentTableItem :
        AbstractTableItem<EnfeoffmentTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long grant_id;

        public long kingdom_id = -1;
        public string kingdom_name = "";
        public long grantor_actor_id = -1;
        public string grantor_name = "";
        public long actor_id = -1;
        public string actor_name = "";
        public int noble_rank = 0;
        public string title_style = "";
        public string title_name = "";
        public string grant_reason = "";
        public long inherited_from_actor_id = -1;
        public long predecessor_grant_id = -1;
        public int grant_year = -1;
        public double start_time = -1d;
        public int end_year = -1;
        public double end_time = -1d;
        [TableItemDef(pDefaultValue: "1")] public int active = 1;
        public string end_reason = "";
    }
}
