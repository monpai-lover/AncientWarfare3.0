using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolSchedulerRules
    {
        public const int MaxTravelActorsPerQuarter = 24;
        public const int MaxDestinationTileProbes = 24;
        public const int MaxDescentAttemptsPerFrame = 1;

        public static int DescentAttemptBudget(bool pHasPendingDescent,
            int pRemainingCandidates)
        {
            if (pHasPendingDescent) return 0;
            return Math.Min(MaxDescentAttemptsPerFrame,
                Math.Max(0, pRemainingCandidates));
        }

        public static int QuarterlyTravelWorkCount(int pEligibleActorCount)
        {
            return Math.Min(MaxTravelActorsPerQuarter,
                Math.Max(0, pEligibleActorCount));
        }

        public static int DestinationTileProbeCount(int pCandidateCount)
        {
            return Math.Min(MaxDestinationTileProbes,
                Math.Max(0, pCandidateCount));
        }

    }

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

    public sealed class HistoricalSchoolBoundedWorkCursor
    {
        private readonly int _start;
        private readonly int _count;
        private readonly int _sourceCount;

        public HistoricalSchoolBoundedWorkCursor(int pStart, int pCount,
            int pSourceCount)
        {
            _sourceCount = Math.Max(0, pSourceCount);
            _count = Math.Min(Math.Max(0, pCount), _sourceCount);
            _start = _sourceCount == 0
                ? 0
                : PositiveModulo(pStart, _sourceCount);
        }

        public int Processed { get; private set; }
        public bool IsComplete => Processed >= _count;

        public bool TryTake(out int pSourceIndex)
        {
            if (IsComplete || _sourceCount <= 0)
            {
                pSourceIndex = -1;
                return false;
            }
            pSourceIndex = (_start + Processed) % _sourceCount;
            Processed++;
            return true;
        }

        private static int PositiveModulo(int pValue, int pCount)
        {
            int value = pValue % pCount;
            return value < 0 ? value + pCount : value;
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
