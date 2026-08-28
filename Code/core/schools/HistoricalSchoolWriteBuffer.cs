using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;

namespace AncientWarfare3.core.schools
{
    internal interface IHistoricalSchoolWriteOperation
    {
        string OperationKey { get; }

        HistoricalSchoolTeachingPersistenceOutcome Execute(
            SQLiteConnection pDb,
            SQLiteTransaction pTransaction);

        void AfterCommit(HistoricalSchoolTeachingPersistenceOutcome pOutcome);
        void OnCleanFailure();
    }

    internal interface IHistoricalSchoolRetainedCleanFailure
    {
        bool RetainsPendingAfterCleanFailure { get; }
    }

    internal interface IHistoricalSchoolWriteBatchExecutor
    {
        HistoricalSchoolWriteBatchResult Execute(
            IReadOnlyList<IHistoricalSchoolWriteOperation> pOperations);
    }

    internal sealed class HistoricalSchoolWriteBatchResult
    {
        private static readonly HistoricalSchoolWriteBatchResult UnknownResult =
            new HistoricalSchoolWriteBatchResult(false,
                Array.Empty<HistoricalSchoolTeachingPersistenceOutcome>());

        private HistoricalSchoolWriteBatchResult(bool pCommitted,
            HistoricalSchoolTeachingPersistenceOutcome[] pOutcomes)
        {
            IsCommitted = pCommitted;
            Outcomes = pOutcomes ??
                Array.Empty<HistoricalSchoolTeachingPersistenceOutcome>();
        }

        public static HistoricalSchoolWriteBatchResult Unknown => UnknownResult;
        public bool IsCommitted { get; }
        public IReadOnlyList<HistoricalSchoolTeachingPersistenceOutcome> Outcomes { get; }

        public static HistoricalSchoolWriteBatchResult Committed(
            HistoricalSchoolTeachingPersistenceOutcome[] pOutcomes)
        {
            return new HistoricalSchoolWriteBatchResult(true, pOutcomes);
        }
    }

    internal sealed class HistoricalSchoolWriteBuffer
    {
        public const int MaxCapacity = 512;
        public const int DefaultBatchSize = 32;
        public const int MaxUnknownAttempts = 4;

        private sealed class Entry
        {
            public IHistoricalSchoolWriteOperation Operation;
            public int Attempts;
            public long ReadyFrame;
        }

        private readonly Queue<Entry> _queue = new Queue<Entry>();
        private readonly HashSet<string> _operationKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly int _maxCapacity;
        private readonly int _maxBatchSize;
        private long _lastProcessFrame = long.MinValue;
        private bool _isolateHead;

        public HistoricalSchoolWriteBuffer(int pMaxCapacity = MaxCapacity,
            int pMaxBatchSize = DefaultBatchSize)
        {
            _maxCapacity = Math.Max(1, Math.Min(MaxCapacity, pMaxCapacity));
            _maxBatchSize = Math.Max(1, Math.Min(_maxCapacity, pMaxBatchSize));
        }

        public int Count => _queue.Count;

        public bool TryEnqueue(IHistoricalSchoolWriteOperation pOperation,
            bool pDurableReady)
        {
            string operationKey = pOperation?.OperationKey;
            if (!pDurableReady || pOperation == null ||
                string.IsNullOrWhiteSpace(operationKey) ||
                _queue.Count >= _maxCapacity || !_operationKeys.Add(operationKey))
                return false;
            _queue.Enqueue(new Entry
            {
                Operation = pOperation,
                ReadyFrame = 0L
            });
            return true;
        }

        public bool ProcessFrame(long pFrame,
            IHistoricalSchoolWriteBatchExecutor pExecutor)
        {
            return ProcessFrame(pFrame, pExecutor, pMaxBatches: 1,
                pBudgetMilliseconds: 0d);
        }

        /// <summary>
        /// 每帧提交若干批。默认 pMaxBatches=1,与原来的「一帧一批」完全一致。
        ///
        /// 一帧一批原本是硬上限,和积压量无关:队列容量 512、批大小 32,排空一个
        /// 满队列要 16 帧。实测积压后每批都装满 32 条,单批成本从 0.61ms 涨到
        /// 2.02ms,而学派步 73~79% 的耗时就是这些提交。
        ///
        /// 退避语义不受影响:重试项的 ReadyFrame > pFrame,循环里下一次
        /// ProcessOneBatch 会因为队头未就绪而返回 false 并跳出,所以指数退避和
        /// _isolateHead 的隔离节奏都保持原样 —— 隔离时依旧是一帧一条。
        /// </summary>
        public bool ProcessFrame(long pFrame,
            IHistoricalSchoolWriteBatchExecutor pExecutor,
            int pMaxBatches, double pBudgetMilliseconds)
        {
            if (pExecutor == null || pFrame == _lastProcessFrame ||
                _queue.Count == 0)
                return false;
            Entry first = _queue.Peek();
            if (first.ReadyFrame > pFrame) return false;
            _lastProcessFrame = pFrame;

            int batches = Math.Max(1, pMaxBatches);
            long deadline = pBudgetMilliseconds > 0d
                ? Stopwatch.GetTimestamp() + (long)(pBudgetMilliseconds *
                    Stopwatch.Frequency / 1000d)
                : 0L;
            for (int i = 0; i < batches; i++)
            {
                if (!ProcessOneBatch(pFrame, pExecutor,
                        pIgnoreBackoff: false))
                    break;
                if (_queue.Count == 0) break;
                // 隔离模式下每批只有一条,继续循环等于绕过「一帧一条」的隔离
                // 节奏,所以一旦进入隔离就当帧收手。
                if (_isolateHead) break;
                if (deadline != 0L &&
                    Stopwatch.GetTimestamp() >= deadline) break;
            }

            return true;
        }

        public bool FlushForSave(IHistoricalSchoolWriteBatchExecutor pExecutor)
        {
            if (pExecutor == null) return false;
            int workBudget = MaxCapacity * (MaxUnknownAttempts + 2);
            while (_queue.Count > 0 && workBudget-- > 0)
            {
                long frame = _lastProcessFrame == long.MaxValue
                    ? long.MaxValue
                    : Math.Max(0L, _lastProcessFrame + 1L);
                if (!ProcessOneBatch(frame, pExecutor, pIgnoreBackoff: true))
                    return false;
            }
            return _queue.Count == 0;
        }

        public void Clear()
        {
            _queue.Clear();
            _operationKeys.Clear();
            _lastProcessFrame = long.MinValue;
            _isolateHead = false;
        }

        private bool ProcessOneBatch(long pFrame,
            IHistoricalSchoolWriteBatchExecutor pExecutor, bool pIgnoreBackoff)
        {
            if (_queue.Count == 0 ||
                (!pIgnoreBackoff && _queue.Peek().ReadyFrame > pFrame)) return false;
            int batchSize = _isolateHead ? 1 : _maxBatchSize;
            var entries = new List<Entry>(Math.Min(batchSize, _queue.Count));
            foreach (Entry entry in _queue)
            {
                if (entries.Count >= batchSize) break;
                entries.Add(entry);
            }
            var operations = new List<IHistoricalSchoolWriteOperation>(entries.Count);
            foreach (Entry entry in entries) operations.Add(entry.Operation);

            HistoricalSchoolWriteBatchResult result;
            try
            {
                result = pExecutor.Execute(operations);
            }
            catch
            {
                result = HistoricalSchoolWriteBatchResult.Unknown;
            }
            if (result == null || !result.IsCommitted ||
                result.Outcomes.Count != entries.Count)
            {
                HandleUnknown(entries, pFrame);
                return true;
            }

            for (int index = 0; index < entries.Count; index++)
            {
                HistoricalSchoolTeachingPersistenceOutcome outcome =
                    result.Outcomes[index];
                if (outcome == HistoricalSchoolTeachingPersistenceOutcome.Unknown)
                {
                    HandleUnknown(entries, pFrame);
                    return true;
                }
                Entry current = _queue.Peek();
                if (!ReferenceEquals(current, entries[index]))
                    throw new InvalidOperationException(
                        "Historical school write FIFO changed during projection");
                try
                {
                    if (outcome == HistoricalSchoolTeachingPersistenceOutcome.CleanFailure)
                        current.Operation.OnCleanFailure();
                    else
                        current.Operation.AfterCommit(outcome);
                }
                catch
                {
                    _isolateHead = true;
                    ScheduleOrRetire(current, pFrame);
                    return true;
                }
                _queue.Dequeue();
                _operationKeys.Remove(current.Operation.OperationKey);
            }
            _isolateHead = false;
            return true;
        }

        private void HandleUnknown(IReadOnlyList<Entry> pEntries, long pFrame)
        {
            if (pEntries == null || pEntries.Count == 0) return;
            _isolateHead = true;
            ScheduleOrRetire(pEntries[0], pFrame);
        }

        private void ScheduleOrRetire(Entry pEntry, long pFrame)
        {
            if (pEntry == null) return;
            pEntry.Attempts = Math.Min(MaxUnknownAttempts,
                pEntry.Attempts + 1);
            if (pEntry.Attempts >= MaxUnknownAttempts)
            {
                RetirePoisonedHead(pEntry);
                return;
            }
            int shift = Math.Min(8, Math.Max(0, pEntry.Attempts - 1));
            long delay = 1L << shift;
            pEntry.ReadyFrame = pFrame > long.MaxValue - delay
                ? long.MaxValue
                : pFrame + delay;
        }

        private void RetirePoisonedHead(Entry pEntry)
        {
            if (_queue.Count == 0 ||
                !ReferenceEquals(_queue.Peek(), pEntry)) return;
            string operationKey = pEntry.Operation?.OperationKey ?? "";
            try { pEntry.Operation?.OnCleanFailure(); }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Historical school poisoned write cleanup failed: key=" +
                    operationKey + " error=" + error.Message);
            }
            _queue.Dequeue();
            _operationKeys.Remove(operationKey);
            _isolateHead = false;
            ModClass.LogWarning(
                "Historical school poisoned write retired after " +
                MaxUnknownAttempts + " attempts: key=" + operationKey);
        }
    }

    internal sealed class HistoricalSchoolSqlWriteBatchExecutor :
        IHistoricalSchoolWriteBatchExecutor
    {
        private static string _lastFailureSignature = string.Empty;
        private readonly SQLiteConnection _db;

        public HistoricalSchoolSqlWriteBatchExecutor(SQLiteConnection pDb)
        {
            _db = pDb;
        }

        public HistoricalSchoolWriteBatchResult Execute(
            IReadOnlyList<IHistoricalSchoolWriteOperation> pOperations)
        {
            if (_db == null || pOperations == null || pOperations.Count == 0)
            {
                if (_db == null)
                    ReportBatchFailure(pOperations, null,
                        "lineage archive connection is unavailable");
                return HistoricalSchoolWriteBatchResult.Unknown;
            }
            long started = Stopwatch.GetTimestamp();
            bool retry = true;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = _db.BeginTransaction();
                var outcomes = new HistoricalSchoolTeachingPersistenceOutcome[
                    pOperations.Count];
                for (int index = 0; index < pOperations.Count; index++)
                {
                    // 按 key 前缀记账,才知道这些写入到底是谁产生的:
                    // write_buffer 占学派 74~98%,而它本身只是执行者。
                    long operationStarted = Stopwatch.GetTimestamp();
                    outcomes[index] = pOperations[index].Execute(_db, transaction);
                    HistoricalSchoolWriteDiagnostics.AccountOperation(
                        pOperations[index].OperationKey, operationStarted);
                    if (outcomes[index] ==
                        HistoricalSchoolTeachingPersistenceOutcome.Unknown)
                    {
                        transaction.Rollback();
                        ReportBatchFailure(pOperations, null,
                            "operation returned Unknown at index " + index);
                        return HistoricalSchoolWriteBatchResult.Unknown;
                    }
                }
                // 语句执行与事务提交分开计时:前者大说明写入条数/单条成本是
                // 问题,后者大说明是事务本身的固定开销(批数太多)。
                long commitStarted = Stopwatch.GetTimestamp();
                transaction.Commit();
                HistoricalSchoolWriteDiagnostics.AccountCommit(commitStarted);
                retry = false;
                return HistoricalSchoolWriteBatchResult.Committed(outcomes);
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ReportBatchFailure(pOperations, error);
                return HistoricalSchoolWriteBatchResult.Unknown;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
                HistoricalSchoolDiagnostics.RecordSqlBatch(pOperations.Count,
                    Stopwatch.GetTimestamp() - started, retry);
            }
        }

        private static void ReportBatchFailure(
            IReadOnlyList<IHistoricalSchoolWriteOperation> pOperations,
            Exception pError, string pDetail = null)
        {
            IHistoricalSchoolWriteOperation first =
                pOperations != null && pOperations.Count > 0
                    ? pOperations[0]
                    : null;
            string operationType = first?.GetType().Name ?? "none";
            string operationKey = first?.OperationKey ?? "none";
            string errorType = pError?.GetType().Name ?? "UnknownOutcome";
            string detail = string.IsNullOrWhiteSpace(pDetail)
                ? pError?.Message ?? "no detail"
                : pDetail;
            string signature = operationType + "|" + errorType + "|" + detail;
            if (string.Equals(_lastFailureSignature, signature,
                    StringComparison.Ordinal)) return;
            _lastFailureSignature = signature;
            ModClass.LogWarning("Historical school write batch blocked: " +
                                "operation=" + operationType +
                                " key=" + operationKey +
                                " error=" + errorType + ": " + detail);
            if (pError?.StackTrace != null)
                ModClass.LogWarning(pError.StackTrace);
        }
    }

}
