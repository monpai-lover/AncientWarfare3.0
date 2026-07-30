using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.schools
{
    internal readonly struct HistoricalSchoolJourneyArrivalStamp
    {
        public HistoricalSchoolJourneyArrivalStamp(long pActorId,
            long pDestinationCityId, long pActorRevision,
            long pDestinationRevision, bool pDestinationExisted)
        {
            ActorId = pActorId;
            DestinationCityId = pDestinationCityId;
            ActorRevision = pActorRevision;
            DestinationRevision = pDestinationRevision;
            DestinationExisted = pDestinationExisted;
        }

        public long ActorId { get; }
        public long DestinationCityId { get; }
        public long ActorRevision { get; }
        public long DestinationRevision { get; }
        public bool DestinationExisted { get; }
    }

    internal static class HistoricalSchoolJourneyArrivalRevision
    {
        private static readonly ConcurrentDictionary<long, long>
            ActorRevisions = new ConcurrentDictionary<long, long>();
        private static long _destinationRevision = 1L;

        public static HistoricalSchoolJourneyArrivalStamp Capture(
            long pActorId, long pDestinationCityId,
            bool pDestinationExisted)
        {
            return new HistoricalSchoolJourneyArrivalStamp(pActorId,
                pDestinationCityId, ActorRevision(pActorId),
                Interlocked.Read(ref _destinationRevision),
                pDestinationExisted);
        }

        public static bool IsCurrent(
            HistoricalSchoolJourneyArrivalStamp pStamp)
        {
            return pStamp.DestinationExisted && pStamp.ActorId >= 0L &&
                   pStamp.DestinationCityId >= 0L &&
                   ActorRevision(pStamp.ActorId) == pStamp.ActorRevision &&
                   Interlocked.Read(ref _destinationRevision) ==
                   pStamp.DestinationRevision;
        }

        public static void MarkActorChanged(long pActorId)
        {
            if (pActorId < 0L) return;
            ActorRevisions.AddOrUpdate(pActorId, 1L,
                (_, current) => current == long.MaxValue
                    ? current
                    : current + 1L);
        }

        public static void MarkDestinationsChanged()
        {
            Advance(ref _destinationRevision);
        }

        public static void Clear()
        {
            MarkDestinationsChanged();
            ActorRevisions.Clear();
        }

        private static long ActorRevision(long pActorId)
        {
            return pActorId >= 0L && ActorRevisions.TryGetValue(pActorId,
                out long revision) ? revision : 0L;
        }

        private static void Advance(ref long pRevision)
        {
            while (true)
            {
                long current = Interlocked.Read(ref pRevision);
                if (current == long.MaxValue) return;
                if (Interlocked.CompareExchange(ref pRevision,
                        current + 1L, current) == current)
                    return;
            }
        }
    }

    internal sealed class HistoricalSchoolJourneyArrivalRetryQueue<T>
        where T : class
    {
        private readonly int _capacity;
        private readonly Dictionary<long, T> _byActor =
            new Dictionary<long, T>();

        public HistoricalSchoolJourneyArrivalRetryQueue(int pCapacity)
        {
            _capacity = pCapacity < 1 ? 1 : pCapacity;
        }

        public int Count => _byActor.Count;

        public bool TryUpsert(long pActorId, T pValue)
        {
            if (pActorId < 0L || pValue == null) return false;
            if (_byActor.ContainsKey(pActorId))
            {
                _byActor[pActorId] = pValue;
                return true;
            }
            if (_byActor.Count >= _capacity) return false;
            _byActor.Add(pActorId, pValue);
            return true;
        }

        public bool TryUpsertOwned(long pActorId, T pValue,
            ISet<long> pPendingOwners)
        {
            if (pPendingOwners == null) return false;
            bool queued = TryUpsert(pActorId, pValue);
            if (queued)
                pPendingOwners.Add(pActorId);
            else
                pPendingOwners.Remove(pActorId);
            return queued;
        }

        public bool TryGet(long pActorId, out T pValue)
        {
            return _byActor.TryGetValue(pActorId, out pValue);
        }

        public bool TryGetFirst(out long pActorId, out T pValue)
        {
            foreach (KeyValuePair<long, T> pair in _byActor)
            {
                pActorId = pair.Key;
                pValue = pair.Value;
                return true;
            }
            pActorId = -1L;
            pValue = null;
            return false;
        }

        public bool Remove(long pActorId)
        {
            return _byActor.Remove(pActorId);
        }

        public void Clear()
        {
            _byActor.Clear();
        }
    }
}
