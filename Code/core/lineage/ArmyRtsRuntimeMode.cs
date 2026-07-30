#if !AW3_RULES_TESTS
using AncientWarfare3.core.performance;
#endif

namespace AncientWarfare3.core.lineage
{
    public enum ArmyRtsMode
    {
        Off = 0,
        Shadow = 1,
        On = 2
    }

    public static class ArmyRtsRuntimeModeRules
    {
        public static ArmyRtsMode ResolveConfiguredMode(bool pEnabled)
        {
            return pEnabled ? ArmyRtsMode.On : ArmyRtsMode.Off;
        }

        public static bool ShouldPlan(ArmyRtsMode pMode)
        {
            return pMode != ArmyRtsMode.Off;
        }

        public static bool ShouldCommit(ArmyRtsMode pMode)
        {
            return pMode == ArmyRtsMode.On;
        }

        public static bool ShouldUseLegacyArmyFollowerOrders(
            ArmyRtsMode pMode)
        {
            return !ShouldCommit(pMode);
        }

        public static bool ShouldUseLegacyStrategicWrites(
            ArmyRtsMode pMode)
        {
            return !ShouldCommit(pMode);
        }

        public static bool ShouldAllowVanillaStrategicDecision(
            ArmyRtsMode pMode, string pDecisionId)
        {
            if (!ShouldCommit(pMode)) return true;
            switch (pDecisionId)
            {
                case "warrior_army_captain_idle_walking_city":
                case "warrior_army_captain_waiting":
                case "warrior_army_leader_move_random":
                case "warrior_army_leader_move_to_attack_target":
                case "warrior_army_follow_leader":
                case "warrior_random_move":
                case "check_warrior_transport":
                    return false;
                default:
                    return true;
            }
        }

        public static bool ShouldAllowVanillaDecisionEvaluation(
            ArmyRtsMode pMode, bool rtsOwnsActor)
        {
            return !ShouldCommit(pMode) || !rtsOwnsActor;
        }

        public static string LogName(ArmyRtsMode pMode)
        {
            return pMode switch
            {
                ArmyRtsMode.Off => "off",
                ArmyRtsMode.On => "on",
                _ => "shadow"
            };
        }
    }

#if !AW3_RULES_TESTS
    internal static class ArmyRtsRuntimeMode
    {
        private static readonly ArmyRtsMode StartupMode =
            ArmyRtsRuntimeModeRules.ResolveConfiguredMode(
                AWPerformanceSettings.EnableArmyRts);

        public static ArmyRtsMode Current => StartupMode;
        public static bool ShouldPlan =>
            ArmyRtsRuntimeModeRules.ShouldPlan(StartupMode);
        public static bool ShouldCommit =>
            ArmyRtsRuntimeModeRules.ShouldCommit(StartupMode);
        public static string LogName =>
            ArmyRtsRuntimeModeRules.LogName(StartupMode);
    }
#endif
}
