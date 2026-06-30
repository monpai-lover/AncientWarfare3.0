using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     人物传记事件表。仅"入谱贵族家系"(IsXia 且 lineage_id>=0)的人有事件。
    ///     一条 = 一次生平事件(出生/死亡/成为国王)。随存档持久化([TableDef] 反射自动建表)。
    /// </summary>
    [TableDef("PersonBiography")]
    public class PersonBiographyTableItem : AbstractTableItem<PersonBiographyTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long event_id;

        public long   actor_id = -1;     // 传记主人(关联 ActorArchive.id)
        public double world_time;        // 排序 + 通用年来源
        public string year_prefix;       // 写入当时拼好的快照,如 "16年 周武王元年" / "16年"
        public string subject_name;      // 事件发生时人名快照
        public string content;           // 事件内容
        public string event_type;        // birth / death / become_king
    }
}
