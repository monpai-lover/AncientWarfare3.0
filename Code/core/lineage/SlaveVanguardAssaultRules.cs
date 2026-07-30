namespace AncientWarfare3.core.lineage
{
    public static class SlaveVanguardAssaultRules
    {
        public const double DaysPerYear = 365d;
        public const int MaximumHeadStartDays = 60;

        public static bool ShouldHoldOrdinaryArmy(
            bool pActorIsCaptain,
            bool pActorArmyIsVanguard,
            bool pVanguardReady,
            bool pSameAttackTarget,
            bool pVanguardReachedTarget,
            bool pVanguardRetreating,
            bool pHeadStartExpired = false)
        {
            if (!pActorIsCaptain || pActorArmyIsVanguard) return false;
            if (!pVanguardReady || !pSameAttackTarget) return false;
            return !pVanguardReachedTarget && !pVanguardRetreating &&
                   !pHeadStartExpired;
        }

        public static bool IsHeadStartExpired(double startedAt, double now)
        {
            if (double.IsNaN(startedAt) || double.IsInfinity(startedAt) ||
                double.IsNaN(now) || double.IsInfinity(now) ||
                startedAt < 0d || now < startedAt) return true;
            return now - startedAt >= MaximumHeadStartDays / DaysPerYear;
        }
    }
}
