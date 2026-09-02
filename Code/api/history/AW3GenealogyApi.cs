using System.Collections.Generic;
using AncientWarfare3.core.historyapi;

namespace AncientWarfare3.api.history
{
    public static class AW3GenealogyApi
    {
        public static IReadOnlyList<AW3GenealogyEntry> GetParents(long actorId)
        {
            return AW3HistoryReadService.ReadParents(actorId);
        }

        public static IReadOnlyList<AW3GenealogyEntry> GetChildren(long actorId)
        {
            return AW3HistoryReadService.ReadChildren(actorId);
        }

        public static IReadOnlyList<AW3GenealogyEntry> GetAncestors(
            long actorId, int maxDepth = 64)
        {
            return AW3HistoryReadService.ReadAncestors(actorId, maxDepth);
        }

        public static IReadOnlyList<AW3GenealogyEntry> GetFamilyTree(
            long actorId)
        {
            return AW3HistoryReadService.ReadFamilyTree(actorId);
        }
    }
}
