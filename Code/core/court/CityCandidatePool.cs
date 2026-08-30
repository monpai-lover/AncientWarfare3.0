using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    /// <summary>
    /// 城官候选池 —— **按行为类分档、跨轮持久、靠事件维护**。
    ///
    /// 顺序只算一次。之后来了新人,只算这一个人的档次与分数,二分插到对应
    /// 区间;走了人就摘掉;分数变了就换位。整池重排只在出错兜底时发生。
    ///
    /// 分档的依据是<see cref="CandidatePoolBehavior"/>:决定排序的只有席位的
    /// 品级、是不是方镇主官、走不走空缺晋升。同品级的两个官职、同品级的两个
    /// 城,候选顺序逐字节相同 —— 所以它们共用一张表。原来按 (城, 官职, 通道)
    /// 缓存且每轮重建,实测一次补缺 381 次建表、遍历 58240 行。
    ///
    /// 籍贯加成不进表(它按城变),由取人方在表头补回 ——
    /// 见 <see cref="CityShortlistRules"/>。
    ///
    /// <para><b>漂移的两个方向都能自愈</b>,所以漏接一个事件只损失性能,不会
    /// 让官位永远补不上:</para>
    /// <list type="bullet">
    ///   <item>多收了人(资格已失效但没摘干净)—— 取人时逐个复核,滤掉;</item>
    ///   <item>少收了人(某个入池事件没接上)—— 补缺走到「没人可补」,
    ///         由调用方触发一次重建。</item>
    /// </list>
    ///
    /// 换表而不是就地改:表会被 <c>CourtCandidateSession</c> 原样交给正在
    /// 遍历它的补缺轮次,就地 Insert 会抛 <c>Collection was modified</c>。
    /// 换表的语义是「已经取到表的那一轮看不到这次改动,下一轮才看到」。
    /// </summary>
    internal static class CityCandidatePool
    {
        /// <summary>一个人在某个行为类下的排序键。</summary>
        internal readonly struct Ranked
        {
            internal readonly int Tier;
            /// <summary>不含籍贯加成的分。</summary>
            internal readonly int Score;

            internal Ranked(int pTier, int pScore)
            {
                Tier = pTier;
                Score = pScore;
            }
        }

        internal sealed class Table
        {
            internal List<Actor> Actors = new List<Actor>();
            internal List<int> Tiers = new List<int>();
            internal List<int> Scores = new List<int>();
            internal List<long> Ids = new List<long>();
            internal List<long> NativeCityIds = new List<long>();
            internal HashSet<long> Members = new HashSet<long>();

            internal int Count => Actors.Count;

            internal Table Copy()
            {
                return new Table
                {
                    Actors = new List<Actor>(Actors),
                    Tiers = new List<int>(Tiers),
                    Scores = new List<int>(Scores),
                    Ids = new List<long>(Ids),
                    NativeCityIds = new List<long>(NativeCityIds),
                    Members = new HashSet<long>(Members)
                };
            }
        }

        private static readonly Dictionary<long, Dictionary<int, Table>>
            Pools = new Dictionary<long, Dictionary<int, Table>>();

        /// <summary>
        /// 取这个王国这个行为类的表。第一次问才建;之后一直用同一张,靠
        /// <see cref="Insert"/> / <see cref="Remove"/> / <see cref="Reposition"/>
        /// 维护。
        /// </summary>
        internal static Table GetOrBuild(Kingdom pKingdom,
            CandidatePoolBehavior pBehavior, Func<Table> pBuild)
        {
            if (pKingdom?.data == null || pBuild == null) return new Table();
            if (!Pools.TryGetValue(pKingdom.id,
                    out Dictionary<int, Table> byBehavior))
            {
                byBehavior = new Dictionary<int, Table>();
                Pools[pKingdom.id] = byBehavior;
            }

            int key = pBehavior.Key();
            if (byBehavior.TryGetValue(key, out Table cached)) return cached;
            Table built = pBuild() ?? new Table();
            byBehavior[key] = built;
            return built;
        }

        /// <summary>
        /// 把一个人插进这个王国**已经建起来的每一张**表。只算他一个人的排序键
        /// (由 <paramref name="pRank"/> 按行为类给出),二分定位插入。
        ///
        /// 只碰已建的表 —— 没建过的行为类第一次被问到时会全量建,那时自然
        /// 包含这个人。
        /// </summary>
        internal static void Insert(Kingdom pKingdom, Actor pActor,
            Func<CandidatePoolBehavior, Ranked> pRank, long pNativeCityId)
        {
            if (pKingdom?.data == null || pActor?.data == null ||
                pRank == null) return;
            if (!Pools.TryGetValue(pKingdom.id,
                    out Dictionary<int, Table> byBehavior)) return;
            long actorId = pActor.data.id;
            var replacements = new List<KeyValuePair<int, Table>>();
            foreach (KeyValuePair<int, Table> pair in byBehavior)
            {
                if (pair.Value.Members.Contains(actorId)) continue;
                Ranked ranked = pRank(CandidatePoolBehavior.FromKey(
                    pair.Key));
                if (ranked.Tier < 0) continue;
                Table next = pair.Value.Copy();
                int position = FindInsertPosition(next, ranked.Tier,
                    ranked.Score, actorId);
                next.Actors.Insert(position, pActor);
                next.Tiers.Insert(position, ranked.Tier);
                next.Scores.Insert(position, ranked.Score);
                next.Ids.Insert(position, actorId);
                next.NativeCityIds.Insert(position, pNativeCityId);
                next.Members.Add(actorId);
                replacements.Add(new KeyValuePair<int, Table>(pair.Key, next));
            }

            for (int index = 0; index < replacements.Count; index++)
                byBehavior[replacements[index].Key] = replacements[index].Value;
        }

        /// <summary>把一个人从这个王国的每一张表里摘掉(死亡、离境、已上任)。</summary>
        internal static void Remove(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            RemoveById(pKingdom.id, pActor.data.id);
        }

        internal static void RemoveById(long pKingdomId, long pActorId)
        {
            if (!Pools.TryGetValue(pKingdomId,
                    out Dictionary<int, Table> byBehavior)) return;
            var replacements = new List<KeyValuePair<int, Table>>();
            foreach (KeyValuePair<int, Table> pair in byBehavior)
            {
                if (!pair.Value.Members.Contains(pActorId)) continue;
                Table next = pair.Value.Copy();
                int position = next.Ids.IndexOf(pActorId);
                if (position >= 0)
                {
                    next.Actors.RemoveAt(position);
                    next.Tiers.RemoveAt(position);
                    next.Scores.RemoveAt(position);
                    next.Ids.RemoveAt(position);
                    next.NativeCityIds.RemoveAt(position);
                }

                next.Members.Remove(pActorId);
                replacements.Add(new KeyValuePair<int, Table>(pair.Key, next));
            }

            for (int index = 0; index < replacements.Count; index++)
                byBehavior[replacements[index].Key] = replacements[index].Value;
        }

        /// <summary>
        /// 排序键变了(升迁、中举、功绩涨了)—— 摘掉再按新键插回去。不这么做,
        /// 表会随着这些变动慢慢失序。
        /// </summary>
        internal static void Reposition(Kingdom pKingdom, Actor pActor,
            Func<CandidatePoolBehavior, Ranked> pRank, long pNativeCityId)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            Remove(pKingdom, pActor);
            Insert(pKingdom, pActor, pRank, pNativeCityId);
        }

        /// <summary>兜底重建入口 —— 只在补缺确实补不上时由调用方触发一次。</summary>
        internal static void Invalidate(Kingdom pKingdom)
        {
            if (pKingdom?.data != null) Pools.Remove(pKingdom.id);
        }

        internal static void ClearRuntime() => Pools.Clear();

        /// <summary>
        /// 按「门第档次升序、分降序、同分按 id 升序」二分定位。和
        /// <see cref="CountyShortlistRules.SortsBefore"/> 同一个序 —— id 唯一,
        /// 所以这是全序,插入位置唯一确定。
        /// </summary>
        private static int FindInsertPosition(Table pTable, int pTier,
            int pScore, long pId)
        {
            int low = 0;
            int high = pTable.Count;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                bool sortsBefore = CountyShortlistRules.SortsBefore(
                    pTable.Tiers[middle], pTable.Scores[middle],
                    pTable.Ids[middle], pTier, pScore, pId);
                if (sortsBefore) low = middle + 1;
                else high = middle;
            }

            return low;
        }
    }
}
