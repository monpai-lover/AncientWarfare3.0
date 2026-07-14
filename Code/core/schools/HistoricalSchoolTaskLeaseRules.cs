using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public readonly struct HistoricalSchoolTaskLease
    {
        public HistoricalSchoolTaskLease(
            long pActorId,
            string pActivityId,
            string pTaskId,
            string pSchoolId,
            long pCityId,
            string pVenueKey,
            long pStartFrame,
            long pExpiryFrame)
        {
            ActorId = pActorId;
            ActivityId = pActivityId ?? "";
            TaskId = pTaskId ?? "";
            SchoolId = pSchoolId ?? "";
            CityId = pCityId;
            VenueKey = pVenueKey ?? "";
            StartFrame = pStartFrame;
            ExpiryFrame = pExpiryFrame;
        }

        public long ActorId { get; }
        public string ActivityId { get; }
        public string TaskId { get; }
        public string SchoolId { get; }
        public long CityId { get; }
        public string VenueKey { get; }
        public long StartFrame { get; }
        public long ExpiryFrame { get; }

        public bool IsValid => ActorId >= 0 && CityId >= 0 &&
                               !string.IsNullOrEmpty(ActivityId) &&
                               !string.IsNullOrEmpty(TaskId) &&
                               !string.IsNullOrEmpty(SchoolId) &&
                               !string.IsNullOrEmpty(VenueKey) &&
                               StartFrame >= 0 && ExpiryFrame >= StartFrame;
    }

    public sealed class HistoricalSchoolTaskLeaseBook
    {
        private readonly struct ExpiryEntry
        {
            public ExpiryEntry(long pActorId, string pActivityId, long pExpiryFrame)
            {
                ActorId = pActorId;
                ActivityId = pActivityId;
                ExpiryFrame = pExpiryFrame;
            }

            public long ActorId { get; }
            public string ActivityId { get; }
            public long ExpiryFrame { get; }
        }

        private readonly Dictionary<long, HistoricalSchoolTaskLease> _byActor =
            new Dictionary<long, HistoricalSchoolTaskLease>();
        private readonly SortedDictionary<long, Queue<ExpiryEntry>> _expiry =
            new SortedDictionary<long, Queue<ExpiryEntry>>();

        public int Count => _byActor.Count;

        public bool TryAcquire(HistoricalSchoolTaskLease pLease)
        {
            if (!pLease.IsValid || _byActor.ContainsKey(pLease.ActorId)) return false;
            _byActor.Add(pLease.ActorId, pLease);
            if (!_expiry.TryGetValue(
                    pLease.ExpiryFrame, out Queue<ExpiryEntry> expiryBucket))
            {
                expiryBucket = new Queue<ExpiryEntry>();
                _expiry.Add(pLease.ExpiryFrame, expiryBucket);
            }
            expiryBucket.Enqueue(new ExpiryEntry(
                pLease.ActorId, pLease.ActivityId, pLease.ExpiryFrame));
            return true;
        }

        public bool IsCurrent(long pActorId, string pActivityId, string pTaskId = null)
        {
            if (!_byActor.TryGetValue(pActorId, out HistoricalSchoolTaskLease lease) ||
                !string.Equals(lease.ActivityId, pActivityId,
                    StringComparison.Ordinal)) return false;
            return pTaskId == null || string.Equals(
                lease.TaskId, pTaskId, StringComparison.Ordinal);
        }

        public bool TryGet(long pActorId, out HistoricalSchoolTaskLease pLease)
        {
            return _byActor.TryGetValue(pActorId, out pLease);
        }

        public bool TryRelease(
            long pActorId,
            string pActivityId,
            out HistoricalSchoolTaskLease pLease)
        {
            if (!_byActor.TryGetValue(pActorId, out pLease) ||
                !string.Equals(pLease.ActivityId, pActivityId,
                    StringComparison.Ordinal))
            {
                pLease = default;
                return false;
            }
            _byActor.Remove(pActorId);
            return true;
        }

        public bool TryReleaseActor(
            long pActorId,
            out HistoricalSchoolTaskLease pLease)
        {
            if (!_byActor.TryGetValue(pActorId, out pLease)) return false;
            _byActor.Remove(pActorId);
            return true;
        }

        public bool TryExpireOne(
            long pFrame,
            out HistoricalSchoolTaskLease pLease)
        {
            while (_expiry.Count > 0)
            {
                long expiryFrame;
                Queue<ExpiryEntry> expiryBucket;
                using (var iterator = _expiry.GetEnumerator())
                {
                    iterator.MoveNext();
                    expiryFrame = iterator.Current.Key;
                    expiryBucket = iterator.Current.Value;
                }
                if (expiryFrame > pFrame)
                {
                    pLease = default;
                    return false;
                }
                ExpiryEntry expiry = expiryBucket.Dequeue();
                if (expiryBucket.Count == 0) _expiry.Remove(expiryFrame);
                if (!_byActor.TryGetValue(expiry.ActorId, out pLease) ||
                    pLease.ExpiryFrame != expiry.ExpiryFrame ||
                    !string.Equals(pLease.ActivityId, expiry.ActivityId,
                        StringComparison.Ordinal))
                    continue;
                _byActor.Remove(expiry.ActorId);
                return true;
            }
            pLease = default;
            return false;
        }

        public void Clear()
        {
            _byActor.Clear();
            _expiry.Clear();
        }
    }
}
