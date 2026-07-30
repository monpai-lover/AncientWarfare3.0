using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("WarParticipantEntrySource")]
    public sealed class WarParticipantEntrySourceTableItem :
        AbstractTableItem<WarParticipantEntrySourceTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long entry_id;
        public long war_id = -1;
        public long kingdom_id = -1;
        public string source_kind = "unknown";
        [TableItemDef(pDefaultValue: "-1")] public long source_kingdom_id = -1;
        [TableItemDef(pDefaultValue: "1")] public int active = 1;
        public double created_time = -1;
        [TableItemDef(pDefaultValue: "-1")] public double ended_time = -1;
    }
}
