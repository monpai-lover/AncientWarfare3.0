using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SchoolAcademyRepairTicket")]
    public sealed class SchoolAcademyRepairTicketTableItem :
        AbstractTableItem<SchoolAcademyRepairTicketTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long institution_id;
        public long city_id;
        [TableItemDef(pDefaultValue: "-1")] public long building_id = -1;
        [TableItemDef(pDefaultValue: "-1")] public int tile_x = -1;
        [TableItemDef(pDefaultValue: "-1")] public int tile_y = -1;
        [TableItemDef(pDefaultValue: "repair_pending")] public string state =
            "repair_pending";
        [TableItemDef(pDefaultValue: "-1")] public long owner_kingdom_id = -1;
        [TableItemDef(pIsUnique: true)] public string operation_key;
        public double updated_time;
    }
}
