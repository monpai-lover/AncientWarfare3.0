using System;
using System.Collections.Concurrent;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    // Measurement-only probe for the "should we build a completed-path cache?"
    // question. It records the reuse key of every retired path session and
    // reports how many later requests would have hit such a cache.
    //
    // This observes only -- it never returns a path and never changes
    // pathfinding behaviour. Delete this file and its diagnostic fields once
    // the cache question is settled.
    //
    // Two hit rates are tracked because they answer different questions:
    //   Loose  -- matches the fields AWPathRequestReuseRules.CanReuse checks
    //             today (actor, request, world generation, inside-boat). This
    //             is the optimistic ceiling.
    //   Strict -- additionally requires StartRegion and TerrainRevision to
    //             match. A completed-path cache MUST gate on these or it hands
    //             actors a path computed from a position they have left, over
    //             terrain that may have changed. This is the number a correct
    //             cache could actually deliver.
    internal sealed class AWPathReuseProbe
    {
        private readonly struct Key : IEquatable<Key>
        {
            internal Key(long pActorId, AWPathRequestKey pRequest)
            {
                ActorId = pActorId;
                Request = pRequest;
            }

            private long ActorId { get; }
            private AWPathRequestKey Request { get; }

            public bool Equals(Key pOther)
            {
                return ActorId == pOther.ActorId &&
                       Request.Equals(pOther.Request);
            }

            public override bool Equals(object pObject)
            {
                return pObject is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ActorId.GetHashCode() * 397 ^ Request.GetHashCode();
                }
            }
        }

        private readonly struct Slot
        {
            internal Slot(int pStartRegion, long pTerrainRevision,
                long pWorldGeneration, bool pInsideBoat)
            {
                StartRegion = pStartRegion;
                TerrainRevision = pTerrainRevision;
                WorldGeneration = pWorldGeneration;
                InsideBoat = pInsideBoat;
            }

            internal int StartRegion { get; }
            internal long TerrainRevision { get; }
            internal long WorldGeneration { get; }
            internal bool InsideBoat { get; }
        }

        private readonly int _capacity;
        private readonly ConcurrentDictionary<Key, Slot> _entries =
            new ConcurrentDictionary<Key, Slot>();
        private long _recorded;
        private long _probes;
        private long _looseHits;
        private long _strictHits;
        private long _evictions;

        internal AWPathReuseProbe(int pCapacity)
        {
            _capacity = Math.Max(1,
                AWPathRequestReuseRules.ClampCompletedCapacity(pCapacity));
        }

        internal long Recorded => Interlocked.Read(ref _recorded);
        internal long Probes => Interlocked.Read(ref _probes);
        internal long LooseHits => Interlocked.Read(ref _looseHits);
        internal long StrictHits => Interlocked.Read(ref _strictHits);
        internal long Evictions => Interlocked.Read(ref _evictions);
        internal int Tracked => _entries.Count;

        // Called when a path session retires. Models a cache write.
        internal void OnRetired(AWPathReuseKey pKey)
        {
            // Clear-on-full models a 2048-entry cache with the crudest possible
            // eviction. A real LRU would retain more, so the hit rates reported
            // here are a lower bound.
            if (_entries.Count >= _capacity)
            {
                _entries.Clear();
                Interlocked.Increment(ref _evictions);
            }
            _entries[new Key(pKey.ActorId, pKey.Request)] =
                new Slot(pKey.StartRegion, pKey.TerrainRevision,
                    pKey.WorldGeneration, pKey.InsideBoat);
            Interlocked.Increment(ref _recorded);
        }

        // Called when live reuse missed. Models a cache read.
        internal void OnMissed(AWPathReuseKey pKey)
        {
            Interlocked.Increment(ref _probes);
            if (!_entries.TryGetValue(new Key(pKey.ActorId, pKey.Request),
                    out Slot slot)) return;
            if (slot.WorldGeneration != pKey.WorldGeneration ||
                slot.InsideBoat != pKey.InsideBoat) return;
            Interlocked.Increment(ref _looseHits);
            if (slot.StartRegion != pKey.StartRegion ||
                slot.TerrainRevision != pKey.TerrainRevision) return;
            Interlocked.Increment(ref _strictHits);
        }

        internal void Reset()
        {
            _entries.Clear();
            Interlocked.Exchange(ref _recorded, 0L);
            Interlocked.Exchange(ref _probes, 0L);
            Interlocked.Exchange(ref _looseHits, 0L);
            Interlocked.Exchange(ref _strictHits, 0L);
            Interlocked.Exchange(ref _evictions, 0L);
        }
    }
}
