using System;

namespace AncientWarfare3.core.policy
{
    public static class CityMaintenanceBenchmarkRules
    {
        public const string Group = "game_total";
        public const string Total = "aw3_city_army_maint_total";
        public const string Retirements = "aw3_city_retirements";
        public const string SlaveLabor = "aw3_city_slave_labor";
        public const string SlaveCatchers = "aw3_city_slave_catchers";
        public const string RoyalGuard = "aw3_city_royal_guard";
        public const string FiefCommand = "aw3_city_fief_command";
        public const string ArmyCleanup = "aw3_city_army_cleanup";
        public const string RetirementsScan = "aw3_city_retirements_scan";
        public const string SlaveLaborCount = "aw3_city_slave_labor_count";
        public const string SlaveCatchersJobGate = "aw3_city_slave_catchers_job_gate";
        public const string SlaveCatchersTargetScan = "aw3_city_slave_catchers_target_scan";
        public const string SlaveArmyNameScan = "aw3_city_slave_army_name_scan";
        public const string RoyalGuardValidate = "aw3_city_royal_guard_validate";
        public const string RoyalGuardAverage = "aw3_city_royal_guard_average";
        public const string RoyalGuardCandidates = "aw3_city_royal_guard_candidates";
        public const string RoyalGuardFill = "aw3_city_royal_guard_fill";
        public const string RoyalGuardArmy = "aw3_city_royal_guard_army";
        public const string RoyalGuardRefresh = "aw3_city_royal_guard_refresh";
        public const string RoyalGuardDismiss = "aw3_city_royal_guard_dismiss";
        public const string ArmyCleanupGuardStrip = "aw3_city_army_cleanup_guard_strip";
        public const string ArmyCleanupSlaveCaptain = "aw3_city_army_cleanup_slave_captain";
        public const string ArmyCleanupSlaveName = "aw3_city_army_cleanup_slave_name";
        public const string ArmyCleanupFiefName = "aw3_city_army_cleanup_fief_name";
        public const string FiefCommandResolve = "aw3_city_fief_command_resolve";
        public const string FiefCommandApply = "aw3_city_fief_command_apply";
        public const string FiefCommandCaptain = "aw3_city_fief_command_captain";
        public const string Food = "aw3_city_food";
        public const string Status = "aw3_city_status";
        public const string Citizens = "aw3_city_citizens";
        public const string Capture = "aw3_city_capture";

        public static readonly string[] EntryIds =
        {
            Total,
            Retirements,
            SlaveLabor,
            SlaveCatchers,
            RoyalGuard,
            FiefCommand,
            ArmyCleanup,
            RetirementsScan,
            SlaveLaborCount,
            SlaveCatchersJobGate,
            SlaveCatchersTargetScan,
            SlaveArmyNameScan,
            RoyalGuardValidate,
            RoyalGuardAverage,
            RoyalGuardCandidates,
            RoyalGuardFill,
            RoyalGuardArmy,
            RoyalGuardRefresh,
            RoyalGuardDismiss,
            ArmyCleanupGuardStrip,
            ArmyCleanupSlaveCaptain,
            ArmyCleanupSlaveName,
            ArmyCleanupFiefName,
            FiefCommandResolve,
            FiefCommandApply,
            FiefCommandCaptain,
            Food,
            Status,
            Citizens,
            Capture
        };

        public static bool Contains(string pId)
        {
            return Array.IndexOf(EntryIds, pId) >= 0;
        }
    }
}
