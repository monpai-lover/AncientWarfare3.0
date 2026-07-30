using System;
using System.Collections.Generic;
using System.Globalization;

namespace AncientWarfare3.core.schools
{
    public readonly struct HistoricalSchoolLectureCandidate
    {
        public HistoricalSchoolLectureCandidate(long pActorId, string pSchoolId,
            long pCityId, long pKingdomId, bool pCanonical, int pStartYear,
            float pReputation,
            HistoricalSchoolStanding pStanding = HistoricalSchoolStanding.Member)
        {
            ActorId = pActorId;
            SchoolId = pSchoolId ?? "";
            CityId = pCityId;
            KingdomId = pKingdomId;
            Canonical = pCanonical;
            StartYear = pStartYear;
            Reputation = pReputation;
            Standing = pStanding;
        }

        public long ActorId { get; }
        public string SchoolId { get; }
        public long CityId { get; }
        public long KingdomId { get; }
        public bool Canonical { get; }
        public int StartYear { get; }
        public float Reputation { get; }
        public HistoricalSchoolStanding Standing { get; }
        public bool IsValid => ActorId >= 0 && CityId >= 0 && KingdomId >= 0 &&
                               !string.IsNullOrWhiteSpace(SchoolId);
    }

    public readonly struct HistoricalSchoolTeachingPlan
    {
        internal HistoricalSchoolTeachingPlan(HistoricalSchoolLectureCandidate pCandidate,
            int pYear, bool pIncludePersuasion, bool pAnnounce)
        {
            Candidate = pCandidate;
            Year = pYear;
            IncludePersuasion = pIncludePersuasion;
            Announce = pAnnounce;
            OperationKey = HistoricalSchoolLectureRules.BuildOperationKey(
                pCandidate.ActorId, pYear, pCandidate.SchoolId, pCandidate.CityId);
        }

        public HistoricalSchoolLectureCandidate Candidate { get; }
        public int Year { get; }
        public bool IncludePersuasion { get; }
        public bool Announce { get; }
        public string OperationKey { get; }
        public bool IsValid => Candidate.IsValid && Year >= 0 &&
                               !string.IsNullOrEmpty(OperationKey);
    }

    public sealed class HistoricalSchoolTeachingHistory
    {
        private readonly Dictionary<long, int> _lastLectureByActor = new();
        private readonly Dictionary<long, int> _lastPersuasionByActor = new();
        private readonly Dictionary<string, int> _lastLectureByCitySchool =
            new(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _worldLecturesByYear = new();
        private readonly Dictionary<string, int> _cityLecturesByYear =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _schoolLecturesByYear =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _citySchoolLecturesByYear =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _kingdomPersuasionsByYear =
            new(StringComparer.Ordinal);

        public void RecordLecture(HistoricalSchoolLectureCandidate pCandidate, int pYear)
        {
            if (!pCandidate.IsValid || pYear < 0) return;
            SetLatest(_lastLectureByActor, pCandidate.ActorId, pYear);
            SetLatest(_lastLectureByCitySchool,
                HistoricalSchoolLectureRules.CitySchoolKey(pCandidate.CityId,
                    pCandidate.SchoolId), pYear);
            Increment(_worldLecturesByYear, pYear);
            Increment(_cityLecturesByYear, YearLongKey(pYear, pCandidate.CityId));
            Increment(_schoolLecturesByYear, YearTextKey(pYear, pCandidate.SchoolId));
            Increment(_citySchoolLecturesByYear, YearTextKey(pYear,
                HistoricalSchoolLectureRules.CitySchoolKey(pCandidate.CityId,
                    pCandidate.SchoolId)));
        }

        public void RecordPersuasion(HistoricalSchoolLectureCandidate pCandidate,
            int pYear)
        {
            if (!pCandidate.IsValid || pYear < 0) return;
            SetLatest(_lastPersuasionByActor, pCandidate.ActorId, pYear);
            Increment(_kingdomPersuasionsByYear,
                YearLongKey(pYear, pCandidate.KingdomId));
        }

        internal int LastLecture(long pActorId)
        {
            return _lastLectureByActor.TryGetValue(pActorId, out int year) ? year : -1;
        }

        internal int LastPersuasion(long pActorId)
        {
            return _lastPersuasionByActor.TryGetValue(pActorId, out int year) ? year : -1;
        }

        internal int LastCitySchoolLecture(long pCityId, string pSchoolId)
        {
            return _lastLectureByCitySchool.TryGetValue(
                HistoricalSchoolLectureRules.CitySchoolKey(pCityId, pSchoolId),
                out int year) ? year : -1;
        }

        internal int WorldLectures(int pYear)
        {
            return Count(_worldLecturesByYear, pYear);
        }

        internal int CityLectures(int pYear, long pCityId)
        {
            return Count(_cityLecturesByYear, YearLongKey(pYear, pCityId));
        }

        internal int SchoolLectures(int pYear, string pSchoolId)
        {
            return Count(_schoolLecturesByYear, YearTextKey(pYear, pSchoolId));
        }

        internal int CitySchoolLectures(int pYear, long pCityId, string pSchoolId)
        {
            return Count(_citySchoolLecturesByYear, YearTextKey(pYear,
                HistoricalSchoolLectureRules.CitySchoolKey(pCityId, pSchoolId)));
        }

        internal int KingdomPersuasions(int pYear, long pKingdomId)
        {
            return Count(_kingdomPersuasionsByYear, YearLongKey(pYear, pKingdomId));
        }

        public HistoricalSchoolTeachingHistory Clone()
        {
            var clone = new HistoricalSchoolTeachingHistory();
            Copy(_lastLectureByActor, clone._lastLectureByActor);
            Copy(_lastPersuasionByActor, clone._lastPersuasionByActor);
            Copy(_lastLectureByCitySchool, clone._lastLectureByCitySchool);
            Copy(_worldLecturesByYear, clone._worldLecturesByYear);
            Copy(_cityLecturesByYear, clone._cityLecturesByYear);
            Copy(_schoolLecturesByYear, clone._schoolLecturesByYear);
            Copy(_citySchoolLecturesByYear, clone._citySchoolLecturesByYear);
            Copy(_kingdomPersuasionsByYear, clone._kingdomPersuasionsByYear);
            return clone;
        }

        private static void SetLatest<TKey>(IDictionary<TKey, int> pValues, TKey pKey,
            int pYear)
        {
            if (!pValues.TryGetValue(pKey, out int current) || pYear > current)
                pValues[pKey] = pYear;
        }

        private static void Increment<TKey>(IDictionary<TKey, int> pValues, TKey pKey)
        {
            pValues.TryGetValue(pKey, out int count);
            pValues[pKey] = count + 1;
        }

        private static int Count<TKey>(IReadOnlyDictionary<TKey, int> pValues, TKey pKey)
        {
            return pValues.TryGetValue(pKey, out int count) ? count : 0;
        }

        private static void Copy<TKey>(IReadOnlyDictionary<TKey, int> pSource,
            IDictionary<TKey, int> pTarget)
        {
            foreach (KeyValuePair<TKey, int> entry in pSource)
                pTarget[entry.Key] = entry.Value;
        }

        private static string YearLongKey(int pYear, long pValue)
        {
            return pYear.ToString(CultureInfo.InvariantCulture) + ":" +
                   pValue.ToString(CultureInfo.InvariantCulture);
        }

        private static string YearTextKey(int pYear, string pValue)
        {
            string value = pValue ?? "";
            return pYear.ToString(CultureInfo.InvariantCulture) + ":" +
                   value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
        }
    }

    public sealed class HistoricalSchoolTeachingBudget
    {
        private readonly int _year;
        private readonly HistoricalSchoolTeachingHistory _history;
        private readonly HashSet<string> _committed = new(StringComparer.Ordinal);
        private int _announced;

        public HistoricalSchoolTeachingBudget(int pYear,
            HistoricalSchoolTeachingHistory pHistory)
        {
            _year = pYear;
            _history = pHistory ?? new HistoricalSchoolTeachingHistory();
            _announced = Math.Min(
                HistoricalSchoolLectureRules.MaxWorldLogAnnouncementsPerYear,
                _history.WorldLectures(_year));
        }

        public bool TryPlan(HistoricalSchoolLectureCandidate pCandidate,
            out HistoricalSchoolTeachingPlan pPlan)
        {
            pPlan = default;
            if (_year < 0 || !HistoricalSchoolLectureRules.IsTeacherEligible(pCandidate,
                    _year)) return false;
            if (_history.WorldLectures(_year) >=
                HistoricalSchoolLectureRules.MaxWorldLecturesPerYear) return false;
            if (_history.CityLectures(_year, pCandidate.CityId) >=
                HistoricalSchoolLectureRules.MaxCityLecturesPerYear) return false;
            if (_history.SchoolLectures(_year, pCandidate.SchoolId) >=
                HistoricalSchoolLectureRules.MaxSchoolLecturesPerYear) return false;
            if (_history.CitySchoolLectures(_year, pCandidate.CityId,
                    pCandidate.SchoolId) >=
                HistoricalSchoolLectureRules.MaxCitySchoolLecturesPerYear) return false;

            int actorCooldown = pCandidate.Canonical
                ? HistoricalSchoolLectureRules.CanonicalLectureCooldownYears
                : HistoricalSchoolLectureRules.LaterLectureCooldownYears;
            if (!HistoricalSchoolLectureRules.CooldownReady(_year,
                    _history.LastLecture(pCandidate.ActorId), actorCooldown)) return false;
            if (!HistoricalSchoolLectureRules.CooldownReady(_year,
                    _history.LastCitySchoolLecture(pCandidate.CityId, pCandidate.SchoolId),
                    HistoricalSchoolLectureRules.CitySchoolLectureCooldownYears))
                return false;

            bool persuasion = HistoricalSchoolLectureRules.CanPersuade(pCandidate) &&
                HistoricalSchoolLectureRules.CooldownReady(_year,
                    _history.LastPersuasion(pCandidate.ActorId),
                    HistoricalSchoolLectureRules.PersuasionCooldownYears) &&
                _history.KingdomPersuasions(_year, pCandidate.KingdomId) <
                HistoricalSchoolLectureRules.MaxKingdomPersuasionsPerYear;
            bool announce = _announced <
                HistoricalSchoolLectureRules.MaxWorldLogAnnouncementsPerYear &&
                (pCandidate.Canonical || _history.CitySchoolLectures(_year,
                    pCandidate.CityId, pCandidate.SchoolId) == 0);
            pPlan = new HistoricalSchoolTeachingPlan(pCandidate, _year, persuasion,
                announce);
            return true;
        }

        public bool Commit(HistoricalSchoolTeachingPlan pPlan)
        {
            if (!pPlan.IsValid || pPlan.Year != _year ||
                !_committed.Add(pPlan.OperationKey)) return false;
            _history.RecordLecture(pPlan.Candidate, _year);
            if (pPlan.IncludePersuasion)
                _history.RecordPersuasion(pPlan.Candidate, _year);
            if (pPlan.Announce && _announced <
                HistoricalSchoolLectureRules.MaxWorldLogAnnouncementsPerYear)
                _announced++;
            return true;
        }
    }

    public static class HistoricalSchoolLectureRules
    {
        public const int StablePopulationTarget = 50;
        public const int SustainableMemberCount = StablePopulationTarget;
        public const int CanonicalLectureCooldownYears = 3;
        public const int LaterLectureCooldownYears = 5;
        public const int MaxWorldLecturesPerYear = 8;
        public const int MaxCityLecturesPerYear = 2;
        public const int MaxSchoolLecturesPerYear = 2;
        public const int MaxCitySchoolLecturesPerYear = 1;
        public const int CitySchoolLectureCooldownYears = 2;
        public const int MaxWorldLogAnnouncementsPerYear = 4;
        public const float PersuasionMinimumReputation = 40f;
        public const int PersuasionCooldownYears = 5;
        public const int MaxKingdomPersuasionsPerYear = 1;

        public static int PopulationPriority(int livingMemberCount)
        {
            return Math.Max(0, SustainableMemberCount -
                               Math.Max(0, livingMemberCount));
        }

        public static IReadOnlyList<int> BuildPopulationPriorityOrder(
            IReadOnlyList<int> pLivingMemberCounts, int pStartIndex)
        {
            int count = pLivingMemberCounts?.Count ?? 0;
            if (count == 0) return Array.Empty<int>();
            int start = PositiveModulo(pStartIndex, count);
            var order = new int[count];
            for (int index = 0; index < count; index++) order[index] = index;
            Array.Sort(order, (left, right) =>
            {
                int priority = PopulationPriority(
                        pLivingMemberCounts[right])
                    .CompareTo(PopulationPriority(
                        pLivingMemberCounts[left]));
                if (priority != 0) return priority;
                return PositiveModulo(left - start, count).CompareTo(
                    PositiveModulo(right - start, count));
            });
            return order;
        }

        public static bool HasDiscipleCapacity(int pDirectDiscipleCount,
            int pDirectDiscipleCap)
        {
            return pDirectDiscipleCount >= 0 && pDirectDiscipleCap > 0 &&
                   pDirectDiscipleCount < pDirectDiscipleCap;
        }

        public static bool TeacherPrecedesForLecture(
            bool pHasDiscipleCapacity, int pStartYear, long pActorId,
            bool pOtherHasDiscipleCapacity, int pOtherStartYear,
            long pOtherActorId)
        {
            if (pHasDiscipleCapacity != pOtherHasDiscipleCapacity)
                return pHasDiscipleCapacity;
            return pStartYear < pOtherStartYear ||
                   pStartYear == pOtherStartYear && pActorId < pOtherActorId;
        }

        public static bool IsTeacherEligible(HistoricalSchoolLectureCandidate pCandidate,
            int pYear)
        {
            if (!pCandidate.IsValid || pYear < 0 || float.IsNaN(pCandidate.Reputation) ||
                float.IsInfinity(pCandidate.Reputation)) return false;
            if (pCandidate.Canonical) return true;
            return pCandidate.Standing == HistoricalSchoolStanding.Teacher ||
                   pCandidate.Standing == HistoricalSchoolStanding.Leader;
        }

        public static bool CanPersuade(HistoricalSchoolLectureCandidate pCandidate)
        {
            return pCandidate.IsValid && (pCandidate.Canonical ||
                pCandidate.Reputation >= PersuasionMinimumReputation);
        }

        public static bool CooldownReady(int pYear, int pLastYear,
            int pCooldownYears)
        {
            return pYear >= 0 && pCooldownYears >= 0 &&
                   (pLastYear < 0 || pYear - pLastYear >= pCooldownYears);
        }

        public static string BuildOperationKey(long pActorId, int pYear,
            string pSchoolId, long pCityId)
        {
            string school = pSchoolId ?? "";
            return "school-teaching:v1|actor=" +
                   pActorId.ToString(CultureInfo.InvariantCulture) + "|year=" +
                   pYear.ToString(CultureInfo.InvariantCulture) + "|school=" +
                   school.Length.ToString(CultureInfo.InvariantCulture) + ":" + school +
                   "|city=" + pCityId.ToString(CultureInfo.InvariantCulture);
        }

        internal static string CitySchoolKey(long pCityId, string pSchoolId)
        {
            string school = pSchoolId ?? "";
            return pCityId.ToString(CultureInfo.InvariantCulture) + ":" +
                   school.Length.ToString(CultureInfo.InvariantCulture) + ":" + school;
        }

        private static int PositiveModulo(int pValue, int pCount)
        {
            if (pCount <= 0) return 0;
            int value = pValue % pCount;
            return value < 0 ? value + pCount : value;
        }
    }
}
