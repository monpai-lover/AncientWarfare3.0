using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.schools
{
    public sealed class HistoricalSchoolDebateCandidate
    {
        public HistoricalSchoolDebateCandidate(long pActorId, string pSchoolId,
            float pReputation, float pLearning, float pDiplomacy, float pIntelligence,
            float pWarfare, float pStewardship, int pDirectDiscipleCount, int pDebateWins,
            float pProblemMatch, IEnumerable<string> pTopics)
            : this(pActorId, pSchoolId, pReputation, pLearning, pDiplomacy, pIntelligence,
                pWarfare, pStewardship, pDirectDiscipleCount, pDebateWins, pProblemMatch,
                pTopics, true, true)
        {
        }

        public HistoricalSchoolDebateCandidate(long pActorId, string pSchoolId,
            float pReputation, float pLearning, float pDiplomacy, float pIntelligence,
            float pWarfare, float pStewardship, int pDirectDiscipleCount, int pDebateWins,
            float pProblemMatch, IEnumerable<string> pTopics, bool pAlive, bool pPresent)
        {
            ActorId = pActorId;
            SchoolId = pSchoolId ?? "";
            Reputation = Bound100(pReputation);
            Learning = Bound100(pLearning);
            Diplomacy = Bound100(pDiplomacy);
            Intelligence = Bound100(pIntelligence);
            Warfare = Bound100(pWarfare);
            Stewardship = Bound100(pStewardship);
            DirectDiscipleCount = Math.Max(0, pDirectDiscipleCount);
            DebateWins = Math.Max(0, pDebateWins);
            ProblemMatch = Bound01(pProblemMatch);
            Topics = Freeze(pTopics);
            Alive = pAlive;
            Present = pPresent;
        }

        public long ActorId { get; }
        public string SchoolId { get; }
        public float Reputation { get; }
        public float Learning { get; }
        public float Diplomacy { get; }
        public float Intelligence { get; }
        public float Warfare { get; }
        public float Stewardship { get; }
        public int DirectDiscipleCount { get; }
        public int DebateWins { get; }
        public float ProblemMatch { get; }
        public IReadOnlyList<string> Topics { get; }
        public bool Alive { get; }
        public bool Present { get; }

        private static IReadOnlyList<string> Freeze(IEnumerable<string> pValues)
        {
            if (pValues == null) return Array.Empty<string>();
            var values = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in pValues)
            {
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(value)) continue;
                values.Add(value);
            }
            return values.AsReadOnly();
        }

        private static float Bound01(float pValue)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue)) return 0f;
            return Math.Max(0f, Math.Min(1f, pValue));
        }

        private static float Bound100(float pValue)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue)) return 0f;
            return Math.Max(0f, Math.Min(100f, pValue));
        }
    }

    public sealed class HistoricalSchoolDebatePair
    {
        public HistoricalSchoolDebatePair(HistoricalSchoolDebateCandidate pFirst,
            HistoricalSchoolDebateCandidate pSecond)
        {
            First = pFirst;
            Second = pSecond;
        }

        public HistoricalSchoolDebateCandidate First { get; }
        public HistoricalSchoolDebateCandidate Second { get; }
    }

    public sealed class HistoricalSchoolDebateScore
    {
        public HistoricalSchoolDebateScore(long pActorId, double pValue)
        {
            ActorId = pActorId;
            Value = BoundedScore(pValue);
        }

        public long ActorId { get; }
        public double Value { get; }

        private static double BoundedScore(double pValue)
        {
            return double.IsNaN(pValue) || double.IsInfinity(pValue) || pValue <= 0d
                ? 0d
                : Math.Min(HistoricalSchoolDebateRules.MaxDebateScore, pValue);
        }
    }

    public sealed class HistoricalSchoolLedgerSnapshot
    {
        public HistoricalSchoolLedgerSnapshot(string pSchoolId, float pTradition,
            float pActivePresence, float pMomentum, int pLastActiveYear,
            float pMembership = 0f, float pInstitutions = 0f)
        {
            SchoolId = pSchoolId ?? "";
            Tradition = Bound01(pTradition);
            ActivePresence = Bound01(pActivePresence);
            Momentum = Bound01(pMomentum);
            Membership = Bound01(pMembership);
            Institutions = BoundInstitutions(pInstitutions);
            LastActiveYear = pLastActiveYear < -1 ? -1 : pLastActiveYear;
        }

        public string SchoolId { get; }
        public float Tradition { get; }
        public float ActivePresence { get; }
        public float Momentum { get; }
        public float Membership { get; }
        public float Institutions { get; }
        public int LastActiveYear { get; }

        private static float Bound01(float pValue)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue)) return 0f;
            return Math.Max(0f, Math.Min(1f, pValue));
        }

        private static float BoundInstitutions(float pValue)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue)) return 0f;
            return Math.Max(0f, Math.Min(100f, pValue));
        }
    }

    public sealed class HistoricalSchoolLedgerDelta
    {
        public HistoricalSchoolLedgerDelta(string pSchoolId, float pTradition,
            float pActivePresence, float pMomentum, float pInstitutions, int pLastActiveYear)
        {
            SchoolId = pSchoolId ?? "";
            Tradition = ClampNonNegative(pTradition,
                HistoricalSchoolDebateRules.MaxTraditionDelta);
            ActivePresence = ClampSigned(pActivePresence,
                HistoricalSchoolDebateRules.MaxActivePresenceDelta);
            Momentum = ClampSigned(pMomentum,
                HistoricalSchoolDebateRules.MaxMomentumDelta);
            Institutions = Math.Max(0f, Finite(pInstitutions));
            LastActiveYear = pLastActiveYear < -1 ? -1 : pLastActiveYear;
        }

        public HistoricalSchoolLedgerDelta(float pTradition, float pActivePresence,
            float pMomentum, float pInstitutions, int pLastActiveYear)
            : this("", pTradition, pActivePresence, pMomentum, pInstitutions,
                pLastActiveYear)
        {
        }

        public string SchoolId { get; }
        public float Tradition { get; }
        public float ActivePresence { get; }
        public float Momentum { get; }
        public float Institutions { get; }
        public int LastActiveYear { get; }

        public HistoricalSchoolLedgerDelta ForSchool(string pSchoolId)
        {
            return new HistoricalSchoolLedgerDelta(pSchoolId, Tradition, ActivePresence,
                Momentum, Institutions, LastActiveYear);
        }

        private static float Finite(float pValue)
        {
            return float.IsNaN(pValue) || float.IsInfinity(pValue) ? 0f : pValue;
        }

        private static float ClampNonNegative(float pValue, float pMaximum)
        {
            return Math.Max(0f, Math.Min(pMaximum, Finite(pValue)));
        }

        private static float ClampSigned(float pValue, float pMaximum)
        {
            return Math.Max(-pMaximum, Math.Min(pMaximum, Finite(pValue)));
        }
    }

    public sealed class HistoricalSchoolDebateRecord
    {
        public HistoricalSchoolDebateRecord(long pDebateId, long pCityId, int pDebateYear,
            string pTopicId, long pFirstActorId, string pFirstSchoolId, long pSecondActorId,
            string pSecondSchoolId, long pSeed, double pFirstScore, double pSecondScore,
            SchoolDebateOutcome pOutcome, bool pResolved = true, bool pPresented = false,
            double pUpdatedTime = 0d)
        {
            DebateId = SafeId(pDebateId);
            CityId = SafeId(pCityId);
            DebateYear = pDebateYear < -1 ? -1 : pDebateYear;
            TopicId = pTopicId ?? "";
            FirstActorId = SafeId(pFirstActorId);
            FirstSchoolId = pFirstSchoolId ?? "";
            SecondActorId = SafeId(pSecondActorId);
            SecondSchoolId = pSecondSchoolId ?? "";
            Seed = pSeed;
            FirstScore = BoundedScore(pFirstScore);
            SecondScore = BoundedScore(pSecondScore);
            Outcome = pOutcome;
            Resolved = pResolved;
            Presented = pPresented;
            UpdatedTime = FiniteNonNegative(pUpdatedTime);
        }

        public long DebateId { get; }
        public long CityId { get; }
        public int DebateYear { get; }
        public string TopicId { get; }
        public long FirstActorId { get; }
        public string FirstSchoolId { get; }
        public long SecondActorId { get; }
        public string SecondSchoolId { get; }
        public long Seed { get; }
        public double FirstScore { get; }
        public double SecondScore { get; }
        public SchoolDebateOutcome Outcome { get; }
        public bool Resolved { get; }
        public bool Presented { get; }
        public double UpdatedTime { get; }

        private static long SafeId(long pValue)
        {
            return pValue < -1L ? -1L : pValue;
        }

        private static double BoundedScore(double pValue)
        {
            return double.IsNaN(pValue) || double.IsInfinity(pValue) || pValue <= 0d
                ? 0d
                : Math.Min(HistoricalSchoolDebateRules.MaxDebateScore, pValue);
        }

        private static double FiniteNonNegative(double pValue)
        {
            return double.IsNaN(pValue) || double.IsInfinity(pValue) || pValue < 0d
                ? 0d
                : pValue;
        }
    }
}
