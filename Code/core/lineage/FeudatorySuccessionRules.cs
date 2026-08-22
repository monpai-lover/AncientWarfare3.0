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
            double birthTime, bool directTreeDescendant = true,
            bool legitimateBirth = true, bool adult = true)
        {
            ActorId = actorId;
            Eligible = eligible;
            DirectSon = directSon;
            SameShiBranch = sameShiBranch;
            KinDistance = Math.Max(0, kinDistance);
            BirthTime = birthTime;
            DirectTreeDescendant = directTreeDescendant;
            LegitimateBirth = legitimateBirth;
            Adult = adult;
        }

        public long ActorId { get; }
        public bool Eligible { get; }
        public bool DirectSon { get; }
        public bool SameShiBranch { get; }
        public int KinDistance { get; }
        public double BirthTime { get; }
        public bool DirectTreeDescendant { get; }
        public bool LegitimateBirth { get; }
        public bool Adult { get; }
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
            if (candidates == null || candidates.Count == 0) return -1L;
            var mapped = new List<HereditaryTitleSuccessionCandidate>(
                candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                FeudatorySuccessionCandidate candidate = candidates[i];
                bool eligible = candidate.Eligible &&
                                candidate.SameShiBranch &&
                                candidate.DirectTreeDescendant;
                mapped.Add(new HereditaryTitleSuccessionCandidate(
                    candidate.ActorId, eligible, candidate.DirectSon,
                    candidate.LegitimateBirth, candidate.Adult,
                    candidate.DirectTreeDescendant, candidate.KinDistance,
                    candidate.BirthTime));
            }
            return HereditaryTitleSuccessionRules.SelectSuccessor(mapped);
        }

        public static bool ShouldAbolish(bool currentPrinceInvalid,
            long successorActorId)
        {
            return currentPrinceInvalid && successorActorId < 0;
        }

        public static bool ShouldRefreshAfterDeath(
            bool dyingActorIsPrince,
            bool dyingActorIsDesignatedSuccessor)
        {
            return dyingActorIsPrince || dyingActorIsDesignatedSuccessor;
        }

    }
}
