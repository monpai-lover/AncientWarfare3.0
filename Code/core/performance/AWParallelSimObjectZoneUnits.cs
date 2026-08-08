using System;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.performance
{
    internal readonly struct AWSpatialActorSnapshot
    {
        internal AWSpatialActorSnapshot(long pActorId, int pChunkId,
            int pIslandId, bool pAlive)
        {
            ActorId = pActorId;
            ChunkId = pChunkId;
            IslandId = pIslandId;
            Alive = pAlive;
        }

        internal long ActorId { get; }
        internal int ChunkId { get; }
        internal int IslandId { get; }
        internal bool Alive { get; }
    }

    internal static class AWParallelSimObjectZoneUnits
    {
        private static readonly List<AWSpatialActorSnapshot> Captured =
            new List<AWSpatialActorSnapshot>(256);
        private static readonly List<AWActorZoneDirtyEntry> Dirty =
            new List<AWActorZoneDirtyEntry>(64);
        private static int _lastGeneration = -1;
        private static int _lastUnitsVersion = -1;
        private static int _lastDirtyCount;
        private static long _rebuilds;
        private static long _skippedRedundant;

        internal static bool TrySkipRedundantCheckUnits()
        {
            if (!AWPerformanceSettings.EnableFramePriorityScheduler ||
                World.world?.units == null) return false;
            int generation = (int)AWSimulationTime.Generation;
            int version = World.world.units.version;
            if (!AWIncrementalSimObjectZoneUnits.IsCurrent(generation, version))
                return false;
            _lastDirtyCount = AWActorZoneMembershipDirtyIndex.Consume(Dirty);
            if (_lastDirtyCount != 0)
                return false;
            Interlocked.Increment(ref _skippedRedundant);
            return true;
        }

        internal static void NotifyUnitMembershipRebuilt()
        {
            if (!AWPerformanceSettings.EnableFramePriorityScheduler)
                return;
            Actor[] actors = World.world?.units?.getSimpleArray() ?? Array.Empty<Actor>();
            int count = World.world?.units?.Count ?? 0;
            Captured.Clear();
            for (int i = 0; i < count && i < actors.Length; i++)
            {
                Actor actor = actors[i];
                if (actor?.data == null) continue;
                WorldTile tile = actor.current_tile;
                Captured.Add(new AWSpatialActorSnapshot(actor.data.id,
                    tile?.chunk?.id ?? -1,
                    tile?.region?.island?.id ?? -1,
                    actor.isAlive() && !actor.isRekt()));
            }

            AWIncrementalChunkActorMembership.Rebuild(Captured);
            AWParallelIslandActorMembership.Rebuild(Captured);
            AWIncrementalSimObjectZoneUnits.Commit(Captured,
                (int)AWSimulationTime.Generation,
                World.world?.units?.version ?? -1);
            Interlocked.Increment(ref _rebuilds);
            _lastGeneration = (int)AWSimulationTime.Generation;
            _lastUnitsVersion = World.world?.units?.version ?? -1;
        }

        internal static void Invalidate()
        {
            Captured.Clear();
            Dirty.Clear();
            AWActorZoneMembershipDirtyIndex.Clear();
            AWIncrementalSimObjectZoneUnits.Invalidate();
            _lastGeneration = -1;
            _lastUnitsVersion = -1;
        }

        internal static string GetDiagnostics()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "rebuilds={0} skipped={1} actors={2} chunks={3} islands={4} " +
                "generation={5} units_version={6} dirty={7}",
                Interlocked.Read(ref _rebuilds),
                Interlocked.Read(ref _skippedRedundant),
                AWIncrementalSimObjectZoneUnits.Count,
                AWIncrementalChunkActorMembership.ChunkCount,
                AWParallelIslandActorMembership.IslandCount,
                _lastGeneration, _lastUnitsVersion, _lastDirtyCount);
        }
    }
}
