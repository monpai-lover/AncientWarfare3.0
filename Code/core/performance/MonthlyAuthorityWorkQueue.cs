using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    internal readonly struct MonthlyAuthorityWorkItem<T>
    {
        internal readonly int MonthKey;
        internal readonly T Value;

        internal MonthlyAuthorityWorkItem(int pMonthKey, T pValue)
        {
            MonthKey = pMonthKey;
            Value = pValue;
        }
    }

    internal sealed class MonthlyAuthorityWorkQueue<T>
    {
        private const int MaxCatchUpMonths = 12;
        private readonly Queue<MonthlyAuthorityWorkItem<T>> _pending =
            new Queue<MonthlyAuthorityWorkItem<T>>();
        private int _lastScheduledMonthKey = int.MinValue;

        internal int PendingCount => _pending.Count;

        internal bool ScheduleMonth(int pMonthKey, IEnumerable<T> pValues)
        {
            if (_lastScheduledMonthKey != int.MinValue &&
                pMonthKey <= _lastScheduledMonthKey)
                return false;

            // Large simulation passes can cross more than one calendar month
            // before authority work runs. Snapshot the current population once
            // and replay it for each missed month so monthly services do not
            // silently lose pregnancy, policy, levy, or war observations.
            List<T> snapshot = pValues == null
                ? new List<T>()
                : new List<T>(pValues);
            long firstCandidate = _lastScheduledMonthKey == int.MinValue
                ? pMonthKey
                : (long)_lastScheduledMonthKey + 1L;
            long catchUpFloor = (long)pMonthKey - MaxCatchUpMonths + 1L;
            int firstMonth = (int)Math.Max(firstCandidate, catchUpFloor);
            for (int month = firstMonth; month <= pMonthKey; month++)
                foreach (T value in snapshot)
                    _pending.Enqueue(new MonthlyAuthorityWorkItem<T>(
                        month, value));
            _lastScheduledMonthKey = pMonthKey;
            return true;
        }

        internal int Drain(int pMaximumItems, Action<int, T> pProcess)
        {
            if (pMaximumItems <= 0 || pProcess == null) return 0;
            int processed = 0;
            while (processed < pMaximumItems && _pending.Count > 0)
            {
                MonthlyAuthorityWorkItem<T> item = _pending.Dequeue();
                processed++;
                pProcess(item.MonthKey, item.Value);
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
            ResetScheduleGate();
        }
    }
}
