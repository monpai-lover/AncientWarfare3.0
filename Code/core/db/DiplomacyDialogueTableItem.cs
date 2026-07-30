using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("DiplomacyDialogue")]
    public sealed class DiplomacyDialogueTableItem :
        AbstractTableItem<DiplomacyDialogueTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long event_id;
        public long kingdom_a_id = -1;
        public long kingdom_b_id = -1;
        public long speaker_kingdom_id = -1;
        public string speaker_name = "";
        public string target_name = "";
        public string event_type = "";
        public string detail = "";
        public int event_year = -1;
        public double event_time = -1;
        public string year_prefix = "";
        public string speaker_title = "";
    }
}
