using System;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace CityMaintenanceRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectStaggeredCityMaintenance();
            ExpectRetirementCheapGate();
            ExpectRetirementStateReadGate();
            ExpectCityRetirementScanGate();
            ExpectOldHeadRefreshGate();
            ExpectSlaveFoodQuotaGate();
            ExpectSlaveArmyMaintenanceGate();
            ExpectSlaveArmyFillPipeline();
            ExpectSlaveLaborPerformanceGate();
            ExpectSlaveArmyPerformanceRules();
            ExpectSpecialArmyCleanupLifecycle();
            ExpectSlaveMeritPersistence();
            ExpectArmyAiSafetyGate();
            ExpectArmySaveSafetyGate();

            Console.WriteLine("City maintenance rule tests passed.");
            return 0;
        }

        private static void ExpectStaggeredCityMaintenance()
        {
            if (CityMaintenanceThrottleRules.ShouldRunStaggered(
                    pNow: 100, pLastRun: -1, pInterval: 20, pObjectId: 3))
                throw new Exception("First staggered maintenance should wait for the city id slot.");

            if (!CityMaintenanceThrottleRules.ShouldRunStaggered(
                    pNow: 103, pLastRun: -1, pInterval: 20, pObjectId: 3))
                throw new Exception("First staggered maintenance should run on its city id slot.");

            if (CityMaintenanceThrottleRules.ShouldRunStaggered(
                    pNow: 119, pLastRun: 103, pInterval: 20, pObjectId: 3))
                throw new Exception("Staggered maintenance should not run before the interval.");

            if (!CityMaintenanceThrottleRules.ShouldRunStaggered(
                    pNow: 123, pLastRun: 103, pInterval: 20, pObjectId: 3))
                throw new Exception("Staggered maintenance should run on the next city id slot.");

            if (!CityMaintenanceThrottleRules.ShouldRunStaggered(
                    pNow: 90, pLastRun: 103, pInterval: 20, pObjectId: 3))
                throw new Exception("Staggered maintenance should recover if world time moves backward.");
        }

        private static void ExpectRetirementCheapGate()
        {
            if (SoldierRetirementRules.ShouldRunExpensiveRetirementChecks(
                    isSupportedActor: true,
                    isRekt: false,
                    isWarrior: false,
                    alreadyRetired: false,
                    age: 80f,
                    lifespan: 100f,
                    retirementAgeRatio: 0.7f))
                throw new Exception("Non-warriors should skip expensive retirement checks.");

            if (SoldierRetirementRules.ShouldRunExpensiveRetirementChecks(
                    isSupportedActor: true,
                    isRekt: false,
                    isWarrior: true,
                    alreadyRetired: false,
                    age: 40f,
                    lifespan: 100f,
                    retirementAgeRatio: 0.7f))
                throw new Exception("Young warriors should skip expensive retirement checks.");

            if (!SoldierRetirementRules.ShouldRunExpensiveRetirementChecks(
                    isSupportedActor: true,
                    isRekt: false,
                    isWarrior: true,
                    alreadyRetired: false,
                    age: 75f,
                    lifespan: 100f,
                    retirementAgeRatio: 0.7f))
                throw new Exception("Old active warriors should run expensive retirement checks.");

            if (SoldierRetirementRules.ShouldRunExpensiveRetirementChecks(
                    isSupportedActor: true,
                    isRekt: false,
                    isWarrior: true,
                    alreadyRetired: false,
                    age: 75f,
                    lifespan: 0f,
                    retirementAgeRatio: 0.7f))
                throw new Exception("Invalid lifespan should skip expensive retirement checks.");
        }

        private static void ExpectCityRetirementScanGate()
        {
            if (SoldierRetirementRules.ShouldRunCityRetirementScan(
                    pActorUpdateAgeRetirementEnabled: true,
                    pMaintenanceDue: true))
                throw new Exception("City retirement scans should be disabled when actor updateAge retirement is active.");

            if (SoldierRetirementRules.ShouldRunCityRetirementScan(
                    pActorUpdateAgeRetirementEnabled: true,
                    pMaintenanceDue: false))
                throw new Exception("City retirement scans should not run before their maintenance gate.");

            if (!SoldierRetirementRules.ShouldRunCityRetirementScan(
                    pActorUpdateAgeRetirementEnabled: false,
                    pMaintenanceDue: true))
                throw new Exception("City retirement scan fallback should remain available if actor updateAge retirement is disabled.");
        }

        private static void ExpectRetirementStateReadGate()
        {
            if (SoldierRetirementRules.ShouldReadRetirementState(
                    isSupportedActor: true,
                    isRekt: false,
                    isWarrior: false))
                throw new Exception("Non-warriors should not read retired-soldier state during actor updateAge.");
            if (!SoldierRetirementRules.ShouldEnterActorUpdateAgeRetirement(
                    isSupportedActor: true,
                    isRekt: false,
                    isWarrior: true))
                throw new Exception("Supported live warriors should enter actor updateAge retirement checks.");
            if (SoldierRetirementRules.ShouldEnterActorUpdateAgeRetirement(
                    isSupportedActor: false,
                    isRekt: false,
                    isWarrior: true))
                throw new Exception("Unsupported actors should not enter actor updateAge retirement checks.");
            if (SoldierRetirementRules.ShouldEnterActorUpdateAgeRetirement(
                    isSupportedActor: true,
                    isRekt: false,
                    isWarrior: false))
                throw new Exception("Non-warriors should not enter actor updateAge retirement checks.");

            if (SoldierRetirementRules.ShouldReadRetirementState(
                    isSupportedActor: false,
                    isRekt: false,
                    isWarrior: true))
                throw new Exception("Unsupported actors should not read retired-soldier state.");

            if (!SoldierRetirementRules.ShouldReadRetirementState(
                    isSupportedActor: true,
                    isRekt: false,
                    isWarrior: true))
                throw new Exception("Supported active warriors should read retired-soldier state.");
        }

        private static void ExpectOldHeadRefreshGate()
        {
            if (XiaOldHeadRefreshRules.ShouldRefresh(wasOldHead: false, shouldUseOldHead: false))
                throw new Exception("Young Xia actors should not refresh head graphics just to mark seen state.");

            if (!XiaOldHeadRefreshRules.ShouldRefresh(wasOldHead: false, shouldUseOldHead: true))
                throw new Exception("Xia actors crossing into old-head state should refresh graphics.");

            if (!XiaOldHeadRefreshRules.ShouldRefresh(wasOldHead: true, shouldUseOldHead: false))
                throw new Exception("Xia actors leaving old-head state should refresh graphics.");

            if (XiaOldHeadRefreshRules.ShouldRefresh(wasOldHead: true, shouldUseOldHead: true))
                throw new Exception("Stable old-head Xia actors should skip graphics refresh.");
        }

        private static void ExpectSlaveFoodQuotaGate()
        {
            if (SlaveFoodQuotaRules.ShouldCountSlavesForFoodQuota(
                    pHasCity: true,
                    pSlaveryEnabled: false,
                    pForceCount: false))
                throw new Exception("Cities in kingdoms without slavery should not scan residents for slave food quota.");

            if (SlaveFoodQuotaRules.ShouldCountSlavesForFoodQuota(
                    pHasCity: false,
                    pSlaveryEnabled: true,
                    pForceCount: true))
                throw new Exception("Invalid cities should not scan slave food quota.");

            if (!SlaveFoodQuotaRules.ShouldCountSlavesForFoodQuota(
                    pHasCity: true,
                    pSlaveryEnabled: true,
                    pForceCount: false))
                throw new Exception("Slavery-enabled cities should count slaves for food quota.");

            if (!SlaveFoodQuotaRules.ShouldCountSlavesForFoodQuota(
                    pHasCity: true,
                    pSlaveryEnabled: false,
                    pForceCount: true))
                throw new Exception("Actual slave food requests should be allowed to force a quota recount.");
        }

        private static void ExpectSlaveArmyMaintenanceGate()
        {
            if (SlaveArmyMaintenanceRules.ShouldRunMaintenance(
                    pSlaveryEnabled: false,
                    pSlaveArmyEnabled: true,
                    pOnSchedule: true))
                throw new Exception("Disabled slavery should skip slave army maintenance.");

            if (SlaveArmyMaintenanceRules.ShouldRunMaintenance(
                    pSlaveryEnabled: true,
                    pSlaveArmyEnabled: false,
                    pOnSchedule: true))
                throw new Exception("Disabled slave army policy should skip slave army maintenance.");

            if (SlaveArmyMaintenanceRules.ShouldRunMaintenance(
                    pSlaveryEnabled: true,
                    pSlaveArmyEnabled: true,
                    pOnSchedule: false))
                throw new Exception("Slave army maintenance should wait for its staggered schedule.");

            if (!SlaveArmyMaintenanceRules.ShouldRunMaintenance(
                    pSlaveryEnabled: true,
                    pSlaveArmyEnabled: true,
                    pOnSchedule: true))
                throw new Exception("Enabled slave army maintenance should run on its staggered schedule.");

            if (!SlaveArmyMaintenanceRules.ShouldSkipStableArmyFill(
                    pArmyExists: true,
                    pTotalWarriors: 10,
                    pSlaveWarriors: 8,
                    pNonSlaveWarriors: 2,
                    pCaptainValid: true,
                    pCitySlaveCount: 8))
                throw new Exception("Underfilled slave armies should skip fill scans when all local slaves are already enlisted.");

            if (SlaveArmyMaintenanceRules.ShouldSkipStableArmyFill(
                    pArmyExists: true,
                    pTotalWarriors: 10,
                    pSlaveWarriors: 8,
                    pNonSlaveWarriors: 2,
                    pCaptainValid: true,
                    pCitySlaveCount: 12))
                throw new Exception("Underfilled slave armies should keep filling when local slave candidates remain.");

            if (!SlaveArmyMaintenanceRules.ShouldSkipAfterFailedMaintenance(
                    pNow: 120,
                    pLastFailure: 112,
                    pCooldownYears: 12))
                throw new Exception("Slave army maintenance should skip repeated failed fills during cooldown.");

            if (SlaveArmyMaintenanceRules.ShouldSkipAfterFailedMaintenance(
                    pNow: 125,
                    pLastFailure: 112,
                    pCooldownYears: 12))
                throw new Exception("Slave army maintenance should resume after the failed-fill cooldown.");
        }

        private static void ExpectSlaveArmyFillPipeline()
        {
            if (SlaveArmyMaintenanceRules.ShouldPromoteCandidate(
                    pCompositionAllowsCandidate: false, pAlreadyWarrior: false,
                    pPromotionsThisPass: 0, pPromotionLimit: 2))
                throw new Exception("Capacity and composition must be checked before promotion.");
            if (!SlaveArmyMaintenanceRules.ShouldPromoteCandidate(
                    pCompositionAllowsCandidate: true, pAlreadyWarrior: false,
                    pPromotionsThisPass: 1, pPromotionLimit: 2))
                throw new Exception("An eligible second promotion should be allowed.");
            if (SlaveArmyMaintenanceRules.ShouldPromoteCandidate(
                    pCompositionAllowsCandidate: true, pAlreadyWarrior: false,
                    pPromotionsThisPass: 2, pPromotionLimit: 2))
                throw new Exception("A third promotion in one pulse must be deferred.");
            if (!SlaveArmyMaintenanceRules.ShouldPreferReadyWarrior(
                    pCandidateIsWarrior: true, pHavePromotionCandidate: true))
                throw new Exception("Existing warriors must be attached before converting citizens.");
            if (SlaveArmyMaintenanceRules.NextScanCursor(
                    pStartCursor: 10, pScanned: 16, pScanComplete: false) != 26)
                throw new Exception("Incomplete candidate scans must persist their cursor.");
            if (SlaveArmyMaintenanceRules.NextScanCursor(
                    pStartCursor: 10, pScanned: 16, pScanComplete: true) != 0)
                throw new Exception("A completed candidate scan must reset its cursor.");
            if (!SlaveArmyMaintenanceRules.ShouldScheduleContinuation(
                    pArmyUnderfilled: true, pScanComplete: false, pAddedThisPass: 2))
                throw new Exception("Underfilled armies with remaining candidates need a short continuation.");
            if (!SlaveArmyMaintenanceRules.ShouldRunMaintenance(
                    pSlaveryEnabled: true, pSlaveArmyEnabled: true,
                    pOnSchedule: false, pContinuationDue: true))
                throw new Exception("A due short continuation must run before the full maintenance interval.");

            if (!CityMaintenanceBenchmarkRules.Contains(CityMaintenanceBenchmarkRules.SlaveArmyFillScan) ||
                !CityMaintenanceBenchmarkRules.Contains(CityMaintenanceBenchmarkRules.SlaveArmyFillPromotion) ||
                !CityMaintenanceBenchmarkRules.Contains(CityMaintenanceBenchmarkRules.SlaveArmyFillAttach))
                throw new Exception("Slave army fill must expose scan, promotion, and attach profiler labels.");
            if (!CityMaintenanceBenchmarkRules.Contains(CityMaintenanceBenchmarkRules.SlaveArmy))
                throw new Exception("Slave army maintenance must have its own profiler label.");
        }

        private static void ExpectSlaveLaborPerformanceGate()
        {
            if (SlaveArmyMaintenanceRules.ShouldCheckSlaveLabor(
                    pHasCity: true, pHasKingdom: true, pSlaveryEnabled: false,
                    pAlreadyRecordedForKingdom: false, pMaintenanceDue: true))
                throw new Exception("Non-slavery cities must skip slave-labor resident scans.");
            if (!SlaveArmyMaintenanceRules.ShouldCheckSlaveLabor(
                    pHasCity: true, pHasKingdom: true, pSlaveryEnabled: true,
                    pAlreadyRecordedForKingdom: false, pMaintenanceDue: true))
                throw new Exception("Due slavery cities must run slave-labor recording.");
            if (SlaveArmyMaintenanceRules.ShouldCheckSlaveLabor(
                    pHasCity: true, pHasKingdom: true, pSlaveryEnabled: true,
                    pAlreadyRecordedForKingdom: true, pMaintenanceDue: true))
                throw new Exception("Recorded slave labor must remain a constant-time fast path.");
        }

        private static void ExpectSlaveArmyPerformanceRules()
        {
            if (SlaveArmyMaintenanceRules.ShouldInferSlaveArmyComposition(
                    pRoleMarkedSlaveArmy: false, pSlaveryEnabled: false))
                throw new Exception("Ordinary non-slavery armies must skip composition scans.");
            if (!SlaveArmyMaintenanceRules.ShouldInferSlaveArmyComposition(
                    pRoleMarkedSlaveArmy: false, pSlaveryEnabled: true))
                throw new Exception("Legacy armies in slavery kingdoms retain composition fallback.");
            if (!SlaveArmyMaintenanceRules.HasReachedFormationThreshold(3, 3) ||
                SlaveArmyMaintenanceRules.HasReachedFormationThreshold(2, 3))
                throw new Exception("Slave formation counting must stop exactly at the minimum.");
            if (!SlaveArmyMaintenanceRules.ShouldReuseFrontlineTarget(
                    pHasEntry: true, pTargetAlive: true, pStillHostile: true,
                    pSameIsland: true, pNow: 10.0, pExpiresAt: 20.0))
                throw new Exception("A valid same-island frontline target should be shared.");
            if (SlaveArmyMaintenanceRules.ShouldReuseFrontlineTarget(
                    pHasEntry: true, pTargetAlive: false, pStillHostile: true,
                    pSameIsland: true, pNow: 10.0, pExpiresAt: 20.0))
                throw new Exception("Dead frontline targets must invalidate the cache.");
            if (!SlaveArmyMaintenanceRules.ShouldReuseFrontlineMiss(
                    pHasEntry: true, pCachedMiss: true, pNow: 10.0, pExpiresAt: 20.0))
                throw new Exception("A current same-island miss should suppress duplicate global scans.");
            if (SlaveArmyMaintenanceRules.ShouldReuseFrontlineMiss(
                    pHasEntry: true, pCachedMiss: true, pNow: 21.0, pExpiresAt: 20.0))
                throw new Exception("An expired frontline miss must allow a fresh search.");
            if (SlaveArmyMaintenanceRules.ShouldIssueFrontlineOrder(
                    pAlreadyTargetsActor: true, pIsMoving: true))
                throw new Exception("Identical active path orders must not be reissued.");
            if (!SlaveArmyMaintenanceRules.ShouldIssueFrontlineOrder(
                    pAlreadyTargetsActor: true, pIsMoving: false))
                throw new Exception("Interrupted units must be allowed to resume the same target.");
            if (!SlaveArmyMaintenanceRules.ShouldReuseCityWarriorCounts(
                    pHasEntry: true, pNow: 10.0, pExpiresAt: 11.0) ||
                SlaveArmyMaintenanceRules.ShouldReuseCityWarriorCounts(
                    pHasEntry: true, pNow: 12.0, pExpiresAt: 11.0))
                throw new Exception("Repeated conscription candidates should reuse only current warrior counts.");
        }

        private static void ExpectSpecialArmyCleanupLifecycle()
        {
            if (SpecialArmyLookupCacheRules.ShouldCleanupDuplicates(
                    pCreated: false, pReanchored: false, pPostLoadRepair: false))
                throw new Exception("Valid EnsureArmy cache hits must skip global duplicate scans.");
            if (!SpecialArmyLookupCacheRules.ShouldCleanupDuplicates(
                    pCreated: true, pReanchored: false, pPostLoadRepair: false) ||
                !SpecialArmyLookupCacheRules.ShouldCleanupDuplicates(
                    pCreated: false, pReanchored: true, pPostLoadRepair: false) ||
                !SpecialArmyLookupCacheRules.ShouldCleanupDuplicates(
                    pCreated: false, pReanchored: false, pPostLoadRepair: true))
                throw new Exception("Create, re-anchor, and load repair must retain duplicate recovery.");
        }

        private static void ExpectSlaveMeritPersistence()
        {
            if (SlaveMeritPersistenceRules.ShouldPersist(
                    pOldMerit: 0, pNewMerit: 1, pPoints: 1,
                    pMilestone: 4, pFreedomThreshold: 8))
                throw new Exception("An ordinary one-point kill must not synchronously write SQLite.");
            if (!SlaveMeritPersistenceRules.ShouldPersist(
                    pOldMerit: 3, pNewMerit: 4, pPoints: 1,
                    pMilestone: 4, pFreedomThreshold: 8))
                throw new Exception("Crossing a merit milestone must persist archive state.");
            if (!SlaveMeritPersistenceRules.ShouldPersist(
                    pOldMerit: 1, pNewMerit: 5, pPoints: 4,
                    pMilestone: 4, pFreedomThreshold: 8))
                throw new Exception("Important multi-point kills must persist archive state.");
            if (SlaveMeritPersistenceRules.ShouldPersist(
                    pOldMerit: 7, pNewMerit: 8, pPoints: 1,
                    pMilestone: 4, pFreedomThreshold: 8))
                throw new Exception("Freedom performs the final write and must avoid a duplicate write.");
        }

        private static void ExpectArmyAiSafetyGate()
        {
            if (!ArmyAiSafetyRules.ShouldSkipCityAttackAction(
                    pHasActor: true,
                    pHasCity: true,
                    pHasAttackZone: true,
                    pHasArmy: false,
                    pHasCurrentTile: true,
                    pHasCurrentZone: true))
                throw new Exception("Warriors without an army must skip the original city attack action.");

            if (ArmyAiSafetyRules.ShouldSkipCityAttackAction(
                    pHasActor: true,
                    pHasCity: true,
                    pHasAttackZone: true,
                    pHasArmy: true,
                    pHasCurrentTile: true,
                    pHasCurrentZone: true))
                throw new Exception("Complete city attack context should use the original action.");
        }

        private static void ExpectArmySaveSafetyGate()
        {
            if (!ArmySaveSafetyRules.ShouldRemoveInvalidSpecialArmy(
                    pIsSpecialArmy: true,
                    pHasKingdom: false,
                    pHasCity: false,
                    pHasCaptain: false,
                    pUnitCount: 0))
                throw new Exception("Empty special armies with no kingdom anchor should be removed before saving.");

            if (ArmySaveSafetyRules.ShouldRemoveInvalidSpecialArmy(
                    pIsSpecialArmy: true,
                    pHasKingdom: true,
                    pHasCity: false,
                    pHasCaptain: true,
                    pUnitCount: 3))
                throw new Exception("Valid detached special armies should be kept.");
        }
    }
}
