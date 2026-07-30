using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.uiquery;

namespace AncientWarfare3.core.asyncwork
{
    internal static class AWAsyncWorldLifecycle
    {
        private static readonly object LifecycleGate = new object();
        private static readonly TimeSpan WorldClearTimeout =
            TimeSpan.FromSeconds(2);
        private static bool _saveBarrierActive;

        public static bool TryBeginWorldChange(out string pError)
        {
            lock (LifecycleGate)
            {
                if (_saveBarrierActive)
                {
                    pError = "async save barrier is active";
                    return false;
                }
                return TryBeginWorldChangeLocked(out pError);
            }
        }

        private static bool TryBeginWorldChangeLocked(out string pError)
        {
            long previousGeneration = AWAsyncRuntime.WorldGeneration;
            if (!AWHistoricalReadService.ClearWorld(WorldClearTimeout,
                    out pError))
            {
                string clearError = pError;
                var rollbackErrors = new List<string>(1);
                TryRestartReader(previousGeneration, rollbackErrors);
                pError = WithRollbackErrors(clearError, rollbackErrors);
                return false;
            }
            if (!HistoricalWriteService.StopWorld(WorldClearTimeout,
                    out pError))
            {
                string stopError = pError;
                var rollbackErrors = new List<string>(2);
                TryRestartWriter(previousGeneration, rollbackErrors);
                TryRestartReader(previousGeneration, rollbackErrors);
                pError = WithRollbackErrors(stopError, rollbackErrors);
                return false;
            }
            try
            {
                AWAsyncRuntime.ClearWorld(WorldClearTimeout);
            }
            catch (Exception error)
            {
                string clearError = "async compute world clear failed: " +
                                    error.Message;
                var rollbackErrors = new List<string>(3);
                try
                {
                    long recoveryGeneration = AWAsyncRuntime.StartWorld();
                    TryRestartWriter(recoveryGeneration, rollbackErrors);
                    TryRestartReader(recoveryGeneration, rollbackErrors);
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add("async compute restart failed: " +
                                       rollbackError.Message +
                                       "; historical I/O services remain stopped");
                }
                pError = WithRollbackErrors(clearError, rollbackErrors);
                return false;
            }
            ActorArchivePendingStore.Clear();
            pError = string.Empty;
            return true;
        }

        private static void TryRestartWriter(long pGeneration,
            List<string> pErrors)
        {
            try { HistoricalWriteService.StartWorld(pGeneration); }
            catch (Exception error)
            {
                pErrors.Add("historical writer rollback failed: " +
                            error.Message);
            }
        }

        private static void TryRestartReader(long pGeneration,
            List<string> pErrors)
        {
            try { AWHistoricalReadService.StartWorld(pGeneration); }
            catch (Exception error)
            {
                pErrors.Add("historical reader rollback failed: " +
                            error.Message);
            }
        }

        private static string WithRollbackErrors(string pOriginal,
            List<string> pRollbackErrors)
        {
            if (pRollbackErrors == null || pRollbackErrors.Count == 0)
                return pOriginal;
            return pOriginal + "; " + string.Join("; ", pRollbackErrors);
        }

        public static void BeginWorldChange()
        {
            lock (LifecycleGate)
            {
                if (_saveBarrierActive)
                    throw new InvalidOperationException(
                        "AW3 world change blocked: async save barrier is active");
                if (TryBeginWorldChangeLocked(out string error)) return;
                throw new InvalidOperationException(
                    "AW3 world change blocked: " + error);
            }
        }

        public static long StartWorld()
        {
            lock (LifecycleGate)
            {
                if (_saveBarrierActive)
                    throw new InvalidOperationException(
                        "AW3 async world start blocked: save barrier is active.");
                long generation = AWAsyncRuntime.StartWorld();
                bool writerStartAttempted = false;
                bool readerStartAttempted = false;
                try
                {
                    writerStartAttempted = true;
                    HistoricalWriteService.StartWorld(generation);
                    readerStartAttempted = true;
                    AWHistoricalReadService.StartWorld(generation);
                    return generation;
                }
                catch (Exception startError)
                {
                    var cleanupErrors = new List<string>(3);
                    bool readerStopped = !readerStartAttempted;
                    if (readerStartAttempted)
                    {
                        try
                        {
                            readerStopped = AWHistoricalReadService.ClearWorld(
                                WorldClearTimeout, out string readerError);
                            if (!readerStopped)
                                cleanupErrors.Add("reader: " + readerError);
                        }
                        catch (Exception cleanupError)
                        {
                            cleanupErrors.Add("reader threw: " +
                                              cleanupError.Message);
                        }
                    }
                    bool writerStopped = !writerStartAttempted;
                    if (writerStartAttempted)
                    {
                        try
                        {
                            writerStopped = HistoricalWriteService.StopWorld(
                                WorldClearTimeout, out string writerError);
                            if (!writerStopped)
                                cleanupErrors.Add("writer: " + writerError);
                        }
                        catch (Exception cleanupError)
                        {
                            cleanupErrors.Add("writer threw: " +
                                              cleanupError.Message);
                        }
                    }
                    if (readerStopped && writerStopped)
                    {
                        try { AWAsyncRuntime.ClearWorld(WorldClearTimeout); }
                        catch (Exception cleanupError)
                        {
                            cleanupErrors.Add("compute threw: " +
                                              cleanupError.Message);
                        }
                    }
                    string cleanupDetails = cleanupErrors.Count == 0
                        ? string.Empty
                        : "; cleanup " + string.Join("; ", cleanupErrors);
                    throw new InvalidOperationException(
                        "AW3 async world start failed: " + startError.Message +
                        cleanupDetails, startError);
                }
            }
        }

        public static bool TryEnterSaveBarrier(TimeSpan pTimeout,
            out string pError)
        {
            return TryEnterSaveBarrier(pTimeout, null, out pError);
        }

        public static bool TryEnterSaveBarrier(TimeSpan pTimeout,
            Action pPendingOwnerWork, out string pError)
        {
            lock (LifecycleGate)
            {
                if (_saveBarrierActive)
                {
                    pError = "async save barrier is already active";
                    return false;
                }
                long deadline = Deadline(pTimeout);
                if (!AWAsyncRuntime.TryEnterSaveBarrier(pTimeout,
                        pPendingOwnerWork, out pError))
                    return false;
                try
                {
                    TimeSpan remaining = Remaining(deadline);
                    if (remaining <= TimeSpan.Zero)
                    {
                        pError = "async save barrier timed out";
                    }
                    else if (AWHistoricalReadService.TryEnterSaveBarrier(
                            remaining, out pError))
                    {
                        _saveBarrierActive = true;
                        return true;
                    }
                }
                catch
                {
                    RollbackSaveBarrierLocked();
                    throw;
                }
                RollbackSaveBarrierLocked();
                return false;
            }
        }

        private static long Deadline(TimeSpan pTimeout)
        {
            return Stopwatch.GetTimestamp() + Math.Max(0L,
                (long)(Stopwatch.Frequency *
                    Math.Max(0d, pTimeout.TotalSeconds)));
        }

        private static TimeSpan Remaining(long pDeadline)
        {
            long ticks = pDeadline - Stopwatch.GetTimestamp();
            if (ticks <= 0L) return TimeSpan.Zero;
            return TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
        }

        private static void RollbackSaveBarrierLocked()
        {
            try { AWHistoricalReadService.ExitSaveBarrier(); }
            finally { AWAsyncRuntime.ExitSaveBarrier(); }
        }

        public static void ExitSaveBarrier()
        {
            lock (LifecycleGate)
            {
                if (!_saveBarrierActive) return;
                try
                {
                    AWHistoricalReadService.ExitSaveBarrier();
                }
                finally
                {
                    try { AWAsyncRuntime.ExitSaveBarrier(); }
                    finally { _saveBarrierActive = false; }
                }
            }
        }

        public static bool TryShutdown(TimeSpan pTimeout, out string pError)
        {
            lock (LifecycleGate)
            {
                if (_saveBarrierActive)
                {
                    pError = "async save barrier is active";
                    return false;
                }
                var errors = new List<string>(3);
                try
                {
                    if (!AWAsyncRuntime.TryShutdown(pTimeout,
                            out string computeError))
                        errors.Add("compute: " + computeError);
                }
                catch (Exception computeException)
                {
                    errors.Add("compute threw: " + computeException.Message);
                }
                try
                {
                    if (!AWHistoricalReadService.TryShutdown(pTimeout,
                            out string readerError))
                        errors.Add("reader: " + readerError);
                }
                catch (Exception readerException)
                {
                    errors.Add("reader threw: " + readerException.Message);
                }
                try
                {
                    if (!HistoricalWriteService.StopWorld(pTimeout,
                            out string writerError))
                        errors.Add("writer: " + writerError);
                }
                catch (Exception writerException)
                {
                    errors.Add("writer threw: " + writerException.Message);
                }
                if (errors.Count > 0)
                {
                    pError = string.Join("; ", errors);
                    return false;
                }
                ActorArchivePendingStore.Clear();
                pError = string.Empty;
                return true;
            }
        }
    }
}
