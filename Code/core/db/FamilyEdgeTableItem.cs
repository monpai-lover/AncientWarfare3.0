using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    /// <summary>
    ///     持久亲子边表 —— 对应 docs 任务书 FamilyEdge。
    ///     原版 actor 销毁后父母/子女关系会丢失,故 AW3 用本表持久化亲子边,
    ///     家族树绘制完全基于本表(而非原版乱序 Clan)。
    ///     主键用 (child_id, parent_slot):一个孩子最多两个父母槽(1/2)。
    ///     这里用复合语义但 SQLite 单主键限制,故用 edge_id 自增主键 + child/parent 唯一约束的替代:
    ///     简化为 child_id 作为主键不行(一子两亲),改用无主键 + 应用层去重(parent_slot 区分)。
    /// </summary>
    [TableDef("FamilyEdge")]
    public class FamilyEdgeTableItem : AbstractTableItem<FamilyEdgeTableItem>
    {
        // edge_id = child_id * 10 + parent_slot,保证一子两亲两条边各自唯一,可作主键。
        [TableItemDef(pIsPrimary: true)] public long edge_id;

        public long   child_id = -1;
        public long   parent_id = -1;
        public int    parent_slot;        // 1 或 2(对应 ActorData.parent_id_1/2)
        public long   child_lineage_id = -1;
        public double created_time;
    }
}
