using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    internal static class AWParallelIslandActorMembership
    {
        private static readonly Dictionary<int, long[]> ActorsByIsland =
            new Dictionary<int, long[]>();

        internal static void Rebuild(IReadOnlyList<AWSpatialActorSnapshot> pActors)
        {
            ActorsByIsland.Clear();
            if (pActors == null) return;
            Dictionary<int, List<long>> grouped = new Dictionary<int, List<long>>();
            for (int i = 0; i < pActors.Count; i++)
            {
                AWSpatialActorSnapshot actor = pActors[i];
                if (!actor.Alive || actor.IslandId < 0) continue;
                if (!grouped.TryGetValue(actor.IslandId, out List<long> ids))
                {
                    ids = new List<long>();
                    grouped.Add(actor.IslandId, ids);
                }
                ids.Add(actor.ActorId);
            }
            foreach (KeyValuePair<int, List<long>> pair in grouped)
                ActorsByIsland[pair.Key] = pair.Value.ToArray();
        }

        internal static int IslandCount => ActorsByIsland.Count;
        internal static void Clear() => ActorsByIsland.Clear();
    }
}
