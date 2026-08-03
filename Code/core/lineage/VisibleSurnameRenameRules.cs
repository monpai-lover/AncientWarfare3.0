using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct SurnameRelationNode
    {
        public SurnameRelationNode(long actorId, bool male, long fatherId)
        {
            ActorId = actorId;
            Male = male;
            FatherId = fatherId;
        }

        public long ActorId { get; }
        public bool Male { get; }
        public long FatherId { get; }
    }

    public static class VisibleSurnameRenameRules
    {
        public const int MaxRenameActors = 16384;

        public static bool TryNormalizeFamilyName(string pRaw,
            out string pFamilyName)
        {
            pFamilyName = (pRaw ?? "").Trim();
            while (pFamilyName.EndsWith("姓", StringComparison.Ordinal) &&
                   pFamilyName.Length > 0)
                pFamilyName = pFamilyName.Substring(0,
                    pFamilyName.Length - 1).Trim();
            return !string.IsNullOrEmpty(pFamilyName);
        }

        public static IReadOnlyList<long> CollectPatrilinealRenameIds(
            long pRootActorId, IEnumerable<SurnameRelationNode> pNodes)
        {
            var result = new List<long>();
            if (pRootActorId < 0 || pNodes == null) return result;

            var nodes = new Dictionary<long, SurnameRelationNode>();
            var childrenByFather = new Dictionary<long, List<long>>();
            foreach (SurnameRelationNode node in pNodes)
            {
                if (node.ActorId < 0 || nodes.ContainsKey(node.ActorId))
                    continue;
                nodes[node.ActorId] = node;
                if (node.FatherId < 0) continue;
                if (!childrenByFather.TryGetValue(node.FatherId,
                        out List<long> children))
                {
                    children = new List<long>();
                    childrenByFather[node.FatherId] = children;
                }
                children.Add(node.ActorId);
            }

            if (!nodes.ContainsKey(pRootActorId)) return result;
            var visited = new HashSet<long>();
            var pending = new Queue<long>();
            pending.Enqueue(pRootActorId);
            while (pending.Count > 0 && result.Count < MaxRenameActors)
            {
                long actorId = pending.Dequeue();
                if (!visited.Add(actorId) || !nodes.TryGetValue(actorId,
                        out SurnameRelationNode actor)) continue;
                result.Add(actorId);
                if (!actor.Male || !childrenByFather.TryGetValue(actorId,
                        out List<long> children)) continue;
                children.Sort();
                for (int i = 0; i < children.Count; i++)
                    if (!visited.Contains(children[i])) pending.Enqueue(children[i]);
            }
            return result;
        }
    }
}
