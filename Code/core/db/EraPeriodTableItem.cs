using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>纪元/年号表：每个年号一行。新王即位 INSERT，下任王即位时 UPDATE end。</summary>
    [TableDef("EraPeriod")]
    public class EraPeriodTableItem : AbstractTableItem<EraPeriodTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long era_id;

        public long   kingdom_id  = -1;
        public string kingdom_color = "";
        public string era_stem    = "";   // 年号词干（如"周伯发"或"远景"）
        public string era_color   = "";
        public double start_time;
        [TableItemDef(pDefaultValue: "-1")] public double end_time;
        public int    start_year  = 0;    // 对应 Date.getYear(start_time)
    }
}
