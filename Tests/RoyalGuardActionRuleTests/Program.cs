using System;
using AncientWarfare3.core.lineage;

namespace RoyalGuardActionRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectNoFollowMove("no_target", pHasTarget: false, pTargetIsCurrentTile: false);
            ExpectNoFollowMove("already_at_target", pHasTarget: true, pTargetIsCurrentTile: true);
            ExpectFollowMove("new_target", pHasTarget: true, pTargetIsCurrentTile: false);

            ExpectNoThreatSearch("peaceful_idle", pHasEnemyWar: false, pKingOrGuardUnderAttack: false);
            ExpectThreatSearch("wartime", pHasEnemyWar: true, pKingOrGuardUnderAttack: false);
            ExpectThreatSearch("direct_attack", pHasEnemyWar: false, pKingOrGuardUnderAttack: true);

            ExpectRecruitmentBatching();
            ExpectRuntimeRefreshBatching();
            ExpectGraphicsRebuildBudget();
            ExpectDismissScanGate();
            ExpectDissolutionRules();
            ExpectGuardArmyCleanupGate();
            ExpectGuardSpatialSearchRadius();

            if (RoyalGuardActionRules.WaitAfterNoThreat(2f, 5f) != 2f)
                throw new Exception("Expected royal guard no-threat wait low bound.");
            if (RoyalGuardActionRules.WaitAfterFollowIdle(3f, 6f) != 3f)
                throw new Exception("Expected royal guard follow-idle wait low bound.");

            Console.WriteLine("Royal guard action rule tests passed.");
            return 0;
        }

        private static void ExpectNoFollowMove(string pLabel, bool pHasTarget, bool pTargetIsCurrentTile)
        {
            if (RoyalGuardActionRules.ShouldIssueFollowMove(pHasTarget, pTargetIsCurrentTile))
                throw new Exception($"Expected follow move '{pLabel}' to be blocked.");
        }

        private static void ExpectFollowMove(string pLabel, bool pHasTarget, bool pTargetIsCurrentTile)
        {
            if (!RoyalGuardActionRules.ShouldIssueFollowMove(pHasTarget, pTargetIsCurrentTile))
                throw new Exception($"Expected follow move '{pLabel}' to be allowed.");
        }

        private static void ExpectNoThreatSearch(string pLabel, bool pHasEnemyWar, bool pKingOrGuardUnderAttack)
        {
            if (RoyalGuardActionRules.ShouldSearchThreats(pHasEnemyWar, pKingOrGuardUnderAttack))
                throw new Exception($"Expected threat search '{pLabel}' to be blocked.");
        }

        private static void ExpectThreatSearch(string pLabel, bool pHasEnemyWar, bool pKingOrGuardUnderAttack)
        {
            if (!RoyalGuardActionRules.ShouldSearchThreats(pHasEnemyWar, pKingOrGuardUnderAttack))
                throw new Exception($"Expected threat search '{pLabel}' to be allowed.");
        }

        private static void ExpectRuntimeRefreshBatching()
        {
            if (!RoyalGuardMaintenanceRules.ShouldApplyRuntimeRefreshNow(
                    pPersistRefresh: false,
                    pRuntimeRefresh: true,
                    pRuntimeRefreshesApplied: 2,
                    pRuntimeRefreshLimit: 4))
                throw new Exception("Expected royal guard runtime refresh below the batch limit.");

            if (RoyalGuardMaintenanceRules.ShouldApplyRuntimeRefreshNow(
                    pPersistRefresh: false,
                    pRuntimeRefresh: true,
                    pRuntimeRefreshesApplied: 4,
                    pRuntimeRefreshLimit: 4))
                throw new Exception("Expected royal guard runtime refresh to be deferred at the batch limit.");

            if (!RoyalGuardMaintenanceRules.ShouldApplyRuntimeRefreshNow(
                    pPersistRefresh: true,
                    pRuntimeRefresh: true,
                    pRuntimeRefreshesApplied: 4,
                    pRuntimeRefreshLimit: 4))
                throw new Exception("Expected royal guard identity persistence to bypass runtime batching.");

            if (RoyalGuardMaintenanceRules.ShouldApplyRuntimeRefreshNow(
                    pPersistRefresh: false,
                    pRuntimeRefresh: false,
                    pRuntimeRefreshesApplied: 0,
                    pRuntimeRefreshLimit: 4))
                throw new Exception("Expected stable guards to skip runtime refresh.");
        }

        private static void ExpectGraphicsRebuildBudget()
        {
            if (RoyalGuardMaintenanceRules.ShouldRebuildGuardGraphicsNow(
                    pGfxDirty: false, pRebuiltThisPass: 0, pBudget: 2))
                throw new Exception("Expected a clean guard to skip the graphics rebuild.");
            if (!RoyalGuardMaintenanceRules.ShouldRebuildGuardGraphicsNow(
                    pGfxDirty: true, pRebuiltThisPass: 1, pBudget: 2))
                throw new Exception("Expected a dirty guard below the per-pass budget to rebuild.");
            if (RoyalGuardMaintenanceRules.ShouldRebuildGuardGraphicsNow(
                    pGfxDirty: true, pRebuiltThisPass: 2, pBudget: 2))
                throw new Exception("Expected the graphics rebuild to defer once the per-pass budget is spent.");
        }

        private static void ExpectRecruitmentBatching()
        {
            if (RoyalGuardMaintenanceRules.ClampDesiredGuardCountForBatch(
                    pCurrentActiveCount: 0,
                    pDesiredCount: 20,
                    pRecruitmentLimit: 4) != 4)
                throw new Exception("Expected first royal guard formation to recruit only one batch.");

            if (RoyalGuardMaintenanceRules.ClampDesiredGuardCountForBatch(
                    pCurrentActiveCount: 15,
                    pDesiredCount: 20,
                    pRecruitmentLimit: 4) != 19)
                throw new Exception("Expected royal guard refill to add at most one batch.");

            if (RoyalGuardMaintenanceRules.ClampDesiredGuardCountForBatch(
                    pCurrentActiveCount: 20,
                    pDesiredCount: 20,
                    pRecruitmentLimit: 4) != 20)
                throw new Exception("Expected full royal guard to keep its desired count.");

            if (RoyalGuardMaintenanceRules.ClampDesiredGuardCountForBatch(
                    pCurrentActiveCount: 2,
                    pDesiredCount: 1,
                    pRecruitmentLimit: 4) != 1)
                throw new Exception("Expected trimming decisions to bypass recruitment batching.");
        }

        private static void ExpectDismissScanGate()
        {
            if (RoyalGuardMaintenanceRules.ShouldScanKingdomForDismiss(
                    pHasCollectedActiveList: true,
                    pActiveGuardCount: 0,
                    pHasGuardStateHint: false))
                throw new Exception("Collected empty guard lists should not trigger kingdom-wide dismiss scans.");

            if (RoyalGuardMaintenanceRules.ShouldScanKingdomForDismiss(
                    pHasCollectedActiveList: true,
                    pActiveGuardCount: 3,
                    pHasGuardStateHint: true))
                throw new Exception("Collected guard lists should dismiss the collected actors directly.");

            if (RoyalGuardMaintenanceRules.ShouldScanKingdomForDismiss(
                    pHasCollectedActiveList: false,
                    pActiveGuardCount: 0,
                    pHasGuardStateHint: false))
                throw new Exception("Kingdoms with no guard state hint should skip full dismiss scans.");

            if (!RoyalGuardMaintenanceRules.ShouldScanKingdomForDismiss(
                    pHasCollectedActiveList: false,
                    pActiveGuardCount: 0,
                    pHasGuardStateHint: true))
                throw new Exception("Kingdoms with stale guard state hints may need one full dismiss scan.");
        }

        private static void ExpectDissolutionRules()
        {
            if (!RoyalGuardMaintenanceRules.ShouldPreserveGuards(
                    pSuccessionPending: true, pIsRepublic: false,
                    pIsRebel: false, pKingdomExtinct: false))
                throw new Exception("Royal guards must survive timer_new_king.");
            if (RoyalGuardMaintenanceRules.ShouldPreserveGuards(
                    pSuccessionPending: false, pIsRepublic: true,
                    pIsRebel: false, pKingdomExtinct: false))
                throw new Exception("Republics must dissolve royal guards.");
            if (!RoyalGuardMaintenanceRules.ShouldDissolveGuards(
                    pIsRepublic: true, pIsRebel: false, pKingdomExtinct: false))
                throw new Exception("Republic government should dissolve a royal guard.");
            if (RoyalGuardMaintenanceRules.DismissCountForPass(
                    pRemainingCount: 20, pBudget: 2) != 2)
                throw new Exception("Guard dissolution must obey its per-pass budget.");
            if (RoyalGuardMaintenanceRules.TrimExcessCountForPass(
                    pActiveCount: 25, pDesiredCount: 20,
                    pHardMaximum: 20, pRemainingBudget: 2) != 2)
                throw new Exception("Over-limit guard trimming must share the per-pass dismissal budget.");
            if (RoyalGuardMaintenanceRules.TrimExcessCountForPass(
                    pActiveCount: 25, pDesiredCount: 20,
                    pHardMaximum: 20, pRemainingBudget: 0) != 0)
                throw new Exception("Over-limit trimming must stop after the per-pass budget is spent.");
            if (RoyalGuardMaintenanceRules.ShouldClearDismissState(pDismissComplete: false))
                throw new Exception("Guard hints must remain until all guards are dismissed.");
            if (!RoyalGuardMaintenanceRules.ShouldClearDismissState(pDismissComplete: true))
                throw new Exception("Completed guard dissolution should clear its state hints.");
        }

        private static void ExpectGuardArmyCleanupGate()
        {
            if (RoyalGuardMaintenanceRules.ShouldInspectNormalArmyForGuards(
                    pIsGuardArmy: false, pHasGuardStateHint: false))
                throw new Exception("Armies in kingdoms without guard state must skip member copies.");
            if (!RoyalGuardMaintenanceRules.ShouldInspectNormalArmyForGuards(
                    pIsGuardArmy: false, pHasGuardStateHint: true))
                throw new Exception("A guard-state hint must retain compatibility cleanup.");
            if (RoyalGuardMaintenanceRules.ShouldInspectNormalArmyForGuards(
                    pIsGuardArmy: true, pHasGuardStateHint: true))
                throw new Exception("The guard army itself must never be stripped.");
        }

        private static void ExpectGuardSpatialSearchRadius()
        {
            if (ActorAiSearchThrottleRules.ChunkRadiusForTileRadius(10, 16) != 1 ||
                ActorAiSearchThrottleRules.ChunkRadiusForTileRadius(0, 16) != 0)
                throw new Exception("Guard spatial search must cover the minimum chunk square.");
        }
    }
}
