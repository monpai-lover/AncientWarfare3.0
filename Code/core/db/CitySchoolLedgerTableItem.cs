using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("CitySchoolLedger")]
    public sealed class CitySchoolLedgerTableItem : AbstractTableItem<CitySchoolLedgerTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public string ledger_key;
        public long city_id;
        public string school_id;
        public double tradition;
        public double membership;
        public double institutions;
        public double active_presence;
        public double momentum;
        public int last_active_year = -1;
        public int last_decay_year = -1;
        public double updated_time;
    }
}
