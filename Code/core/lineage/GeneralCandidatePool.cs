using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// 将领候选池 —— **跨年持久、靠事件维护**。
    ///
    /// 顺序只算一次。之后来了新人只算他一个人的稳定分,二分插到位;走了人
    /// 就摘掉;功绩涨了就换位。整池重建只在「有缺额却补不上」时兜底一次。
    ///
    /// 原来 <c>CollectCandidates</c> 每王国每年把全国几千人扫一遍,逐人问
    /// 九项资格再全排。更贵的是 <c>GetMerit</c> 里有一条 SQL,而资格判定和
    /// 评分各调它一次 —— 几千人 × 2 条查询,实测 191ms 单次。现在功绩由
    /// <see cref="GeneralMeritIndex"/> 一次批量读入。
    ///
    /// 表里存的是**稳定分**,漂移项(军队长、职业、战斗属性)在取人时补回,
    /// 判据见 <see cref="GeneralShortlistRules"/>。
    ///
    /// <para><b>漂移的两个方向都能自愈</b>,所以漏接一个事件只损失性能:</para>
    /// <list type="bullet">
    ///   <item>多收了人(已经不合格但没摘干净)—— 取人时逐个复核,滤掉;</item>
    ///   <item>少收了人(某个入池事件没接上)—— 有缺额却挑不出人,
    ///         由调用方触发一次重建。</item>
    /// </list>
    ///
    /// 换表而不是就地改:表可能正被某一轮任命遍历着,就地 Insert 会抛
    /// <c>Collection was modified</c>。
    /// </summary>
    internal static class GeneralCandidatePool
    {
        internal sealed class Table
        {
            internal List<Actor> Actors = new List<Actor>();
            internal List<int> Stable = new List<int>();
            internal List<long> Ids = new List<long>();
            internal HashSet<long> Members = new HashSet<long>();

            internal int Count => Actors.Count;

            internal Table Copy()
            {
                return new Table
                {
                    Actors = new List<Actor>(Actors),
                    Stable = new List<int>(Stable),
                    Ids = new List<long>(Ids),
                    Members = new HashSet<long>(Members)
                };
            }
        }

        private static readonly Dictionary<long, Table> Pools =
            new Dictionary<long, Table>();

        /// <summary>第一次问才建;之后一直用同一张。</summary>
        internal static Table GetOrBuild(Kingdom pKingdom, Func<Table> pBuild)
        {
            if (pKingdom?.data == null || pBuild == null) return new Table();
            if (Pools.TryGetValue(pKingdom.id, out Table cached))
                return cached;
            Table built = pBuild() ?? new Table();
            Pools[pKingdom.id] = built;
            return built;
        }

        /// <summary>已经建过表的王国才需要增量维护 —— 没建过的第一次问时会全量建。</summary>
        internal static bool HasTable(Kingdom pKingdom)
        {
            return pKingdom?.data != null && Pools.ContainsKey(pKingdom.id);
        }

        /// <summary>
        /// 把一个人按稳定分二分插入。已经在表里就不动 —— 重复插入会让同一个人
        /// 占两个位置,取人时复核也发现不了(他确实合格)。
        /// </summary>
        internal static void Insert(Kingdom pKingdom, Actor pActor,
            int pStableScore)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            if (!Pools.TryGetValue(pKingdom.id, out Table current)) return;
            long actorId = pActor.data.id;
            if (current.Members.Contains(actorId)) return;

            Table next = current.Copy();
            int position = FindInsertPosition(next, pStableScore, actorId);
            next.Actors.Insert(position, pActor);
            next.Stable.Insert(position, pStableScore);
            next.Ids.Insert(position, actorId);
            next.Members.Add(actorId);
            Pools[pKingdom.id] = next;
        }

        /// <summary>死亡、离境、已任将领、失去资格 —— 都从表里摘掉。</summary>
        internal static void Remove(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            RemoveById(pKingdom.id, pActor.data.id);
        }

        internal static void RemoveById(long pKingdomId, long pActorId)
        {
            if (!Pools.TryGetValue(pKingdomId, out Table current)) return;
            if (!current.Members.Contains(pActorId)) return;
            Table next = current.Copy();
            int position = next.Ids.IndexOf(pActorId);
            if (position >= 0)
            {
                next.Actors.RemoveAt(position);
                next.Stable.RemoveAt(position);
                next.Ids.RemoveAt(position);
            }

            next.Members.Remove(pActorId);
            Pools[pKingdomId] = next;
        }

        /// <summary>
        /// 稳定分变了(功绩涨了、当上城主、封了爵)—— 摘掉再按新分插回。
        /// 不这么做,表会随这些变动慢慢失序。
        /// </summary>
        internal static void Reposition(Kingdom pKingdom, Actor pActor,
            int pStableScore)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            if (!Pools.ContainsKey(pKingdom.id)) return;
            Remove(pKingdom, pActor);
            Insert(pKingdom, pActor, pStableScore);
        }

        /// <summary>兜底重建入口 —— 只在有缺额却补不上时由调用方触发一次。</summary>
        internal static void Invalidate(Kingdom pKingdom)
        {
            if (pKingdom?.data != null) Pools.Remove(pKingdom.id);
        }

        internal static void ClearRuntime() => Pools.Clear();

        private static int FindInsertPosition(Table pTable, int pStable,
            long pId)
        {
            int low = 0;
            int high = pTable.Count;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                bool sortsBefore = GeneralShortlistRules.SortsBefore(
                    pTable.Stable[middle], pTable.Ids[middle], pStable, pId);
                if (sortsBefore) low = middle + 1;
                else high = middle;
            }

            return low;
        }
    }
}
