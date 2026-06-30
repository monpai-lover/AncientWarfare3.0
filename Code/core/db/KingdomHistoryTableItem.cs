using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     国家历史事件表(建国/换君/亡国)。随存档持久化([TableDef] 反射自动建表)。
    /// </summary>
    [TableDef("KingdomHistory")]
    public class KingdomHistoryTableItem : AbstractTableItem<KingdomHistoryTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long event_id;

        public long   kingdom_id = -1;   // 关联 Kingdom.id
        public double world_time;
        public string year_prefix;
        public string subject_name;      // 事件发生时国名快照
        public string content;
        public string event_type;        // found / rule_change / destroyed
    }
}
