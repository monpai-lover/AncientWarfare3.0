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

    // An explicit surname edit has to decide three coupled values at once:
    // the family name, the clan name that trails it, and whether the two are
    // rendered as one merged token. Returning them together keeps the caller
    // from deriving each independently and drifting out of agreement.
    public readonly struct VisibleSurnameWritePlan
    {
        public VisibleSurnameWritePlan(string familyName, string clanName,
            bool nameIntegrated)
        {
            FamilyName = familyName ?? string.Empty;
            ClanName = clanName ?? string.Empty;
            NameIntegrated = nameIntegrated;
        }

        public string FamilyName { get; }
        public string ClanName { get; }
        public bool NameIntegrated { get; }
    }

    public static class VisibleSurnameRenameRules
    {
        public const int MaxRenameActors = 16384;

        // A rename that clears the surname must also drop the merged flag,
        // otherwise the display builder keeps rendering a separator for a
        // family name that no longer exists. An integrated identity keeps the
        // clan aligned with the family so the two cannot disagree after the
        // edit; a non-integrated one preserves the clan as recorded.
        public static VisibleSurnameWritePlan PlanSurnameWrite(
            string pRequestedFamilyName, string pCurrentClanName,
            bool pNameIntegrated)
        {
            if (!TryNormalizeFamilyName(pRequestedFamilyName,
                    out string family))
                return new VisibleSurnameWritePlan(string.Empty,
                    pNameIntegrated
                        ? string.Empty
                        : (pCurrentClanName ?? string.Empty).Trim(),
                    false);
            string clan = pNameIntegrated
                ? family
                : (pCurrentClanName ?? string.Empty).Trim();
            return new VisibleSurnameWritePlan(family, clan,
                pNameIntegrated);
        }

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
