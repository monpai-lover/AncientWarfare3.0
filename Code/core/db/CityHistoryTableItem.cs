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
        public string year_prefix_rich = "";
        public string subject_name;      // 事件发生时城名快照
        public string subject_color = "";
        public string content;
        public string content_rich = "";
        public string event_type;        // city_found / city_transfer
        public string kingdom_name = ""; // 该事件时城市所属国名快照(分段折叠按此切"归属期",亡国也准)
        public string kingdom_color = "";
        public long   context_kingdom_id = -1;
        public string context_kingdom_name = "";
        public string context_kingdom_color = "";
        public string target_type = "";
        [TableItemDef(pDefaultValue: "-1")] public long target_id = -1;
    }
}
