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
            // 停扫闩是按运行时信号记的,换存档必须清掉,否则新世界会带着
            // 旧世界的「已经全部核心化」结论开局。
            KingdomPolicyService.ClearCoreTargetLatch();
        }

        internal static void ProcessAuthorityCycle()
        {
            if (World.world?.kingdoms == null) return;
            int monthKey = KingdomDecisionMonthlyRules.ToMonthKey(
                Date.getCurrentYear(), Date.getCurrentMonth());
            MonthlyWork.ScheduleMonth(monthKey,
                MonthlyKingdomSnapshotService.Get(monthKey));
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
