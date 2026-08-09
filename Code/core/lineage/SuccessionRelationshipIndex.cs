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

        internal static long NearestCommonAgnaticAncestor(long pFirstId,
            long pSecondId, out int pFirstDepth, out int pSecondDepth)
        {
            pFirstDepth = -1;
            pSecondDepth = -1;
            if (!State.IsReady || pFirstId < 0L || pSecondId < 0L)
                return -1L;
            var firstDepths = new Dictionary<long, int>();
            long current = pFirstId;
            for (int depth = 0; depth <= 96 && current >= 0L; depth++)
            {
                if (firstDepths.ContainsKey(current)) break;
                firstDepths.Add(current, depth);
                long father = GetFatherId(current);
                if (father < 0L || father == current) break;
                current = father;
            }

            var visited = new HashSet<long>();
            current = pSecondId;
            for (int depth = 0; depth <= 96 && current >= 0L; depth++)
            {
                if (firstDepths.TryGetValue(current, out int firstDepth))
                {
                    pFirstDepth = firstDepth;
                    pSecondDepth = depth;
                    return current;
                }
                if (!visited.Add(current)) break;
                long father = GetFatherId(current);
                if (father < 0L || father == current) break;
                current = father;
            }
            return -1L;
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
