using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class SuccessionDirtyQueue
    {
        private readonly Queue<long> _queue = new Queue<long>();
        private readonly HashSet<long> _queued = new HashSet<long>();

        public int Count => _queue.Count;

        public void MarkDirty(long pKingdomId)
        {
            if (pKingdomId < 0L || !_queued.Add(pKingdomId)) return;
            _queue.Enqueue(pKingdomId);
        }

        public IReadOnlyList<long> Take(int pBudget)
        {
            if (pBudget <= 0 || _queue.Count == 0)
                return Array.Empty<long>();
            int count = Math.Min(pBudget, _queue.Count);
            var result = new long[count];
            for (int i = 0; i < count; i++)
            {
                long kingdomId = _queue.Dequeue();
                _queued.Remove(kingdomId);
                result[i] = kingdomId;
            }
            return result;
        }

        public void Clear()
        {
            _queue.Clear();
            _queued.Clear();
        }
    }

    public static class SuccessionSnapshotRules
    {
        public static bool IsCurrent(long pSnapshotRevision,
            long pCurrentRevision, long pSnapshotKingId,
            long pCurrentKingId, bool candidateAlive,
            bool candidateInRealm)
        {
            return pSnapshotRevision == pCurrentRevision &&
                   pSnapshotKingId == pCurrentKingId &&
                   candidateAlive && candidateInRealm;
        }
    }

    public static class HeirSelectionSignatureRules
    {
        public static bool IsUnchanged(long pStoredCandidateId,
            string pStoredMode, long pStoredReferenceKingId, bool pDirty,
            long pCandidateId, string pMode, long pReferenceKingId)
        {
            return !pDirty &&
                   pStoredCandidateId == pCandidateId &&
                   string.Equals(pStoredMode ?? string.Empty,
                       pMode ?? string.Empty, StringComparison.Ordinal) &&
                   pStoredReferenceKingId == pReferenceKingId;
        }
    }

    public readonly struct KingSuccessionKey :
        IEquatable<KingSuccessionKey>
    {
        public KingSuccessionKey(long pWorldGeneration, long pKingdomId,
            long pPredecessorId)
        {
            WorldGeneration = pWorldGeneration;
            KingdomId = pKingdomId;
            PredecessorId = pPredecessorId;
        }

        public long WorldGeneration { get; }
        public long KingdomId { get; }
        public long PredecessorId { get; }

        public bool Equals(KingSuccessionKey pOther)
        {
            return WorldGeneration == pOther.WorldGeneration &&
                   KingdomId == pOther.KingdomId &&
                   PredecessorId == pOther.PredecessorId;
        }

        public override bool Equals(object pValue)
        {
            return pValue is KingSuccessionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldGeneration.GetHashCode();
                hash = (hash * 397) ^ KingdomId.GetHashCode();
                return (hash * 397) ^ PredecessorId.GetHashCode();
            }
        }
    }

    public readonly struct PreparedSuccession
    {
        public PreparedSuccession(long pCandidateId, string pMode)
        {
            CandidateId = pCandidateId;
            Mode = pMode ?? string.Empty;
        }

        public long CandidateId { get; }
        public string Mode { get; }
    }

    public sealed class KingSuccessionPreparationState
    {
        private readonly Dictionary<KingSuccessionKey, Entry> _entries =
            new Dictionary<KingSuccessionKey, Entry>();

        public int Count => _entries.Count;

        public bool TryCapture(KingSuccessionKey pKey, long pRevision,
            long pCandidateId)
        {
            if (_entries.ContainsKey(pKey)) return false;
            _entries.Add(pKey, new Entry
            {
                CapturedRevision = pRevision,
                PublishedRevision = -1L,
                CandidateId = pCandidateId,
                Mode = string.Empty,
                Published = false
            });
            return true;
        }

        public void Publish(KingSuccessionKey pKey, long pRevision,
            long pCandidateId, string pMode)
        {
            if (!_entries.TryGetValue(pKey, out Entry entry)) return;
            entry.PublishedRevision = pRevision;
            entry.CandidateId = pCandidateId;
            entry.Mode = pMode ?? string.Empty;
            entry.Published = true;
            _entries[pKey] = entry;
        }

        public bool TryConsume(KingSuccessionKey pKey, long pRevision,
            out PreparedSuccession pPrepared)
        {
            pPrepared = default;
            if (!_entries.TryGetValue(pKey, out Entry entry) ||
                !entry.Published || entry.CapturedRevision != pRevision ||
                entry.PublishedRevision != pRevision)
                return false;
            _entries.Remove(pKey);
            pPrepared = new PreparedSuccession(entry.CandidateId,
                entry.Mode);
            return true;
        }

        public void Clear()
        {
            _entries.Clear();
        }

        private struct Entry
        {
            internal long CapturedRevision;
            internal long PublishedRevision;
            internal long CandidateId;
            internal string Mode;
            internal bool Published;
        }
    }
}
