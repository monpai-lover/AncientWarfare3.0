using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("VirtualNobleTitle")]
    public sealed class VirtualNobleTitleTableItem :
        AbstractTableItem<VirtualNobleTitleTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long title_id;
        public long kingdom_id = -1L;
        public string kingdom_name = "";
        public long current_actor_id = -1L;
        public string title_text = "";
        public string normalized_key = "";
        public long grantor_actor_id = -1L;
        public string grantor_name = "";
        public long predecessor_title_id = -1L;
        public long inherited_from_actor_id = -1L;
        public string succession_state = "active";
        public int granted_year = -1;
        public double granted_time = -1d;
        public int end_year = -1;
        public double end_time = -1d;
        [TableItemDef(pDefaultValue: "1")] public int active = 1;
        public string end_reason = "";
        public string primary_title_snapshot = "";
    }
}
