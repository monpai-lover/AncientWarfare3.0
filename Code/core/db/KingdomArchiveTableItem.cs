using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     王国档案表(全王国名册,含已亡国)。随存档持久化([TableDef] 反射自动建表)。
    ///     存旗帜重建所需快照(banner id + 颜色),亡国后不引用已销毁 Kingdom 对象即可重画旗帜。
    /// </summary>
    [TableDef("KingdomArchive")]
    public class KingdomArchiveTableItem : AbstractTableItem<KingdomArchiveTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long kingdom_id;

        public string kingdom_name;          // 最新国名快照
        public string color_text;            // 国家颜色 hex(亡国后名字/旗帜配色)
        public int    color_id;              // 颜色索引(kingdom_colors_library)
        public int    banner_icon_id;        // 旗帜图标 id
        public int    banner_background_id;  // 旗帜背景 id
        public string banner_id;             // 旗帜集 id(= ActorAsset.banner_id,如 "Xia")

        public int    is_alive = 1;          // 1 存活 / 0 已亡
        public double founded_time;
        [TableItemDef(pDefaultValue: "-1")] public double destroyed_time;
    }
}
