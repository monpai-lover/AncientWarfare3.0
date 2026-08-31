using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// 历任官职的排序与去重 —— 族谱 tooltip 和传记死亡总结都用这一份。
    ///
    /// 一个人一生可能同一个职位任过多次（罢了又起复），也可能同时挂着几个。
    /// 直接把 CourtOfficer 的行铺出来会得到一长串重复。规则：
    ///
    /// <list type="bullet">
    ///   <item>按<b>就任年</b>升序 —— 读起来就是履历的时间顺序；</item>
    ///   <item>同年就任按 office_id 字典序，保证全序（否则展示会随机抖动）；</item>
    ///   <item>同一 office_id 只留**最早**那次，但「是否仍在任」取所有那几次的
    ///         或值 —— 起复过的人现在还在任，就不该显示成「前」；</item>
    ///   <item>条数有上限。当了一辈子官的人 tooltip 不能无限长，
    ///         留最早的若干任，因为履历的开头才是身份的由来。</item>
    /// </list>
    ///
    /// 这里只排序去重，**不碰 locale** —— 返回 office_id，由展示层取词。
    /// 死者的 Kingdom 可能已经不存在了，官名解析必须留给拿得到 Kingdom 的
    /// 那一层，规则层不该假装自己能解析。
    /// </summary>
    public static class OfficeTenureSummaryRules
    {
        /// <summary>tooltip 里最多列几任。</summary>
        public const int MaximumEntries = 6;

        /// <summary>已卸任的官职前缀 locale 键 —— 「前·京兆尹」那个「前」。</summary>
        public const string FormerPrefixKey = "aw_career_former_prefix";

        /// <summary>一任官职：职位、就任年、是否仍在任。</summary>
        public struct Tenure
        {
            public string OfficeId;
            public int AppointedYear;
            public bool Active;

            public Tenure(string pOfficeId, int pAppointedYear, bool pActive)
            {
                OfficeId = pOfficeId;
                AppointedYear = pAppointedYear;
                Active = pActive;
            }
        }

        /// <summary>
        /// 就任年升序，同年按 office_id 升序。office_id 在同一年内唯一
        /// （同一年同一职位只可能有一条留下来，见 <see cref="Summarize"/> 的
        /// 去重），所以这是全序，展示顺序唯一确定。
        /// </summary>
        public static bool SortsBefore(Tenure pLeft, Tenure pRight)
        {
            if (pLeft.AppointedYear != pRight.AppointedYear)
                return pLeft.AppointedYear < pRight.AppointedYear;
            return string.CompareOrdinal(pLeft.OfficeId ?? "",
                pRight.OfficeId ?? "") < 0;
        }

        /// <summary>
        /// 排序 + 去重 + 截断。输入可以是任意顺序、含重复的原始任职记录。
        ///
        /// 空 office_id 的行直接丢弃 —— 那是脏数据，显示出来是个空条目。
        /// </summary>
        public static List<Tenure> Summarize(IEnumerable<Tenure> pTenures,
            int pMaximum)
        {
            var result = new List<Tenure>();
            if (pTenures == null) return result;

            // 先按 office_id 归并：留最早那次，Active 取或值。
            var earliest = new Dictionary<string, Tenure>();
            var order = new List<string>();
            foreach (Tenure tenure in pTenures)
            {
                if (string.IsNullOrWhiteSpace(tenure.OfficeId)) continue;
                if (!earliest.TryGetValue(tenure.OfficeId, out Tenure kept))
                {
                    earliest[tenure.OfficeId] = tenure;
                    order.Add(tenure.OfficeId);
                    continue;
                }

                var merged = new Tenure(tenure.OfficeId,
                    tenure.AppointedYear < kept.AppointedYear
                        ? tenure.AppointedYear : kept.AppointedYear,
                    kept.Active || tenure.Active);
                earliest[tenure.OfficeId] = merged;
            }

            foreach (string officeId in order)
                result.Add(earliest[officeId]);

            // 插入排序：条数被 MaximumEntries 量级压着，不值得上更复杂的。
            for (int i = 1; i < result.Count; i++)
            for (int j = i; j > 0; j--)
            {
                if (SortsBefore(result[j - 1], result[j])) break;
                Tenure carry = result[j - 1];
                result[j - 1] = result[j];
                result[j] = carry;
            }

            int limit = pMaximum < 0 ? 0 : pMaximum;
            if (result.Count > limit) result.RemoveRange(limit,
                result.Count - limit);
            return result;
        }

        /// <summary>
        /// 这一任要不要加「前」字。仍在任的不加 —— 「前京兆尹」和「京兆尹」
        /// 是两个意思，混了就等于在谱系上谎报某人还在位。
        /// </summary>
        public static bool NeedsFormerPrefix(Tenure pTenure)
        {
            return !pTenure.Active;
        }

        /// <summary>
        /// 已故者的任职一律算「前」。人死了官位当然不在了，而 CourtOfficer
        /// 的 active 位在某些结束路径上可能来不及落（国灭时整表作废那类），
        /// 所以死亡这个信号要盖在 active 之上，不能反过来信表里的位。
        /// </summary>
        public static bool NeedsFormerPrefix(Tenure pTenure, bool pDeceased)
        {
            return pDeceased || NeedsFormerPrefix(pTenure);
        }

        /// <summary>条目分隔符 —— office_id 里不会出现。</summary>
        public const char EntrySeparator = '|';

        /// <summary>已卸任标记，编码时前置。</summary>
        public const char FormerMark = '-';

        /// <summary>
        /// 编成一个串带出查询层。
        ///
        /// 为什么不在查询层就把官名解析好：死者的 Kingdom 可能已经不存在，
        /// 而官名要靠 Kingdom 才能解析（自定义官制、学派、品级都挂在国上）。
        /// 所以查询层带出 office_id，展示层解析 —— 那一层手上有 Kingdom。
        /// </summary>
        public static string Encode(IEnumerable<Tenure> pTenures,
            bool pDeceased)
        {
            if (pTenures == null) return "";
            var text = new System.Text.StringBuilder();
            foreach (Tenure tenure in pTenures)
            {
                if (string.IsNullOrWhiteSpace(tenure.OfficeId)) continue;
                if (text.Length > 0) text.Append(EntrySeparator);
                if (NeedsFormerPrefix(tenure, pDeceased))
                    text.Append(FormerMark);
                text.Append(tenure.OfficeId);
            }

            return text.ToString();
        }

        /// <summary>
        /// 解回 (office_id, 是否为前任)。<see cref="Encode"/> 的逆 ——
        /// 两边必须放在一起，分开写就会慢慢分叉。
        /// </summary>
        public static List<KeyValuePair<string, bool>> Decode(string pEncoded)
        {
            var result = new List<KeyValuePair<string, bool>>();
            if (string.IsNullOrEmpty(pEncoded)) return result;
            string[] parts = pEncoded.Split(EntrySeparator);
            foreach (string part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                bool former = part[0] == FormerMark;
                string officeId = former ? part.Substring(1) : part;
                if (officeId.Length == 0) continue;
                result.Add(new KeyValuePair<string, bool>(officeId, former));
            }

            return result;
        }
    }
}
