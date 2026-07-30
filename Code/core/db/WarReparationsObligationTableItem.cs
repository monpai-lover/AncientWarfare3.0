using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("WarReparationsObligation")]
    public sealed class WarReparationsObligationTableItem :
        AbstractTableItem<WarReparationsObligationTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long obligation_id;
        public long proposal_id = -1;
        public long term_id = -1;
        public long war_id = -1;
        public long payer_kingdom_id = -1;
        public long recipient_kingdom_id = -1;
        public string resource_id = "gold";
        public int annual_amount;
        public int start_year = -1;
        public int end_year = -1;
        public int next_due_year = -1;
        public int total_paid;
        public int active = 1;
    }
}
