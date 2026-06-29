using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     国家姓氏合流状态表 —— 对应 docs 任务书 KingdomLineageState(阶段3)。
    ///     先用本表 + kingdom.data 自定义字段保存状态;等 AW3 国策系统迁移后,
    ///     国策完成时调用 LineageService.ApplyNameIntegration(kingdom) 翻转状态。
    /// </summary>
    [TableDef("KingdomLineageState")]
    public class KingdomLineageStateTableItem : AbstractTableItem<KingdomLineageStateTableItem>
    {
        [TableItemDef(pIsPrimary: true)] public long kingdom_id;

        public string kingdom_name;
        public int    name_integrated;    // 1 = 该国已完成姓氏合流
        public double integration_time;
    }
}
