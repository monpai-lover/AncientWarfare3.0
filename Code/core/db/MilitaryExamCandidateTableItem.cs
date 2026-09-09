using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("MilitaryExamCandidate")]
    public sealed class MilitaryExamCandidateTableItem :
        AbstractTableItem<MilitaryExamCandidateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long id;
        public long session_id = -1L;
        public long kingdom_id = -1L;
        public long actor_id = -1L;
        public string actor_name = "";
        public long home_city_id = -1L;
        public string home_city_name = "";
        public int age_snapshot = -1;
        public int warfare_score = -1;
        public int damage_score = -1;
        public int strength_score = -1;
        public int speed_score = -1;
        public int diplomacy_score = -1;
        public int military_merit_score = -1;
        public int total_score = -1;
        public string local_result = "pending";
        public string prefectural_result = "pending";
        public string metropolitan_result = "pending";
        public string palace_result = "pending";
        public string qualification = "none";
        public string appointment_tier = "none";
        public string appointment_status = "pending";
        public int final_rank;
        public string final_title = "";
        public double updated_time = -1d;
    }
}
