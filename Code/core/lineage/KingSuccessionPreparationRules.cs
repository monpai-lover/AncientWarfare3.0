using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum SuccessionEvidenceStatus
    {
        Found = 0,
        PendingEvidence = 1,
        ExtinctConfirmed = 2,
        EligibleLineExhausted = 3
    }

    public readonly struct SuccessionArchiveCandidateFact
    {
        public SuccessionArchiveCandidateFact(long pActorId,
            int pGeneration, int pAttribute, bool pEligible)
        {
            ActorId = pActorId;
            Generation = pGeneration;
            Attribute = pAttribute;
            Eligible = pEligible;
        }

        public long ActorId { get; }
        public int Generation { get; }
        public int Attribute { get; }
        public bool Eligible { get; }
    }

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

    public readonly struct KingSuccessionKey : IEquatable<KingSuccessionKey>
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
        public static bool IsRegisteredHeirAvailable(bool pIsAlive,
            bool pIsPresentInWorldUnits)
        {
            return pIsAlive && pIsPresentInWorldUnits;
        }

        public static SuccessionEvidenceStatus ResolveEvidenceStatus(
            bool pHasCandidate, bool pEvidenceAvailable,
            bool pSearchComplete, bool pHasLivingLineageMembers)
        {
            if (pHasCandidate) return SuccessionEvidenceStatus.Found;
            if (!pEvidenceAvailable || !pSearchComplete)
                return SuccessionEvidenceStatus.PendingEvidence;
            return pHasLivingLineageMembers
                ? SuccessionEvidenceStatus.EligibleLineExhausted
                : SuccessionEvidenceStatus.ExtinctConfirmed;
        }

        public static bool CanStartCourtUsurpation(
            SuccessionEvidenceStatus pStatus)
        {
            return pStatus == SuccessionEvidenceStatus.ExtinctConfirmed ||
                   pStatus == SuccessionEvidenceStatus.EligibleLineExhausted;
        }

        public static long SelectArchiveCandidateId(
            int pPredecessorGeneration,
            IReadOnlyList<SuccessionArchiveCandidateFact> pCandidates)
        {
            long selectedId = -1L;
            int selectedAttribute = int.MinValue;
            int count = pCandidates?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                SuccessionArchiveCandidateFact candidate = pCandidates[i];
                if (!candidate.Eligible || candidate.ActorId < 0L ||
                    Math.Abs((long)candidate.Generation -
                             pPredecessorGeneration) > 2L)
                    continue;
                if (candidate.Attribute < selectedAttribute ||
                    candidate.Attribute == selectedAttribute &&
                    selectedId >= 0L && candidate.ActorId >= selectedId)
                    continue;
                selectedId = candidate.ActorId;
                selectedAttribute = candidate.Attribute;
            }
            return selectedId;
        }

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
        private readonly Dictionary<KingSuccessionKey, long> _nextAttempts =
            new Dictionary<KingSuccessionKey, long>();

        public bool TryBegin(KingSuccessionKey pKey, long pCurrentFrame,
            long pRetryDelayFrames)
        {
            if (_nextAttempts.TryGetValue(pKey, out long nextAttempt) &&
                pCurrentFrame < nextAttempt)
                return false;
            _nextAttempts[pKey] = pCurrentFrame +
                Math.Max(1L, pRetryDelayFrames);
            return true;
        }

        public void ScheduleRetry(KingSuccessionKey pKey,
            long pCurrentFrame, long pRetryDelayFrames)
        {
            _nextAttempts[pKey] = pCurrentFrame +
                Math.Max(1L, pRetryDelayFrames);
        }

        public void Complete(KingSuccessionKey pKey)
        {
            _nextAttempts.Remove(pKey);
        }

        public void Clear()
        {
            _nextAttempts.Clear();
        }
    }
}
