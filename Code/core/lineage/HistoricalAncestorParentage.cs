namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     一位历史人物(开国君主 / 学派宗师)史载的真实双亲。
    ///
    ///     内容表只填**确有史载**的部分,查不到的一律留空 —— 空 = 不建合成祖先,
    ///     UI 显示「未详」。绝不用推测填充。
    ///
    ///     父的姓/氏默认沿用历史人物本人的(同姓同氏),所以这里只需要显示名与单名;
    ///     母的姓通常不同,单独给 <see cref="MotherFamilyName"/>。
    /// </summary>
    internal readonly struct HistoricalAncestorParentage
    {
        internal HistoricalAncestorParentage(string pFatherName,
            string pFatherGivenName, string pMotherName,
            string pMotherFamilyName)
        {
            FatherName = pFatherName ?? string.Empty;
            FatherGivenName = pFatherGivenName ?? string.Empty;
            MotherName = pMotherName ?? string.Empty;
            MotherFamilyName = pMotherFamilyName ?? string.Empty;
        }

        /// <summary>父的显示名(如「姬昌」)。空 = 不可考。</summary>
        internal string FatherName { get; }

        /// <summary>父的单名(如「昌」)。可空。</summary>
        internal string FatherGivenName { get; }

        /// <summary>母的显示名(如「太姒」「卞氏」)。空 = 不可考。</summary>
        internal string MotherName { get; }

        /// <summary>母的姓(如「姒」)。可空。</summary>
        internal string MotherFamilyName { get; }

        internal bool HasFather =>
            HistoricalAncestorRules.IsAttested(FatherName);

        internal bool HasMother =>
            HistoricalAncestorRules.IsAttested(MotherName);

        internal bool IsEmpty => !HasFather && !HasMother;
    }
}
