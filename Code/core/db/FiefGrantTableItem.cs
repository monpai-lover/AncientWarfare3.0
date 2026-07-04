using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("FiefGrant")]
    public class FiefGrantTableItem : AbstractTableItem<FiefGrantTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long grant_id;

        public long general_actor_id = -1;
        public string general_name = "";
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public long king_actor_id = -1;
        public string king_name = "";
        public long city_id = -1;
        public string city_name = "";
        public string city_color = "";
        public string grant_reason = "";
        public double start_time = -1;
        public double end_time = -1;
        public int revoked = 0;
        public string revoke_reason = "";
    }
}
