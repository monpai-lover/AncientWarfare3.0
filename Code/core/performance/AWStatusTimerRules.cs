namespace AncientWarfare3.core.performance
{
    internal static class AWStatusTimerRules
    {
        internal static long CountTimerDecrementTicks(float pTimer,
            float pStep)
        {
            if (float.IsNaN(pTimer) || pTimer <= 0f) return 0L;
            if (float.IsPositiveInfinity(pTimer)) return long.MaxValue;
            if (float.IsNaN(pStep) || pStep <= 0f) return long.MaxValue;

            long ticks = 0L;
            float remaining = pTimer;
            while (remaining > 0f)
            {
                remaining -= pStep;
                if (ticks == long.MaxValue - 1L)
                    return long.MaxValue;
                ticks++;
            }
            return ticks;
        }
    }
}
