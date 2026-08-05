using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("KingdomInstitutionalXiaizationState")]
    public class KingdomInstitutionalXiaizationStateTableItem :
        AbstractTableItem<KingdomInstitutionalXiaizationStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long kingdom_id;
        public int version;
        public string phase = "prepared";
        public long cursor_actor_id = -1L;
        public int failure_count;
        public string last_error = "";
        public double requested_time = -1d;
        public double updated_time = -1d;
    }
}
