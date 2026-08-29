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
            string pMotherFamilyName, bool pFatherDisplayOnly = false)
        {
            FatherName = pFatherName ?? string.Empty;
            FatherGivenName = pFatherGivenName ?? string.Empty;
            MotherName = pMotherName ?? string.Empty;
            MotherFamilyName = pMotherFamilyName ?? string.Empty;
            FatherDisplayOnly = pFatherDisplayOnly;
        }

        /// <summary>父的显示名(如「姬昌」)。空 = 不可考。</summary>
        internal string FatherName { get; }

        /// <summary>父的单名(如「昌」)。可空。</summary>
        internal string FatherGivenName { get; }

        /// <summary>母的显示名(如「太姒」「卞氏」)。空 = 不可考。</summary>
        internal string MotherName { get; }

        /// <summary>母的姓(如「姒」)。可空。</summary>
        internal string MotherFamilyName { get; }

        /// <summary>
        ///     父只显示名字,不建合成祖先。
        ///
        ///     用于「父亲本人也在名册里」的情形(司马迁之父司马谈亦是宗师):史实要
        ///     照实显示,但给他造一个合成的司马谈,会和世上可能同时存在的真司马谈
        ///     重影 —— 家族树上出现两个司马谈,其中一个还是假的。
        /// </summary>
        internal bool FatherDisplayOnly { get; }

        internal bool HasFather =>
            HistoricalAncestorRules.IsAttested(FatherName);

        /// <summary>父是否要落成合成祖先(可考 且 未被标为仅显示)。</summary>
        internal bool BuildsFatherAncestor => HasFather && !FatherDisplayOnly;

        internal bool HasMother =>
            HistoricalAncestorRules.IsAttested(MotherName);

        internal bool IsEmpty => !HasFather && !HasMother;
    }
}
