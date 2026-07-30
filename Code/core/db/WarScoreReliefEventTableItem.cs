using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("WarScoreReliefEvent")]
    public sealed class WarScoreReliefEventTableItem :
        AbstractTableItem<WarScoreReliefEventTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public string event_key = "";
        public long war_id = -1;
        public string event_kind = "";
        public string subject_id = "";
        public int beneficiary_side;
        public int amount;
        public double world_time = -1d;
    }
}
