using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("CourtOfficer")]
    public class CourtOfficerTableItem : AbstractTableItem<CourtOfficerTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long officer_id;
        public long kingdom_id;
        public long actor_id;
        public string actor_name;
        public long city_id;
        public string layer;
        public string office_id;
        public string school_id;
        public double influence;
        public int appointed_year;
        public double appointed_time;
        public int ended_year;
        public double ended_time;
        public int active;
        public string end_reason;
        public double updated_time;
    }
}
