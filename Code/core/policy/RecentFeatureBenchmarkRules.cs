using System;

namespace AncientWarfare3.core.policy
{
    public enum RecentActorAiCategory
    {
        None = 0,
        School = 1,
        Military = 2,
        OtherAw3 = 3
    }

    public static class RecentFeatureBenchmarkRules
    {
        public const string Group = "aw3_recent_runtime";
        public const string Total = "aw3_recent_runtime_total";
        public const string TotalParentGroup = "aw3_recent_runtime_summary";
        public const string EnvironmentVariable = "AW3_BENCHMARK";

        public const int PathfindingIndex = 0;
        public const int SchoolsIndex = 1;
        public const int DiplomacyIndex = 2;
        public const int DeferredWorkIndex = 3;
        public const int CaptureScanIndex = 4;
        public const int SchoolMapIndex = 5;
        public const int ArmyMarchIndex = 6;
        public const int WartimeGarrisonIndex = 7;
        public const int KingdomAnnualQueueIndex = 8;
        public const int KingdomMobilizationIndex = 9;
        public const int KingdomDiplomacyIndex = 10;
        public const int KingdomCourtSupportIndex = 11;
        public const int KingdomFeudatoryIndex = 12;
        public const int PathSubmitIndex = 13;
        public const int PathMovementIndex = 14;
        public const int NameplatesIndex = 15;
        public const int MinimapMarkersIndex = 16;
        public const int MapDirtyIndex = 17;
        public const int OccupationIndex = 18;
        public const int KingdomAuxiliaryLawsIndex = 19;
        public const int ConferredPosthumousIndex = 20;
        public const int KingdomHeirIndex = 21;
        public const int KingdomRoyalAsylumIndex = 22;
        public const int KingdomCourtAuxiliaryIndex = 23;
        public const int KingdomDiplomaticMarriageIndex = 24;
        public const int KingdomDiplomaticOperationIndex = 25;
        public const int RoyalGuardGraphicsIndex = 26;
        public const int RoyalGuardArchiveIndex = 27;
        public const int RoyalGuardRosterIndex = 28;
        public const int ArmyAttackMissingStateIndex = 29;
        public const int ArmyAttackRetreatHoldIndex = 30;
        public const int ArmyAttackVanguardHoldIndex = 31;
        public const int ArmyAttackCrossIslandIndex = 32;
        public const int ArmyAttackReadyIndex = 33;
        public const int KingdomInheritanceLawIndex = 34;
        public const int KingdomHeirReconcileIndex = 35;
        public const int KingdomSuccessionDisputeIndex = 36;
        public const int ActorAiSchoolIndex = 37;
        public const int ActorAiMilitaryIndex = 38;
        public const int ActorAiOtherIndex = 39;
        public const int AsyncCaptureIndex = 40;
        public const int AsyncComputeIndex = 41;
        public const int AsyncCommitIndex = 42;
        public const int ArmyRtsCoalitionIndex = 43;
        public const int ArmyRtsDirectorIndex = 44;
        public const int ArmyRtsControllerIndex = 45;
        public const int ArmyRtsLogisticsIndex = 46;
        public const int ArmyRtsWatchdogIndex = 47;
        public const int CivilServiceExamAnnualIndex = 48;
        public const int CivilServiceExamRuntimeIndex = 49;

        public const string Pathfinding = "aw3_runtime_pathfinding";
        public const string Schools = "aw3_runtime_schools";
        public const string Diplomacy = "aw3_runtime_diplomacy";
        public const string DeferredWork = "aw3_runtime_deferred_work";
        public const string CaptureScan = "aw3_runtime_capture_scan";
        public const string SchoolMap = "aw3_runtime_school_map";
        public const string ArmyMarch = "aw3_actor_army_march";
        public const string WartimeGarrison = "aw3_runtime_wartime_garrison";
        public const string KingdomAnnualQueue = "aw3_year_queue_submit";
        public const string KingdomMobilization = "aw3_year_war_mobilization";
        public const string KingdomDiplomacy = "aw3_year_diplomacy";
        public const string KingdomCourtSupport = "aw3_year_court_support";
        public const string KingdomFeudatory = "aw3_year_feudatory";
        public const string PathSubmit = "aw3_actor_path_submit";
        public const string PathMovement = "aw3_actor_path_movement";
        public const string Nameplates = "aw3_render_nameplates";
        public const string MinimapMarkers = "aw3_render_minimap_markers";
        public const string MapDirty = "aw3_render_map_dirty";
        public const string Occupation = "aw3_runtime_occupation";
        public const string KingdomAuxiliaryLaws = "aw3_year_auxiliary_laws";
        public const string ConferredPosthumous = "aw3_year_conferred_posthumous";
        public const string KingdomHeir = "aw3_year_heir";
        public const string KingdomRoyalAsylum = "aw3_year_royal_asylum";
        public const string KingdomCourtAuxiliary = "aw3_year_court_auxiliary";
        public const string KingdomDiplomaticMarriage =
            "aw3_year_diplomatic_marriage";
        public const string KingdomDiplomaticOperation =
            "aw3_year_diplomatic_operation";
        public const string RoyalGuardGraphics = "aw3_guard_graphics";
        public const string RoyalGuardArchive = "aw3_guard_archive";
        public const string RoyalGuardRoster = "aw3_guard_roster";
        public const string ArmyAttackMissingState =
            "aw3_army_attack_missing_state";
        public const string ArmyAttackRetreatHold =
            "aw3_army_attack_retreat_hold";
        public const string ArmyAttackVanguardHold =
            "aw3_army_attack_vanguard_hold";
        public const string ArmyAttackCrossIsland =
            "aw3_army_attack_cross_island";
        public const string ArmyAttackReady = "aw3_army_attack_ready";
        public const string KingdomInheritanceLaw =
            "aw3_year_heir_inheritance_law";
        public const string KingdomHeirReconcile =
            "aw3_year_heir_reconcile";
        public const string KingdomSuccessionDispute =
            "aw3_year_heir_dispute";
        public const string ActorAiSchool = "aw3_actor_ai_school";
        public const string ActorAiMilitary = "aw3_actor_ai_military";
        public const string ActorAiOther = "aw3_actor_ai_other";
        public const string AsyncCapture = "aw3_async_capture";
        public const string AsyncCompute = "aw3_async_compute";
        public const string AsyncCommit = "aw3_async_commit";
        public const string ArmyRtsCoalition = "aw3_army_rts_coalition";
        public const string ArmyRtsDirector = "aw3_army_rts_director";
        public const string ArmyRtsController = "aw3_army_rts_controller";
        public const string ArmyRtsLogistics = "aw3_army_rts_logistics";
        public const string ArmyRtsWatchdog = "aw3_army_rts_watchdog";
        public const string CivilServiceExamAnnual =
            "aw3_year_civil_service_exam";
        public const string CivilServiceExamRuntime =
            "aw3_runtime_civil_service_exam";

        public static readonly string[] EntryIds =
        {
            Pathfinding,
            Schools,
            Diplomacy,
            DeferredWork,
            CaptureScan,
            SchoolMap,
            ArmyMarch,
            WartimeGarrison,
            KingdomAnnualQueue,
            KingdomMobilization,
            KingdomDiplomacy,
            KingdomCourtSupport,
            KingdomFeudatory,
            PathSubmit,
            PathMovement,
            Nameplates,
            MinimapMarkers,
            MapDirty,
            Occupation,
            KingdomAuxiliaryLaws,
            ConferredPosthumous,
            KingdomHeir,
            KingdomRoyalAsylum,
            KingdomCourtAuxiliary,
            KingdomDiplomaticMarriage,
            KingdomDiplomaticOperation,
            RoyalGuardGraphics,
            RoyalGuardArchive,
            RoyalGuardRoster,
            ArmyAttackMissingState,
            ArmyAttackRetreatHold,
            ArmyAttackVanguardHold,
            ArmyAttackCrossIsland,
            ArmyAttackReady,
            KingdomInheritanceLaw,
            KingdomHeirReconcile,
            KingdomSuccessionDispute,
            ActorAiSchool,
            ActorAiMilitary,
            ActorAiOther,
            AsyncCapture,
            AsyncCompute,
            AsyncCommit,
            ArmyRtsCoalition,
            ArmyRtsDirector,
            ArmyRtsController,
            ArmyRtsLogistics,
            ArmyRtsWatchdog,
            CivilServiceExamAnnual,
            CivilServiceExamRuntime
        };

        public static RecentActorAiCategory ClassifyActorAiTask(
            string pTaskId)
        {
            if (string.IsNullOrEmpty(pTaskId) ||
                !pTaskId.StartsWith("aw_", StringComparison.Ordinal))
                return RecentActorAiCategory.None;
            if (pTaskId.StartsWith("aw_historical_school_",
                    StringComparison.Ordinal))
                return RecentActorAiCategory.School;
            if (pTaskId.StartsWith("aw_war_", StringComparison.Ordinal) ||
                pTaskId.StartsWith("aw_wartime_garrison_",
                    StringComparison.Ordinal))
                return RecentActorAiCategory.Military;
            return RecentActorAiCategory.OtherAw3;
        }

        public static int ActorAiIndex(RecentActorAiCategory pCategory)
        {
            return pCategory == RecentActorAiCategory.School
                ? ActorAiSchoolIndex
                : pCategory == RecentActorAiCategory.Military
                    ? ActorAiMilitaryIndex
                    : pCategory == RecentActorAiCategory.OtherAw3
                        ? ActorAiOtherIndex
                        : -1;
        }

        public static bool IsValidIndex(int pIndex)
        {
            return pIndex >= 0 && pIndex < EntryIds.Length;
        }

        public static string IdForIndex(int pIndex)
        {
            return IsValidIndex(pIndex) ? EntryIds[pIndex] : "";
        }

        public static bool Contains(string pId)
        {
            return Array.IndexOf(EntryIds, pId) >= 0;
        }

        public static bool ShouldEnableFromEnvironment(string pValue)
        {
            return string.Equals(pValue, "1", StringComparison.Ordinal) ||
                   string.Equals(pValue, "true",
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldLogSnapshot(string diagnosticSwitch,
            double totalMilliseconds)
        {
            return totalMilliseconds > 0d &&
                   ShouldEnableFromEnvironment(diagnosticSwitch);
        }

        public static long EncodeScopeStart(long startTicks,
            int depthBeforeBegin)
        {
            long normalized = Math.Max(1L, startTicks);
            return depthBeforeBegin <= 0 ? normalized : -normalized;
        }

        public static bool IsOutermostScopeToken(long pToken)
        {
            return pToken > 0L;
        }

        public static long DecodeScopeStart(long pToken)
        {
            if (pToken == long.MinValue) return long.MaxValue;
            return Math.Abs(pToken);
        }

        public static long ExclusiveScopeTicks(long elapsedTicks,
            long nestedTicks)
        {
            return Math.Max(0L, elapsedTicks - Math.Max(0L, nestedTicks));
        }

        public static bool ShouldSaveSample(int pCalls)
        {
            return pCalls >= 0;
        }
    }
}
