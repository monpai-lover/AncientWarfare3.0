using System;

namespace AncientWarfare3.core.policy
{
    public static class CityMaintenanceBenchmarkRules
    {
        public const string Group = "aw3_city_runtime";
        public const string Total = "aw3_city_army_maint_total";
        public const string Retirements = "aw3_city_retirements";
        public const string StandingArmy = "aw3_city_standing_army";
        public const string SlaveLabor = "aw3_city_slave_labor";
        public const string SlaveCatchers = "aw3_city_slave_catchers";
        public const string RoyalGuard = "aw3_city_royal_guard";
        public const string FiefCommand = "aw3_city_fief_command";
        public const string ArmyCleanup = "aw3_city_army_cleanup";
        public const string RetirementsScan = "aw3_city_retirements_scan";
        public const string SlaveLaborCount = "aw3_city_slave_labor_count";
        public const string SlaveCatchersJobGate = "aw3_city_slave_catchers_job_gate";
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
        public const string RoyalGuardThreatScan = "aw3_ai_royal_guard_threat_scan";
        public const string SlaveMeritPersist = "aw3_slave_merit_persist";
        public const string DeferredFlush = "aw3_deferred_flush";
        public const string CaptureScanSubmit = "aw3_capture_scan_submit";
        public const string CaptureScanStep = "aw3_capture_scan_step";
        public const string CaptureCacheHit = "aw3_capture_cache_hit";
        public const string ArmyCleanupGuardStrip = "aw3_city_army_cleanup_guard_strip";
        public const string ArmyCleanupSlaveCaptain = "aw3_city_army_cleanup_slave_captain";
        public const string ArmyCleanupFiefName = "aw3_city_army_cleanup_fief_name";
        public const string EmptyArmyDetection = "aw3_empty_army_detection";
        public const string EmptyArmyRemoval = "aw3_empty_army_removal";
        public const string FiefCommandResolve = "aw3_city_fief_command_resolve";
        public const string FiefCommandApply = "aw3_city_fief_command_apply";
        public const string FiefCommandCaptain = "aw3_city_fief_command_captain";
        public const string SpecialArmyCacheHit = "aw3_city_special_army_cache_hit";
        public const string SpecialArmyCacheMiss = "aw3_city_special_army_cache_miss";
        public const string DeathBondChildScan = "aw3_death_bond_child_scan";
        public const string Food = "aw3_city_food";
        public const string Status = "aw3_city_status";
        public const string Citizens = "aw3_city_citizens";
        public const string Capture = "aw3_city_capture";

        public static readonly string[] EntryIds =
        {
            Total,
            Retirements,
            StandingArmy,
            SlaveLabor,
            SlaveCatchers,
            RoyalGuard,
            FiefCommand,
            ArmyCleanup,
            RetirementsScan,
            SlaveLaborCount,
            SlaveCatchersJobGate,
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
            RoyalGuardThreatScan,
            SlaveMeritPersist,
            DeferredFlush,
            CaptureScanSubmit,
            CaptureScanStep,
            CaptureCacheHit,
            ArmyCleanupGuardStrip,
            ArmyCleanupSlaveCaptain,
            ArmyCleanupFiefName,
            EmptyArmyDetection,
            EmptyArmyRemoval,
            FiefCommandResolve,
            FiefCommandApply,
            FiefCommandCaptain,
            SpecialArmyCacheHit,
            SpecialArmyCacheMiss,
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
