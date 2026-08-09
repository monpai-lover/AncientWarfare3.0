using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct SuccessionActorFacts
    {
        public SuccessionActorFacts(long pActorId, long pParent1Id,
            long pParent2Id, long pFatherId, long pLineageId, long pShiId,
            bool pAlive)
        {
            ActorId = pActorId;
            Parent1Id = pParent1Id;
            Parent2Id = pParent2Id;
            FatherId = pFatherId;
            LineageId = pLineageId;
            ShiId = pShiId;
            Alive = pAlive;
        }

        public long ActorId { get; }
        public long Parent1Id { get; }
        public long Parent2Id { get; }
        public long FatherId { get; }
        public long LineageId { get; }
        public long ShiId { get; }
        public bool Alive { get; }
    }

    public sealed class SuccessionRelationshipIndexState
    {
        private readonly Dictionary<long, SuccessionActorFacts> _facts =
            new Dictionary<long, SuccessionActorFacts>();
        private readonly Dictionary<long, HashSet<long>> _children =
            new Dictionary<long, HashSet<long>>();
        private readonly Dictionary<long, HashSet<long>> _lineages =
            new Dictionary<long, HashSet<long>>();
        private readonly Dictionary<long, HashSet<long>> _shi =
            new Dictionary<long, HashSet<long>>();

        public bool IsReady { get; private set; }

        public void BeginRebuild()
        {
            Clear();
        }

        public void CompleteRebuild()
        {
            IsReady = true;
        }

        public void Upsert(SuccessionActorFacts pFacts)
        {
            Remove(pFacts.ActorId);
            if (!pFacts.Alive || pFacts.ActorId < 0L) return;
            _facts[pFacts.ActorId] = pFacts;
            Add(_children, pFacts.Parent1Id, pFacts.ActorId);
            if (pFacts.Parent2Id != pFacts.Parent1Id)
                Add(_children, pFacts.Parent2Id, pFacts.ActorId);
            Add(_lineages, pFacts.LineageId, pFacts.ActorId);
            Add(_shi, pFacts.ShiId, pFacts.ActorId);
        }

        public void Remove(long pActorId)
        {
            if (!_facts.TryGetValue(pActorId,
                    out SuccessionActorFacts facts)) return;
            _facts.Remove(pActorId);
            Remove(_children, facts.Parent1Id, pActorId);
            if (facts.Parent2Id != facts.Parent1Id)
                Remove(_children, facts.Parent2Id, pActorId);
            Remove(_lineages, facts.LineageId, pActorId);
            Remove(_shi, facts.ShiId, pActorId);
        }

        public bool TryGetFather(long pActorId, out long pFatherId)
        {
            if (_facts.TryGetValue(pActorId,
                    out SuccessionActorFacts facts) && facts.FatherId >= 0L)
            {
                pFatherId = facts.FatherId;
                return true;
            }
            pFatherId = -1L;
            return false;
        }

        public bool TryGetFacts(long pActorId,
            out SuccessionActorFacts pFacts)
        {
            return _facts.TryGetValue(pActorId, out pFacts);
        }

        public IReadOnlyList<long> ParentIds(long pActorId)
        {
            if (!_facts.TryGetValue(pActorId,
                    out SuccessionActorFacts facts))
                return Array.Empty<long>();
            if (facts.Parent1Id < 0L && facts.Parent2Id < 0L)
                return Array.Empty<long>();
            if (facts.Parent2Id < 0L ||
                facts.Parent2Id == facts.Parent1Id)
                return new[] { facts.Parent1Id };
            if (facts.Parent1Id < 0L) return new[] { facts.Parent2Id };
            return facts.Parent1Id < facts.Parent2Id
                ? new[] { facts.Parent1Id, facts.Parent2Id }
                : new[] { facts.Parent2Id, facts.Parent1Id };
        }

        public IReadOnlyList<long> ChildrenOf(long pActorId)
        {
            return Read(_children, pActorId);
        }

        public IReadOnlyList<long> LineageMembers(long pLineageId)
        {
            return Read(_lineages, pLineageId);
        }

        public IReadOnlyList<long> ShiMembers(long pShiId)
        {
            return Read(_shi, pShiId);
        }

        public void Clear()
        {
            _facts.Clear();
            _children.Clear();
            _lineages.Clear();
            _shi.Clear();
            IsReady = false;
        }

        private static void Add(Dictionary<long, HashSet<long>> pIndex,
            long pKey, long pActorId)
        {
            if (pKey < 0L) return;
            if (!pIndex.TryGetValue(pKey, out HashSet<long> ids))
            {
                ids = new HashSet<long>();
                pIndex.Add(pKey, ids);
            }
            ids.Add(pActorId);
        }

        private static void Remove(Dictionary<long, HashSet<long>> pIndex,
            long pKey, long pActorId)
        {
            if (pKey < 0L || !pIndex.TryGetValue(pKey,
                    out HashSet<long> ids)) return;
            ids.Remove(pActorId);
            if (ids.Count == 0) pIndex.Remove(pKey);
        }

        private static IReadOnlyList<long> Read(
            Dictionary<long, HashSet<long>> pIndex, long pKey)
        {
            if (!pIndex.TryGetValue(pKey, out HashSet<long> ids))
                return Array.Empty<long>();
            var result = new long[ids.Count];
            ids.CopyTo(result);
            Array.Sort(result);
            return result;
        }
    }
}
