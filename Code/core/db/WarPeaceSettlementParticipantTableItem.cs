using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("WarPeaceSettlementParticipant")]
    public sealed class WarPeaceSettlementParticipantTableItem :
        AbstractTableItem<WarPeaceSettlementParticipantTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long participant_id;
        public long proposal_id = -1;
        public long kingdom_id = -1;
        public string side_kind = "";
        public string participant_role = "";
        [TableItemDef(pDefaultValue: "-1")] public long exit_parent_id = -1;
        [TableItemDef(pDefaultValue: "-1")] public long vassal_relation_id = -1;
        public string entry_source_kind = "unknown";
        [TableItemDef(pDefaultValue: "unknown")]
        public string entry_source_fingerprint = "unknown";
        [TableItemDef(pDefaultValue: "0")] public int included_in_exit_group;
    }
}
