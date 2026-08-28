using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolWriteBufferService
    {
        private sealed class AsyncEntry
        {
            public IHistoricalSchoolWriteOperation Operation;
            public HistoricalSchoolTeachingPersistenceOutcome ProjectionOutcome;
            public int ProjectionAttempts;
            public long ProjectionReadyFrame;
            public bool ProjectionQueued;
        }

        private static readonly HistoricalSchoolWriteBuffer Buffer =
            new HistoricalSchoolWriteBuffer();
        // 实测:积压时单批 32 条约 2.0ms。4 批 + 3ms 预算意味着积压帧最多多花
        // 约 3ms,但排空速度提高 4 倍,队列不再单向增长。预算先于批数生效,
        // 所以单批变慢时不会把帧撑爆。
        private const int MaxBatchesPerFrame = 4;
        private const double BatchBudgetMilliseconds = 3d;
        private static readonly Dictionary<string,
            AsyncEntry> PendingAsync =
            new Dictionary<string, AsyncEntry>(
                StringComparer.Ordinal);
        private static readonly Queue<string> ProjectionRetries =
            new Queue<string>();
        private static long _frame;
        private const int MaxSaveDrainPasses =
            HistoricalSchoolWriteBuffer.MaxCapacity;

        public static int Count => Buffer.Count + PendingAsync.Count;

        public static bool TryEnqueue(IHistoricalSchoolWriteOperation pOperation,
            bool pDurableReady = true)
        {
            IHistoricalSchoolAsyncWriteOperation asyncOperation = pOperation as
                IHistoricalSchoolAsyncWriteOperation;
            bool syncDependency = pDurableReady && pOperation != null &&
                                  asyncOperation == null;
            if (pDurableReady && asyncOperation != null &&
                HistoricalWriteService.Ready &&
                !string.IsNullOrWhiteSpace(pOperation.OperationKey) &&
                !PendingAsync.ContainsKey(pOperation.OperationKey))
            {
                IHistoricalSchoolBackgroundWrite background = null;
                try { background = asyncOperation.DetachBackgroundWrite(); }
                catch (Exception error)
                {
                    ModClass.LogWarning(
                        "Historical school detach failed: " + error.Message);
                }
                if (background != null)
                {
                    string operationKey = pOperation.OperationKey;
                    PendingAsync.Add(operationKey, new AsyncEntry
                    {
                        Operation = pOperation
                    });
                    if (HistoricalWriteService.TryEnqueueCustom(operationKey,
                            (sequence, stamp) =>
                                new HistoricalSchoolAsyncEnvelope(sequence,
                                    operationKey, stamp, background),
                            (sequence, outcome) => ResolveAsync(operationKey,
                                outcome),
                            (sequence, error) => FailAsync(operationKey, error),
                            out _, out _))
                        return true;
                    PendingAsync.Remove(operationKey);
                }
                else
                {
                    syncDependency = true;
                }
            }
            if (syncDependency)
                HistoricalSchoolDiagnostics.RecordDbSyncDependency();
            return Buffer.TryEnqueue(pOperation, pDurableReady);
        }

        public static bool ProcessFrame()
        {
            if (Buffer.Count == 0 && ProjectionRetries.Count == 0) return false;
            if (_frame < long.MaxValue) _frame++;
            if (ProcessProjectionRetry(pIgnoreBackoff: false)) return true;
            if (Buffer.Count == 0) return false;
            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            // 积压时一帧一批排不完(512 容量 ÷ 32 批 = 16 帧),队列越堆越高、
            // 每批越装越满。给一个小预算允许多提交几批,没积压时第一批就把队列
            // 清空、循环立刻结束,行为与原来一致。
            return Buffer.ProcessFrame(_frame,
                new HistoricalSchoolSqlWriteBatchExecutor(db),
                pMaxBatches: MaxBatchesPerFrame,
                pBudgetMilliseconds: BatchBudgetMilliseconds);
        }

        public static bool FlushForSave()
        {
            if (HistoricalWriteModeRules.ShouldRequireWorkerForFlush(
                    AWAsyncRuntime.DatabaseEnabled,
                    HistoricalWriteService.Ready) &&
                !HistoricalWriteService.EnsureRequiredWorker(
                    out string writerError))
            {
                ModClass.LogWarning(
                    "Historical school save writer recovery failed: " +
                    writerError);
                return false;
            }

            long deadline = Stopwatch.GetTimestamp() +
                Stopwatch.Frequency * 5L;
            return HistoricalSchoolSavePreparation.DrainUntilQuiescent(
                () => FlushOneSavePass(deadline),
                () => Count,
                MaxSaveDrainPasses);
        }

        private static bool FlushOneSavePass(long pDeadline)
        {
            bool buffered = true;
            if (Buffer.Count > 0)
            {
                SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
                buffered = Buffer.FlushForSave(
                    new HistoricalSchoolSqlWriteBatchExecutor(db));
            }
            long remainingTicks = pDeadline - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0L) return false;
            bool asynchronous = HistoricalWriteService.FlushForSave(
                TimeSpan.FromSeconds((double)remainingTicks /
                    Stopwatch.Frequency), out _);
            bool projections = asynchronous && FlushProjectionRetries();
            return buffered && asynchronous && projections &&
                   Buffer.Count == 0 && PendingAsync.Count == 0;
        }

        public static void Clear()
        {
            Buffer.Clear();
            PendingAsync.Clear();
            ProjectionRetries.Clear();
            _frame = 0L;
        }

        private static void ResolveAsync(string pOperationKey,
            object pOutcome)
        {
            if (!PendingAsync.TryGetValue(pOperationKey,
                    out AsyncEntry entry)) return;
            IHistoricalSchoolWriteOperation operation = entry.Operation;
            HistoricalSchoolTeachingPersistenceOutcome outcome =
                pOutcome is HistoricalSchoolTeachingPersistenceOutcome typed
                    ? typed
                    : HistoricalSchoolTeachingPersistenceOutcome.Unknown;
            if (outcome == HistoricalSchoolTeachingPersistenceOutcome.CleanFailure)
            {
                RetireAsync(pOperationKey, entry, "clean failure");
                return;
            }
            if (outcome == HistoricalSchoolTeachingPersistenceOutcome.Unknown)
            {
                FailAsync(pOperationKey, "unknown committed outcome");
                return;
            }
            try
            {
                operation.AfterCommit(outcome);
                PendingAsync.Remove(pOperationKey);
            }
            catch (Exception error)
            {
                entry.ProjectionOutcome = outcome;
                ScheduleProjectionRetry(pOperationKey, entry, error.Message);
            }
        }

        private static void FailAsync(string pOperationKey, string pError)
        {
            if (!PendingAsync.TryGetValue(pOperationKey,
                    out AsyncEntry entry)) return;
            RetireAsync(pOperationKey, entry,
                string.IsNullOrWhiteSpace(pError)
                    ? "background write failed"
                    : pError);
        }

        private static bool ProcessProjectionRetry(bool pIgnoreBackoff)
        {
            int scan = ProjectionRetries.Count;
            for (int index = 0; index < scan; index++)
            {
                string operationKey = ProjectionRetries.Dequeue();
                if (!PendingAsync.TryGetValue(operationKey,
                        out AsyncEntry entry) || !entry.ProjectionQueued)
                    continue;
                if (!pIgnoreBackoff && entry.ProjectionReadyFrame > _frame)
                {
                    ProjectionRetries.Enqueue(operationKey);
                    continue;
                }
                entry.ProjectionQueued = false;
                try
                {
                    entry.Operation.AfterCommit(entry.ProjectionOutcome);
                    PendingAsync.Remove(operationKey);
                }
                catch (Exception error)
                {
                    ScheduleProjectionRetry(operationKey, entry, error.Message);
                }
                return true;
            }
            return false;
        }

        private static bool FlushProjectionRetries()
        {
            int budget = HistoricalSchoolWriteBuffer.MaxCapacity *
                         (HistoricalSchoolWriteBuffer.MaxUnknownAttempts + 1);
            while (ProjectionRetries.Count > 0 && budget-- > 0)
            {
                if (_frame < long.MaxValue) _frame++;
                if (!ProcessProjectionRetry(pIgnoreBackoff: true)) break;
            }
            return ProjectionRetries.Count == 0;
        }

        private static void ScheduleProjectionRetry(string pOperationKey,
            AsyncEntry pEntry, string pError)
        {
            if (pEntry == null) return;
            pEntry.ProjectionAttempts = Math.Min(
                HistoricalSchoolWriteBuffer.MaxUnknownAttempts,
                pEntry.ProjectionAttempts + 1);
            if (pEntry.ProjectionAttempts >=
                HistoricalSchoolWriteBuffer.MaxUnknownAttempts)
            {
                RetireAsync(pOperationKey, pEntry,
                    "projection retries exhausted: " + (pError ?? ""));
                return;
            }
            int shift = Math.Min(8,
                Math.Max(0, pEntry.ProjectionAttempts - 1));
            long delay = 1L << shift;
            pEntry.ProjectionReadyFrame = _frame > long.MaxValue - delay
                ? long.MaxValue
                : _frame + delay;
            if (pEntry.ProjectionQueued) return;
            pEntry.ProjectionQueued = true;
            ProjectionRetries.Enqueue(pOperationKey);
        }

        private static void RetireAsync(string pOperationKey,
            AsyncEntry pEntry, string pReason)
        {
            PendingAsync.Remove(pOperationKey);
            if (pEntry != null) pEntry.ProjectionQueued = false;
            try { pEntry?.Operation?.OnCleanFailure(); }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Historical school async cleanup failed: key=" +
                    pOperationKey + " error=" + error.Message);
            }
            if (pEntry?.Operation is
                    IHistoricalSchoolRetainedCleanFailure retained &&
                retained.RetainsPendingAfterCleanFailure)
                return;
            ModClass.LogWarning("Historical school async operation retired: key=" +
                                pOperationKey + " reason=" + (pReason ?? ""));
        }
    }
}
