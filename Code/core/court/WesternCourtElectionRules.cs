using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public readonly struct WesternCourtElectionCandidate
    {
        public WesternCourtElectionCandidate(long actorId, bool eligible,
            bool isKing, bool incumbent, float ability, float merit,
            float familyInfluence, float schoolCompatibility,
            float factionInfluence)
        {
            ActorId = actorId;
            Eligible = eligible;
            IsKing = isKing;
            Incumbent = incumbent;
            Ability = NonNegative(ability);
            Merit = NonNegative(merit);
            FamilyInfluence = NonNegative(familyInfluence);
            SchoolCompatibility = NonNegative(schoolCompatibility);
            FactionInfluence = NonNegative(factionInfluence);
        }

        public long ActorId { get; }
        public bool Eligible { get; }
        public bool IsKing { get; }
        public bool Incumbent { get; }
        public float Ability { get; }
        public float Merit { get; }
        public float FamilyInfluence { get; }
        public float SchoolCompatibility { get; }
        public float FactionInfluence { get; }

        private static float NonNegative(float value)
        {
            return float.IsNaN(value) ? 0f : Math.Max(0f, value);
        }
    }

    public static class WesternCourtElectionRules
    {
        public const int TermYears = 6;
        public const int MaxVacanciesPerCycle = 2;
        public const int MaxCandidatesPerVacancy = 32;

        public static int TermEndYear(int appointmentYear)
        {
            return appointmentYear >= int.MaxValue - TermYears
                ? int.MaxValue
                : appointmentYear + TermYears;
        }

        public static bool ShouldQueueVacancy(bool hasIncumbent,
            int termEndYear, int currentYear)
        {
            return !hasIncumbent || termEndYear <= currentYear;
        }

        public static bool CanStand(bool eligible, bool isKing)
        {
            return eligible && !isKing;
        }

        public static WesternCourtElectionCandidate SelectWinner(
            IEnumerable<WesternCourtElectionCandidate> candidates)
        {
            WesternCourtElectionCandidate best = EmptyCandidate();
            float bestScore = float.MinValue;
            int inspected = 0;
            foreach (WesternCourtElectionCandidate candidate in
                     candidates ??
                     Array.Empty<WesternCourtElectionCandidate>())
            {
                if (inspected++ >= MaxCandidatesPerVacancy) break;
                if (candidate.ActorId < 0 ||
                    !CanStand(candidate.Eligible, candidate.IsKing))
                    continue;
                float score = Score(candidate);
                if (score < bestScore ||
                    Math.Abs(score - bestScore) < 0.0001f &&
                    best.ActorId >= 0 &&
                    candidate.ActorId >= best.ActorId) continue;
                best = candidate;
                bestScore = score;
            }
            return best;
        }

        public static float Score(WesternCourtElectionCandidate candidate)
        {
            return candidate.Ability * 4f +
                   candidate.Merit * 3f +
                   candidate.FamilyInfluence * 2f +
                   candidate.SchoolCompatibility * 2f +
                   candidate.FactionInfluence +
                   (candidate.Incumbent ? 5f : 0f);
        }

        public static string ResolveGovernmentInstitution(bool elective,
            bool feudal, bool royalDirect)
        {
            if (royalDirect)
                return CourtInstitutionId.WesternRoyalDirect;
            if (feudal)
                return CourtInstitutionId.WesternFeudal;
            if (elective)
                return CourtInstitutionId.WesternElective;
            return CourtInstitutionId.WesternBase;
        }

        public static bool CanManualAppoint(string institutionId)
        {
            return string.Equals(institutionId,
                CourtInstitutionId.WesternRoyalDirect,
                StringComparison.Ordinal);
        }

        private static WesternCourtElectionCandidate EmptyCandidate()
        {
            return new WesternCourtElectionCandidate(-1L,
                eligible: false, isKing: false, incumbent: false,
                ability: 0f, merit: 0f, familyInfluence: 0f,
                schoolCompatibility: 0f, factionInfluence: 0f);
        }
    }
}
