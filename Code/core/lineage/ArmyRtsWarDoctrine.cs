#if !AW3_RULES_TESTS
using AncientWarfare3.core.performance;
#endif

namespace AncientWarfare3.core.lineage
{
#if !AW3_RULES_TESTS
    internal static class ArmyRtsWarDoctrine
    {
        private static readonly ArmyRtsWarResolutionMode StartupMode =
            ArmyRtsWarDoctrineRules.Normalize(
                AWPerformanceSettings.ArmyRtsWarResolutionModeIndex);

        public static ArmyRtsWarResolutionMode Current => StartupMode;

        public static bool IsLastStand =>
            Current == ArmyRtsWarResolutionMode.LastStand;

        public static bool IsAbstractDecisive =>
            Current == ArmyRtsWarResolutionMode.AbstractDecisive;

        public static bool ShouldCreateStrategicRoute =>
            ArmyRtsWarDoctrineRules.ShouldCreateStrategicRoute(Current);

        public static bool ShouldResolveRemoteDuel =>
            ArmyRtsWarDoctrineRules.ShouldResolveRemoteDuel(Current);
    }
#endif
}
