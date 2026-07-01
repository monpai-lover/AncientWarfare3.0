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
        public string king_name    = "";
        public string king_color   = "";
        public string title_char   = "";   // 谥字（武/文/哀等）
        public string title_suffix = "";   // 后缀（帝/王）
        public string full_title   = "";   // 完整谥号（周武王 / 远景帝）
        public string full_title_color = "";
        public string eval         = "";   // 评级：good（褒）/ neutral（平）/ bad（恶）
        public string score_detail = "";   // 分项评定摘要：治绩/武功/衰败/结局
        public double decided_time;
    }
}
