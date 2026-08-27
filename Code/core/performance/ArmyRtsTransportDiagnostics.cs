using System.Threading;

namespace AncientWarfare3.core.performance
{
    internal static class ArmyRtsTransportDiagnostics
    {
        private static long _routeCandidateFailures;
        private static long _emergencyShoreRoutes;
        private static long _temporaryBoatsCreated;
        private static long _temporaryBoatsDestroyed;
        private static long _boardingTimeouts;
        private static long _sailingTimeouts;
        private static long _landingTimeouts;
        private static long _cooldownSuppressedReplans;

        internal static long RouteCandidateFailures =>
            Interlocked.Read(ref _routeCandidateFailures);
        internal static long EmergencyShoreRoutes =>
            Interlocked.Read(ref _emergencyShoreRoutes);
        internal static long TemporaryBoatsCreated =>
            Interlocked.Read(ref _temporaryBoatsCreated);
        internal static long TemporaryBoatsDestroyed =>
            Interlocked.Read(ref _temporaryBoatsDestroyed);
        internal static long BoardingTimeouts =>
            Interlocked.Read(ref _boardingTimeouts);
        internal static long SailingTimeouts =>
            Interlocked.Read(ref _sailingTimeouts);
        internal static long LandingTimeouts =>
            Interlocked.Read(ref _landingTimeouts);
        internal static long CooldownSuppressedReplans =>
            Interlocked.Read(ref _cooldownSuppressedReplans);

        internal static void RecordRouteCandidateFailure() =>
            Interlocked.Increment(ref _routeCandidateFailures);
        internal static void RecordEmergencyShoreRoute() =>
            Interlocked.Increment(ref _emergencyShoreRoutes);
        internal static void RecordTemporaryBoatCreated() =>
            Interlocked.Increment(ref _temporaryBoatsCreated);
        internal static void RecordTemporaryBoatDestroyed() =>
            Interlocked.Increment(ref _temporaryBoatsDestroyed);
        internal static void RecordBoardingTimeout() =>
            Interlocked.Increment(ref _boardingTimeouts);
        internal static void RecordSailingTimeout() =>
            Interlocked.Increment(ref _sailingTimeouts);
        internal static void RecordLandingTimeout() =>
            Interlocked.Increment(ref _landingTimeouts);
        internal static void RecordCooldownSuppressedReplan() =>
            Interlocked.Increment(ref _cooldownSuppressedReplans);

        internal static string Snapshot()
        {
            return "route_candidate_failures=" + RouteCandidateFailures +
                   ",emergency_shore_routes=" + EmergencyShoreRoutes +
                   ",temporary_boats_created=" + TemporaryBoatsCreated +
                   ",temporary_boats_destroyed=" + TemporaryBoatsDestroyed +
                   ",boarding_timeouts=" + BoardingTimeouts +
                   ",sailing_timeouts=" + SailingTimeouts +
                   ",landing_timeouts=" + LandingTimeouts +
                   ",cooldown_suppressed_replans=" +
                   CooldownSuppressedReplans;
        }

        internal static void Reset()
        {
            Interlocked.Exchange(ref _routeCandidateFailures, 0L);
            Interlocked.Exchange(ref _emergencyShoreRoutes, 0L);
            Interlocked.Exchange(ref _temporaryBoatsCreated, 0L);
            Interlocked.Exchange(ref _temporaryBoatsDestroyed, 0L);
            Interlocked.Exchange(ref _boardingTimeouts, 0L);
            Interlocked.Exchange(ref _sailingTimeouts, 0L);
            Interlocked.Exchange(ref _landingTimeouts, 0L);
            Interlocked.Exchange(ref _cooldownSuppressedReplans, 0L);
        }
    }
}
