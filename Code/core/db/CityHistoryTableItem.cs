using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     城市历史事件表(易主:换所属王国)。随存档持久化([TableDef] 反射自动建表)。
    /// </summary>
    [TableDef("CityHistory")]
    public class CityHistoryTableItem : AbstractTableItem<CityHistoryTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long event_id;

        public long   city_id = -1;      // 关联 City.id
        public double world_time;
        public string year_prefix;
        public string subject_name;      // 事件发生时城名快照
        public string content;
        public string event_type;        // city_transfer
    }
}
