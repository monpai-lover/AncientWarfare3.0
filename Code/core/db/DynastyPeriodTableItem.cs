using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>朝代表：同一氏族（shi_id）连续统治为一个朝代。换氏族时切新朝代。</summary>
    [TableDef("DynastyPeriod")]
    public class DynastyPeriodTableItem : AbstractTableItem<DynastyPeriodTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long dynasty_id;

        public long   kingdom_id          = -1;
        public string kingdom_color       = "";
        public int    dynasty_index       = 0;    // 该国第几代朝代（1起）
        public long   shi_id              = -1;   // 建立者氏支 id（切朝代判断依据）
        public string clan_name           = "";   // 氏名快照
        public long   founder_king_actor_id = -1;
        public string dynasty_name        = "";   // 朝代名（随机雅字/氏名）
        public string dynasty_color       = "";
        public double start_time;
        [TableItemDef(pDefaultValue: "-1")] public double end_time;
    }
}
