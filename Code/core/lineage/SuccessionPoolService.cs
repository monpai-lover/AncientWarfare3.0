using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     王位继承的候选池。**继承人就是池子第一席**(按有效继承法排完序之后)。
    ///
    ///     池子的内容是「和当朝君主有亲缘关系的人」,由
    ///     <c>InheritanceCandidateService.CollectRoyalCandidates</c> 那趟亲缘遍历
    ///     算出来:索引子女 → 兄弟支系 → 祖父支系 → 宗族/世系存档。那一趟不便宜,
    ///     而一次继承人刷新最多要跑三遍(正统/军功/文治各一次),外加派系支持
    ///     计算里还要再跑。实测 succession:reconcile_heir 单次到过 228ms。
    ///
    ///     现在**按「王国 + 参照君主」记一次**,之后靠事件维护:
    ///       入池  <see cref="Insert"/>   君主添了新子嗣
    ///       出池  惰性剔除                取用时发现已死/已不在本国
    ///       重建  参照君主一变(即改朝换代)键就不匹配,自动重算
    ///
    ///     池子只是**候选集合**,不是结论:是否够格、按哪条法排序,仍然由各条
    ///     继承法在取人时实时判定。所以多收了人无害(会被资格闸挡掉),少收了人
    ///     则由调用方在「一个都挑不出来」时重建一次兜底。
    ///
    ///     上限 32(<c>MaximumLiveResolutions</c>),所以取用时逐个校验的开销可以忽略。
    /// </summary>
    internal static class SuccessionPoolService
    {
        private sealed class Entry
        {
            internal long ReferenceKingId = -1L;
            internal List<Actor> Candidates = new List<Actor>();
            internal HashSet<long> Ids = new HashSet<long>();
        }

        private static readonly Dictionary<long, Entry> Entries =
            new Dictionary<long, Entry>();

        /// <summary>
        ///     取这个王国相对某位参照君主的继承候选池。缺失或参照君主变了才重算。
        /// </summary>
        internal static List<Actor> Get(Kingdom pKingdom,
            long pReferenceKingId, Func<List<Actor>> pBuild)
        {
            if (pKingdom?.data == null || pBuild == null)
                return pBuild?.Invoke() ?? new List<Actor>();
            if (Entries.TryGetValue(pKingdom.id, out Entry entry) &&
                entry.ReferenceKingId == pReferenceKingId)
                return Prune(pKingdom, entry);

            List<Actor> built = pBuild() ?? new List<Actor>();
            var ids = new HashSet<long>();
            for (int index = 0; index < built.Count; index++)
                if (built[index]?.data != null) ids.Add(built[index].data.id);
            Entries[pKingdom.id] = new Entry
            {
                ReferenceKingId = pReferenceKingId,
                Candidates = built,
                Ids = ids
            };
            return built;
        }

        /// <summary>
        ///     惰性出池:池子不再定期重建,所以**永久**失效的人必须在取用时摘掉,
        ///     否则死者会一直占着 32 个名额,把还活着的宗亲挤出去。
        ///
        ///     只摘不可逆条件。未成年、暂时不够格都会变回来,摘掉就是把人弄丢。
        /// </summary>
        private static List<Actor> Prune(Kingdom pKingdom, Entry pEntry)
        {
            List<Actor> stale = null;
            for (int index = 0; index < pEntry.Candidates.Count; index++)
            {
                Actor actor = pEntry.Candidates[index];
                if (IsPermanentlyOut(actor, pKingdom))
                    (stale ??= new List<Actor>()).Add(actor);
            }

            if (stale == null) return pEntry.Candidates;
            var next = new List<Actor>(pEntry.Candidates.Count - stale.Count);
            var nextIds = new HashSet<long>();
            for (int index = 0; index < pEntry.Candidates.Count; index++)
            {
                Actor actor = pEntry.Candidates[index];
                if (IsPermanentlyOut(actor, pKingdom)) continue;
                next.Add(actor);
                if (actor?.data != null) nextIds.Add(actor.data.id);
            }

            // 换表而不是就地删:取到名单的调用方可能正在遍历它。
            Entries[pKingdom.id] = new Entry
            {
                ReferenceKingId = pEntry.ReferenceKingId,
                Candidates = next,
                Ids = nextIds
            };
            return next;
        }

        private static bool IsPermanentlyOut(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null) return true;
            try
            {
                if (!pActor.isAlive() || pActor.isRekt()) return true;
                // 宗亲可能因联姻/流亡换国,换回来会由 Insert 或重建补上。
                return pActor.kingdom != pKingdom;
            }
            catch { return false; }
        }

        /// <summary>
        ///     君主添了新子嗣:插进池子,而不是把整池丢掉重算。
        ///
        ///     顺位由取人时按有效继承法排定,所以这里只管「在集合里」。
        /// </summary>
        internal static void Insert(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            if (!Entries.TryGetValue(pKingdom.id, out Entry entry)) return;
            long actorId = pActor.data.id;
            if (entry.Ids.Contains(actorId)) return;
            var next = new List<Actor>(entry.Candidates.Count + 1);
            next.AddRange(entry.Candidates);
            next.Add(pActor);
            Entries[pKingdom.id] = new Entry
            {
                ReferenceKingId = entry.ReferenceKingId,
                Candidates = next,
                Ids = new HashSet<long>(entry.Ids) { actorId }
            };
        }

        /// <summary>补不出继承人时的兜底重建入口,以及改朝换代等结构性变动。</summary>
        internal static void Invalidate(Kingdom pKingdom)
        {
            if (pKingdom?.data != null) Entries.Remove(pKingdom.id);
        }

        internal static void ClearRuntime() => Entries.Clear();
    }
}
