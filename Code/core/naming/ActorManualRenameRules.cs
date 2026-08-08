using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.naming
{
    internal sealed class ActorManualBranchPlan
    {
        internal ActorManualBranchPlan(IReadOnlyList<long> pActorIds,
            bool pRequiresBranchFork)
        {
            ActorIds = pActorIds;
            RequiresBranchFork = pRequiresBranchFork;
        }

        internal IReadOnlyList<long> ActorIds { get; }
        internal bool RequiresBranchFork { get; }
    }

    internal static class ActorManualRenameRules
    {
        internal static ActorManualBranchPlan PlanBranchChange(long pRootId,
            string pCurrentFamily, string pRequestedFamily,
            IEnumerable<long> pPatrilinealIds)
        {
            string current = Normalize(pCurrentFamily);
            string requested = Normalize(pRequestedFamily);
            var result = new List<long>();
            var seen = new HashSet<long>();
            if (pPatrilinealIds != null)
            {
                foreach (long id in pPatrilinealIds)
                {
                    if (id < 0 || !seen.Add(id)) continue;
                    result.Add(id);
                }
            }
            if (pRootId >= 0 && seen.Add(pRootId))
                result.Insert(0, pRootId);
            return new ActorManualBranchPlan(result,
                !string.Equals(current, requested,
                    StringComparison.Ordinal));
        }

        private static string Normalize(string pValue)
        {
            return string.Join(" ", (pValue ?? string.Empty)
                .Trim()
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
