using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using AncientWarfare3.core.asyncwork;

namespace AncientWarfare3.core.db
{
    internal static class HistoricalWriteService
    {
        private static readonly object Gate = new object();
        private static readonly object LifecycleGate = new object();
        private static readonly string[] HistoryTables =
        {
            PersonBiographyTableItem.GetTableName(),
            KingdomHistoryTableItem.GetTableName(),
            CityHistoryTableItem.GetTableName()
        };
        private static readonly HistoricalWriteCallbackRegistry Callbacks =
            new HistoricalWriteCallbackRegistry();

        private static HistoricalWriteWorker _worker;
        private static HistoryEventIdAllocator _eventIds =
            new HistoryEventIdAllocator(HistoryTables);
        private static long _worldGeneration;
        private static long _nextSequence;
        private static bool _eventIdsReady;

        public static bool Ready
        {
            get
            {
                lock (Gate)
                    return _worker != null && !_worker.TerminalFaulted;
            }
        }

        public static int PendingCount
        {
            get { lock (Gate) return _worker?.PendingCount ?? 0; }
        }

        public static bool TerminalFaulted
        {
            get { lock (Gate) return _worker?.TerminalFaulted == true; }
        }

        public static long EarliestUncommittedSequence
        {
            get
            {
                lock (Gate)
                    return _worker?.EarliestUncommittedSequence ?? 0L;
            }
        }

        public static void StartWorld(long pWorldGeneration)
        {
            lock (LifecycleGate)
            {
                if (!StopWorld(TimeSpan.FromSeconds(2), out string stopError))
                    throw new InvalidOperationException(
                        "Cannot start historical writer: " + stopError);
                SQLiteConnection database =
                    LineageArchiveManager.Instance.OperatingDB;
                if (database == null) return;
                var allocator = new HistoryEventIdAllocator(HistoryTables);
                foreach (string table in HistoryTables)
                    allocator.Seed(table, ReadMaximumEventId(database, table));

                lock (Gate)
                {
                    _eventIds = allocator;
                    _eventIdsReady = true;
                    _worldGeneration = pWorldGeneration;
                    _nextSequence = 0L;
                }
                if (!HistoricalWriteModeRules.ShouldStartWorker(
                        AWAsyncRuntime.DatabaseEnabled)) return;

                long epoch = LineageArchiveManager.RuntimeDatabaseEpoch;
                var sink = new HistoricalSqliteBatchSink(
                    LineageArchiveManager.RuntimeDbPath, epoch,
                    () => LineageArchiveManager.RuntimeDatabaseEpoch);
                var worker = new HistoricalWriteWorker(sink);
                try
                {
                    worker.Start();
                }
                catch (Exception startError)
                {
                    try { worker.Dispose(); }
                    catch (Exception disposeError)
                    {
                        throw new InvalidOperationException(
                            "Historical writer start and cleanup failed.",
                            new AggregateException(startError, disposeError));
                    }
                    throw;
                }
                lock (Gate) _worker = worker;
            }
        }

        public static bool TryAppendHistory(string pTable,
            IReadOnlyList<HistoricalSqlColumn> pColumns, out long pEventId,
            out string pError)
        {
            string shadowExpected = null;
            string shadowActual = null;
            string operationKey = null;
            bool accepted = false;
            lock (Gate)
            {
                if (!_eventIdsReady && !TrySeedAllocatorLocked(out pError))
                {
                    pEventId = 0L;
                    return false;
                }

                bool shadow = AWAsyncRuntime.ShadowEnabled;
                pEventId = _eventIds.Next(pTable);
                if (!HistoricalWriteModeRules.ShouldAttemptAsyncWrite(
                        AWAsyncRuntime.DatabaseEnabled, _worker != null))
                {
                    pError = AWAsyncRuntime.DatabaseEnabled
                        ? "historical async writer is unavailable"
                        : "historical async writer is disabled";
                    return false;
                }
                var columns = new List<HistoricalSqlColumn>(
                    (pColumns?.Count ?? 0) + 1)
                {
                    new HistoricalSqlColumn("EVENT_ID", pEventId)
                };
                if (pColumns != null)
                    for (int index = 0; index < pColumns.Count; index++)
                        columns.Add(pColumns[index]);

                long sequence = ++_nextSequence;
                operationKey = "history:" + pTable + ":" + pEventId;
                HistoricalWriteEnvelope envelope =
                    HistoricalWriteSqlBuilder.BuildInsert(sequence,
                        operationKey, pTable, columns,
                        HistoricalWriteKind.Append,
                        new AWAsyncStamp(_worldGeneration, sequence,
                            pEventId));
                if (shadow)
                {
                    shadowExpected = HistoricalWriteShadowRules
                        .SummarizeInsert(pTable, operationKey, sequence,
                            columns, pGeneratedEventId: false);
                    shadowActual = envelope.ShadowSummary;
                }
                if (_worker.TryEnqueue(envelope, out _))
                {
                    accepted = true;
                    pError = string.Empty;
                }
                else pError = "historical async writer queue is full";
            }
            if (HistoricalWriteModeRules.ShouldCompareShadow(
                    AWAsyncRuntime.ShadowEnabled, accepted))
                CompareShadow(operationKey, shadowExpected, shadowActual);
            return accepted;
        }

        public static bool TryReserveEventId(string pTable,
            out long pEventId, out string pError)
        {
            lock (Gate)
            {
                if (!_eventIdsReady && !TrySeedAllocatorLocked(out pError))
                {
                    pEventId = 0L;
                    return false;
                }
                try
                {
                    pEventId = _eventIds.Next(pTable);
                    pError = string.Empty;
                    return true;
                }
                catch (Exception error)
                {
                    pEventId = 0L;
                    pError = error.Message;
                    return false;
                }
            }
        }

        public static bool FlushForSave(TimeSpan pTimeout, out string pError)
        {
            HistoricalWriteWorker worker;
            lock (Gate) worker = _worker;
            if (worker == null)
            {
                pError = string.Empty;
                return true;
            }
            long started = Stopwatch.GetTimestamp();
            bool flushed = worker.Flush(pTimeout,
                () => DrainCompletions(64), out pError);
            if (!flushed)
            {
                long elapsedMilliseconds = Math.Max(0L,
                    (Stopwatch.GetTimestamp() - started) * 1000L /
                    Stopwatch.Frequency);
                pError = (pError ?? "historical write flush failed") +
                    "; pending=" + worker.PendingCount +
                    "; earliest_uncommitted=" +
                    worker.EarliestUncommittedSequence +
                    "; elapsed_ms=" + elapsedMilliseconds;
            }
            return flushed;
        }

        public static bool TryUpsertState(string pOperationKey,
            string pTable, IReadOnlyList<HistoricalSqlColumn> pKeys,
            IReadOnlyList<HistoricalSqlColumn> pUpdates,
            IReadOnlyList<HistoricalSqlColumn> pInserts,
            Action<long> pOnCommitted, out long pSequence,
            out string pError)
        {
            return TryUpsertState(pOperationKey, pTable, pKeys, pUpdates,
                pInserts, (Action<long, long>)null, pOnCommitted, null,
                out pSequence,
                out pError);
        }

        public static bool TryUpsertState(string pOperationKey,
            string pTable, IReadOnlyList<HistoricalSqlColumn> pKeys,
            IReadOnlyList<HistoricalSqlColumn> pUpdates,
            IReadOnlyList<HistoricalSqlColumn> pInserts,
            Action<long> pOnCommitted, Action<long, string> pOnFailed,
            out long pSequence, out string pError)
        {
            return TryUpsertState(pOperationKey, pTable, pKeys, pUpdates,
                pInserts, (Action<long, long>)null, pOnCommitted, pOnFailed,
                out pSequence, out pError);
        }

        public static bool TryUpsertState(string pOperationKey,
            string pTable, IReadOnlyList<HistoricalSqlColumn> pKeys,
            IReadOnlyList<HistoricalSqlColumn> pUpdates,
            IReadOnlyList<HistoricalSqlColumn> pInserts,
            Action<long> pOnAccepted, Action<long> pOnCommitted,
            Action<long, string> pOnFailed, out long pSequence,
            out string pError)
        {
            Action<long, long> accepted = pOnAccepted == null
                ? null
                : (sequence, replacedSequence) => pOnAccepted(sequence);
            return TryUpsertState(pOperationKey, pTable, pKeys, pUpdates,
                pInserts, accepted, pOnCommitted, pOnFailed, out pSequence,
                out pError);
        }

        public static bool TryUpsertState(string pOperationKey,
            string pTable, IReadOnlyList<HistoricalSqlColumn> pKeys,
            IReadOnlyList<HistoricalSqlColumn> pUpdates,
            IReadOnlyList<HistoricalSqlColumn> pInserts,
            Action<long, long> pOnAccepted, Action<long> pOnCommitted,
            Action<long, string> pOnFailed, out long pSequence,
            out string pError)
        {
            string shadowExpected = null;
            string shadowActual = null;
            bool accepted = false;
            lock (Gate)
            {
                bool shadow = AWAsyncRuntime.ShadowEnabled;
                if (!HistoricalWriteModeRules.ShouldAttemptAsyncWrite(
                        AWAsyncRuntime.DatabaseEnabled, _worker != null))
                {
                    pSequence = 0L;
                    pError = AWAsyncRuntime.DatabaseEnabled
                        ? "historical async writer is unavailable"
                        : "historical async writer is disabled";
                    return false;
                }
                pSequence = ++_nextSequence;
                HistoricalWriteEnvelope envelope =
                    HistoricalWriteSqlBuilder.BuildUpdateThenInsert(
                        pSequence, pOperationKey, pTable, pKeys, pUpdates,
                        pInserts, new AWAsyncStamp(_worldGeneration,
                            pSequence, pSequence));
                if (shadow)
                {
                    shadowExpected = HistoricalWriteShadowRules
                        .SummarizeState(pTable, pOperationKey, pSequence,
                            pKeys, pUpdates, pInserts);
                    shadowActual = envelope.ShadowSummary;
                }
                Action<long, object> committed = pOnCommitted == null
                    ? null
                    : (sequence, outcome) => pOnCommitted(sequence);
                if (!Callbacks.TryEnqueue(_worker, envelope,
                        pOnAccepted, committed, pOnFailed, out _))
                {
                    pError = "historical async state queue is full";
                }
                else
                {
                    accepted = true;
                    pError = string.Empty;
                }
            }
            if (HistoricalWriteModeRules.ShouldCompareShadow(
                    AWAsyncRuntime.ShadowEnabled, accepted))
                CompareShadow(pOperationKey, shadowExpected, shadowActual);
            return accepted;
        }

        private static void CompareShadow(string operationKey,
            string pExpected, string pActual)
        {
            AWAsyncShadowRuntime.CompareSummary("db", operationKey,
                pExpected, pActual);
        }

        public static bool TryEnqueueCustom(string pOperationKey,
            Func<long, AWAsyncStamp, HistoricalWriteEnvelope> pFactory,
            Action<long, object> pOnCommitted, out long pSequence,
            out string pError)
        {
            return TryEnqueueCustom(pOperationKey, pFactory,
                (Action<long, long>)null, pOnCommitted, null,
                out pSequence, out pError);
        }

        public static bool TryEnqueueCustom(string pOperationKey,
            Func<long, AWAsyncStamp, HistoricalWriteEnvelope> pFactory,
            Action<long, object> pOnCommitted,
            Action<long, string> pOnFailed, out long pSequence,
            out string pError)
        {
            return TryEnqueueCustom(pOperationKey, pFactory,
                (Action<long, long>)null, pOnCommitted, pOnFailed,
                out pSequence, out pError);
        }

        public static bool TryEnqueueCustom(string pOperationKey,
            Func<long, AWAsyncStamp, HistoricalWriteEnvelope> pFactory,
            Action<long> pOnAccepted, Action<long, object> pOnCommitted,
            Action<long, string> pOnFailed, out long pSequence,
            out string pError)
        {
            Action<long, long> accepted = pOnAccepted == null
                ? null
                : (sequence, replacedSequence) => pOnAccepted(sequence);
            return TryEnqueueCustom(pOperationKey, pFactory, accepted,
                pOnCommitted, pOnFailed, out pSequence, out pError);
        }

        public static bool TryEnqueueCustom(string pOperationKey,
            Func<long, AWAsyncStamp, HistoricalWriteEnvelope> pFactory,
            Action<long, long> pOnAccepted,
            Action<long, object> pOnCommitted,
            Action<long, string> pOnFailed, out long pSequence,
            out string pError)
        {
            lock (Gate)
            {
                if (_worker == null || pFactory == null ||
                    string.IsNullOrEmpty(pOperationKey))
                {
                    pSequence = 0L;
                    pError = "historical async writer is unavailable";
                    return false;
                }
                pSequence = ++_nextSequence;
                var stamp = new AWAsyncStamp(_worldGeneration, pSequence,
                    pSequence);
                HistoricalWriteEnvelope envelope = pFactory(pSequence, stamp);
                if (envelope == null || envelope.Sequence != pSequence ||
                    !string.Equals(envelope.OperationKey, pOperationKey,
                        StringComparison.Ordinal))
                {
                    pError = "custom historical write envelope is invalid";
                    return false;
                }
                if (!Callbacks.TryEnqueue(_worker, envelope, pOnAccepted,
                        pOnCommitted, pOnFailed, out _))
                {
                    pError = "historical async custom queue is full";
                    return false;
                }
                pError = string.Empty;
                return true;
            }
        }

        public static bool FlushForSynchronousFallback(TimeSpan pTimeout,
            out string pError)
        {
            return FlushForSave(pTimeout, out pError);
        }

        public static void DrainCompletions(int pMaxBatches)
        {
            DrainCompletionsCore(pMaxBatches, 0L);
        }

        public static void DrainCompletions(double pMilliseconds,
            int pMaxBatches)
        {
            long budget = Math.Max(1L, (long)(Stopwatch.Frequency *
                Math.Max(0.01, pMilliseconds) / 1000.0));
            DrainCompletionsCore(pMaxBatches,
                Stopwatch.GetTimestamp() + budget);
        }

        private static void DrainCompletionsCore(int pMaxBatches,
            long pDeadline)
        {
            HistoricalWriteWorker worker;
            lock (Gate) worker = _worker;
            if (worker == null || pMaxBatches <= 0) return;
            int processed = 0;
            while (processed < pMaxBatches &&
                   (processed == 0 || pDeadline == 0L ||
                    Stopwatch.GetTimestamp() < pDeadline) &&
                   worker.TryDequeueCompletion(
                       out HistoricalWriteCompletion completion))
            {
                processed++;
                for (int index = 0; index < completion.Sequences.Count;
                     index++)
                {
                    long sequence = completion.Sequences[index];
                    Action<long, object> callback;
                    Action<long, string> failureCallback;
                    Callbacks.Take(sequence, out callback,
                        out failureCallback);
                    try
                    {
                        if (completion.IsCommitted)
                        {
                            callback?.Invoke(sequence,
                                completion.Outcomes[index]);
                        }
                        else
                        {
                            failureCallback?.Invoke(sequence,
                                completion.Error);
                        }
                    }
                    catch (Exception error)
                    {
                        ModClass.LogWarning(
                            "Historical write completion failed: " +
                            error.Message);
                    }
                }
            }
        }

        public static bool StopWorld(TimeSpan pTimeout, out string pError)
        {
            lock (LifecycleGate)
            {
                HistoricalWriteWorker worker;
                lock (Gate) worker = _worker;
                if (worker != null)
                {
                    if (!worker.TryStop(pTimeout, out pError))
                    {
                        DrainCompletions(int.MaxValue);
                        long earliest = worker.EarliestUncommittedSequence;
                        if (earliest > 0L && (pError?.IndexOf(
                                "earliest uncommitted sequence",
                                StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                            pError = (pError ??
                                      "historical writer stop failed") +
                                     "; earliest uncommitted sequence " +
                                     earliest;
                        return false;
                    }
                    DrainCompletions(int.MaxValue);
                    worker.Dispose();
                }

                lock (Gate)
                {
                    if (_worker != null && !ReferenceEquals(_worker, worker))
                    {
                        pError = "historical writer changed during stop";
                        return false;
                    }
                    _worker = null;
                    _worldGeneration = 0L;
                    _eventIdsReady = false;
                    Callbacks.Clear();
                }
                pError = string.Empty;
                return true;
            }
        }

        public static void StopWorld(TimeSpan pTimeout)
        {
            if (StopWorld(pTimeout, out string error)) return;
            throw new InvalidOperationException(error);
        }

        private static long ReadMaximumEventId(SQLiteConnection pDatabase,
            string pTable)
        {
            using var command = new SQLiteCommand(pDatabase)
            {
                CommandText = "SELECT IFNULL(MAX(EVENT_ID), 0) FROM \"" +
                              pTable + "\";"
            };
            object result = command.ExecuteScalar();
            return result == null || result == DBNull.Value
                ? 0L
                : Convert.ToInt64(result);
        }

        private static bool TrySeedAllocatorLocked(out string pError)
        {
            try
            {
                SQLiteConnection database =
                    LineageArchiveManager.Instance.OperatingDB;
                if (database == null)
                {
                    pError = "history archive is unavailable";
                    return false;
                }
                var allocator = new HistoryEventIdAllocator(HistoryTables);
                foreach (string table in HistoryTables)
                    allocator.Seed(table,
                        ReadMaximumEventId(database, table));
                _eventIds = allocator;
                _eventIdsReady = true;
                _worldGeneration = AWAsyncRuntime.WorldGeneration;
                _nextSequence = 0L;
                pError = string.Empty;
                return true;
            }
            catch (Exception error)
            {
                pError = error.Message;
                return false;
            }
        }
    }
}
