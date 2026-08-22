using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct HereditaryTitleSuccessionCandidate
    {
        public HereditaryTitleSuccessionCandidate(long actorId, bool eligible,
            bool directSon, bool legitimateBirth, bool adult, bool agnatic,
            int kinDistance, double birthTime)
        {
            ActorId = actorId;
            Eligible = eligible;
            DirectSon = directSon;
            LegitimateBirth = legitimateBirth;
            Adult = adult;
            Agnatic = agnatic;
            KinDistance = Math.Max(0, kinDistance);
            BirthTime = birthTime;
        }

        public long ActorId { get; }
        public bool Eligible { get; }
        public bool DirectSon { get; }
        public bool LegitimateBirth { get; }
        public bool Adult { get; }
        public bool Agnatic { get; }
        public int KinDistance { get; }
        public double BirthTime { get; }
    }

    public static class HereditaryTitleSuccessionRules
    {
        public static bool CanTransfer(bool hereditary, bool holderMale,
            bool maleLineIdentity)
        {
            return hereditary && holderMale && maleLineIdentity;
        }

        public static long SelectSuccessor(
            IReadOnlyList<HereditaryTitleSuccessionCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0) return -1L;
            HereditaryTitleSuccessionCandidate? direct = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                HereditaryTitleSuccessionCandidate candidate = candidates[i];
                if (candidate.ActorId < 0 || !candidate.Eligible ||
                    !candidate.DirectSon || !candidate.Agnatic) continue;
                if (!direct.HasValue || BetterDirect(candidate,
                        direct.Value)) direct = candidate;
            }
            if (direct.HasValue) return direct.Value.ActorId;

            HereditaryTitleSuccessionCandidate? collateral = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                HereditaryTitleSuccessionCandidate candidate = candidates[i];
                if (candidate.ActorId < 0 || !candidate.Eligible ||
                    candidate.DirectSon || !candidate.Agnatic ||
                    !candidate.Adult) continue;
                if (!collateral.HasValue || BetterCollateral(candidate,
                        collateral.Value)) collateral = candidate;
            }
            return collateral?.ActorId ?? -1L;
        }

        private static bool BetterDirect(
            HereditaryTitleSuccessionCandidate left,
            HereditaryTitleSuccessionCandidate right)
        {
            if (left.LegitimateBirth != right.LegitimateBirth)
                return left.LegitimateBirth;
            return CompareBirth(left, right);
        }

        private static bool BetterCollateral(
            HereditaryTitleSuccessionCandidate left,
            HereditaryTitleSuccessionCandidate right)
        {
            if (left.KinDistance != right.KinDistance)
                return left.KinDistance < right.KinDistance;
            return CompareBirth(left, right);
        }

        private static bool CompareBirth(
            HereditaryTitleSuccessionCandidate left,
            HereditaryTitleSuccessionCandidate right)
        {
            double lb = double.IsNaN(left.BirthTime)
                ? double.MaxValue : left.BirthTime;
            double rb = double.IsNaN(right.BirthTime)
                ? double.MaxValue : right.BirthTime;
            if (lb != rb) return lb < rb;
            return left.ActorId < right.ActorId;
        }
    }
}
