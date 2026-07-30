using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct LineageDispositionCandidate
    {
        public LineageDispositionCandidate(long pActorId, long pFatherId,
            int pSex, bool pAlive, bool pMarried)
        {
            ActorId = pActorId;
            FatherId = pFatherId;
            Sex = pSex;
            Alive = pAlive;
            Married = pMarried;
        }

        public long ActorId { get; }
        public long FatherId { get; }
        public int Sex { get; }
        public bool Alive { get; }
        public bool Married { get; }
        public bool MarriedFemale => Sex != 0 && Married;
    }

    public static class LineageDispositionRules
    {
        public const int MaximumMigrants = 128;

        public static IReadOnlyList<long> SelectMigrants(
            IReadOnlyList<LineageDispositionCandidate> pCandidates,
            long pRootActorId, int pLimit)
        {
            int limit = pLimit <= 0
                ? MaximumMigrants
                : System.Math.Min(MaximumMigrants, pLimit);
            var byId = new Dictionary<long, LineageDispositionCandidate>();
            var children = new Dictionary<long, List<long>>();
            if (pCandidates != null)
            {
                for (int i = 0; i < pCandidates.Count; i++)
                {
                    LineageDispositionCandidate candidate = pCandidates[i];
                    if (candidate.ActorId < 0) continue;
                    byId[candidate.ActorId] = candidate;
                    if (candidate.FatherId < 0) continue;
                    if (!children.TryGetValue(candidate.FatherId,
                            out List<long> childIds))
                    {
                        childIds = new List<long>();
                        children[candidate.FatherId] = childIds;
                    }
                    childIds.Add(candidate.ActorId);
                }
            }

            var result = new List<long>(limit);
            if (!byId.ContainsKey(pRootActorId)) return result;
            var visited = new HashSet<long>();
            var queue = new Queue<long>();
            queue.Enqueue(pRootActorId);
            while (queue.Count > 0 && result.Count < limit)
            {
                long actorId = queue.Dequeue();
                if (!visited.Add(actorId) ||
                    !byId.TryGetValue(actorId,
                        out LineageDispositionCandidate candidate))
                    continue;
                if (candidate.MarriedFemale) continue;
                if (candidate.Alive) result.Add(actorId);
                if (!children.TryGetValue(actorId,
                        out List<long> childIds)) continue;
                childIds.Sort();
                for (int i = 0; i < childIds.Count; i++)
                    queue.Enqueue(childIds[i]);
            }
            result.Sort();
            return result;
        }
    }
}
