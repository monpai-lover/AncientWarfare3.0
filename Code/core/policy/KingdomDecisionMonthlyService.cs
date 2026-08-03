using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.policy
{
    internal static class KingdomDecisionMonthlyService
    {
        private const int KingdomsPerAuthorityCycle = 1;
        private static readonly MonthlyAuthorityWorkQueue<Kingdom>
            MonthlyWork = new MonthlyAuthorityWorkQueue<Kingdom>();

        internal static int PendingMonthlyWorkForDiagnostics =>
            MonthlyWork.PendingCount;

        internal static void Reset()
        {
            MonthlyWork.Clear();
        }

        internal static void ProcessAuthorityCycle()
        {
            if (World.world?.kingdoms == null) return;
            int monthKey = KingdomDecisionMonthlyRules.ToMonthKey(
                Date.getCurrentYear(), Date.getCurrentMonth());
            MonthlyWork.ScheduleMonth(monthKey, World.world.kingdoms);
            MonthlyWork.Drain(KingdomsPerAuthorityCycle,
                (queuedMonthKey, kingdom) =>
                {
                    long benchmark = RecentFeatureBenchmark.Begin();
                    try
                    {
                        KingdomPolicyService.OnKingdomDecisionMonth(kingdom,
                            queuedMonthKey);
                    }
                    finally
                    {
                        RecentFeatureBenchmark.End(
                            RecentFeatureBenchmarkRules.MonthKingdomPolicyIndex,
                            benchmark);
                    }
                });
        }
    }
}
