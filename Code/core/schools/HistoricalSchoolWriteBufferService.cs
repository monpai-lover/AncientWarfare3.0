using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolWriteBufferService
    {
        private static readonly HistoricalSchoolWriteBuffer Buffer =
            new HistoricalSchoolWriteBuffer();
        private static readonly Dictionary<string,
            IHistoricalSchoolWriteOperation> PendingAsync =
            new Dictionary<string, IHistoricalSchoolWriteOperation>(
                StringComparer.Ordinal);
        private static long _frame;

        public static int Count => Buffer.Count + PendingAsync.Count;

        public static bool TryEnqueue(IHistoricalSchoolWriteOperation pOperation,
            bool pDurableReady = true)
        {
            IHistoricalSchoolAsyncWriteOperation asyncOperation = pOperation as
                IHistoricalSchoolAsyncWriteOperation;
            bool backgroundOnly = pOperation is
                IHistoricalSchoolBackgroundOnlyWriteOperation;
            bool syncDependency = pDurableReady && pOperation != null &&
                                  asyncOperation == null;
            if (pDurableReady && asyncOperation != null)
            {
                string operationKey = pOperation.OperationKey;
                if (string.IsNullOrWhiteSpace(operationKey))
                    return backgroundOnly
                        ? false
                        : Buffer.TryEnqueue(pOperation, true);
                if (PendingAsync.ContainsKey(operationKey)) return true;
                if (!HistoricalWriteService.Ready && backgroundOnly &&
                    !HistoricalWriteService.EnsureRequiredWorker(out _))
                    return false;
                if (!HistoricalWriteService.Ready)
                    return backgroundOnly
                        ? false
                        : Buffer.TryEnqueue(pOperation, true);

                IHistoricalSchoolBackgroundWrite background = null;
                try { background = asyncOperation.DetachBackgroundWrite(); }
                catch (Exception error)
                {
                    ModClass.LogWarning(
                        "Historical school detach failed: " + error.Message);
                }
                if (background != null)
                {
                    PendingAsync.Add(operationKey, pOperation);
                    if (HistoricalWriteService.TryEnqueueCustom(operationKey,
                            (sequence, stamp) =>
                                new HistoricalSchoolAsyncEnvelope(sequence,
                                    operationKey, stamp, background),
                            (sequence, outcome) => ResolveAsync(operationKey,
                                outcome), out _, out _))
                        return true;
                    PendingAsync.Remove(operationKey);
                }
                if (backgroundOnly) return false;
                syncDependency = true;
            }
            if (syncDependency)
                HistoricalSchoolDiagnostics.RecordDbSyncDependency();
            return Buffer.TryEnqueue(pOperation, pDurableReady);
        }

        public static bool ProcessFrame()
        {
            if (Buffer.Count == 0) return false;
            if (_frame < long.MaxValue) _frame++;
            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            return Buffer.ProcessFrame(_frame,
                new HistoricalSchoolSqlWriteBatchExecutor(db));
        }

        public static bool FlushForSave()
        {
            bool buffered = true;
            if (Buffer.Count > 0)
            {
                SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
                buffered = Buffer.FlushForSave(
                    new HistoricalSchoolSqlWriteBatchExecutor(db));
            }
            bool asynchronous = HistoricalWriteService.FlushForSave(
                TimeSpan.FromSeconds(5), out _);
            return buffered && asynchronous && PendingAsync.Count == 0;
        }

        public static void Clear()
        {
            Buffer.Clear();
            PendingAsync.Clear();
            _frame = 0L;
        }

        private static void ResolveAsync(string pOperationKey,
            object pOutcome)
        {
            if (!PendingAsync.TryGetValue(pOperationKey,
                    out IHistoricalSchoolWriteOperation operation)) return;
            PendingAsync.Remove(pOperationKey);
            HistoricalSchoolTeachingPersistenceOutcome outcome =
                pOutcome is HistoricalSchoolTeachingPersistenceOutcome typed
                    ? typed
                    : HistoricalSchoolTeachingPersistenceOutcome.Unknown;
            if (outcome == HistoricalSchoolTeachingPersistenceOutcome.CleanFailure)
                operation.OnCleanFailure();
            else if (outcome != HistoricalSchoolTeachingPersistenceOutcome.Unknown)
                operation.AfterCommit(outcome);
        }
    }
}
