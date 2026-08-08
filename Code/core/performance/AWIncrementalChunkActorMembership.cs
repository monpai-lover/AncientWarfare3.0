using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    internal static class AWIncrementalChunkActorMembership
    {
        private static readonly Dictionary<int, long[]> ActorsByChunk =
            new Dictionary<int, long[]>();

        internal static void Rebuild(IReadOnlyList<AWSpatialActorSnapshot> pActors)
        {
            ActorsByChunk.Clear();
            if (pActors == null) return;
            Dictionary<int, List<long>> grouped = new Dictionary<int, List<long>>();
            for (int i = 0; i < pActors.Count; i++)
            {
                AWSpatialActorSnapshot actor = pActors[i];
                if (!actor.Alive || actor.ChunkId < 0) continue;
                if (!grouped.TryGetValue(actor.ChunkId, out List<long> ids))
                {
                    ids = new List<long>();
                    grouped.Add(actor.ChunkId, ids);
                }
                ids.Add(actor.ActorId);
            }
            foreach (KeyValuePair<int, List<long>> pair in grouped)
                ActorsByChunk[pair.Key] = pair.Value.ToArray();
        }

        internal static int ChunkCount => ActorsByChunk.Count;
        internal static void Clear() => ActorsByChunk.Clear();
    }
}
