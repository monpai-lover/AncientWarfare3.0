namespace AncientWarfare3.core.court
{
    /// <summary>
    ///     「上次没找到人」的记账何时作废。
    ///
    ///     一个确实空着、但当下没人够格的席位会一直留在
    ///     <see cref="CourtVacancyRegistry"/> 里。没有这层判断的话,它每一次
    ///     唤醒都要重建候选会话、重扫一遍全国名单,而结果必然还是没人 ——
    ///     补缺路径上最后一处无上限的重复劳动。
    ///
    ///     判据用「候选池代际号」而不是固定年数冷却:池一变就立刻重试,不会
    ///     像年数冷却那样把本可以马上补上的席位压着不补。年份只作兜底,防止
    ///     某个改变候选池的事件漏接 <c>CandidatePoolChanged</c>。
    /// </summary>
    internal static class CourtVacancyPoolMemoRules
    {
        public static bool ShouldRetry(bool pHasMemo, int pMemoGeneration,
            int pMemoYear, int pCurrentGeneration, int pCurrentYear)
        {
            if (!pHasMemo) return true;
            return pMemoGeneration != pCurrentGeneration ||
                   pMemoYear != pCurrentYear;
        }
    }
}
