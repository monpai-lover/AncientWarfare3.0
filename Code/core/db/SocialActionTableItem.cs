using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SocialAction")]
    public sealed class SocialActionTableItem :
        AbstractTableItem<SocialActionTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long action_id;
        [TableItemDef(pIsUnique: true)] public string operation_key = "";

        public long kingdom_id = -1L;
        public long ruler_actor_id = -1L;
        public long target_actor_id = -1L;
        public string action_type = "";
        public int int_parameter;
        public long long_parameter = -1L;
        public int political_cost;
        public string result = "pending";
        public string reason = "";
        public int start_year = -1;
        public double start_time = -1d;
        public double end_time = -1d;
    }
}
