using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>纪元/年号表：每个年号一行。新王即位 INSERT，下任王即位时 UPDATE end。</summary>
    [TableDef("EraPeriod")]
    public class EraPeriodTableItem : AbstractTableItem<EraPeriodTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long era_id;

        public long kingdom_id = -1;
        public string kingdom_color = "";
        [TableItemDef(pDefaultValue: "-1")] public long shi_id = -1;
        [TableItemDef(pDefaultValue: "-1")] public long actor_id = -1;
        [TableItemDef(pDefaultValue: "-1")] public long reign_id = -1;
        public string era_stem = "";
        public string era_color = "";
        public string change_kind = "";
        public string change_reason = "";
        public string source_event_id = "";
        public double decided_time;
        public double start_time;
        [TableItemDef(pDefaultValue: "-1")] public double end_time;
        public int start_year;
    }
}
