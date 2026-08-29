using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class SuccessionRelationshipIndex
    {
        private const int RebuildActorsPerCycle = 128;
        private static readonly SuccessionRelationshipIndexState State =
            new SuccessionRelationshipIndexState();
        private static Actor[] _rebuildActors;
        private static int _rebuildCount;
        private static int _rebuildCursor;

        internal static bool IsReady => State.IsReady;

        internal static void ProcessAuthorityCycle()
        {
            if (State.IsReady || !Config.game_loaded ||
                SmoothLoader.isLoading()) return;
            if (_rebuildActors == null && !BeginRebuild()) return;

            int end = Math.Min(_rebuildCount,
                _rebuildCursor + RebuildActorsPerCycle);
            for (; _rebuildCursor < end; _rebuildCursor++)
            {
                Actor actor = _rebuildActors[_rebuildCursor];
                if (actor?.data == null || !actor.isAlive() ||
                    actor.isRekt()) continue;
                State.Upsert(Capture(actor, null, null));
            }

            if (_rebuildCursor < _rebuildCount) return;
            _rebuildActors = null;
            _rebuildCount = 0;
            _rebuildCursor = 0;
            State.CompleteRebuild();
        }

        internal static void OnBorn(Actor pActor, Actor pParent1 = null,
            Actor pParent2 = null)
        {
            if (pActor?.data == null || !pActor.isAlive() ||
                pActor.isRekt()) return;
            State.Upsert(Capture(pActor, pParent1, pParent2));
        }

        internal static void OnDying(Actor pActor)
        {
            if (pActor?.data == null) return;
            State.Remove(pActor.data.id);
        }

        internal static IReadOnlyList<long> GetChildIds(long pParentId)
        {
            return State.IsReady
                ? State.ChildrenOf(pParentId)
                : Array.Empty<long>();
        }

        internal static IReadOnlyList<long> GetParentIds(long pActorId)
        {
            return State.IsReady
                ? State.ParentIds(pActorId)
                : Array.Empty<long>();
        }

        internal static long GetFatherId(long pActorId)
        {
            return State.IsReady && State.TryGetFather(pActorId,
                out long fatherId) ? fatherId : -1L;
        }

        /// <summary>
        /// 某个基准 actor 的父系祖先深度表(本人 = 0)。
        ///
        /// 一个基准要和一整池候选人逐个比亲缘时,这张表是不变的。原来
        /// NearestCommonAgnaticAncestor 每次调用都要新建一个 Dictionary 并把
        /// 基准的整条父系链(最多 96 层)重走一遍 —— HeirService.FindHeir 对候选池
        /// 里每个人都调一次、kingId 始终相同,等于把同一张表重建了 N 遍。实测
        /// succession:reconcile_heir 单次 60.9ms、占 annual_succession 的 99.8%。
        ///
        /// 由调用方持有、串行使用,所以第二段游走的去重集合也挂在这里复用,
        /// 不再每次分配。不是共享全局状态。
        /// </summary>
        internal sealed class AgnaticAncestorDepths
        {
            private readonly Dictionary<long, int> _depths =
                new Dictionary<long, int>();
            private readonly HashSet<long> _scratch = new HashSet<long>();

            internal long RootId { get; private set; } = -1L;
            internal bool IsUsable => RootId >= 0L;

            internal void Reset(long pRootId)
            {
                _depths.Clear();
                RootId = -1L;
                if (!State.IsReady || pRootId < 0L) return;
                RootId = pRootId;
                long current = pRootId;
                for (int depth = 0; depth <= 96 && current >= 0L; depth++)
                {
                    if (_depths.ContainsKey(current)) break;
                    _depths.Add(current, depth);
                    long father = GetFatherId(current);
                    if (father < 0L || father == current) break;
                    current = father;
                }
            }

            internal long NearestCommon(long pSecondId, out int pRootDepth,
                out int pSecondDepth)
            {
                pRootDepth = -1;
                pSecondDepth = -1;
                if (!IsUsable || pSecondId < 0L) return -1L;
                _scratch.Clear();
                long current = pSecondId;
                for (int depth = 0; depth <= 96 && current >= 0L; depth++)
                {
                    if (_depths.TryGetValue(current, out int rootDepth))
                    {
                        pRootDepth = rootDepth;
                        pSecondDepth = depth;
                        return current;
                    }
                    if (!_scratch.Add(current)) break;
                    long father = GetFatherId(current);
                    if (father < 0L || father == current) break;
                    current = father;
                }
                return -1L;
            }
        }

        /// <summary>
        /// 单次比较。基准侧的祖先表用完就丢 —— 循环里请改用
        /// <see cref="AgnaticAncestorDepths"/>,把基准表提到循环外面。
        /// </summary>
        internal static long NearestCommonAgnaticAncestor(long pFirstId,
            long pSecondId, out int pFirstDepth, out int pSecondDepth)
        {
            pFirstDepth = -1;
            pSecondDepth = -1;
            if (!State.IsReady || pFirstId < 0L || pSecondId < 0L)
                return -1L;
            var depths = new AgnaticAncestorDepths();
            depths.Reset(pFirstId);
            return depths.NearestCommon(pSecondId, out pFirstDepth,
                out pSecondDepth);
        }

        internal static bool IsAgnaticDescendant(long pActorId,
            long pLineageId)
        {
            if (!State.IsReady || pActorId < 0L || pLineageId < 0L)
                return false;
            var visited = new HashSet<long>();
            long current = pActorId;
            for (int depth = 0; depth <= 96 && current >= 0L; depth++)
            {
                if (!visited.Add(current) ||
                    !State.TryGetFacts(current,
                        out SuccessionActorFacts facts) ||
                    facts.LineageId != pLineageId)
                    return false;
                if (facts.FatherId < 0L) return true;
                current = facts.FatherId;
            }
            return false;
        }

        internal static IReadOnlyList<long> GetLivingLineageMemberIds(
            long pLineageId, int pLimit)
        {
            return Limit(State.IsReady
                ? State.LineageMembers(pLineageId)
                : Array.Empty<long>(), pLimit);
        }

        internal static bool HasLivingLineageMembers(long pLineageId)
        {
            return State.IsReady && pLineageId >= 0L &&
                   State.LineageMembers(pLineageId).Count > 0;
        }

        internal static IReadOnlyList<long> GetLivingShiMemberIds(
            long pShiId, int pLimit)
        {
            return Limit(State.IsReady
                ? State.ShiMembers(pShiId)
                : Array.Empty<long>(), pLimit);
        }

        internal static void Refresh(Actor pActor)
        {
            OnBorn(pActor);
        }

        internal static void Reset()
        {
            State.Clear();
            _rebuildActors = null;
            _rebuildCount = 0;
            _rebuildCursor = 0;
        }

        private static bool BeginRebuild()
        {
            ActorManager manager = World.world?.units;
            if (manager == null) return false;
            manager.checkContainer();
            manager.prepareArray();
            _rebuildActors = manager.getSimpleArray();
            _rebuildCount = manager.Count;
            _rebuildCursor = 0;
            State.BeginRebuild();
            return _rebuildActors != null;
        }

        private static SuccessionActorFacts Capture(Actor pActor,
            Actor pParent1, Actor pParent2)
        {
            long parent1Id = pActor.data.parent_id_1;
            long parent2Id = pActor.data.parent_id_2;
            Actor parent1 = ResolveParent(pParent1, parent1Id);
            Actor parent2 = ResolveParent(pParent2, parent2Id);
            long fatherId = ResolveFather(parent1, parent2);
            pActor.data.get(LineageKeys.LINEAGE_ID,
                out long lineageId, -1L);
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            return new SuccessionActorFacts(pActor.data.id, parent1Id,
                parent2Id, fatherId, lineageId, shiId,
                pActor.isAlive() && !pActor.isRekt());
        }

        private static Actor ResolveParent(Actor pKnown, long pActorId)
        {
            if (pKnown?.data != null && pKnown.data.id == pActorId)
                return pKnown;
            if (pActorId < 0L) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static long ResolveFather(Actor pParent1, Actor pParent2)
        {
            if (pParent1?.data != null && pParent1.isSexMale())
                return pParent1.data.id;
            if (pParent2?.data != null && pParent2.isSexMale())
                return pParent2.data.id;
            return -1L;
        }

        private static IReadOnlyList<long> Limit(
            IReadOnlyList<long> pIds, int pLimit)
        {
            if (pIds == null || pLimit <= 0) return Array.Empty<long>();
            if (pIds.Count <= pLimit) return pIds;
            var result = new long[pLimit];
            for (int i = 0; i < result.Length; i++) result[i] = pIds[i];
            return result;
        }
    }
}
