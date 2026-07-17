using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("SacrificeRecord")]
    public sealed class SacrificeRecordTableItem : AbstractTableItem<SacrificeRecordTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long record_id;

        public long period_id = -1;
        public long kingdom_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public long emperor_actor_id = -1;
        public string emperor_name = "";
        public string choice = "";
        public int qualified;
        public int roll_basis_points;
        public string outcome = "";
        public int mandate_delta;
        public int authority_delta;
        public int prestige_delta;
        public int annual_mandate_delta;
        public int buff_until_year;
        public int ritual_completeness;
        public double world_time = -1;
        public string year_prefix = "";
    }
}
