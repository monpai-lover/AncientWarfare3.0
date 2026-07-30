namespace AncientWarfare3.core.policy
{
    public static class RecentFeatureBenchmarkSnapshotRules
    {
        public static int SlowestIndex(long[] pTicks)
        {
            if (pTicks == null || pTicks.Length == 0) return -1;
            int slowest = 0;
            long maximum = pTicks[0];
            for (int i = 1; i < pTicks.Length; i++)
            {
                if (pTicks[i] <= maximum) continue;
                maximum = pTicks[i];
                slowest = i;
            }
            return slowest;
        }
    }
}
