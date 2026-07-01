using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     战争记录表(每场战争一行)。随存档持久化([TableDef] 反射自动建表)。
    ///     供谥号"战绩"统计与(可选)战争史查询。开战即插行(end=-1),结束时 UPDATE。
    /// </summary>
    [TableDef("WarRecord")]
    public class WarRecordTableItem : AbstractTableItem<WarRecordTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long war_id;

        public long   attacker_kingdom_id = -1;
        public long   defender_kingdom_id = -1;
        public string attacker_name = "";     // 开战时攻方国名快照
        public string attacker_color = "";
        public string defender_name = "";     // 开战时守方国名快照
        public string defender_color = "";
        public string war_type = "";          // WarTypeAsset id
        public int    is_rebellion = 0;       // 1 = 叛乱战争
        public double start_time;
        [TableItemDef(pDefaultValue: "-1")] public double end_time; // -1 = 进行中
        public string winner = "";            // attackers/defenders/peace/nobody(结束时定)
        public string winner_color = "";
        public int    attacker_kills = 0;
        public int    defender_kills = 0;
        public string year_prefix = "";       // 开战时通用年+年号快照
    }
}
