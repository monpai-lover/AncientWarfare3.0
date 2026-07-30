using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.schools
{
    internal enum HistoricalSchoolSchedulerStage
    {
        None,
        Bootstrap,
        Descent,
        ServiceClose,
        ServiceAppointment,
        Promotion,
        Recovery,
        LecturePlan,
        DebatePlan,
        Conversion,
        Rediscovery,
        RuntimeCommit
    }

    internal static class HistoricalSchoolScheduler
    {
        public const double FrameBudgetMilliseconds = 0.75d;

        private static readonly HistoricalSchoolSchedulerState Years =
            new HistoricalSchoolSchedulerState();
        private static readonly HistoricalSchoolPendingRuntimeState PendingRuntimeState =
            new HistoricalSchoolPendingRuntimeState();
        private static readonly HistoricalSchoolBootstrapRetryGate BootstrapRetryGate =
            new HistoricalSchoolBootstrapRetryGate();

        private static HistoricalSchoolSchedulerStage _stage;
        private static int _activeYear = -1;
        private static int _eligibleYear;
        private static int _nextEligibleYear;
        private static int _lastCompletedYear = -1;
        private static List<City> _livingXiaCities;

        public static int EligibleYear => _eligibleYear;
        public static bool HasPendingWork =>
            _stage != HistoricalSchoolSchedulerStage.None || Years.HasPendingWork();

        public static bool EnqueueYear(int pYear)
        {
            if (pYear <= _lastCompletedYear) return false;
            return Years.EnqueueYear(pYear);
        }

        public static void RestorePersistentState(int pEligibleYear, int pLastWorldYear)
        {
            _eligibleYear = Math.Max(0, pEligibleYear);
            _nextEligibleYear = _eligibleYear;
            _lastCompletedYear = pLastWorldYear;
        }

        public static bool ProcessFrame()
        {
            if (!HasPendingWork) return false;

            long started = Stopwatch.GetTimestamp();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                BootstrapRetryGate.AdvanceFrame();
                if (_stage == HistoricalSchoolSchedulerStage.None && !StartNextYear())
                    return false;

                HistoricalSchoolSchedulerStage current = _stage;
                long stageDiagnostic = RuntimePerformanceDiagnostic.BeginScope();
                bool advance;
                try { advance = ExecuteStage(current); }
                finally
                {
                    RuntimePerformanceDiagnostic.EndAnnualStage(
                        "school_" + StageId(current), stageDiagnostic);
                }
                if (advance && _stage == current) AdvanceStage();
                return true;
            }
            finally
            {
                HistoricalSchoolDiagnostics.RecordSchedulerFrame(
                    Stopwatch.GetTimestamp() - started,
                    GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
                    pIdle: false);
            }
        }

        public static bool FlushPendingStateForSave()
        {
            return PendingRuntimeState.FlushForSave(
                HistoricalSchoolStore.SaveRuntimeState);
        }

        public static void Clear()
        {
            Years.Clear();
            PendingRuntimeState.Clear();
            BootstrapRetryGate.Clear();
            _stage = HistoricalSchoolSchedulerStage.None;
            _activeYear = -1;
            _eligibleYear = 0;
            _nextEligibleYear = 0;
            _lastCompletedYear = -1;
            _livingXiaCities = null;
        }

        private static bool StartNextYear()
        {
            int year = Years.TakePendingYear();
            if (year < 0 || year <= _lastCompletedYear) return false;
            _activeYear = year;
            _nextEligibleYear = _eligibleYear;
            _livingXiaCities = null;
            _stage = HistoricalSchoolSchedulerStage.Bootstrap;
            return true;
        }

        private static bool ExecuteStage(HistoricalSchoolSchedulerStage pStage)
        {
            if (pStage == HistoricalSchoolSchedulerStage.Bootstrap)
                return ProcessBootstrap();

            try
            {
                switch (pStage)
                {
                    case HistoricalSchoolSchedulerStage.Descent:
                        return ProcessDescent();
                    case HistoricalSchoolSchedulerStage.ServiceClose:
                        return SchoolGuestOfficeService.ProcessYearFrame(_activeYear);
                    case HistoricalSchoolSchedulerStage.ServiceAppointment:
                        return HistoricalSchoolEliteEnrollmentService.ProcessYearFrame(
                            _activeYear, _livingXiaCities);
                    case HistoricalSchoolSchedulerStage.Promotion:
                        return SchoolMembershipService.ProcessStandingFrame(_activeYear);
                    case HistoricalSchoolSchedulerStage.Recovery:
                        return HistoricalSchoolPopulationRecoveryService.ProcessYearFrame(
                            _activeYear);
                    case HistoricalSchoolSchedulerStage.LecturePlan:
                        return HistoricalSchoolActionService.ProcessYearFrame(_activeYear);
                    case HistoricalSchoolSchedulerStage.DebatePlan:
                        return HistoricalSchoolDebateService.ProcessYearFrame(_activeYear);
                    case HistoricalSchoolSchedulerStage.Conversion:
                    case HistoricalSchoolSchedulerStage.Rediscovery:
                        break;
                    case HistoricalSchoolSchedulerStage.RuntimeCommit:
                        return ProcessRuntimeCommit();
                }
            }
            catch (Exception error)
            {
                HistoricalSchoolRuntime.LogAnnualStageFailure(
                    StageId(pStage), error);
            }
            return true;
        }

        private static bool ProcessBootstrap()
        {
            if (HistoricalSchoolRuntime.IsLoaded) return true;
            if (!BootstrapRetryGate.CanAttempt()) return false;
            try
            {
                HistoricalSchoolRuntime.LoadState();
                BootstrapRetryGate.RecordSuccess();
                return true;
            }
            catch (Exception error)
            {
                BootstrapRetryGate.RecordFailure();
                HistoricalSchoolRuntime.LogAnnualStageFailure(
                    HistoricalSchoolAnnualStageId.Bootstrap, error);
                return false;
            }
        }

        private static bool ProcessDescent()
        {
            if (_livingXiaCities == null)
            {
                _livingXiaCities = HistoricalSchoolRuntime.LivingXiaCities();
                _nextEligibleYear = HistoricalSchoolRules.AdvanceEligibleYear(
                    _eligibleYear, _livingXiaCities.Count > 0);
            }
            return _livingXiaCities.Count == 0 ||
                   HistoricalSchoolDescentService.ProcessDueFrame(
                       _nextEligibleYear, _livingXiaCities);
        }

        private static bool ProcessRuntimeCommit()
        {
            PendingRuntimeState.Freeze(
                _nextEligibleYear,
                _activeYear,
                World.world?.getCurWorldTime() ?? 0d);
            if (!PendingRuntimeState.AdvanceAndTryFlush(
                    HistoricalSchoolStore.SaveRuntimeState))
                return false;

            _eligibleYear = _nextEligibleYear;
            _lastCompletedYear = _activeYear;
            _activeYear = -1;
            _livingXiaCities = null;
            _stage = HistoricalSchoolSchedulerStage.None;
            return true;
        }

        private static void AdvanceStage()
        {
            if (_stage < HistoricalSchoolSchedulerStage.RuntimeCommit)
                _stage++;
        }

        private static string StageId(HistoricalSchoolSchedulerStage pStage)
        {
            switch (pStage)
            {
                case HistoricalSchoolSchedulerStage.Bootstrap: return "bootstrap";
                case HistoricalSchoolSchedulerStage.Descent: return "descent";
                case HistoricalSchoolSchedulerStage.ServiceClose: return "service_close";
                case HistoricalSchoolSchedulerStage.ServiceAppointment:
                    return "service_appointment";
                case HistoricalSchoolSchedulerStage.Promotion: return "promotion";
                case HistoricalSchoolSchedulerStage.Recovery: return "recovery";
                case HistoricalSchoolSchedulerStage.LecturePlan: return "lecture_plan";
                case HistoricalSchoolSchedulerStage.DebatePlan: return "debate_plan";
                case HistoricalSchoolSchedulerStage.Conversion: return "conversion";
                case HistoricalSchoolSchedulerStage.Rediscovery: return "rediscovery";
                case HistoricalSchoolSchedulerStage.RuntimeCommit: return "runtime_commit";
                default: return "none";
            }
        }
    }
}
