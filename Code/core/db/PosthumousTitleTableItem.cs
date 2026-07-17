using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>谥号表：君主死亡/退位时评定，一行一个谥号。</summary>
    [TableDef("PosthumousTitle")]
    public class PosthumousTitleTableItem : AbstractTableItem<PosthumousTitleTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long record_id;

        public long   actor_id     = -1;
        public long   kingdom_id   = -1;
        public long   reign_id     = -1;   // 关联 KingdomReign.reign_id
        public long   shi_id       = -1;
        public long   dynasty_id   = -1;
        public long   mandate_period_id = -1;
        public int    highest_title;
        public string king_name    = "";
        public string king_color   = "";
        public string title_char   = "";   // 谥字（武/文/哀等）
        public string title_suffix = "";   // 后缀（帝/王）
        public string full_title   = "";   // 完整谥号（周武王 / 远景帝）
        public string full_title_color = "";
        public string title_kind = "posthumous";
        public string temple_name = "";
        public string posthumous_name = "";
        public int is_mandate;
        public int is_retrospective;
        public string retrospective_relation = "";
        public string qualification_key = "";
        public string fact_snapshot = "";
        public string eval         = "";   // 评级：good（褒）/ neutral（平）/ bad（恶）
        public string score_detail = "";   // 分项评定摘要：治绩/武功/衰败/结局
        public string grade = "";              // praise_high / praise / neutral / blame / blame_high
        public string dominant_dimension = ""; // civil / territory / war / order / ending / balanced
        public int score_civil = 0;
        public int score_territory = 0;
        public int score_war = 0;
        public int score_order = 0;
        public int score_ending = 0;
        public int total_score = 0;
        public string reason_text = "";
        public double decided_time;
    }
}
