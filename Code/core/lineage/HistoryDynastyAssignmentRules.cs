using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct HistoryReignTimelineSegment
    {
        public readonly bool HasKing;
        public readonly double StartTime;
        public readonly double EndTime;
        public readonly int OpeningTransitionIndex;

        public HistoryReignTimelineSegment(bool pHasKing,
            double pStartTime, double pEndTime,
            int pOpeningTransitionIndex)
        {
            HasKing = pHasKing;
            StartTime = pStartTime;
            EndTime = pEndTime;
            OpeningTransitionIndex = pOpeningTransitionIndex;
        }
    }

    public static class HistoryDynastyAssignmentRules
    {
        public static bool ShouldAssignToDynasty(bool pHasKing)
        {
            return pHasKing;
        }

        public static bool ShouldCreateFallbackDynasty(
            IReadOnlyList<bool> pHasKingPeriods)
        {
            if (pHasKingPeriods == null) return false;
            for (int index = 0; index < pHasKingPeriods.Count; index++)
                if (pHasKingPeriods[index]) return true;
            return false;
        }

        public static bool TryGetFallbackDynastyRange(
            IReadOnlyList<bool> pHasKingPeriods,
            IReadOnlyList<double> pStarts,
            IReadOnlyList<double> pEnds,
            out double pStart, out double pEnd)
        {
            pStart = 0d;
            pEnd = -1d;
            int count = Math.Min(pHasKingPeriods?.Count ?? 0,
                Math.Min(pStarts?.Count ?? 0, pEnds?.Count ?? 0));
            bool found = false;
            for (int index = 0; index < count; index++)
            {
                if (!pHasKingPeriods[index]) continue;
                if (!found)
                {
                    pStart = pStarts[index];
                    found = true;
                }
                pEnd = pEnds[index];
            }
            return found;
        }

        public static int SelectIndex(double pReignStart,
            IReadOnlyList<double> pDynastyStarts,
            IReadOnlyList<double> pDynastyEnds)
        {
            int count = Math.Min(pDynastyStarts?.Count ?? 0,
                pDynastyEnds?.Count ?? 0);
            if (count <= 0) return -1;
            if (pReignStart < pDynastyStarts[0]) return 0;

            int best = 0;
            double bestDistance = double.MaxValue;
            for (int index = 0; index < count; index++)
            {
                double start = pDynastyStarts[index];
                double end = pDynastyEnds[index];
                bool contains = pReignStart >= start &&
                                (end < 0 || pReignStart < end);
                if (contains) return index;

                double distance = pReignStart < start
                    ? start - pReignStart
                    : end >= 0
                        ? Math.Max(0d, pReignStart - end)
                        : 0d;
                if (distance >= bestDistance) continue;
                best = index;
                bestDistance = distance;
            }
            return best;
        }

        public static List<HistoryReignTimelineSegment> BuildReignTimeline(
            double pInitialTime, IReadOnlyList<double> pTransitionTimes,
            IReadOnlyList<bool> pBeginsKingPeriod,
            IReadOnlyList<long> pEventIds)
        {
            int count = Math.Min(pTransitionTimes?.Count ?? 0,
                Math.Min(pBeginsKingPeriod?.Count ?? 0,
                    pEventIds?.Count ?? 0));
            var order = new List<int>(count);
            for (int index = 0; index < count; index++) order.Add(index);
            order.Sort((left, right) =>
            {
                int time = pTransitionTimes[left].CompareTo(
                    pTransitionTimes[right]);
                return time != 0
                    ? time
                    : pEventIds[left].CompareTo(pEventIds[right]);
            });

            var result = new List<HistoryReignTimelineSegment>
            {
                new HistoryReignTimelineSegment(false, pInitialTime, -1d, -1)
            };
            foreach (int transitionIndex in order)
            {
                bool beginsKing = pBeginsKingPeriod[transitionIndex];
                HistoryReignTimelineSegment current = result[result.Count - 1];
                if (!beginsKing && !current.HasKing) continue;

                double transitionTime = Math.Max(current.StartTime,
                    pTransitionTimes[transitionIndex]);
                result[result.Count - 1] =
                    new HistoryReignTimelineSegment(current.HasKing,
                        current.StartTime, transitionTime,
                        current.OpeningTransitionIndex);
                result.Add(new HistoryReignTimelineSegment(beginsKing,
                    transitionTime, -1d, transitionIndex));
            }
            return result;
        }
    }
}
