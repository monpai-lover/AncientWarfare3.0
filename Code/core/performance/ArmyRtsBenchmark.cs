using System;
using System.Threading;

namespace AncientWarfare3.core.performance
{
    public enum ArmyRtsRouteLifecycle
    {
        Submitted = 0,
        Reused = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }

    public readonly struct ArmyRtsBenchmarkSnapshot
    {
        internal ArmyRtsBenchmarkSnapshot(long[] pValues)
        {
            PlannerPasses = pValues[0];
            Missions = pValues[1];
            TargetComparisons = pValues[2];
            TargetAgreements = pValues[3];
            DuplicateReservations = pValues[4];
            RoutesSubmitted = pValues[5];
            RoutesReused = pValues[6];
            RoutesCompleted = pValues[7];
            RoutesFailed = pValues[8];
            RoutesCancelled = pValues[9];
            FormationCorrections = pValues[10];
            Retreats = pValues[11];
            Replans = pValues[12];
            NoProgressMilliseconds = pValues[13];
        }

        public long PlannerPasses { get; }
        public long Missions { get; }
        public long TargetComparisons { get; }
        public long TargetAgreements { get; }
        public long DuplicateReservations { get; }
        public long RoutesSubmitted { get; }
        public long RoutesReused { get; }
        public long RoutesCompleted { get; }
        public long RoutesFailed { get; }
        public long RoutesCancelled { get; }
        public long FormationCorrections { get; }
        public long Retreats { get; }
        public long Replans { get; }
        public long NoProgressMilliseconds { get; }

        public long ValueAt(int pIndex)
        {
            return pIndex switch
            {
                0 => PlannerPasses,
                1 => Missions,
                2 => TargetComparisons,
                3 => TargetAgreements,
                4 => DuplicateReservations,
                5 => RoutesSubmitted,
                6 => RoutesReused,
                7 => RoutesCompleted,
                8 => RoutesFailed,
                9 => RoutesCancelled,
                10 => FormationCorrections,
                11 => Retreats,
                12 => Replans,
                13 => NoProgressMilliseconds,
                _ => 0L
            };
        }
    }

    public static class ArmyRtsBenchmark
    {
        public const string Group = "aw3_army_rts_counters";
        public const string Total = "aw3_army_rts_counter_total";
        public const string TotalParentGroup =
            "aw3_army_rts_counter_summary";

        public static readonly string[] EntryIds =
        {
            "army_rts_planner_passes",
            "army_rts_missions",
            "army_rts_target_comparisons",
            "army_rts_target_agreements",
            "army_rts_duplicate_reservations",
            "army_rts_routes_submitted",
            "army_rts_routes_reused",
            "army_rts_routes_completed",
            "army_rts_routes_failed",
            "army_rts_routes_cancelled",
            "army_rts_formation_corrections",
            "army_rts_retreats",
            "army_rts_replans",
            "army_rts_no_progress_ms"
        };

        private static readonly long[] Lifetime =
            new long[EntryIds.Length];
        private static readonly long[] Interval =
            new long[EntryIds.Length];

        public static void RecordPlannerPass(int missions,
            int targetComparisons, int targetAgreements,
            int duplicateReservations)
        {
            Add(0, 1L);
            Add(1, missions);
            Add(2, targetComparisons);
            Add(3, targetAgreements);
            Add(4, duplicateReservations);
        }

        public static void RecordRoute(ArmyRtsRouteLifecycle pLifecycle)
        {
            int index = pLifecycle switch
            {
                ArmyRtsRouteLifecycle.Submitted => 5,
                ArmyRtsRouteLifecycle.Reused => 6,
                ArmyRtsRouteLifecycle.Completed => 7,
                ArmyRtsRouteLifecycle.Failed => 8,
                ArmyRtsRouteLifecycle.Cancelled => 9,
                _ => -1
            };
            Add(index, 1L);
        }

        public static void RecordFormationCorrection()
        {
            Add(10, 1L);
        }

        public static void RecordRetreat()
        {
            Add(11, 1L);
        }

        public static void RecordReplan()
        {
            Add(12, 1L);
        }

        public static void RecordNoProgressSeconds(double pSeconds)
        {
            if (double.IsNaN(pSeconds) || double.IsInfinity(pSeconds) ||
                pSeconds <= 0d) return;
            double milliseconds = pSeconds * 1000d;
            long value = milliseconds >= long.MaxValue
                ? long.MaxValue
                : (long)Math.Round(milliseconds,
                    MidpointRounding.AwayFromZero);
            Add(13, value);
        }

        public static ArmyRtsBenchmarkSnapshot Snapshot()
        {
            var values = new long[EntryIds.Length];
            for (var index = 0; index < values.Length; index++)
                values[index] = Interlocked.Read(ref Lifetime[index]);
            return new ArmyRtsBenchmarkSnapshot(values);
        }

        public static ArmyRtsBenchmarkSnapshot TakeIntervalSnapshot()
        {
            var values = new long[EntryIds.Length];
            for (var index = 0; index < values.Length; index++)
                values[index] = Interlocked.Exchange(
                    ref Interval[index], 0L);
            return new ArmyRtsBenchmarkSnapshot(values);
        }

        public static void Reset()
        {
            for (var index = 0; index < EntryIds.Length; index++)
            {
                Interlocked.Exchange(ref Lifetime[index], 0L);
                Interlocked.Exchange(ref Interval[index], 0L);
            }
        }

        private static void Add(int pIndex, long pValue)
        {
            if (pIndex < 0 || pIndex >= EntryIds.Length || pValue <= 0L)
                return;
            Interlocked.Add(ref Lifetime[pIndex], pValue);
            Interlocked.Add(ref Interval[pIndex], pValue);
        }
    }
}
