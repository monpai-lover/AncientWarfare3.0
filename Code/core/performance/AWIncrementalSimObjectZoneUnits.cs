using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    internal static class AWIncrementalSimObjectZoneUnits
    {
        private static readonly Dictionary<long, AWSpatialActorSnapshot> Committed =
            new Dictionary<long, AWSpatialActorSnapshot>();
        private static int _generation = -1;
        private static int _unitsVersion = -1;
        private static bool _ready;

        internal static bool IsCurrent(int pGeneration, int pUnitsVersion)
        {
            return _ready && _generation == pGeneration &&
                   _unitsVersion == pUnitsVersion;
        }

        internal static void Commit(IReadOnlyList<AWSpatialActorSnapshot> pActors,
            int pGeneration, int pUnitsVersion)
        {
            Committed.Clear();
            if (pActors != null)
                for (int i = 0; i < pActors.Count; i++)
                    Committed[pActors[i].ActorId] = pActors[i];
            _generation = pGeneration;
            _unitsVersion = pUnitsVersion;
            _ready = true;
        }

        internal static void Invalidate()
        {
            Committed.Clear();
            _generation = -1;
            _unitsVersion = -1;
            _ready = false;
            AWIncrementalChunkActorMembership.Clear();
            AWParallelIslandActorMembership.Clear();
        }

        internal static int Count => Committed.Count;
    }
}
