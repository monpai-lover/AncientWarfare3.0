using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("CivilServiceExamCandidate")]
    public sealed class CivilServiceExamCandidateTableItem :
        AbstractTableItem<CivilServiceExamCandidateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long id;
        public long session_id = -1L;
        public long kingdom_id = -1L;
        public long actor_id = -1L;
        public string actor_name = "";
        public long home_city_id = -1L;
        public string home_city_name = "";
        public string social_origin = "commoner";
        public string school_id = "";
        public int local_grade;
        public int local_score = -1;
        public int metropolitan_score = -1;
        public int palace_score = -1;
        public int national_score = -1;
        [TableItemDef(pDefaultValue: "pending")] public string local_result = "pending";
        [TableItemDef(pDefaultValue: "pending")] public string metropolitan_result = "pending";
        [TableItemDef(pDefaultValue: "pending")] public string palace_result = "pending";
        [TableItemDef(pDefaultValue: "pending")] public string national_result = "pending";
        public string current_stage_result = "pending";
        public string qualification = "none";
        public int final_rank;
        public string final_title = "";
        public int entry_bonus;
        public double updated_time = -1d;
    }
}
