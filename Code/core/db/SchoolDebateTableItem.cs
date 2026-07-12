using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SchoolDebate")]
    public sealed class SchoolDebateTableItem : AbstractTableItem<SchoolDebateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long debate_id;
        public long city_id;
        public int debate_year;
        public string topic_id;
        public long first_actor_id;
        public string first_school_id;
        public long second_actor_id;
        public string second_school_id;
        public long seed;
        public double first_score;
        public double second_score;
        public string result;
        public int resolved;
        public int presented;
        public double updated_time;
    }
}
