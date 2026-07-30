using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("CivilServiceExamSession")]
    public sealed class CivilServiceExamSessionTableItem :
        AbstractTableItem<CivilServiceExamSessionTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long id;
        public long kingdom_id = -1L;
        public string kingdom_name = "";
        public string mode = "";
        public int cycle_year = -1;
        public string stage = "scheduled";
        public string status = "scheduled";
        public long open_world_day = -1L;
        public long next_due_world_day = -1L;
        public long host_ruler_id = -1L;
        public long final_ruler_id = -1L;
        public int player_ranking_pending;
        public int candidate_cursor;
        [TableItemDef(pDefaultValue: "-1")] public int central_vacancies = -1;
        [TableItemDef(pDefaultValue: "-1")] public int city_vacancies = -1;
        [TableItemDef(pDefaultValue: "-1")]
        public int waiting_candidate_count = -1;
        [TableItemDef(pDefaultValue: "-1")] public int reserve_target = -1;
        [TableItemDef(pDefaultValue: "-1")] public int admission_quota = -1;
        public double updated_time = -1d;
    }
}
