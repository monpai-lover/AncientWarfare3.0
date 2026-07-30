using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("DiplomaticOperation")]
    public sealed class DiplomaticOperationTableItem :
        AbstractTableItem<DiplomaticOperationTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long operation_id;
        public long source_kingdom_id = -1;
        public long target_kingdom_id = -1;
        public string operation_type = "";
        public int status;
        [TableItemDef(pDefaultValue: "-1")] public long target_city_id = -1;
        public string project_type = "";
        public int strong_forgery;
        public int start_year = -1;
        public int due_year = -1;
        public double start_time = -1;
        public double due_time = -1;
        public int network_strength;
        public int success_chance;
        public int discovery_chance;
        public int discovered;
        public string result = "";
        public int player_initiated;
    }
}
