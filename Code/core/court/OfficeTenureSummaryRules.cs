using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    /// <summary>
    /// 「历任官职」这一行怎么排。
    ///
    /// 族谱 tooltip 和传记的死亡总结都要一句话交代此人做过什么官。两处都是
    /// **有限宽度**的展示位:tooltip 多一行就挡住底下的节点,死亡总结是写进
    /// 存档的定型字符串。所以不能把履历全倒出来,得先排序、去重、截断。
    ///
    /// 这里只做排序去重截断,**不查 locale、不碰 DB**。传进来的
    /// <see cref="Entry.Label"/> 已经是译好的官职名 —— 谁来译由调用方决定
    /// (归档路径要过 <see cref="CourtOfficeDisplayRules.IsUntranslated"/>,
    /// 活人 tooltip 不必)。分开是为了能在 Rules.Tests 里直接测。
    /// </summary>
    public static class OfficeTenureSummaryRules
    {
        /// <summary>tooltip 里最多列几任。再多就挤掉别的行了。</summary>
        public const int MaxListedOffices = 4;

        /// <summary>被截断时前面加这个,提示「还有更早的」。</summary>
        public const string TruncationMark = "…";

        /// <summary>官职之间的分隔符 —— 和 <c>ComposeJointTitle</c> 一致。</summary>
        public const string Separator = " · ";

        /// <summary>
        /// 一任官。<see cref="OfficerId"/> 是主键所以唯一,有它
        /// <see cref="SortsBefore"/> 才是全序。
        /// </summary>
        public struct Entry
        {
            public string Label;
            public double AppointedTime;
            public long OfficerId;
        }

        /// <summary>
        /// 时间升序,同时刻按 officer_id 升序。同一年里几任官的先后本来就分不出
        /// 来,用主键兜住,免得两次渲染给出不同顺序。
        /// </summary>
        public static bool SortsBefore(double pTimeA, long pIdA,
            double pTimeB, long pIdB)
        {
            if (pTimeA != pTimeB) return pTimeA < pTimeB;
            return pIdA < pIdB;
        }

        /// <summary>
        /// 拼出「历任官职」正文。空串表示无可展示的履历,调用方应整行省略。
        ///
        /// 截断保留**最近**的几任:一个人最后做到什么官,比他二十岁时的第一个
        /// 差事更能说明他是谁。保留的部分内部仍是时间升序,读起来还是履历。
        /// </summary>
        public static string Compose(IEnumerable<Entry> pEntries,
            int pMaxListed)
        {
            int limit = pMaxListed > 0 ? pMaxListed : MaxListedOffices;
            List<Entry> ordered = Normalize(pEntries);
            if (ordered.Count == 0) return "";

            bool truncated = ordered.Count > limit;
            int start = truncated ? ordered.Count - limit : 0;
            var text = new System.Text.StringBuilder();
            if (truncated) text.Append(TruncationMark).Append(Separator);
            for (int index = start; index < ordered.Count; index++)
            {
                if (index > start) text.Append(Separator);
                text.Append(ordered[index].Label);
            }

            return text.ToString();
        }

        /// <summary>
        /// 排序 + 去重,截断之前的那一步。单独暴露是为了让测试能分别验「序对不对」
        /// 和「截断截在哪」,也让死亡总结那种想全量列出的调用方直接用。
        ///
        /// 去重按官职名:同一个官反复出任(常见 —— 罢了又起用)只算一次,留**最早**
        /// 那次的时间。留最早才能让「他从什么官起家」在长履历里不被同名的后一任
        /// 顶掉;而截断保留最近几任是按位置算的,与此不冲突。
        /// </summary>
        public static List<Entry> Normalize(IEnumerable<Entry> pEntries)
        {
            var ordered = new List<Entry>();
            if (pEntries == null) return ordered;

            foreach (Entry entry in pEntries)
            {
                string label = (entry.Label ?? "").Trim();
                if (label.Length == 0) continue;
                Entry candidate = entry;
                candidate.Label = label;
                InsertSorted(ordered, candidate);
            }

            return Dedupe(ordered);
        }

        /// <summary>
        /// 二分插入。<see cref="SortsBefore"/> 是全序,所以插入位置唯一确定。
        /// </summary>
        private static void InsertSorted(List<Entry> pOrdered, Entry pEntry)
        {
            int low = 0;
            int high = pOrdered.Count;
            while (low < high)
            {
                int mid = low + ((high - low) >> 1);
                if (SortsBefore(pOrdered[mid].AppointedTime,
                        pOrdered[mid].OfficerId, pEntry.AppointedTime,
                        pEntry.OfficerId))
                    low = mid + 1;
                else
                    high = mid;
            }

            pOrdered.Insert(low, pEntry);
        }

        /// <summary>
        /// 同名只留最早那次。表已按时间升序,所以第一次见到就是最早的。
        /// </summary>
        private static List<Entry> Dedupe(List<Entry> pOrdered)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var unique = new List<Entry>();
            for (int index = 0; index < pOrdered.Count; index++)
            {
                if (!seen.Add(pOrdered[index].Label)) continue;
                unique.Add(pOrdered[index]);
            }

            return unique;
        }
    }
}
