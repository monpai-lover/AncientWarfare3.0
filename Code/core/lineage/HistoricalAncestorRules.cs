namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     历史人物史载双亲的纯规则:合成祖先 id 分配 + 建档判定。无副作用,可单测。
    ///
    ///     合成祖先是一条只存在于 ActorArchive 的「档案人」,没有 live Actor。这样
    ///     家族树能显示姬昌,而原版引擎、以及**全部继承逻辑**都看不见他:
    ///     - `SuccessionRelationshipIndex` 只由活 actor 构建 → 结构上不可见;
    ///     - 合成行 `LINEAGE_ID = -1` / `SHI_ID = -1` → 进不了任何按姓族/氏支
    ///       作用域的查询(氏支大树、姓族成员、绝嗣判定);
    ///     - 历史人物本人的 `data.parent_id_1/2` 保持 -1 → 原版 `getParents()` /
    ///       `isChildOf` / 家庭 UI 一概无感。
    ///
    ///     id 取**大正数**而非负数:`LineageQuery.GetParentIds` 的 SQL 带
    ///     `PARENT_ID>=0`、`FamilyTreeRelationRules.MergeRelationIds` 丢 `id < 0`,
    ///     负数 id 会被到处过滤掉,合成祖先将永不可见。
    /// </summary>
    internal static class HistoricalAncestorRules
    {
        /// <summary>
        ///     合成 id 起点。真实 unit id 由 `map_stats.getNextId("unit")` 单调发号,
        ///     长局也在 10⁷ 量级 —— 差 5 个数量级,不会相撞。
        /// </summary>
        internal const long SyntheticBase = 1_000_000_000_000L;

        /// <summary>宗师段相对 <see cref="SyntheticBase"/> 的偏移,与君主段隔开。</summary>
        internal const long MasterBandOffset = 100_000L;

        /// <summary>每段最多容纳的人数(段内每人占 2 个 id:父 + 母)。</summary>
        internal const int MaxPerBand = (int)(MasterBandOffset / 2L);

        /// <summary>父母槽位沿用 FamilyEdge 的 PARENT_SLOT 语义(1 / 2)。</summary>
        internal const int FatherSlot = 1;
        internal const int MotherSlot = 2;

        internal static bool IsSynthetic(long pActorId)
        {
            return pActorId >= SyntheticBase;
        }

        /// <summary>开国君主(HistoricalFigureDef.RegistryIndex)的合成祖先 id。</summary>
        internal static long FigureAncestorId(int pRegistryIndex, int pParentSlot)
        {
            return AncestorId(0L, pRegistryIndex, pParentSlot);
        }

        /// <summary>学派宗师(HistoricalSchoolMasterDefinition.RegistryIndex)的合成祖先 id。</summary>
        internal static long MasterAncestorId(int pRegistryIndex, int pParentSlot)
        {
            return AncestorId(MasterBandOffset, pRegistryIndex, pParentSlot);
        }

        private static long AncestorId(long pBandOffset, int pRegistryIndex,
            int pParentSlot)
        {
            if (pRegistryIndex < 0 || pRegistryIndex >= MaxPerBand) return -1L;
            if (pParentSlot != FatherSlot && pParentSlot != MotherSlot)
                return -1L;
            return SyntheticBase + pBandOffset + pRegistryIndex * 2L +
                   (pParentSlot - FatherSlot);
        }

        /// <summary>史载是否可考。空白名一律视为不可考。</summary>
        internal static bool IsAttested(string pName)
        {
            return !string.IsNullOrWhiteSpace(pName);
        }

        /// <summary>
        ///     该槽位是否应当建合成祖先。要求名字可考且 id 分配成功。
        /// </summary>
        internal static bool ShouldCreateAncestor(long pAncestorId, string pName)
        {
            return pAncestorId >= SyntheticBase && IsAttested(pName);
        }

        /// <summary>
        ///     入档时该写哪个双亲 id。
        ///
        ///     `LineageArchiveWriter` 一向直接抄 live `data.parent_id_*`,而历史人物
        ///     的 live 槽位被刻意清成 -1,所以必须让史载 id 优先 —— 否则每次重新
        ///     入档(晋升 / 死亡 / 改氏)都会把合成祖先抹掉。
        /// </summary>
        internal static long ResolveArchiveParentId(long pHistoricalId,
            long pLiveParentId)
        {
            return pHistoricalId >= SyntheticBase ? pHistoricalId : pLiveParentId;
        }

        /// <summary>
        ///     该 actor 的双亲是否由史载合成祖先固定。为真时,任何按引擎槽位推断
        ///     双亲的路径都必须让位 —— 他的 `data.parent_id_*` 是刻意清空的,不是
        ///     「缺数据待补」。
        /// </summary>
        internal static bool HasHistoricalParentage(long pFatherId,
            long pMotherId)
        {
            return IsSynthetic(pFatherId) || IsSynthetic(pMotherId);
        }

        /// <summary>
        ///     是否已经就是目标状态。为真则整个写入可以跳过,一条 SQL 都不发。
        ///
        ///     必须连**名字**一起比,不能只比合成祖先 id:仅显示的父亲
        ///     (FatherDisplayOnly,如司马迁之父司马谈)两个 id 都是 -1,只比 id 会
        ///     误判「已就位」,名字就永远写不进去。
        /// </summary>
        internal static bool IsAlreadyApplied(long pLiveParent1,
            long pLiveParent2, HistoricalParentageState pStored,
            HistoricalParentageState pExpected)
        {
            return pLiveParent1 < 0L && pLiveParent2 < 0L &&
                   pStored.Matches(pExpected);
        }
    }

    /// <summary>
    ///     actor 身上已记录 / 内容表期望的史载双亲状态。用于幂等比较。
    /// </summary>
    internal readonly struct HistoricalParentageState
    {
        internal HistoricalParentageState(long pFatherId, long pMotherId,
            string pFatherName, string pMotherName)
        {
            FatherId = pFatherId;
            MotherId = pMotherId;
            FatherName = pFatherName ?? string.Empty;
            MotherName = pMotherName ?? string.Empty;
        }

        internal long FatherId { get; }
        internal long MotherId { get; }
        internal string FatherName { get; }
        internal string MotherName { get; }

        internal bool Matches(HistoricalParentageState pOther)
        {
            return FatherId == pOther.FatherId &&
                   MotherId == pOther.MotherId &&
                   string.Equals(FatherName, pOther.FatherName,
                       System.StringComparison.Ordinal) &&
                   string.Equals(MotherName, pOther.MotherName,
                       System.StringComparison.Ordinal);
        }
    }
}
