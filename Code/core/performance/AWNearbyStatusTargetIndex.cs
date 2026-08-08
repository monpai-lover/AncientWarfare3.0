using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace AncientWarfare3.core.performance;

/// <summary>
/// ??? chunk ????????????????
/// ???????????????????????????????
/// ???? chunk ???????
/// </summary>
internal static class AWNearbyStatusTargetIndex
{
    private const int SparseMembershipScanThreshold = 64;
    private const int InitialDenseActorHashCapacity = 4096;
    private const int MaximumDenseActorHashCapacity =
        4 * 1024 * 1024;

    private static readonly Dictionary<MapChunk, List<IndexedActor>>
        ActorsByChunk = new();
    private static readonly Stack<List<IndexedActor>>
        ActorListPool = new();
    private static readonly HashSet<Actor> IndexedActors = new();
    private static readonly Dictionary<Actor, MapChunk>
        IndexedActorChunks = new();
    private static readonly HashSet<string> TrackedStatusIds =
        new(StringComparer.Ordinal);
    private static readonly HashSet<string> GlobalStatusIds =
        new(StringComparer.Ordinal);
    private static bool[] indexedActorHashFlags =
        Array.Empty<bool>();

    private static int indexedGeneration = -1;
    private static int indexedUnitMembershipVersion = -1;
    private static bool indexAvailable;
    private static bool fusedRebuildInProgress;
    private static long fusedRebuildStartedAt;
    private static int fusedRebuildActorEntries;

    private static long queries;
    private static long handledQueries;
    private static long fastNegativeQueries;
    private static long foundQueries;
    private static long fallbackQueries;
    private static long rebuilds;
    private static long queryRebuilds;
    private static long fusedRebuilds;
    private static long totalBuildTicks;
    private static long fusedBuildTicks;
    private static long maximumBuildTicks;
    private static long lastBuildTicks;
    private static long statusChecks;
    private static long unitChecks;
    private static long incrementalAdds;
    private static long incrementalRemoves;
    private static long incrementalChunkRebuilds;
    private static long incrementalChunkScans;
    private static long incrementalChunkBuildTicks;
    private static int indexedChunkCount;
    private static int indexedActorEntryCount;

    /// <summary>
    /// ?? true ??????????????result ?????
    /// ?? false ???????????????????????????
    /// </summary>
    internal static bool TryFindClosest(
        Actor actor,
        string[] statusIds,
        out Actor result)
    {
        result = null;
        Interlocked.Increment(ref queries);
        if (actor?.current_tile?.chunk == null ||
            statusIds == null ||
            statusIds.Length == 0 ||
            World.world?.map_chunk_manager == null)
        {
            Interlocked.Increment(ref fallbackQueries);
            return false;
        }

        RegisterTrackedStatusIds(statusIds);
        EnsureBuilt();

        bool existsGlobally = false;
        for (int i = 0; i < statusIds.Length; i++)
        {
            if (GlobalStatusIds.Contains(statusIds[i]))
            {
                existsGlobally = true;
                break;
            }
        }

        result = FindClosest(
            actor,
            statusIds,
            existsGlobally);
        Interlocked.Increment(ref handledQueries);
        if (result == null)
        {
            Interlocked.Increment(ref fastNegativeQueries);
        }
        else
        {
            Interlocked.Increment(ref foundQueries);
        }

        return true;
    }

    /// <summary>
    /// ?????????????????? tick ??????
    /// ???????? chunk ??????????????????
    /// ??????? current_tile.chunk?
    /// </summary>
    internal static void NotifyStatusAdded(
        BaseSimObject simObject,
        StatusAsset statusAsset)
    {
        if (simObject is not Actor actor ||
            statusAsset == null ||
            !TrackedStatusIds.Contains(statusAsset.id) ||
            !actor.isAlive())
        {
            return;
        }

        bool newlyIndexed = AddIndexedActor(actor);
        GlobalStatusIds.Add(statusAsset.id);
        if (!newlyIndexed ||
            !IsCurrentIndex())
        {
            return;
        }

        long checkedUnits = 0L;
        if (TryAddCurrentMembership(
                World.world,
                actor,
                ref checkedUnits))
        {
            Interlocked.Increment(ref incrementalAdds);
            UpdateIndexedEntryDiagnostics();
        }

        Interlocked.Add(ref unitChecks, checkedUnits);
    }

    internal static void NotifyStatusRemoved(
        BaseSimObject simObject,
        StatusAsset statusAsset)
    {
        if (simObject is not Actor actor ||
            statusAsset == null ||
            !TrackedStatusIds.Contains(statusAsset.id))
        {
            return;
        }

        if (!HasAnyTrackedStatus(actor))
        {
            if (RemoveIndexedActor(actor) &&
                IsCurrentIndex() &&
                RemoveActorMembership(actor))
            {
                Interlocked.Increment(
                    ref incrementalRemoves);
            }
        }
    }

    internal static void NotifyAllStatusesRemoved(
        BaseSimObject simObject)
    {
        if (simObject is not Actor actor ||
            !RemoveIndexedActor(actor))
        {
            return;
        }

        if (IsCurrentIndex() &&
            RemoveActorMembership(actor))
        {
            Interlocked.Increment(ref incrementalRemoves);
        }
    }

    internal static string GetDiagnostics()
    {
        long buildCount = Interlocked.Read(ref rebuilds);
        return string.Format(
            CultureInfo.InvariantCulture,
            "queries={0} handled={1} fast_negative={2} found={3} " +
            "fallback={4} rebuilds={5} chunks={6} " +
            "(query={16},fused={17}) " +
            "actor_entries={7} tracked_actors={13} " +
            "status_checks={8} unit_checks={9} " +
            "incremental={14}/{15}(add/remove) " +
            "build={10:0.000}ms(avg={11:0.000},max={12:0.000}) " +
            "fused_avg={18:0.000}ms " +
            "chunk_incremental={19}/{20} rebuild/scan " +
            "avg={21:0.000}ms",
            Interlocked.Read(ref queries),
            Interlocked.Read(ref handledQueries),
            Interlocked.Read(ref fastNegativeQueries),
            Interlocked.Read(ref foundQueries),
            Interlocked.Read(ref fallbackQueries),
            buildCount,
            Volatile.Read(ref indexedChunkCount),
            Volatile.Read(ref indexedActorEntryCount),
            Interlocked.Read(ref statusChecks),
            Interlocked.Read(ref unitChecks),
            TicksToMilliseconds(Interlocked.Read(ref lastBuildTicks)),
            buildCount == 0L
                ? 0.0
                : TicksToMilliseconds(
                    Interlocked.Read(ref totalBuildTicks)) / buildCount,
            TicksToMilliseconds(
                Interlocked.Read(ref maximumBuildTicks)),
            IndexedActors.Count,
            Interlocked.Read(ref incrementalAdds),
            Interlocked.Read(ref incrementalRemoves),
            Interlocked.Read(ref queryRebuilds),
            Interlocked.Read(ref fusedRebuilds),
            Interlocked.Read(ref fusedRebuilds) == 0L
                ? 0.0
                : TicksToMilliseconds(
                    Interlocked.Read(ref fusedBuildTicks)) /
                  Interlocked.Read(ref fusedRebuilds),
            Interlocked.Read(ref incrementalChunkRebuilds),
            Interlocked.Read(ref incrementalChunkScans),
            Interlocked.Read(ref incrementalChunkRebuilds) == 0L
                ? 0.0
                : TicksToMilliseconds(
                    Interlocked.Read(
                        ref incrementalChunkBuildTicks)) /
                  Interlocked.Read(
                      ref incrementalChunkRebuilds));
    }

    internal static void Reset()
    {
        RecycleActorLists();
        IndexedActors.Clear();
        if (indexedActorHashFlags.Length > 0)
        {
            Array.Clear(
                indexedActorHashFlags,
                0,
                indexedActorHashFlags.Length);
        }

        TrackedStatusIds.Clear();
        GlobalStatusIds.Clear();
        indexedGeneration = -1;
        indexedUnitMembershipVersion = -1;
        indexAvailable = false;
        fusedRebuildInProgress = false;
        fusedRebuildStartedAt = 0L;
        fusedRebuildActorEntries = 0;
        Volatile.Write(ref indexedChunkCount, 0);
        Volatile.Write(ref indexedActorEntryCount, 0);
    }

    /// <summary>
    /// ? SimObjectsZones ????????????????
    /// ??????????????????????????
    /// </summary>
    internal static void BeginUnitMembershipRebuild()
    {
        long startedAt = Stopwatch.GetTimestamp();
        RecycleActorLists();
        // ??????????? IndexedActors???????????
        // ????????????? World.units ???????
        // O(???? ? ????)?GlobalStatusIds ????????
        // ?????????????????????????????
        // HasRequestedStatus ???????????????????????
        fusedRebuildActorEntries = 0;
        fusedRebuildStartedAt = startedAt;
        fusedRebuildInProgress = true;
        indexAvailable = false;
    }

    /// <summary>
    /// actor ??? World.units ???? chunk ???????????
    /// ?? Finder ??? chunk ??????
    /// </summary>
    internal static void AddUnitMembership(
        Actor actor,
        MapChunk chunk,
        int unitIndex)
    {
        if (!fusedRebuildInProgress ||
            !actor.hasAnyStatusEffect() ||
            !IsIndexedActor(actor))
        {
            return;
        }

        if (!ActorsByChunk.TryGetValue(
                chunk,
                out List<IndexedActor> candidates))
        {
            candidates = RentActorList();
            ActorsByChunk.Add(chunk, candidates);
        }

        candidates.Add(
            new IndexedActor(actor, unitIndex));
        IndexedActorChunks.Add(actor, chunk);
        fusedRebuildActorEntries++;
    }

    /// <summary>
    /// ? SimObjectsZones ? chunk worker ???????
    /// ???????????????????? worker ??????????
    /// </summary>
    internal static bool ShouldAddUnitMembership(
        Actor actor)
    {
        return fusedRebuildInProgress &&
               actor.hasAnyStatusEffect() &&
               IsIndexedActor(actor);
    }

    internal static void NotifyUnitMembershipRebuilt(
        int membershipVersion,
        bool fusedIndexPrepared)
    {
        if (!fusedIndexPrepared ||
            !fusedRebuildInProgress)
        {
            indexAvailable = false;
            fusedRebuildInProgress = false;
            fusedRebuildStartedAt = 0L;
            fusedRebuildActorEntries = 0;
            return;
        }

        indexedGeneration = AWSimulationTime.Generation;
        indexedUnitMembershipVersion = membershipVersion;
        indexAvailable = true;
        fusedRebuildInProgress = false;
        Volatile.Write(
            ref indexedChunkCount,
            ActorsByChunk.Count);
        Volatile.Write(
            ref indexedActorEntryCount,
            fusedRebuildActorEntries);
        long elapsedTicks =
            Stopwatch.GetTimestamp() -
            fusedRebuildStartedAt;
        fusedRebuildStartedAt = 0L;
        fusedRebuildActorEntries = 0;
        Interlocked.Increment(ref fusedRebuilds);
        Interlocked.Add(ref fusedBuildTicks, elapsedTicks);
        RecordBuildDuration(elapsedTicks);
    }

    /// <summary>
    /// chunk ???????????????????????????
    /// ???? units_all ???????????????????
    /// </summary>
    internal static bool TryApplyChunkMembershipChanges(
        int previousMembershipVersion,
        int nextMembershipVersion,
        IReadOnlyList<int> dirtyChunkIndices,
        MapChunk[] chunks)
    {
        if (!indexAvailable ||
            fusedRebuildInProgress ||
            indexedGeneration !=
            AWSimulationTime.Generation ||
            indexedUnitMembershipVersion !=
            previousMembershipVersion)
        {
            return false;
        }

        long startedAt = Stopwatch.GetTimestamp();
        for (int i = 0;
             i < dirtyChunkIndices.Count;
             i++)
        {
            MapChunk chunk =
                chunks[dirtyChunkIndices[i]];
            if (!ActorsByChunk.TryGetValue(
                    chunk,
                    out List<IndexedActor> candidates))
            {
                continue;
            }

            for (int candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                IndexedActorChunks.Remove(
                    candidates[candidateIndex].Actor);
            }

            ActorsByChunk.Remove(chunk);
            candidates.Clear();
            ActorListPool.Push(candidates);
        }

        long scannedUnits = 0L;
        for (int i = 0;
             i < dirtyChunkIndices.Count;
             i++)
        {
            MapChunk chunk =
                chunks[dirtyChunkIndices[i]];
            List<Actor> units =
                chunk.objects.units_all;
            scannedUnits += units.Count;
            List<IndexedActor> candidates = null;
            for (int unitIndex = 0;
                 unitIndex < units.Count;
                 unitIndex++)
            {
                Actor actor = units[unitIndex];
                if (!actor.hasAnyStatusEffect() ||
                    !IsIndexedActor(actor))
                {
                    continue;
                }

                candidates ??= RentActorList();
                candidates.Add(
                    new IndexedActor(
                        actor,
                        unitIndex));
                IndexedActorChunks.Add(
                    actor,
                    chunk);
            }

            if (candidates != null)
            {
                ActorsByChunk.Add(
                    chunk,
                    candidates);
            }
        }

        indexedUnitMembershipVersion =
            nextMembershipVersion;
        Volatile.Write(
            ref indexedChunkCount,
            ActorsByChunk.Count);
        Volatile.Write(
            ref indexedActorEntryCount,
            IndexedActorChunks.Count);
        Interlocked.Increment(
            ref incrementalChunkRebuilds);
        Interlocked.Add(
            ref incrementalChunkScans,
            scannedUnits);
        Interlocked.Add(
            ref incrementalChunkBuildTicks,
            Stopwatch.GetTimestamp() - startedAt);
        return true;
    }

    internal static void AbortUnitMembershipRebuild()
    {
        if (fusedRebuildInProgress)
        {
            RecycleActorLists();
        }

        fusedRebuildInProgress = false;
        fusedRebuildStartedAt = 0L;
        fusedRebuildActorEntries = 0;
        indexAvailable = false;
    }

    private static Actor FindClosest(
        Actor self,
        string[] statusIds,
        bool existsGlobally)
    {
        bool randomizeUnits = Randy.randomBool();
        MapChunk[] chunks =
            AWChunkWindowIndex.Get(self.current_tile.chunk, 1);
        int chunkCount = chunks.Length;
        int chunkOffset = Randy.randomInt(0, chunkCount);
        int closestDistanceSquared = int.MaxValue;
        Actor closest = null;

        for (int i = 0; i < chunkCount; i++)
        {
            MapChunk chunk =
                chunks[(i + chunkOffset) % chunkCount];
            List<Actor> units = chunk.objects.units_all;
            int unitOffset = randomizeUnits
                ? Randy.randomInt(0, units.Count)
                : 0;
            if (!existsGlobally ||
                !ActorsByChunk.TryGetValue(
                    chunk,
                    out List<IndexedActor> candidates))
            {
                continue;
            }

            int candidateStart = randomizeUnits
                ? LowerBound(candidates, unitOffset)
                : 0;
            int candidateCount = candidates.Count;
            if (candidateStart == candidateCount)
            {
                candidateStart = 0;
            }

            for (int j = 0; j < candidateCount; j++)
            {
                int candidateIndex = randomizeUnits
                    ? (candidateStart + j) % candidateCount
                    : j;
                Actor target = candidates[candidateIndex].Actor;
                if (!target.isAlive() ||
                    target == self)
                {
                    continue;
                }

                int distanceSquared = Toolbox.SquaredDistTile(
                    target.current_tile,
                    self.current_tile);
                if (distanceSquared >= closestDistanceSquared ||
                    !self.isSameIslandAs(target) ||
                    !target.hasAnyStatusEffect() ||
                    !HasRequestedStatus(target, statusIds))
                {
                    continue;
                }

                closestDistanceSquared = distanceSquared;
                closest = target;
                if (randomizeUnits || Randy.randomBool())
                {
                    return closest;
                }
            }
        }

        return closest;
    }

    private static bool HasRequestedStatus(
        Actor actor,
        string[] statusIds)
    {
        for (int i = 0; i < statusIds.Length; i++)
        {
            if (actor.hasStatus(statusIds[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static int LowerBound(
        List<IndexedActor> candidates,
        int unitOffset)
    {
        int low = 0;
        int high = candidates.Count;
        while (low < high)
        {
            int middle = low + ((high - low) >> 1);
            if (candidates[middle].UnitIndex < unitOffset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static void EnsureBuilt()
    {
        if (IsCurrentIndex())
        {
            return;
        }

        long startedAt = Stopwatch.GetTimestamp();
        RecycleActorLists();
        GlobalStatusIds.Clear();

        MapBox world = World.world;
        long checkedUnits = 0L;
        int actorEntries = 0;
        RemoveInvalidTrackedActors();
        RefreshGlobalStatusIds();
        if (IndexedActors.Count > 0 &&
            world.map_chunk_manager != null)
        {
            if (IndexedActors.Count <=
                SparseMembershipScanThreshold)
            {
                actorEntries = BuildSparseMembership(
                    world,
                    ref checkedUnits);
            }
            else
            {
                actorEntries = BuildDenseMembership(
                    world,
                    ref checkedUnits);
            }
        }

        indexedGeneration = AWSimulationTime.Generation;
        indexedUnitMembershipVersion =
            AWParallelSimObjectZoneUnits
                .UnitMembershipVersion;
        indexAvailable = true;
        Interlocked.Add(ref unitChecks, checkedUnits);
        Volatile.Write(
            ref indexedChunkCount,
            ActorsByChunk.Count);
        Volatile.Write(
            ref indexedActorEntryCount,
            actorEntries);
        Interlocked.Increment(ref queryRebuilds);
        RecordBuildDuration(
            Stopwatch.GetTimestamp() - startedAt);
    }

    private static int BuildSparseMembership(
        MapBox world,
        ref long checkedUnits)
    {
        int actorEntries = 0;
        foreach (Actor actor in IndexedActors)
        {
            if (TryAddCurrentMembership(
                    world,
                    actor,
                    ref checkedUnits))
            {
                actorEntries++;
            }
        }

        return actorEntries;
    }

    private static int BuildDenseMembership(
        MapBox world,
        ref long checkedUnits)
    {
        int actorEntries = 0;
        MapChunk[] chunks =
            world.map_chunk_manager.chunks;
        for (int chunkIndex = 0;
             chunkIndex < chunks.Length;
             chunkIndex++)
        {
            MapChunk chunk = chunks[chunkIndex];
            List<Actor> units = chunk.objects.units_all;
            int count = units.Count;
            checkedUnits += count;
            List<IndexedActor> candidates = null;
            for (int unitIndex = 0;
                 unitIndex < count;
                 unitIndex++)
            {
                Actor actor = units[unitIndex];
                if (!IndexedActors.Contains(actor))
                {
                    continue;
                }

                candidates ??= RentActorList();
                candidates.Add(
                    new IndexedActor(actor, unitIndex));
                IndexedActorChunks.Add(actor, chunk);
                actorEntries++;
            }

            if (candidates != null)
            {
                ActorsByChunk.Add(chunk, candidates);
            }
        }

        return actorEntries;
    }

    private static bool TryAddCurrentMembership(
        MapBox world,
        Actor actor,
        ref long checkedUnits)
    {
        MapChunk origin = actor.current_tile?.chunk;
        if (origin != null)
        {
            if (TryAddActorMembership(
                    actor,
                    origin,
                    ref checkedUnits))
            {
                return true;
            }

            MapChunk[] nearby =
                AWChunkWindowIndex.Get(origin, 1);
            for (int i = 0; i < nearby.Length; i++)
            {
                if (ReferenceEquals(nearby[i], origin))
                {
                    continue;
                }

                if (TryAddActorMembership(
                        actor,
                        nearby[i],
                        ref checkedUnits))
                {
                    return true;
                }
            }
        }

        // ??? chunk ???????????????????
        // ????????????????????????
        MapChunk[] allChunks =
            world.map_chunk_manager.chunks;
        for (int i = 0; i < allChunks.Length; i++)
        {
            if (TryAddActorMembership(
                    actor,
                    allChunks[i],
                    ref checkedUnits))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryAddActorMembership(
        Actor actor,
        MapChunk chunk,
        ref long checkedUnits)
    {
        List<Actor> units = chunk.objects.units_all;
        int count = units.Count;
        checkedUnits += count;
        for (int unitIndex = 0;
             unitIndex < count;
             unitIndex++)
        {
            if (!ReferenceEquals(units[unitIndex], actor))
            {
                continue;
            }

            if (!ActorsByChunk.TryGetValue(
                    chunk,
                    out List<IndexedActor> candidates))
            {
                candidates = RentActorList();
                ActorsByChunk.Add(chunk, candidates);
            }

            int insertIndex =
                LowerBound(candidates, unitIndex);
            candidates.Insert(
                insertIndex,
                new IndexedActor(actor, unitIndex));
            IndexedActorChunks.Add(actor, chunk);
            return true;
        }

        return false;
    }

    private static bool RemoveActorMembership(Actor actor)
    {
        if (!IndexedActorChunks.TryGetValue(
                actor,
                out MapChunk chunk) ||
            !ActorsByChunk.TryGetValue(
                chunk,
                out List<IndexedActor> candidates))
        {
            return false;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (!ReferenceEquals(
                    candidates[i].Actor,
                    actor))
            {
                continue;
            }

            candidates.RemoveAt(i);
            IndexedActorChunks.Remove(actor);
            if (candidates.Count == 0)
            {
                ActorsByChunk.Remove(chunk);
                candidates.Clear();
                ActorListPool.Push(candidates);
            }

            UpdateIndexedEntryDiagnostics();
            return true;
        }

        return false;
    }

    private static void UpdateIndexedEntryDiagnostics()
    {
        Volatile.Write(
            ref indexedChunkCount,
            ActorsByChunk.Count);
        Volatile.Write(
            ref indexedActorEntryCount,
            IndexedActorChunks.Count);
    }

    private static void RemoveInvalidTrackedActors()
    {
        IndexedActors.RemoveWhere(
            actor =>
            {
                bool remove =
                    actor == null ||
                    !actor.isAlive() ||
                    !HasAnyTrackedStatus(actor);
                if (remove)
                {
                    SetActorIndexed(
                        actor,
                        false);
                }

                return remove;
            });
    }

    private static void RefreshGlobalStatusIds()
    {
        foreach (Actor actor in IndexedActors)
        {
            foreach (string statusId in TrackedStatusIds)
            {
                Interlocked.Increment(ref statusChecks);
                if (actor.hasStatus(statusId))
                {
                    GlobalStatusIds.Add(statusId);
                }
            }
        }
    }

    private static bool HasAnyTrackedStatus(Actor actor)
    {
        foreach (string statusId in TrackedStatusIds)
        {
            if (actor.hasStatus(statusId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCurrentIndex()
    {
        return indexAvailable &&
               indexedGeneration == AWSimulationTime.Generation &&
               indexedUnitMembershipVersion ==
               AWParallelSimObjectZoneUnits
                   .UnitMembershipVersion;
    }

    private static void RegisterTrackedStatusIds(
        string[] statusIds)
    {
        bool currentIndex = IsCurrentIndex();
        bool added = false;
        bool membershipComplete = true;
        long checkedUnits = 0L;
        for (int i = 0; i < statusIds.Length; i++)
        {
            string statusId = statusIds[i];
            if (statusId != null &&
                TrackedStatusIds.Add(statusId))
            {
                membershipComplete &=
                    InitializeTrackedStatus(
                        statusId,
                        currentIndex,
                        ref checkedUnits);
                added = true;
            }
        }

        Interlocked.Add(ref unitChecks, checkedUnits);
        if (added &&
            (!currentIndex ||
             !membershipComplete))
        {
            indexAvailable = false;
        }
        else if (added)
        {
            UpdateIndexedEntryDiagnostics();
        }
    }

    private static bool InitializeTrackedStatus(
        string statusId,
        bool updateCurrentMembership,
        ref long checkedUnits)
    {
        StatusManager manager = World.world?.statuses;
        if (manager == null)
        {
            return true;
        }

        bool complete = true;
        foreach (Status status in manager)
        {
            Interlocked.Increment(ref statusChecks);
            if (status?.asset?.id == statusId &&
                status.sim_object is Actor actor &&
                actor.isAlive() &&
                actor.hasStatus(statusId))
            {
                GlobalStatusIds.Add(statusId);
                if (AddIndexedActor(actor) &&
                    updateCurrentMembership &&
                    !TryAddCurrentMembership(
                        World.world,
                        actor,
                        ref checkedUnits))
                {
                    complete = false;
                }
            }
        }

        return complete;
    }

    private static bool AddIndexedActor(Actor actor)
    {
        bool added = IndexedActors.Add(actor);
        SetActorIndexed(actor, true);
        return added;
    }

    private static bool RemoveIndexedActor(Actor actor)
    {
        bool removed = IndexedActors.Remove(actor);
        if (removed)
        {
            SetActorIndexed(actor, false);
        }

        return removed;
    }

    /// <summary>
    /// NanoObject ? hash ? BaseSystemManager ?????????????
    /// ??????????????????? SimObjectsZones ???
    /// ???????????? HashSet ?????????? hash
    /// ?????????
    /// </summary>
    private static bool IsIndexedActor(Actor actor)
    {
        int hash = actor.GetHashCode();
        if ((uint)hash <
            (uint)indexedActorHashFlags.Length)
        {
            return indexedActorHashFlags[hash];
        }

        return IndexedActors.Contains(actor);
    }

    private static void SetActorIndexed(
        Actor actor,
        bool indexed)
    {
        if (actor == null)
        {
            return;
        }

        int hash = actor.GetHashCode();
        if (indexed &&
            hash >= 0 &&
            hash < MaximumDenseActorHashCapacity &&
            hash >= indexedActorHashFlags.Length)
        {
            int capacity = Math.Max(
                InitialDenseActorHashCapacity,
                indexedActorHashFlags.Length);
            while (capacity <= hash)
            {
                capacity = Math.Min(
                    MaximumDenseActorHashCapacity,
                    capacity << 1);
            }

            Array.Resize(
                ref indexedActorHashFlags,
                capacity);
        }

        if ((uint)hash <
            (uint)indexedActorHashFlags.Length)
        {
            indexedActorHashFlags[hash] = indexed;
        }
    }

    private static List<IndexedActor> RentActorList()
    {
        return ActorListPool.Count == 0
            ? new List<IndexedActor>(4)
            : ActorListPool.Pop();
    }

    private static void RecycleActorLists()
    {
        foreach (List<IndexedActor> actors in
                 ActorsByChunk.Values)
        {
            actors.Clear();
            ActorListPool.Push(actors);
        }

        ActorsByChunk.Clear();
        IndexedActorChunks.Clear();
    }

    private static void RecordBuildDuration(long elapsedTicks)
    {
        Interlocked.Exchange(ref lastBuildTicks, elapsedTicks);
        Interlocked.Add(ref totalBuildTicks, elapsedTicks);
        Interlocked.Increment(ref rebuilds);
        long maximum = Interlocked.Read(ref maximumBuildTicks);
        while (elapsedTicks > maximum)
        {
            long observed = Interlocked.CompareExchange(
                ref maximumBuildTicks,
                elapsedTicks,
                maximum);
            if (observed == maximum)
            {
                break;
            }

            maximum = observed;
        }
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private readonly struct IndexedActor
    {
        internal IndexedActor(
            Actor actor,
            int unitIndex)
        {
            Actor = actor;
            UnitIndex = unitIndex;
        }

        internal Actor Actor { get; }
        internal int UnitIndex { get; }
    }
}
