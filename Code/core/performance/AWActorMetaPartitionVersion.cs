using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.performance
{
    internal static class AWActorMetaPartitionVersion
    {
        private static readonly object SyncRoot = new object();
        private static readonly HashSet<Actor> DirtyActors =
            new HashSet<Actor>();

        private static int _version;
        private static int _aliveManagerVersionBumps;

        internal static int Version => Volatile.Read(ref _version);

        internal static int GetStructuralVersion(int pManagerVersion)
        {
            return unchecked(pManagerVersion -
                Volatile.Read(ref _aliveManagerVersionBumps));
        }

        internal static int ConsumeDirtyActors(List<Actor> pTarget)
        {
            lock (SyncRoot)
            {
                pTarget.Clear();
                pTarget.AddRange(DirtyActors);
                DirtyActors.Clear();
                return _version;
            }
        }

        internal static void MarkAliveCall(
            Actor pActor,
            bool pPreviousAlive,
            bool pNextAlive)
        {
            if (!pNextAlive)
                Interlocked.Increment(ref _aliveManagerVersionBumps);

            if (pPreviousAlive == pNextAlive) return;
            MarkPartitionChange(pActor);
        }

        internal static void MarkKingdomChange(
            Actor pActor,
            Kingdom pNextKingdom)
        {
            Kingdom pPreviousKingdom = pActor.kingdom;
            if (!pActor.isAlive() ||
                ReferenceEquals(pPreviousKingdom, pNextKingdom))
                return;

            if (pPreviousKingdom == null ||
                pNextKingdom == null ||
                pPreviousKingdom.wild != pNextKingdom.wild)
                MarkPartitionChange(pActor);
        }

        internal static void Clear()
        {
            lock (SyncRoot)
            {
                DirtyActors.Clear();
                _version = 0;
                _aliveManagerVersionBumps = 0;
            }
        }

        private static void MarkPartitionChange(Actor pActor)
        {
            lock (SyncRoot)
            {
                DirtyActors.Add(pActor);
                unchecked
                {
                    _version++;
                }
            }
        }
    }
}
