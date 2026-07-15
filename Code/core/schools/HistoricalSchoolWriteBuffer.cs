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
            if (pExecutor == null || pFrame == _lastProcessFrame || _queue.Count == 0)
                return false;
            Entry first = _queue.Peek();
            if (first.ReadyFrame > pFrame) return false;
            _lastProcessFrame = pFrame;
            ProcessOneBatch(pFrame, pExecutor, pIgnoreBackoff: false);
            return true;
        }

        public bool FlushForSave(IHistoricalSchoolWriteBatchExecutor pExecutor)
        {
            if (pExecutor == null) return false;
            while (_queue.Count > 0)
            {
                int before = _queue.Count;
                long frame = _lastProcessFrame == long.MaxValue
                    ? long.MaxValue
                    : Math.Max(0L, _lastProcessFrame + 1L);
                if (!ProcessOneBatch(frame, pExecutor, pIgnoreBackoff: true) ||
                    _queue.Count >= before) return false;
            }
            return true;
        }

        public void Clear()
        {
            _queue.Clear();
            _operationKeys.Clear();
            _lastProcessFrame = long.MinValue;
        }

        private bool ProcessOneBatch(long pFrame,
            IHistoricalSchoolWriteBatchExecutor pExecutor, bool pIgnoreBackoff)
        {
            if (_queue.Count == 0 ||
                (!pIgnoreBackoff && _queue.Peek().ReadyFrame > pFrame)) return false;
            var entries = new List<Entry>(Math.Min(_maxBatchSize, _queue.Count));
            foreach (Entry entry in _queue)
            {
                if (entries.Count >= _maxBatchSize) break;
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
                ScheduleUnknown(entries, pFrame);
                return false;
            }

            for (int index = 0; index < entries.Count; index++)
            {
                HistoricalSchoolTeachingPersistenceOutcome outcome =
                    result.Outcomes[index];
                if (outcome == HistoricalSchoolTeachingPersistenceOutcome.Unknown)
                {
                    ScheduleUnknown(entries, pFrame);
                    return false;
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
                    ScheduleUnknown(current, pFrame);
                    return false;
                }
                _queue.Dequeue();
                _operationKeys.Remove(current.Operation.OperationKey);
            }
            return true;
        }

        private static void ScheduleUnknown(IEnumerable<Entry> pEntries, long pFrame)
        {
            if (pEntries == null) return;
            foreach (Entry entry in pEntries) ScheduleUnknown(entry, pFrame);
        }

        private static void ScheduleUnknown(Entry pEntry, long pFrame)
        {
            if (pEntry == null) return;
            pEntry.Attempts = Math.Min(31, pEntry.Attempts + 1);
            int shift = Math.Min(8, Math.Max(0, pEntry.Attempts - 1));
            long delay = 1L << shift;
            pEntry.ReadyFrame = pFrame > long.MaxValue - delay
                ? long.MaxValue
                : pFrame + delay;
        }
    }

    internal sealed class HistoricalSchoolSqlWriteBatchExecutor :
        IHistoricalSchoolWriteBatchExecutor
    {
        private readonly SQLiteConnection _db;

        public HistoricalSchoolSqlWriteBatchExecutor(SQLiteConnection pDb)
        {
            _db = pDb;
        }

        public HistoricalSchoolWriteBatchResult Execute(
            IReadOnlyList<IHistoricalSchoolWriteOperation> pOperations)
        {
            if (_db == null || pOperations == null || pOperations.Count == 0)
                return HistoricalSchoolWriteBatchResult.Unknown;
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
                    outcomes[index] = pOperations[index].Execute(_db, transaction);
                    if (outcomes[index] ==
                        HistoricalSchoolTeachingPersistenceOutcome.Unknown)
                    {
                        transaction.Rollback();
                        return HistoricalSchoolWriteBatchResult.Unknown;
                    }
                }
                transaction.Commit();
                retry = false;
                return HistoricalSchoolWriteBatchResult.Committed(outcomes);
            }
            catch
            {
                try { transaction?.Rollback(); } catch { }
                return HistoricalSchoolWriteBatchResult.Unknown;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
                HistoricalSchoolDiagnostics.RecordSqlBatch(pOperations.Count,
                    Stopwatch.GetTimestamp() - started, retry);
            }
        }
    }

}
