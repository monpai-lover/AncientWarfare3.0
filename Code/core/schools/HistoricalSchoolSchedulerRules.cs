using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public sealed class HistoricalSchoolSchedulerState
    {
        private int _pendingYear = -1;
        private int _latestAcceptedYear = -1;

        public int PendingYear => _pendingYear;

        public bool EnqueueYear(int pYear)
        {
            if (pYear < 0 || pYear <= _latestAcceptedYear) return false;
            _latestAcceptedYear = pYear;
            _pendingYear = pYear;
            return true;
        }

        public int TakePendingYear()
        {
            int year = _pendingYear;
            _pendingYear = -1;
            return year;
        }

        public bool HasPendingWork() => _pendingYear >= 0;

        public void Clear()
        {
            _pendingYear = -1;
            _latestAcceptedYear = -1;
        }
    }

    public sealed class HistoricalSchoolBoundedYearKeys
    {
        private readonly HashSet<string> _keys =
            new HashSet<string>(StringComparer.Ordinal);
        private int _oldestYear = -1;

        public int Count => _keys.Count;

        public bool Add(int pYear, string pKey)
        {
            if (pYear < 0 || string.IsNullOrEmpty(pKey)) return false;
            Prune(pYear - 1);
            if (pYear < _oldestYear) return false;
            return _keys.Add(pYear + ":" + pKey);
        }

        public bool Contains(int pYear, string pKey)
        {
            return pYear >= 0 && !string.IsNullOrEmpty(pKey) &&
                   _keys.Contains(pYear + ":" + pKey);
        }

        public void Prune(int pOldestYear)
        {
            if (pOldestYear <= _oldestYear) return;
            _oldestYear = pOldestYear;
            _keys.RemoveWhere(p => ParseYear(p) < pOldestYear);
        }

        public void Clear()
        {
            _keys.Clear();
            _oldestYear = -1;
        }

        private static int ParseYear(string pKey)
        {
            int separator = pKey.IndexOf(':');
            return separator > 0 &&
                   int.TryParse(pKey.Substring(0, separator), out int year)
                ? year
                : int.MinValue;
        }
    }
}
