using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;

namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolAnnualStageId
    {
        public const string Bootstrap = "bootstrap";
        public const string XiaCityScan = "xia_city_scan";
        public const string Descent = "descent";
        public const string Guest = "guest";
        public const string LedgerDecay = "ledger_decay";
        public const string AnnualSnapshot = "annual_snapshot";
        public const string Action = "action";
        public const string Debate = "debate";
        public const string RuntimeSave = "runtime_save";
    }

    public sealed class HistoricalSchoolAnnualStageRunner
    {
        private readonly Action<string, Exception> _recordFailure;

        public HistoricalSchoolAnnualStageRunner(Action<string, Exception> pRecordFailure)
        {
            _recordFailure = pRecordFailure ??
                             throw new ArgumentNullException(nameof(pRecordFailure));
        }

        public bool TryRun(string pStageId, Action pStage)
        {
            try
            {
                if (pStage == null) throw new ArgumentNullException(nameof(pStage));
                pStage();
                return true;
            }
            catch (Exception error)
            {
                RecordFailure(pStageId, error);
                return false;
            }
        }

        public bool TryRun<TResult>(string pStageId, Func<TResult> pStage,
            out TResult pResult)
        {
            try
            {
                if (pStage == null) throw new ArgumentNullException(nameof(pStage));
                pResult = pStage();
                return true;
            }
            catch (Exception error)
            {
                pResult = default;
                RecordFailure(pStageId, error);
                return false;
            }
        }

        private void RecordFailure(string pStageId, Exception pError)
        {
            try { _recordFailure(pStageId ?? "", pError); }
            catch { }
        }
    }

    public sealed class HistoricalSchoolBootstrapRetryGate
    {
        public const int InitialRetryDelayFrames = 30;
        public const int MaxRetryDelayFrames = 240;

        private int _failedAttempts;
        private long _frame;
        private long _nextAttemptFrame;
        private long _attemptedFrame = long.MinValue;

        public bool CanAttempt()
        {
            if (_frame < _nextAttemptFrame || _attemptedFrame == _frame) return false;
            _attemptedFrame = _frame;
            return true;
        }

        public void AdvanceFrame()
        {
            if (_frame < long.MaxValue) _frame++;
        }

        public int RecordFailure()
        {
            _failedAttempts = Math.Min(31, _failedAttempts + 1);
            int delay = DelayForAttempt(_failedAttempts);
            _nextAttemptFrame = _frame > long.MaxValue - delay
                ? long.MaxValue
                : _frame + delay;
            return delay;
        }

        public void RecordSuccess()
        {
            _failedAttempts = 0;
            _nextAttemptFrame = _frame;
        }

        public void Clear()
        {
            _failedAttempts = 0;
            _frame = 0L;
            _nextAttemptFrame = 0L;
            _attemptedFrame = long.MinValue;
        }

        private static int DelayForAttempt(int pFailedAttempts)
        {
            int shift = Math.Min(3, Math.Max(0, pFailedAttempts - 1));
            return Math.Min(MaxRetryDelayFrames, InitialRetryDelayFrames << shift);
        }
    }

    public sealed class HistoricalSchoolPendingRuntimeState
    {
        public const int InitialRetryDelayFrames = 30;
        public const int MaxRetryDelayFrames = 240;

        private int _pendingEligibleYear;
        private int _pendingWorldYear = -1;
        private double _pendingTime;
        private int _retryDelayFrames;
        private int _remainingRetryFrames;

        public bool HasPending { get; private set; }
        public int PendingEligibleYear => _pendingEligibleYear;
        public int PendingWorldYear => _pendingWorldYear;

        public void Freeze(int pEligibleYear, int pWorldYear, double pTime)
        {
            int eligibleYear = Math.Max(0, pEligibleYear);
            double time = double.IsNaN(pTime) || double.IsInfinity(pTime)
                ? 0d
                : Math.Max(0d, pTime);
            if (!HasPending)
            {
                _pendingEligibleYear = eligibleYear;
                _pendingWorldYear = pWorldYear;
                _pendingTime = time;
                HasPending = true;
                return;
            }

            _pendingEligibleYear = Math.Max(_pendingEligibleYear, eligibleYear);
            _pendingWorldYear = Math.Max(_pendingWorldYear, pWorldYear);
            _pendingTime = Math.Max(_pendingTime, time);
        }

        public bool AdvanceAndTryFlush(Func<int, int, double, bool> pPersist)
        {
            if (!HasPending) return true;
            if (_remainingRetryFrames > 0)
            {
                _remainingRetryFrames--;
                return false;
            }
            return TryFlush(pPersist);
        }

        public bool FlushForSave(Func<int, int, double, bool> pPersist)
        {
            return !HasPending || TryFlush(pPersist);
        }

        public void Clear()
        {
            HasPending = false;
            _pendingEligibleYear = 0;
            _pendingWorldYear = -1;
            _pendingTime = 0d;
            _retryDelayFrames = 0;
            _remainingRetryFrames = 0;
        }

        private bool TryFlush(Func<int, int, double, bool> pPersist)
        {
            bool persisted;
            try
            {
                persisted = pPersist != null && pPersist(_pendingEligibleYear,
                    _pendingWorldYear, _pendingTime);
            }
            catch
            {
                persisted = false;
            }

            if (persisted)
            {
                Clear();
                return true;
            }

            _retryDelayFrames = _retryDelayFrames <= 0
                ? InitialRetryDelayFrames
                : Math.Min(MaxRetryDelayFrames, _retryDelayFrames * 2);
            _remainingRetryFrames = _retryDelayFrames;
            return false;
        }
    }

    public static class HistoricalSchoolRules
    {
        public const int MaxDescentsPerEligibleYear = 2;
        public const int TravelReturnCooldownYears = 12;
        public const int MaxNonHistoricalItinerantsPerSchool = 6;

        public static int WaveForOrder(int pOrder)
        {
            switch (pOrder)
            {
                case 1: return 1;
                case 2: return 2;
                case 3:
                case 4: return 3;
                case 5: return 4;
                case 6: return 5;
                default: return 0;
            }
        }

        public static int WaveOpeningYear(int pWave)
        {
            switch (pWave)
            {
                case 1: return 10;
                case 2: return 35;
                case 3: return 70;
                case 4: return 120;
                case 5: return 180;
                default: return int.MaxValue;
            }
        }

        public static int AdvanceEligibleYear(int pCurrentEligibleYear,
            bool pHasLivingXiaCity)
        {
            return pHasLivingXiaCity ? Math.Max(0, pCurrentEligibleYear) + 1 :
                Math.Max(0, pCurrentEligibleYear);
        }

        public static IReadOnlyList<HistoricalSchoolMasterDefinition> SelectDue(
            int pEligibleYear, HistoricalSchoolDescentLedger pLedger, int pLimit = 2)
        {
            if (pLedger == null || pEligibleYear <= 0 || pLimit <= 0)
                return Array.Empty<HistoricalSchoolMasterDefinition>();
            int limit = Math.Min(MaxDescentsPerEligibleYear, pLimit);
            var nextBySchool = HistoricalSchoolMasterRegistry.All
                .Where(p => !pLedger.IsSpawned(p.Id))
                .GroupBy(p => p.SchoolId, StringComparer.Ordinal)
                .Select(p => p.OrderBy(v => v.Order).ThenBy(v => v.RegistryIndex).First())
                .Where(p => pEligibleYear >= WaveOpeningYear(p.Wave))
                .ToList();
            if (nextBySchool.Count == 0) return Array.Empty<HistoricalSchoolMasterDefinition>();

            var localCounts = nextBySchool.Select(p => p.SchoolId)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(p => p, p => pLedger.CountForSchool(p), StringComparer.Ordinal);
            var localLastYears = nextBySchool.Select(p => p.SchoolId)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(p => p, p => pLedger.LastSelectionYear(p), StringComparer.Ordinal);
            var result = new List<HistoricalSchoolMasterDefinition>(limit);
            while (result.Count < limit && nextBySchool.Count > 0)
            {
                HistoricalSchoolMasterDefinition selected = nextBySchool
                    .OrderBy(p => localCounts[p.SchoolId])
                    .ThenBy(p => p.Wave)
                    .ThenBy(p => p.Order)
                    .ThenBy(p => localLastYears[p.SchoolId])
                    .ThenBy(p => p.RegistryIndex)
                    .ThenBy(p => p.Id, StringComparer.Ordinal)
                    .First();
                result.Add(selected);
                nextBySchool.Remove(selected);
                localCounts[selected.SchoolId]++;
                localLastYears[selected.SchoolId] = pEligibleYear;
            }
            return result;
        }

        public static HistoricalSchoolHomeCandidate SelectHome(
            HistoricalSchoolMasterDefinition pMaster,
            IEnumerable<HistoricalSchoolHomeCandidate> pCandidates)
        {
            if (pMaster == null || pCandidates == null) return null;
            List<HistoricalSchoolHomeCandidate> living = pCandidates
                .Where(p => p != null && p.LivingXia && p.KingdomId >= 0 && p.CityId >= 0)
                .ToList();
            if (living.Count == 0) return null;
            List<HistoricalSchoolHomeCandidate> preferred = living.Where(p =>
                    pMaster.PreferredStateNames.Any(name => StateNameMatches(name, p.KingdomName)))
                .ToList();
            List<HistoricalSchoolHomeCandidate> pool = preferred.Count > 0 ? preferred : living;
            return pool.OrderBy(p => p.ExistingMasterCount)
                .ThenByDescending(p => p.Capital)
                .ThenByDescending(p => p.Development)
                .ThenByDescending(p => p.Population)
                .ThenBy(p => p.KingdomId)
                .ThenBy(p => p.CityId)
                .First();
        }

        public static bool StateNameMatches(string pPreferredName, string pCurrentName)
        {
            return string.Equals(NormalizeStateName(pPreferredName),
                NormalizeStateName(pCurrentName), StringComparison.Ordinal);
        }

        private static string NormalizeStateName(string pName)
        {
            string value = (pName ?? "").Trim();
            foreach (string suffix in new[] { "共和国", "帝国", "王国", "义军", "朝", "国" })
                if (value.Length > suffix.Length && value.EndsWith(suffix,
                        StringComparison.Ordinal))
                    return value.Substring(0, value.Length - suffix.Length);
            return value;
        }

        public static bool CanTravelTransition(HistoricalSchoolLifecycleState pFrom,
            HistoricalSchoolLifecycleState pTo)
        {
            if (pFrom == HistoricalSchoolLifecycleState.AtHome ||
                pFrom == HistoricalSchoolLifecycleState.Resident)
                return pTo == HistoricalSchoolLifecycleState.ChoosingDestination;
            if (pFrom == HistoricalSchoolLifecycleState.ChoosingDestination)
                return pTo == HistoricalSchoolLifecycleState.Travelling;
            if (pFrom == HistoricalSchoolLifecycleState.Travelling)
                return pTo == HistoricalSchoolLifecycleState.Resident ||
                       pTo == HistoricalSchoolLifecycleState.Voyage;
            return pFrom == HistoricalSchoolLifecycleState.Voyage &&
                   pTo == HistoricalSchoolLifecycleState.Resident;
        }

        public static int TravelBucket(long pActorId)
        {
            long positive = pActorId == long.MinValue ? long.MaxValue : Math.Abs(pActorId);
            return (int)(positive % 4L);
        }

        public static float ScoreTravelDestination(HistoricalSchoolTravelContext pContext,
            HistoricalSchoolTravelCandidate pCandidate)
        {
            if (pContext == null || pCandidate == null || pContext.Serving ||
                pCandidate.CityId < 0 || pCandidate.CityId == pContext.ResidenceCityId)
                return float.NegativeInfinity;
            if (pCandidate.CityId == pContext.PreviousResidenceCityId &&
                pContext.LastTravelYear >= 0 &&
                pContext.CurrentYear - pContext.LastTravelYear < TravelReturnCooldownYears)
                return float.NegativeInfinity;

            float score = Math.Min(20f, (float)Math.Sqrt(pCandidate.Population));
            score += Math.Min(20f, pCandidate.Development * 0.2f);
            if (pCandidate.Capital) score += 10f;
            score += pCandidate.SchoolUnderrepresentation * 25f;
            score += Math.Min(12f, pCandidate.DebateRivals * 3f);
            score += Math.Min(10f, pCandidate.DiscipleCandidates * 0.5f);
            if (pCandidate.ReceptiveRuler) score += 8f;
            if (pCandidate.OpenOffice) score += 5f;
            score += pCandidate.ProblemMatch * 12f;
            score += pCandidate.TransportAvailable ? 4f : -8f;
            if (pCandidate.AtWar) score -= 25f;
            if (pCandidate.Occupied) score -= 35f;
            if (pCandidate.Disaster) score -= 30f;
            score -= Math.Min(15f, (float)Math.Sqrt(pCandidate.SquaredDistance) * 0.05f);
            return score;
        }

        public static HistoricalSchoolTravelCandidate SelectTravelDestination(
            HistoricalSchoolTravelContext pContext,
            IEnumerable<HistoricalSchoolTravelCandidate> pCandidates, int pLimit)
        {
            if (pContext == null || pCandidates == null || pContext.Serving || pLimit <= 0)
                return null;
            return pCandidates.Where(p => p != null)
                .OrderBy(p => StableCandidateOrder(pContext.ActorId, p.CityId))
                .Take(pLimit)
                .Select(p => new { Candidate = p, Score = ScoreTravelDestination(pContext, p) })
                .Where(p => !float.IsNegativeInfinity(p.Score) && p.Score > 0f)
                .OrderByDescending(p => p.Score)
                .ThenBy(p => p.Candidate.CityId)
                .Select(p => p.Candidate)
                .FirstOrDefault();
        }

        public static TResult[] BuildStableTravelCandidateWindow<TSource, TResult>(
            long pActorId, IEnumerable<TSource> pCandidates,
            Func<TSource, long> pCityId, Func<TSource, TResult> pProfileFactory, int pLimit)
        {
            if (pCandidates == null || pCityId == null || pProfileFactory == null ||
                pLimit <= 0) return Array.Empty<TResult>();
            return pCandidates
                .OrderBy(candidate => StableCandidateOrder(pActorId, pCityId(candidate)))
                .Take(pLimit)
                .Select(pProfileFactory)
                .ToArray();
        }

        public static bool CanStartTimedVoyage(bool pTravelEligible,
            int pTransportFailures, int pWaitingYears, bool pServing)
        {
            return pTravelEligible && !pServing && pTransportFailures >= 2 &&
                   pWaitingYears >= 5;
        }

        public static int VoyageArrivalYear(int pCurrentYear, int pSquaredDistance)
        {
            int years = 2 + (int)Math.Ceiling(Math.Sqrt(Math.Max(0, pSquaredDistance)) / 40d);
            years = Math.Max(2, Math.Min(12, years));
            return pCurrentYear + years;
        }

        private static long StableCandidateOrder(long pActorId, long pCityId)
        {
            unchecked
            {
                long value = pActorId * 6364136223846793005L +
                             pCityId * 1442695040888963407L;
                return value ^ value >> 33;
            }
        }

        public static int AnnualDirectDiscipleLimit(long pTeacherActorId, int pYear)
        {
            unchecked
            {
                long value = pTeacherActorId * 31L + pYear * 17L;
                return (value & 1L) == 0L ? 1 : 2;
            }
        }

        public static long TeacherOrder(long pActorId, int pYear)
        {
            unchecked
            {
                long value = pActorId * 6364136223846793005L +
                             pYear * 1442695040888963407L;
                return value ^ value >> 33;
            }
        }

        public static bool CanRecruitDisciple(bool pRealActor, bool pAlive,
            bool pSameResidence, bool pAlreadyMember, int pDirectDiscipleCount,
            int pDirectDiscipleCap)
        {
            return pRealActor && pAlive && pSameResidence && !pAlreadyMember &&
                   pDirectDiscipleCap > 0 && pDirectDiscipleCount >= 0 &&
                   pDirectDiscipleCount < pDirectDiscipleCap;
        }

        public static SchoolLineageCandidate SelectLineageSuccessor(
            IEnumerable<SchoolLineageCandidate> pCandidates)
        {
            return (pCandidates ?? Array.Empty<SchoolLineageCandidate>())
                .Where(p => p != null && p.Alive && p.DirectDisciple && p.ActorId >= 0)
                .OrderByDescending(LineageSuccessorScore)
                .ThenBy(p => p.ActorId)
                .FirstOrDefault();
        }

        public static bool CanExplicitlyConvert(bool pHistoricalMaster,
            int pYearsWithoutOwnTeacher, float pRivalExposure, bool pRecordedAction)
        {
            return !pHistoricalMaster && pRecordedAction && pYearsWithoutOwnTeacher >= 3 &&
                   pRivalExposure >= 0.75f;
        }

        public static bool CanRediscover(int pLivingMemberCount, bool pHasPreservedSource,
            bool pHasRealReader)
        {
            return pLivingMemberCount == 0 && pHasPreservedSource && pHasRealReader;
        }

        private static float LineageSuccessorScore(SchoolLineageCandidate pCandidate)
        {
            return pCandidate.Reputation * 2f + pCandidate.Learning +
                   pCandidate.DebateWins * 10f + pCandidate.FollowerCount * 5f;
        }
    }
}
