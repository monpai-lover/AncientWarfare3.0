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
        private int _pendingCount;

        internal int PendingCount => _pendingCount;
        internal int PendingBatchCount => _pending.Count;

        internal bool ShouldScheduleMonth(int pMonthKey)
        {
            return _lastScheduledMonthKey == int.MinValue ||
                   pMonthKey > _lastScheduledMonthKey;
        }

        internal bool ScheduleMonth(int pMonthKey, IEnumerable<T> pValues)
        {
            if (!ShouldScheduleMonth(pMonthKey)) return false;

            // Large simulation passes can cross more than one calendar month
            // before authority work runs. Snapshot the current population once
            // and replay it for each missed month so monthly services do not
            // silently lose pregnancy, policy, levy, or war observations.
            IReadOnlyList<T> snapshot = pValues as IReadOnlyList<T> ??
                (pValues == null
                    ? Array.Empty<T>()
                    : new List<T>(pValues));
            long firstCandidate = _lastScheduledMonthKey == int.MinValue
                ? pMonthKey
                : (long)_lastScheduledMonthKey + 1L;
            long catchUpFloor = (long)pMonthKey - MaxCatchUpMonths + 1L;
            int firstMonth = (int)Math.Max(firstCandidate, catchUpFloor);
            for (int month = firstMonth; month <= pMonthKey; month++)
            {
                if (snapshot.Count == 0) continue;
                _pending.Enqueue(new MonthlyAuthorityWorkBatch<T>(
                    month, snapshot));
                _pendingCount += snapshot.Count;
            }
            _lastScheduledMonthKey = pMonthKey;
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
        }

        internal void Clear()
        {
            _pending.Clear();
            _pendingCount = 0;
            ResetScheduleGate();
        }
    }
}
