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
        public const string SlaveArmyExisting = "aw3_city_slave_army_existing";
        public const string SlaveArmySlaveCount = "aw3_city_slave_army_slave_count";
        public const string SlaveArmyCaptain = "aw3_city_slave_army_captain";
        public const string SlaveArmyEnsure = "aw3_city_slave_army_ensure";
        public const string SlaveArmyFill = "aw3_city_slave_army_fill";
        public const string SlaveArmyFillScan = "aw3_city_slave_army_fill_scan";
        public const string SlaveArmyFillPromotion = "aw3_city_slave_army_fill_promotion";
        public const string SlaveArmyFillAttach = "aw3_city_slave_army_fill_attach";
        public const string SlaveArmyFrontline = "aw3_city_slave_army_frontline";
        public const string SlaveArmyRecord = "aw3_city_slave_army_record";
        public const string RoyalGuardValidate = "aw3_city_royal_guard_validate";
        public const string RoyalGuardAverage = "aw3_city_royal_guard_average";
        public const string RoyalGuardCandidates = "aw3_city_royal_guard_candidates";
        public const string RoyalGuardActiveArmyFastPath = "aw3_city_royal_guard_active_army_fast_path";
        public const string RoyalGuardActiveFallbackScan = "aw3_city_royal_guard_active_fallback_scan";
        public const string RoyalGuardCandidateScan = "aw3_city_royal_guard_candidate_scan";
        public const string RoyalGuardCandidateScore = "aw3_city_royal_guard_candidate_score";
        public const string RoyalGuardCandidateSort = "aw3_city_royal_guard_candidate_sort";
        public const string RoyalGuardFill = "aw3_city_royal_guard_fill";
        public const string RoyalGuardArmy = "aw3_city_royal_guard_army";
        public const string RoyalGuardRefresh = "aw3_city_royal_guard_refresh";
        public const string RoyalGuardRefreshCaptain = "aw3_city_royal_guard_refresh_captain";
        public const string RoyalGuardRefreshBatch = "aw3_city_royal_guard_refresh_batch";
        public const string RoyalGuardRefreshPersist = "aw3_city_royal_guard_refresh_persist";
        public const string RoyalGuardRefreshRuntime = "aw3_city_royal_guard_refresh_runtime";
        public const string RoyalGuardDismiss = "aw3_city_royal_guard_dismiss";
        public const string ArmyCleanupGuardStrip = "aw3_city_army_cleanup_guard_strip";
        public const string ArmyCleanupSlaveCaptain = "aw3_city_army_cleanup_slave_captain";
        public const string ArmyCleanupSlaveName = "aw3_city_army_cleanup_slave_name";
        public const string ArmyCleanupFiefName = "aw3_city_army_cleanup_fief_name";
        public const string FiefCommandResolve = "aw3_city_fief_command_resolve";
        public const string FiefCommandApply = "aw3_city_fief_command_apply";
        public const string FiefCommandCaptain = "aw3_city_fief_command_captain";
        public const string SpecialArmyCacheHit = "aw3_city_special_army_cache_hit";
        public const string SpecialArmyCacheMiss = "aw3_city_special_army_cache_miss";
        public const string SpecialArmyGlobalScan = "aw3_city_special_army_global_scan";
        public const string DeathBondChildScan = "aw3_death_bond_child_scan";
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
            SlaveArmyExisting,
            SlaveArmySlaveCount,
            SlaveArmyCaptain,
            SlaveArmyEnsure,
            SlaveArmyFill,
            SlaveArmyFillScan,
            SlaveArmyFillPromotion,
            SlaveArmyFillAttach,
            SlaveArmyFrontline,
            SlaveArmyRecord,
            RoyalGuardValidate,
            RoyalGuardAverage,
            RoyalGuardCandidates,
            RoyalGuardActiveArmyFastPath,
            RoyalGuardActiveFallbackScan,
            RoyalGuardCandidateScan,
            RoyalGuardCandidateScore,
            RoyalGuardCandidateSort,
            RoyalGuardFill,
            RoyalGuardArmy,
            RoyalGuardRefresh,
            RoyalGuardRefreshCaptain,
            RoyalGuardRefreshBatch,
            RoyalGuardRefreshPersist,
            RoyalGuardRefreshRuntime,
            RoyalGuardDismiss,
            ArmyCleanupGuardStrip,
            ArmyCleanupSlaveCaptain,
            ArmyCleanupSlaveName,
            ArmyCleanupFiefName,
            FiefCommandResolve,
            FiefCommandApply,
            FiefCommandCaptain,
            SpecialArmyCacheHit,
            SpecialArmyCacheMiss,
            SpecialArmyGlobalScan,
            DeathBondChildScan,
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
