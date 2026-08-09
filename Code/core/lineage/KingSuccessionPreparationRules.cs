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

    public static class AuthoritativeSuccessionRules
    {
        public static bool ShouldRunFallback(bool pHasValidRegisteredHeir,
            bool pFallbackAlreadyAttempted)
        {
            return !pHasValidRegisteredHeir && !pFallbackAlreadyAttempted;
        }
    }

    public static class RoyalSuccessionEventRules
    {
        public static bool ShouldMarkSelectionDirty(long pActorLineageId,
            long pKingdomRoyalLineageId,
            bool pLineageReignsInThisKingdom, bool pRegisteredHeir,
            bool pDirectRoyalChild)
        {
            return pActorLineageId >= 0L &&
                   pActorLineageId == pKingdomRoyalLineageId &&
                   pLineageReignsInThisKingdom &&
                   (pRegisteredHeir || pDirectRoyalChild);
        }
    }

    public sealed class SuccessionFallbackAttemptState
    {
        private readonly HashSet<KingSuccessionKey> _attempted =
            new HashSet<KingSuccessionKey>();

        public bool TryBegin(KingSuccessionKey pKey)
        {
            return _attempted.Add(pKey);
        }

        public void Complete(KingSuccessionKey pKey)
        {
            _attempted.Remove(pKey);
        }

        public void Clear()
        {
            _attempted.Clear();
        }
    }
}
