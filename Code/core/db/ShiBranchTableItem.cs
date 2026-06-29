using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     氏支表 —— 对应 docs 任务书 ShiBranch。
    ///     一个氏支 = 一个贵族始祖及其父系后代树(纯血缘、跨国家稳定)。
    ///     氏名(clan_name)与 source_type 是"来源属性",查询以血缘树为准、来源作展示。
    ///     注意:clan_name 是 AW3 的"氏",不是原版 Clan 对象 id。
    ///     shi_id 全局唯一(由 LineageService 分配);lineage_id 指向所属姓族。
    /// </summary>
    [TableDef("ShiBranch")]
    public class ShiBranchTableItem : AbstractTableItem<ShiBranchTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long shi_id;

        public long   lineage_id = -1;    // 所属姓族
        public string clan_name;          // 氏(如 夏后/有扈/斟鄩)
        public long   founder_actor_id = -1;
        public string source_type;        // enfeoffed/inherited/random/integration/special_figure
        public long   origin_kingdom_id = -1;
        public long   origin_city_id = -1;
        public long   origin_original_clan_id = -1; // 生成时参考的原版 Clan id(仅记录,非权威)
        public double created_time;
        public int    is_extinct;
    }
}
