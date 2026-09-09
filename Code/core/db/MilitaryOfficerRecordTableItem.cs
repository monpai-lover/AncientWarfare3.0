using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("MilitaryOfficerRecord")]
    public sealed class MilitaryOfficerRecordTableItem :
        AbstractTableItem<MilitaryOfficerRecordTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long id;
        public long actor_id = -1L;
        public string actor_name = "";
        public long kingdom_id = -1L;
        public string kingdom_name = "";
        public string tier = "local";
        public string slot_key = "";
        public long city_id = -1L;
        public long army_id = -1L;
        public long exam_session_id = -1L;
        public double appointed_time = -1d;
        public double ended_time = -1d;
        public int active = 1;
        public string end_reason = "";
    }
}
