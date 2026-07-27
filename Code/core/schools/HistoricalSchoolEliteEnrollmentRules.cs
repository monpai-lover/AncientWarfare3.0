using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public enum HistoricalSchoolElitePriority
    {
        Ruler = 0,
        Heir = 1,
        FeudatoryPrince = 2,
        TitledNoble = 3,
        CentralOfficial = 4,
        UntitledNoble = 5,
        LocalOfficial = 6,
        AcademyCommoner = 7
    }

    public readonly struct HistoricalSchoolEliteCandidate
    {
        public HistoricalSchoolEliteCandidate(long pKingdomId,
            long pActorId, HistoricalSchoolElitePriority pPriority)
            : this(pKingdomId, pActorId, pPriority, -1L)
        {
        }

        public HistoricalSchoolEliteCandidate(long pKingdomId,
            long pActorId, HistoricalSchoolElitePriority pPriority,
            long pCityId)
        {
            KingdomId = pKingdomId;
            ActorId = pActorId;
            Priority = pPriority;
            CityId = pCityId;
        }

        public long KingdomId { get; }
        public long ActorId { get; }
        public HistoricalSchoolElitePriority Priority { get; }
        public long CityId { get; }
        public bool IsValid => KingdomId >= 0 && ActorId >= 0;
    }

    public static class HistoricalSchoolEliteEnrollmentRules
    {
        public const int MaxSuccessfulJoinsPerRealmPerYear = 6;
        public const int MaxSuccessfulJoinsPerRealmHardCap = 16;
        public const int MaxCandidateAttemptsPerRealmPerYear = 24;
        public const int MaxTeacherIdsPerSchool = 8;
        public const int MaxNobleArchiveRowsPerRealmYear = 24;
        public const int MaxAcademyResidentsPerYear = 24;
        public const int MaxCommonerAdmissionsPerAcademyYear = 2;

        public static bool NeedsEnrollment(bool isValid,
            bool hasMembership, bool writePending)
        {
            return isValid && !hasMembership && !writePending;
        }

        public static bool IsNobleCandidateEligible(bool valid,
            bool adult, bool noble, bool domestic)
        {
            return valid && adult && noble && domestic;
        }

        public static bool CanReserveAdmission(int currentReservations,
            int annualLimit)
        {
            return currentReservations >= 0 && annualLimit > 0 &&
                   currentReservations < annualLimit;
        }

        public static bool CanReserveTeacher(int committedDisciples,
            int pendingAdmissions, int directDiscipleCap)
        {
            return committedDisciples >= 0 && pendingAdmissions >= 0 &&
                   directDiscipleCap > 0 &&
                   committedDisciples + pendingAdmissions <
                   directDiscipleCap;
        }

        public static int FrameAttemptBudget(int pRemainingCandidates)
        {
            return pRemainingCandidates > 0 ? 1 : 0;
        }

        public static int RealmPreparationBudget(int pRemainingRealms)
        {
            return pRemainingRealms > 0 ? 1 : 0;
        }

        public static int RealmSuccessfulJoinLimit(int qualifiedTeachers,
            int academies)
        {
            int teacherSeats = Math.Min(4,
                Math.Max(0, qualifiedTeachers) / 2);
            int academySeats = Math.Min(4,
                Math.Max(0, academies) * 2);
            return Math.Min(MaxSuccessfulJoinsPerRealmHardCap,
                MaxSuccessfulJoinsPerRealmPerYear + teacherSeats +
                academySeats);
        }

        public static bool IsAcademyCommonerEligible(bool valid,
            bool adult, bool localResident, bool noble, bool slave,
            bool madness, bool hasMembership, bool writePending,
            bool available)
        {
            return valid && adult && localResident && !noble && !slave &&
                   !madness && !hasMembership && !writePending && available;
        }

        public static float AcademyCandidateScore(float intelligence,
            float stewardship, float diplomacy)
        {
            return Math.Max(0f, intelligence) * 2f +
                   Math.Max(0f, stewardship) +
                   Math.Max(0f, diplomacy) * 0.75f;
        }

        public static IReadOnlyList<HistoricalSchoolEliteCandidate>
            SelectCandidates(
                IReadOnlyList<HistoricalSchoolEliteCandidate> pCandidates,
                int pYear, int pPerRealmLimit)
        {
            int limit = Math.Max(0, pPerRealmLimit);
            if (limit == 0 || pCandidates == null || pCandidates.Count == 0)
                return Array.Empty<HistoricalSchoolEliteCandidate>();

            var byRealm = new Dictionary<long, Dictionary<long,
                HistoricalSchoolEliteCandidate>>();
            for (int index = 0; index < pCandidates.Count; index++)
            {
                HistoricalSchoolEliteCandidate candidate =
                    pCandidates[index];
                if (!candidate.IsValid) continue;
                if (!byRealm.TryGetValue(candidate.KingdomId,
                        out Dictionary<long,
                            HistoricalSchoolEliteCandidate> byActor))
                {
                    byActor = new Dictionary<long,
                        HistoricalSchoolEliteCandidate>();
                    byRealm.Add(candidate.KingdomId, byActor);
                }
                if (!byActor.TryGetValue(candidate.ActorId,
                        out HistoricalSchoolEliteCandidate existing) ||
                    candidate.Priority < existing.Priority)
                    byActor[candidate.ActorId] = candidate;
            }

            var realmIds = new List<long>(byRealm.Keys);
            realmIds.Sort();
            var result = new List<HistoricalSchoolEliteCandidate>(
                realmIds.Count * limit);
            for (int realmIndex = 0; realmIndex < realmIds.Count;
                 realmIndex++)
            {
                long realmId = realmIds[realmIndex];
                Dictionary<long, HistoricalSchoolEliteCandidate> byActor =
                    byRealm[realmId];
                var academy = new List<HistoricalSchoolEliteCandidate>();
                foreach (HistoricalSchoolEliteCandidate candidate in
                         byActor.Values)
                    if (candidate.Priority ==
                        HistoricalSchoolElitePriority.AcademyCommoner)
                        academy.Add(candidate);
                academy.Sort((left, right) =>
                    left.ActorId.CompareTo(right.ActorId));
                int academyReserve = Math.Min(
                    MaxCommonerAdmissionsPerAcademyYear,
                    Math.Min(academy.Count, limit));
                int nonAcademyLimit = limit - academyReserve;
                int selected = 0;
                for (int priority = (int)HistoricalSchoolElitePriority.Ruler;
                     priority <=
                     (int)HistoricalSchoolElitePriority.LocalOfficial &&
                     selected < nonAcademyLimit;
                     priority++)
                {
                    var bucket = new List<HistoricalSchoolEliteCandidate>();
                    foreach (HistoricalSchoolEliteCandidate candidate in
                             byActor.Values)
                    {
                        if ((int)candidate.Priority == priority)
                            bucket.Add(candidate);
                    }
                    bucket.Sort((left, right) =>
                        left.ActorId.CompareTo(right.ActorId));
                    if (bucket.Count == 0) continue;
                    int start = PositiveModulo(pYear + realmIndex,
                        bucket.Count);
                    for (int offset = 0;
                         offset < bucket.Count && selected < nonAcademyLimit;
                         offset++)
                    {
                        result.Add(bucket[(start + offset) % bucket.Count]);
                        selected++;
                    }
                }

                if (academy.Count == 0) continue;
                int academyStart = PositiveModulo(pYear + realmIndex,
                    academy.Count);
                for (int offset = 0;
                     offset < academy.Count && selected < limit; offset++)
                {
                    result.Add(academy[(academyStart + offset) %
                                       academy.Count]);
                    selected++;
                }
            }
            return result;
        }

        private static int PositiveModulo(int pValue, int pCount)
        {
            if (pCount <= 0) return 0;
            int value = pValue % pCount;
            return value < 0 ? value + pCount : value;
        }
    }
}
