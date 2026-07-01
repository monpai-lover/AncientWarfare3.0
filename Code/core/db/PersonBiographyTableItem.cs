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
        public string year_prefix_rich = "";
        public string subject_name;      // 事件发生时人名快照
        public string subject_color = "";
        public string content;           // 事件内容
        public string content_rich = "";
        public string event_type;        // birth/death/become_king/had_child/become_leader/... 见 ChronicleEvents
        public string category = "";     // 分类(UI 筛选):life/honor/clan/war/bond。老档自动迁移补空串
        public long   context_kingdom_id = -1;
        public string context_kingdom_name = "";
        public string context_kingdom_color = "";
        public string target_type = "";
        [TableItemDef(pDefaultValue: "-1")] public long target_id = -1;
    }
}
