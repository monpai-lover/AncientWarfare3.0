using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum FeudatoryAccessionDisposition
    {
        TransferToSuccessor,
        RevertToCrown
    }

    public readonly struct FeudatorySuccessionCandidate
    {
        public FeudatorySuccessionCandidate(long actorId, bool eligible,
            bool directSon, bool sameShiBranch, int kinDistance,
            double birthTime, bool directTreeDescendant = true)
        {
            ActorId = actorId;
            Eligible = eligible;
            DirectSon = directSon;
            SameShiBranch = sameShiBranch;
            KinDistance = Math.Max(0, kinDistance);
            BirthTime = birthTime;
            DirectTreeDescendant = directTreeDescendant;
        }

        public long ActorId { get; }
        public bool Eligible { get; }
        public bool DirectSon { get; }
        public bool SameShiBranch { get; }
        public int KinDistance { get; }
        public double BirthTime { get; }
        public bool DirectTreeDescendant { get; }
    }

    public static class FeudatorySuccessionRules
    {
        public static bool CanRemainPrince(bool alive,
            bool belongsToEmpire, bool isReigningKing)
        {
            return alive && belongsToEmpire && !isReigningKing;
        }

        public static FeudatoryAccessionDisposition
            ResolveAccessionDisposition(long successorActorId)
        {
            return successorActorId >= 0
                ? FeudatoryAccessionDisposition.TransferToSuccessor
                : FeudatoryAccessionDisposition.RevertToCrown;
        }

        public static bool IsDirectBiologicalSon(long parent1ActorId,
            long parent2ActorId, long princeActorId, bool male)
        {
            return male && princeActorId >= 0 &&
                   (parent1ActorId == princeActorId ||
                    parent2ActorId == princeActorId);
        }

        public static long SelectSuccessor(
            IReadOnlyList<FeudatorySuccessionCandidate> candidates)
        {
            FeudatorySuccessionCandidate? best = null;
            int count = candidates?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                FeudatorySuccessionCandidate candidate = candidates[i];
                if (candidate.ActorId < 0 || !candidate.Eligible ||
                    !candidate.SameShiBranch ||
                    !candidate.DirectTreeDescendant)
                    continue;
                if (!best.HasValue || Better(candidate, best.Value))
                    best = candidate;
            }
            return best?.ActorId ?? -1L;
        }

        public static bool ShouldAbolish(bool currentPrinceInvalid,
            long successorActorId)
        {
            return currentPrinceInvalid && successorActorId < 0;
        }

        private static bool Better(FeudatorySuccessionCandidate left,
            FeudatorySuccessionCandidate right)
        {
            if (left.DirectSon != right.DirectSon) return left.DirectSon;
            if (!left.DirectSon && left.KinDistance != right.KinDistance)
                return left.KinDistance < right.KinDistance;
            int birth = left.BirthTime.CompareTo(right.BirthTime);
            if (birth != 0) return birth < 0;
            return left.ActorId < right.ActorId;
        }
    }
}
