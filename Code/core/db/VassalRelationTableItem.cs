using AncientWarfare3.attributes;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.db
{
    [TableDef("VassalRelation")]
    public class VassalRelationTableItem : AbstractTableItem<VassalRelationTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long relation_id;

        public long vassal_id = -1;
        public string vassal_name = "";
        public string vassal_color = "";
        public long suzerain_id = -1;
        public string suzerain_name = "";
        public string suzerain_color = "";
        public string relation_type = "vassal";
        public int autonomy = 50;
        public int tribute_rate = 10;
        public int military_obligation = 50;
        public int contract_tier = VassalContractTierRules.Outer;
        public long created_by_war_id = -1;
        public double start_time = 0;
        [TableItemDef(pDefaultValue: "-1")] public double end_time = -1;
        public int active = 1;
        public int absorbed = 0;
        public string end_reason = "";
    }
}
