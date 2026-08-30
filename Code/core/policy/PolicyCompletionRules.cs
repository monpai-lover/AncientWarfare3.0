using System;

namespace AncientWarfare3.core.policy
{
    /// <summary>
    /// 已完成政策/技术串的纯判定。
    ///
    /// 串的形状是 <c>"a;b;c"</c>,分号分隔、允许空段。这里唯一的规则是
    /// 「某个 id 在不在串里」,但它是**全模组最热的一个谓词**:
    /// <c>HasExaminationSystem</c> / <c>HasNineRankSystem</c> 都落在它上面,
    /// 而补缺的资格判定对候选池里每一个人各问一次。
    ///
    /// 所以这里不切分。原来的写法是 <c>raw.Split(';').Contains(id)</c> ——
    /// 一个成熟王国的已完成串有几十项,每问一次就 new 一个 char[] 加一整套
    /// 子串。8k 存档实测一次王国补缺要走「候选池 × (城,官职) 对数」遍,
    /// 于是这一行自己就能产出几百万次字符串分配。
    /// </summary>
    public static class PolicyCompletionRules
    {
        public const char Separator = ';';

        /// <summary>
        /// 等价于 <c>raw.Split(';', RemoveEmptyEntries).Contains(id)</c>:
        /// 分隔符相同;空段长度为 0,与非空 id 长度不等而自然跳过,等同
        /// RemoveEmptyEntries;字符串比较同为序数比较。id 为空时恒为 false ——
        /// 空 id 本来就不该匹配上任何一段,RemoveEmptyEntries 也已经把空段删了。
        /// </summary>
        public static bool ContainsId(string pRaw, string pId)
        {
            if (string.IsNullOrEmpty(pRaw) || string.IsNullOrEmpty(pId))
                return false;
            int index = 0;
            while (index < pRaw.Length)
            {
                int end = pRaw.IndexOf(Separator, index);
                if (end < 0) end = pRaw.Length;
                int length = end - index;
                if (length == pId.Length &&
                    string.CompareOrdinal(pRaw, index, pId, 0, length) == 0)
                    return true;
                index = end + 1;
            }
            return false;
        }
    }
}
