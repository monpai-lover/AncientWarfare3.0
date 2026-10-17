using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    internal sealed class MonthlyAuthorityWorkBatch<T>
    {
        internal readonly int MonthKey;
        internal readonly IReadOnlyList<T> Values;
        internal int Cursor;

        internal MonthlyAuthorityWorkBatch(int pMonthKey,
            IReadOnlyList<T> pValues)
        {
            MonthKey = pMonthKey;
            Values = pValues ?? Array.Empty<T>();
        }
    }

    internal sealed class MonthlyAuthorityWorkQueue<T>
    {
        private const int MaxCatchUpMonths = 12;
        private readonly Queue<MonthlyAuthorityWorkBatch<T>> _pending =
            new Queue<MonthlyAuthorityWorkBatch<T>>();
        private int _lastScheduledMonthKey = int.MinValue;
        private bool _hasScheduledMonth;
        private int _pendingCount;

        internal int PendingCount => _pendingCount;
        internal int PendingBatchCount => _pending.Count;

        internal bool ShouldScheduleMonth(int pMonthKey)
        {
            return !_hasScheduledMonth ||
                   pMonthKey > _lastScheduledMonthKey;
        }

        internal bool ScheduleMonth(int pMonthKey, IEnumerable<T> pValues)
        {
            if (!ShouldScheduleMonth(pMonthKey)) return false;

            // Large simulation passes can cross more than one calendar month
            // before authority work runs. Snapshot the current population once
            // and replay it for each missed month so monthly services do not
            // silently lose pregnancy, policy, levy, or war observations.
            // IReadOnlyList only restricts this reference; its owner can still
            // mutate the underlying list while queued months are draining.
            IReadOnlyList<T> snapshot = pValues == null
                ? Array.Empty<T>()
                : new List<T>(pValues);
            long firstCandidate = !_hasScheduledMonth
                ? pMonthKey
                : (long)_lastScheduledMonthKey + 1L;
            long catchUpFloor = (long)pMonthKey - MaxCatchUpMonths + 1L;
            long firstMonth = Math.Max(firstCandidate, catchUpFloor);
            // A 32-bit inclusive cursor wraps at int.MaxValue and never exits.
            for (long month = firstMonth; month <= pMonthKey; month++)
            {
                if (snapshot.Count == 0) continue;
                _pending.Enqueue(new MonthlyAuthorityWorkBatch<T>(
                    (int)month, snapshot));
                _pendingCount += snapshot.Count;
            }
            _lastScheduledMonthKey = pMonthKey;
            _hasScheduledMonth = true;
            return true;
        }

        internal int Drain(int pMaximumItems, Action<int, T> pProcess)
        {
            if (pMaximumItems <= 0 || pProcess == null) return 0;
            int processed = 0;
            while (processed < pMaximumItems && _pending.Count > 0)
            {
                MonthlyAuthorityWorkBatch<T> batch = _pending.Peek();
                if (batch.Cursor >= batch.Values.Count)
                {
                    _pending.Dequeue();
                    continue;
                }

                T value = batch.Values[batch.Cursor++];
                _pendingCount--;
                if (batch.Cursor >= batch.Values.Count)
                    _pending.Dequeue();
                processed++;
                pProcess(batch.MonthKey, value);
            }
            return processed;
        }

        internal void ResetScheduleGate()
        {
            _lastScheduledMonthKey = int.MinValue;
            _hasScheduledMonth = false;
        }

        internal void Clear()
        {
            _pending.Clear();
            _pendingCount = 0;
            ResetScheduleGate();
        }
    }
}
