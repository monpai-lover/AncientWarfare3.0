using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SchoolMembership")]
    public sealed class SchoolMembershipTableItem : AbstractTableItem<SchoolMembershipTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long membership_id;
        public long actor_id;
        public string school_id;
        public string source_type;
        public string source_id;
        public long teacher_actor_id = -1;
        public long city_id = -1;
        public int generation;
        public double reputation;
        public int start_year;
        public int end_year = -1;
        public int active;
        public string end_reason;
        public double updated_time;
    }
}
