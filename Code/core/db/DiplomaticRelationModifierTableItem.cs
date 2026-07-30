using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    [TableDef("DiplomaticRelationModifier")]
    public sealed class DiplomaticRelationModifierTableItem :
        AbstractTableItem<DiplomaticRelationModifierTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long modifier_id;
        public long kingdom_a_id = -1;
        public long kingdom_b_id = -1;
        public string source_type = "";
        public long source_id = -1;
        public int value;
        public int start_year = -1;
        public int until_year = -1;
        public int active = 1;
    }
}
