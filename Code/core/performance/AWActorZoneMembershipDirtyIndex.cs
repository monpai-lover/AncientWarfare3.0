using System;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.performance
{
    [Flags]
    internal enum AWActorZoneDirtyKind : byte
    {
        None = 0,
        Spatial = 1,
        ChunkMetadata = 2,
        CityEligibility = 4
    }

    internal readonly struct AWActorZoneDirtyEntry
    {
        internal AWActorZoneDirtyEntry(Actor pActor, AWActorZoneDirtyKind pKind)
        {
            Actor = pActor;
            Kind = pKind;
        }

        internal Actor Actor { get; }
        internal AWActorZoneDirtyKind Kind { get; }
    }

    internal static class AWActorZoneMembershipDirtyIndex
    {
        private static readonly ThreadLocal<Dictionary<Actor, AWActorZoneDirtyKind>>
            DirtyByThread = new ThreadLocal<Dictionary<Actor, AWActorZoneDirtyKind>>(
                () => new Dictionary<Actor, AWActorZoneDirtyKind>(), true);
        private static readonly Dictionary<Actor, AWActorZoneDirtyKind> Merged =
            new Dictionary<Actor, AWActorZoneDirtyKind>();

        internal static void Mark(Actor pActor, AWActorZoneDirtyKind pKind)
        {
            if (pActor == null || pKind == AWActorZoneDirtyKind.None ||
                !AWPerformanceSettings.EnableFramePriorityScheduler) return;
            Dictionary<Actor, AWActorZoneDirtyKind> bucket = DirtyByThread.Value;
            if (bucket.TryGetValue(pActor, out AWActorZoneDirtyKind previous))
                bucket[pActor] = previous | pKind;
            else
                bucket.Add(pActor, pKind);
        }

        internal static int Consume(List<AWActorZoneDirtyEntry> pTarget)
        {
            pTarget.Clear();
            Merged.Clear();
            foreach (Dictionary<Actor, AWActorZoneDirtyKind> bucket in DirtyByThread.Values)
            {
                foreach (KeyValuePair<Actor, AWActorZoneDirtyKind> pair in bucket)
                {
                    if (Merged.TryGetValue(pair.Key, out AWActorZoneDirtyKind previous))
                        Merged[pair.Key] = previous | pair.Value;
                    else
                        Merged.Add(pair.Key, pair.Value);
                }
                bucket.Clear();
            }
            foreach (KeyValuePair<Actor, AWActorZoneDirtyKind> pair in Merged)
                pTarget.Add(new AWActorZoneDirtyEntry(pair.Key, pair.Value));
            Merged.Clear();
            return pTarget.Count;
        }

        internal static bool HasPending()
        {
            foreach (Dictionary<Actor, AWActorZoneDirtyKind> bucket in
                     DirtyByThread.Values)
            {
                if (bucket.Count != 0)
                {
                    return true;
                }
            }

            return Merged.Count != 0;
        }

        internal static void Clear()
        {
            Merged.Clear();
            foreach (Dictionary<Actor, AWActorZoneDirtyKind> bucket in DirtyByThread.Values)
                bucket.Clear();
        }
    }
}
