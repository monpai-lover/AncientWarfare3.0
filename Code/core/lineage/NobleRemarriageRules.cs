using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum NobleRemarriagePriority
    {
        Ruler = 0,
        Heir = 1,
        FeudatoryPrince = 2,
        TitledNoble = 3
    }

    public readonly struct NobleRemarriageSubjectCandidate
    {
        public NobleRemarriageSubjectCandidate(long actorId,
            NobleRemarriagePriority priority, bool eligible,
            double birthTime)
        {
            ActorId = actorId;
            Priority = priority;
            Eligible = eligible;
            BirthTime = birthTime;
        }

        public long ActorId { get; }
        public NobleRemarriagePriority Priority { get; }
        public bool Eligible { get; }
        public double BirthTime { get; }
    }

    public readonly struct NobleRemarriageSpouseCandidate
    {
        public NobleRemarriageSpouseCandidate(long actorId, bool eligible,
            bool sameCity, int ageDifference, int merit)
        {
            ActorId = actorId;
            Eligible = eligible;
            SameCity = sameCity;
            AgeDifference = Math.Max(0, ageDifference);
            Merit = merit;
        }

        public long ActorId { get; }
        public bool Eligible { get; }
        public bool SameCity { get; }
        public int AgeDifference { get; }
        public int Merit { get; }
    }

    public static class NobleRemarriageRules
    {
        public const int MaximumSubjectsPerKingdomYear = 2;
        public const int MaximumSpouseCandidates = 32;

        public static bool NeedsRemarriage(bool alive, bool adult,
            bool breedingAge, bool partnerReferenceExists,
            bool partnerAlive)
        {
            return alive && adult && breedingAge &&
                   (!partnerReferenceExists || !partnerAlive);
        }

        public static bool CanUseSpouse(bool alive, bool adult,
            bool breedingAge, bool hasLivingPartner, bool related,
            bool sameShi, bool foreignKingdom)
        {
            return alive && adult && breedingAge && !hasLivingPartner &&
                   !related && !sameShi && !foreignKingdom;
        }

        public static IReadOnlyList<long> SelectSubjects(
            IReadOnlyList<NobleRemarriageSubjectCandidate> candidates,
            int maximum)
        {
            int limit = Math.Max(0, Math.Min(
                MaximumSubjectsPerKingdomYear, maximum));
            var ordered = new List<NobleRemarriageSubjectCandidate>();
            int count = candidates?.Count ?? 0;
            for (int i = 0; i < count; i++)
                if (candidates[i].Eligible && candidates[i].ActorId >= 0)
                    ordered.Add(candidates[i]);
            ordered.Sort(CompareSubjects);
            var result = new List<long>(Math.Min(limit, ordered.Count));
            for (int i = 0; i < ordered.Count && result.Count < limit; i++)
                result.Add(ordered[i].ActorId);
            return result.AsReadOnly();
        }

        public static long SelectSpouse(
            IReadOnlyList<NobleRemarriageSpouseCandidate> candidates)
        {
            NobleRemarriageSpouseCandidate? best = null;
            int count = Math.Min(MaximumSpouseCandidates,
                candidates?.Count ?? 0);
            for (int i = 0; i < count; i++)
            {
                NobleRemarriageSpouseCandidate candidate = candidates[i];
                if (!candidate.Eligible || candidate.ActorId < 0) continue;
                if (!best.HasValue || BetterSpouse(candidate, best.Value))
                    best = candidate;
            }
            return best?.ActorId ?? -1L;
        }

        private static int CompareSubjects(
            NobleRemarriageSubjectCandidate left,
            NobleRemarriageSubjectCandidate right)
        {
            int priority = left.Priority.CompareTo(right.Priority);
            if (priority != 0) return priority;
            int birth = left.BirthTime.CompareTo(right.BirthTime);
            return birth != 0 ? birth : left.ActorId.CompareTo(right.ActorId);
        }

        private static bool BetterSpouse(
            NobleRemarriageSpouseCandidate left,
            NobleRemarriageSpouseCandidate right)
        {
            if (left.SameCity != right.SameCity) return left.SameCity;
            if (left.AgeDifference != right.AgeDifference)
                return left.AgeDifference < right.AgeDifference;
            if (left.Merit != right.Merit) return left.Merit > right.Merit;
            return left.ActorId < right.ActorId;
        }
    }
}
