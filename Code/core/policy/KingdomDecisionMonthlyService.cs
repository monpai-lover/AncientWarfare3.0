namespace AncientWarfare3.core.policy
{
    internal static class KingdomDecisionMonthlyService
    {
        private static int _lastProcessedMonthKey = int.MinValue;

        internal static void Reset()
        {
            _lastProcessedMonthKey = int.MinValue;
        }

        internal static void ProcessAuthorityCycle()
        {
            if (World.world?.kingdoms == null) return;
            int monthKey = KingdomDecisionMonthlyRules.ToMonthKey(
                Date.getCurrentYear(), Date.getCurrentMonth());
            if (!KingdomDecisionMonthlyRules.ShouldProcessMonth(monthKey,
                    _lastProcessedMonthKey)) return;
            _lastProcessedMonthKey = monthKey;
            foreach (Kingdom kingdom in World.world.kingdoms)
                KingdomPolicyService.OnKingdomDecisionMonth(kingdom,
                    monthKey);
        }
    }
}
