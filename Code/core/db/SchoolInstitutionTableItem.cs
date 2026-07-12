using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SchoolInstitution")]
    public sealed class SchoolInstitutionTableItem : AbstractTableItem<SchoolInstitutionTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long institution_id;
        public string institution_type;
        public string school_id;
        public long city_id;
        public long founder_actor_id = -1;
        public int founding_year;
        public int level = 1;
        public long custodian_actor_id = -1;
        public double condition = 100d;
        public int active = 1;
        public double updated_time;
    }
}
