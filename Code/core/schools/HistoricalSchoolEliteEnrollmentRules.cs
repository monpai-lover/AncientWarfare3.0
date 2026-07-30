using System;
using System.Collections.Generic;
using System.Linq;

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
        DeclinedNoble = 6,
        LocalOfficial = 7,
        AcademyCommoner = 8
    }

    public readonly struct HistoricalSchoolEliteCandidate
    {
        public HistoricalSchoolEliteCandidate(long pKingdomId,
            long pActorId, HistoricalSchoolElitePriority pPriority)
            : this(pKingdomId, pActorId, pPriority, -1L, int.MaxValue)
        {
        }

        public HistoricalSchoolEliteCandidate(long pKingdomId,
            long pActorId, HistoricalSchoolElitePriority pPriority,
            long pCityId)
            : this(pKingdomId, pActorId, pPriority, pCityId, int.MaxValue)
        {
        }

        public HistoricalSchoolEliteCandidate(long pKingdomId,
            long pActorId, HistoricalSchoolElitePriority pPriority,
            long pCityId, int pAge)
            : this(pKingdomId, pActorId, pPriority, pCityId, pAge,
                pExamPipelineEligible: false)
        {
        }

        public HistoricalSchoolEliteCandidate(long pKingdomId,
            long pActorId, HistoricalSchoolElitePriority pPriority,
            long pCityId, int pAge, bool pExamPipelineEligible)
        {
            KingdomId = pKingdomId;
            ActorId = pActorId;
            Priority = pPriority;
            CityId = pCityId;
            Age = pAge < 0 ? int.MaxValue : pAge;
            ExamPipelineEligible = pExamPipelineEligible;
        }

        public long KingdomId { get; }
        public long ActorId { get; }
        public HistoricalSchoolElitePriority Priority { get; }
        public long CityId { get; }
        public int Age { get; }
        public bool ExamPipelineEligible { get; }
        public bool IsValid => KingdomId >= 0 && ActorId >= 0;
    }

    public static class HistoricalSchoolEliteEnrollmentRules
    {
        public const int MaxSuccessfulJoinsPerRealmPerYear = 6;
        public const int MaxSuccessfulJoinsPerRealmHardCap = 16;
        public const int MaxCandidateAttemptsPerRealmPerYear = 24;
        public const int MaxTeacherIdsPerSchool = 8;
        public const int MaxNobleArchiveRowsPerRealmYear = 24;
        public const int MaxDeclinedNobleAdmissionsPerRealmYear = 1;
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

        public static bool IsDeclinedNobleCandidateEligible(bool valid,
            bool adult, bool currentNoble, bool everNoble, long lineageId,
            bool domestic)
        {
            return valid && adult && !currentNoble &&
                   (everNoble || lineageId >= 0L) && domestic;
        }

        public static bool CanReserveAdmission(int currentReservations,
            int annualLimit)
        {
            return currentReservations >= 0 && annualLimit > 0 &&
                   currentReservations < annualLimit;
        }

        public static bool CanUseAdmissionSlot(int currentReservations,
            int normalLimit, int expandedLimit, bool examPipelineEligible)
        {
            if (currentReservations < 0 || normalLimit < 0 ||
                expandedLimit <= 0 || currentReservations >= expandedLimit)
                return false;
            return currentReservations < Math.Min(normalLimit, expandedLimit) ||
                   examPipelineEligible;
        }

        public static bool IsExamPipelineEducationPriority(
            HistoricalSchoolElitePriority priority)
        {
            return priority == HistoricalSchoolElitePriority.TitledNoble ||
                   priority == HistoricalSchoolElitePriority.UntitledNoble ||
                   priority == HistoricalSchoolElitePriority.DeclinedNoble ||
                   priority == HistoricalSchoolElitePriority.AcademyCommoner;
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

        public static int RealmSuccessfulJoinLimitForExamPipeline(
            bool examinationEnabled, int qualifiedTeachers, int academies,
            int eligibleLocalCandidates, int targetCandidates)
        {
            int normalLimit = RealmSuccessfulJoinLimit(qualifiedTeachers,
                academies);
            if (!examinationEnabled) return normalLimit;
            int deficit = Math.Max(0, targetCandidates -
                Math.Max(0, eligibleLocalCandidates));
            return Math.Min(MaxSuccessfulJoinsPerRealmHardCap,
                normalLimit + deficit);
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
            return SelectCandidates(pCandidates, pYear, pPerRealmLimit,
                pNormalAdmissionLimits: null,
                pExpandedAdmissionLimits: null);
        }

        public static IReadOnlyList<HistoricalSchoolEliteCandidate>
            SelectCandidates(
                IReadOnlyList<HistoricalSchoolEliteCandidate> pCandidates,
                int pYear, int pPerRealmLimit,
                IReadOnlyDictionary<long, int> pNormalAdmissionLimits,
                IReadOnlyDictionary<long, int> pExpandedAdmissionLimits)
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
                    candidate.Priority < existing.Priority ||
                    candidate.Priority == existing.Priority &&
                    (CompareByAgeThenActor(candidate, existing) < 0 ||
                     CompareByAgeThenActor(candidate, existing) == 0 &&
                     candidate.ExamPipelineEligible &&
                     !existing.ExamPipelineEligible))
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
                int realmResultStart = result.Count;
                Dictionary<long, HistoricalSchoolEliteCandidate> byActor =
                    byRealm[realmId];
                var academy = new List<HistoricalSchoolEliteCandidate>();
                var declined = new List<HistoricalSchoolEliteCandidate>();
                foreach (HistoricalSchoolEliteCandidate candidate in
                         byActor.Values)
                {
                    if (candidate.Priority ==
                        HistoricalSchoolElitePriority.AcademyCommoner)
                        academy.Add(candidate);
                    else if (candidate.Priority ==
                             HistoricalSchoolElitePriority.DeclinedNoble)
                        declined.Add(candidate);
                }
                academy.Sort(CompareByAgeThenActor);
                declined.Sort(CompareByAgeThenActor);
                int academyReserve = Math.Min(
                    MaxCommonerAdmissionsPerAcademyYear,
                    Math.Min(academy.Count, limit));
                int declinedReserve = Math.Min(
                    MaxDeclinedNobleAdmissionsPerRealmYear,
                    Math.Min(declined.Count, limit - academyReserve));
                int priorityLimit = limit - academyReserve - declinedReserve;
                int selected = 0;
                var selectedActorIds = new HashSet<long>();
                for (int priority = (int)HistoricalSchoolElitePriority.Ruler;
                     priority <=
                     (int)HistoricalSchoolElitePriority.LocalOfficial &&
                     selected < priorityLimit;
                     priority++)
                {
                    if (priority ==
                        (int)HistoricalSchoolElitePriority.DeclinedNoble)
                        continue;
                    var bucket = new List<HistoricalSchoolEliteCandidate>();
                    foreach (HistoricalSchoolEliteCandidate candidate in
                             byActor.Values)
                    {
                        if ((int)candidate.Priority == priority)
                            bucket.Add(candidate);
                    }
                    bucket.Sort(CompareByAgeThenActor);
                    if (bucket.Count == 0) continue;
                    for (int offset = 0;
                         offset < bucket.Count && selected < priorityLimit;
                         offset++)
                    {
                        HistoricalSchoolEliteCandidate candidate =
                            bucket[offset];
                        result.Add(candidate);
                        selectedActorIds.Add(candidate.ActorId);
                        selected++;
                    }
                }

                for (int offset = 0;
                     offset < declined.Count && offset < declinedReserve;
                     offset++)
                {
                    HistoricalSchoolEliteCandidate candidate =
                        declined[offset];
                    if (!selectedActorIds.Add(candidate.ActorId)) continue;
                    result.Add(candidate);
                    selected++;
                }
                for (int offset = 0; offset < academy.Count &&
                     offset < academyReserve && selected < limit; offset++)
                {
                    HistoricalSchoolEliteCandidate candidate = academy[offset];
                    if (!selectedActorIds.Add(candidate.ActorId)) continue;
                    result.Add(candidate);
                    selected++;
                }

                foreach (HistoricalSchoolEliteCandidate candidate in
                         byActor.Values.OrderBy(p => p.Priority)
                             .ThenBy(p => p.Age)
                             .ThenBy(p => p.ActorId))
                {
                    if (selected >= limit) break;
                    if (!selectedActorIds.Add(candidate.ActorId)) continue;
                    result.Add(candidate);
                    selected++;
                }
                ReorderForExamPipeline(result, realmResultStart,
                    byActor.Values, limit, realmId,
                    pNormalAdmissionLimits, pExpandedAdmissionLimits);
            }
            return result;
        }

        private static void ReorderForExamPipeline(
            List<HistoricalSchoolEliteCandidate> pResult, int pRealmStart,
            IEnumerable<HistoricalSchoolEliteCandidate> pAllCandidates,
            int pAttemptLimit, long pRealmId,
            IReadOnlyDictionary<long, int> pNormalAdmissionLimits,
            IReadOnlyDictionary<long, int> pExpandedAdmissionLimits)
        {
            if (pResult == null || pRealmStart < 0 ||
                pRealmStart > pResult.Count || pAttemptLimit <= 0 ||
                pNormalAdmissionLimits == null ||
                pExpandedAdmissionLimits == null ||
                !pNormalAdmissionLimits.TryGetValue(pRealmId,
                    out int normalLimit) ||
                !pExpandedAdmissionLimits.TryGetValue(pRealmId,
                    out int expandedLimit) || expandedLimit <= normalLimit)
                return;

            int realmCount = pResult.Count - pRealmStart;
            var baseline = pResult.GetRange(pRealmStart, realmCount);
            pResult.RemoveRange(pRealmStart, realmCount);
            var reordered = new List<HistoricalSchoolEliteCandidate>(
                pAttemptLimit);
            var selected = new HashSet<long>();
            int normalCount = Math.Min(Math.Max(0, normalLimit),
                baseline.Count);
            for (int index = 0; index < normalCount; index++)
            {
                reordered.Add(baseline[index]);
                selected.Add(baseline[index].ActorId);
            }

            int extraSeats = Math.Max(0, expandedLimit - normalLimit);
            foreach (HistoricalSchoolEliteCandidate candidate in
                     pAllCandidates.Where(p => p.ExamPipelineEligible)
                         .OrderBy(p => p.Priority)
                         .ThenBy(p => p.Age)
                         .ThenBy(p => p.ActorId))
            {
                if (extraSeats <= 0 || reordered.Count >= pAttemptLimit) break;
                if (!selected.Add(candidate.ActorId)) continue;
                reordered.Add(candidate);
                extraSeats--;
            }

            foreach (HistoricalSchoolEliteCandidate candidate in baseline)
            {
                if (reordered.Count >= pAttemptLimit) break;
                if (!selected.Add(candidate.ActorId)) continue;
                reordered.Add(candidate);
            }
            foreach (HistoricalSchoolEliteCandidate candidate in
                     pAllCandidates.OrderBy(p => p.Priority)
                         .ThenBy(p => p.Age)
                         .ThenBy(p => p.ActorId))
            {
                if (reordered.Count >= pAttemptLimit) break;
                if (!selected.Add(candidate.ActorId)) continue;
                reordered.Add(candidate);
            }
            pResult.AddRange(reordered);
        }

        private static int CompareByAgeThenActor(
            HistoricalSchoolEliteCandidate pLeft,
            HistoricalSchoolEliteCandidate pRight)
        {
            int age = pLeft.Age.CompareTo(pRight.Age);
            return age != 0 ? age : pLeft.ActorId.CompareTo(pRight.ActorId);
        }

        private static int PositiveModulo(int pValue, int pCount)
        {
            if (pCount <= 0) return 0;
            int value = pValue % pCount;
            return value < 0 ? value + pCount : value;
        }
    }
}
