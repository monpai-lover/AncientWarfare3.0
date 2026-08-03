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
        private readonly Queue<MonthlyAuthorityWorkItem<T>> _pending =
            new Queue<MonthlyAuthorityWorkItem<T>>();
        private int _lastScheduledMonthKey = int.MinValue;

        internal int PendingCount => _pending.Count;

        internal bool ScheduleMonth(int pMonthKey, IEnumerable<T> pValues)
        {
            if (pMonthKey == _lastScheduledMonthKey) return false;
            _lastScheduledMonthKey = pMonthKey;
            if (pValues != null)
                foreach (T value in pValues)
                    _pending.Enqueue(new MonthlyAuthorityWorkItem<T>(
                        pMonthKey, value));
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
