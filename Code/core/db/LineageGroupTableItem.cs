using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     姓族(血缘大流)表 —— 对应 docs 任务书 LineageGroup。
    ///     一个姓族 = 一个上古姓(family_name)下的血缘谱系根。先秦古姓只有几十个,
    ///     很多人同姓不同氏;姓族是"血缘大流",氏支(ShiBranch)挂在姓族下。
    ///     lineage_id 全局唯一(由 LineageService 用自增计数分配)。
    /// </summary>
    [TableDef("LineageGroup")]
    public class LineageGroupTableItem : AbstractTableItem<LineageGroupTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long lineage_id;

        public string family_name;        // 姓(如 姬/姜/嬴/姒)
        public long   founder_actor_id = -1;
        public string founder_name;
        public double created_time;
        public long   origin_kingdom_id = -1;
        public long   origin_city_id = -1;
        public int    is_extinct;         // 1 = 该姓族已无存活成员(仅档案保留)
    }
}
