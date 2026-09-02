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
    ///       出池  惰性剔除                取用时发现已死(只有死亡不可逆)
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
        ///     只摘不可逆条件。未成年、换国、暂时不够格都会变回来,摘掉就是把人弄丢
        ///     (见 <see cref="IsPermanentlyOut"/>)。
        /// </summary>
        private static List<Actor> Prune(Kingdom pKingdom, Entry pEntry)
        {
            List<Actor> stale = null;
            for (int index = 0; index < pEntry.Candidates.Count; index++)
            {
                Actor actor = pEntry.Candidates[index];
                if (IsPermanentlyOut(actor))
                    (stale ??= new List<Actor>()).Add(actor);
            }

            if (stale == null) return pEntry.Candidates;
            var next = new List<Actor>(pEntry.Candidates.Count - stale.Count);
            var nextIds = new HashSet<long>();
            for (int index = 0; index < pEntry.Candidates.Count; index++)
            {
                Actor actor = pEntry.Candidates[index];
                if (IsPermanentlyOut(actor)) continue;
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

        /// <summary>
        ///     只有死亡是不可逆的。
        ///
        ///     这里原来还把「换了国」当永久失效摘掉,但摘掉之后没人放得回去:
        ///     <see cref="Insert"/> 只在君主添新子嗣时调用,重建只在改朝换代或
        ///     那一次性的兜底修复时发生。宗亲联姻、流亡、随军出境再回来,在本朝
        ///     余下的年份里就永远不在池子里了 —— 而正统继承本来是**允许**候选人
        ///     不在本国的(HeirService.IsHeirBaseEligible 明确放开,登记时由
        ///     NormalizeHeirForRegistration 归化)。摘人等于跟那条规则对着干。
        ///
        ///     多留几个人无害:是否够格由各条继承法在取人时实时判定。
        /// </summary>
        private static bool IsPermanentlyOut(Actor pActor)
        {
            if (pActor?.data == null) return true;
            try
            {
                return !pActor.isAlive() || pActor.isRekt();
            }
            catch { return false; }
        }

        /// <summary>
        ///     宗室添了新子嗣:插进池子,而不是把整池丢掉重算。
        ///
        ///     顺位由取人时按有效继承法排定,所以这里只管「在集合里」。
        ///
        ///     **只收池中人的子女**(或参照君主的子女)。池子因此对"成员之子"封闭,
        ///     跟建池时那趟直系逐辈遍历是同一套语义 —— 王孙、王曾孙照样进得来,
        ///     而与王室无关的新生儿进不来。
        ///
        ///     不加这道闸的话:调用方(RefreshForNewRoyalChild)对**本国每一个**
        ///     男婴都调一次 Insert,而池子是按参照君主长期驻留的 —— 一朝下来
        ///     几百上千个平民男婴会一路堆进"继承池",而 FindHeir 每次刷新都要
        ///     对池中每人走一趟父系链求最近共同祖先(每步一次族谱查询)。他们最终
        ///     都会被 anc&lt;0 挡掉,白走。
        ///
        ///     池满则整池作废、下次重建:重建按直系优先重收,新生的嫡系不会被
        ///     挤在门外(直接追加则会,因为追加不看顺位)。
        /// </summary>
        internal static void Insert(Kingdom pKingdom, Actor pActor,
            long pParentIdA, long pParentIdB)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            if (!Entries.TryGetValue(pKingdom.id, out Entry entry)) return;
            long actorId = pActor.data.id;
            if (entry.Ids.Contains(actorId)) return;
            if (!IsPoolMember(entry, pParentIdA) &&
                !IsPoolMember(entry, pParentIdB)) return;
            if (entry.Candidates.Count >=
                InheritanceCandidateRules.MaximumLiveResolutions)
            {
                Entries.Remove(pKingdom.id);
                return;
            }

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

        /// <summary>
        ///     这个 id 算不算池子里的人。参照君主本人不在候选集合里(他不是自己的
        ///     继承人),但他的子女当然要进池,所以单独认一下。
        /// </summary>
        private static bool IsPoolMember(Entry pEntry, long pActorId)
        {
            return pActorId >= 0L &&
                   (pActorId == pEntry.ReferenceKingId ||
                    pEntry.Ids.Contains(pActorId));
        }

        /// <summary>补不出继承人时的兜底重建入口,以及改朝换代等结构性变动。</summary>
        internal static void Invalidate(Kingdom pKingdom)
        {
            if (pKingdom?.data != null) Entries.Remove(pKingdom.id);
        }

        internal static void ClearRuntime() => Entries.Clear();
    }
}
