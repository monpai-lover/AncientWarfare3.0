using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     人物档案表(对应 docs 任务书 ActorArchive)。保存有谱系/有氏的 Xia,含已死者。
    ///     id 用 long(新版 actor id 是 long,区别于 AW2 的 string)。
    ///     clan_name 表示 AW3 的“氏”,不是原版 Clan 对象 id。
    /// </summary>
    [TableDef("ActorArchive")]
    public class ActorArchiveTableItem : AbstractTableItem<ActorArchiveTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long id;

        public string given_name;     // 单名
        public string display_name;   // 当前显示全名
        public string family_name;    // 姓(血统姓)
        public string clan_name;      // 氏(AW3 的氏,非原版 Clan)
        public long   lineage_id = -1;
        public long   shi_id = -1;
        public string asset_id;       // 种族(应为 Xia)
        public int    sex;            // 0/1
        public string status = "none"; // none/noble/common_lineage/slave_lineage

        public long   kingdom_id = -1;
        public string kingdom_name;
        public long   city_id = -1;
        public string city_name;
        public long   original_clan_id = -1;

        public long   parent_id_1 = -1;
        public long   parent_id_2 = -1;
        public int    generation;
        public int    noble_distance;

        public double birth_time;
        [TableItemDef(pDefaultValue: "-1")] public double death_time;
        public int    is_alive = 1;       // 1 存活 / 0 已死
        public int    name_integrated;    // 是否被合流规则处理过

        public int    head;
        public int    skin;
        public int    skin_set;
    }
}
