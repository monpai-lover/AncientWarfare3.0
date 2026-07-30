using System;

namespace AncientWarfare3.core.lineage
{
    public static class ArmyMarchRules
    {
        public const string VanillaAttackMarchTaskId =
            "warrior_army_leader_move_to_attack_target";
        public const string DeploymentMarchTaskId = "aw_war_deployment";
        public const string RtsMissionTaskId = "aw_army_rts_mission";
        public const int MaxRouteSteps = 64;
        public const int MaxTrackedFollowers = 128;
        public const int MaxConcurrentFollowerCorrectionsPerArmy = 8;
        public const int MaxFollowerCorrectionsPerTick = 32;
        public const double FollowerCorrectionCooldownSeconds = 0.35d;
        public const double FollowerCorrectionTimeoutSeconds = 2.5d;
        public const float FollowerCorrectionDistanceSquared = 36f;

        public static bool ShouldRecordLeaderStep(bool pHasArmy,
            bool pIsLeader, bool pHasValidTarget)
        {
            return pHasArmy && pIsLeader && pHasValidTarget;
        }

        public static bool ShouldUseArmyMarch(bool hasArmy, bool isLeader,
            bool hasValidTarget)
        {
            return hasArmy && isLeader && hasValidTarget;
        }

        public static bool ShouldUseFollowerMarch(bool hasArmy,
            bool hasMarchPlan, bool hasValidTarget,
            bool sameIslandAsCaptain)
        {
            return hasArmy && hasMarchPlan && hasValidTarget &&
                   sameIslandAsCaptain;
        }

        public static bool ShouldOwnFollowerMarch(bool hasArmy,
            bool hasMarchPlan, bool hasRouteSteps,
            bool sameIslandAsCaptain)
        {
            return hasArmy && hasMarchPlan && hasRouteSteps &&
                   sameIslandAsCaptain;
        }

        public static bool ShouldRunVanillaFollowerSearch(
            bool pMarchOwnedByAw3)
        {
            return !pMarchOwnedByAw3;
        }

        public static bool ShouldUseIdleFollowerTarget(
            bool pMarchOwnedByAw3, bool pHasCorrectionTarget,
            bool pHasCurrentTile)
        {
            return pMarchOwnedByAw3 && !pHasCorrectionTarget &&
                   pHasCurrentTile;
        }

        public static bool ShouldInvalidateRoute(long routeTargetId, long currentTargetId,
            long routeGeneration, long currentGeneration)
        {
            return routeTargetId != currentTargetId || routeGeneration != currentGeneration;
        }

        public static bool ShouldClearRouteBeforeReplacement(bool requestReused)
        {
            return !requestReused;
        }

        public static bool ShouldRetainCompletedLeaderTrail(
            bool usesProvider, bool actorIsCaptain,
            bool deploymentActive, bool hasLivingFollowers)
        {
            return !usesProvider && actorIsCaptain && deploymentActive &&
                   hasLivingFollowers;
        }

        public static bool ShouldBootstrapVanillaLeaderTrail(
            bool hasState, bool hasValidTarget)
        {
            return !hasState && hasValidTarget;
        }

        public static bool ShouldReuseVanillaLeaderTrail(
            bool hasMatchingState, bool trailCompleted,
            string retainedAssignmentKey, string activeAssignmentKey)
        {
            if (!hasMatchingState) return false;
            if (!trailCompleted) return true;
            return !string.IsNullOrEmpty(activeAssignmentKey) &&
                   string.Equals(retainedAssignmentKey,
                       activeAssignmentKey, StringComparison.Ordinal);
        }

        public static bool ShouldPreserveProviderRouteForVanillaStep(
            bool usesProvider, bool targetMatches, bool activeRtsMission)
        {
            return usesProvider && (targetMatches || activeRtsMission);
        }

        public static bool ShouldClearRetainedDeploymentTrail(
            bool usesProvider, bool trailCompleted,
            string retainedAssignmentKey, string closingAssignmentKey)
        {
            return !usesProvider && trailCompleted &&
                   !string.IsNullOrEmpty(retainedAssignmentKey) &&
                   string.Equals(retainedAssignmentKey,
                       closingAssignmentKey, StringComparison.Ordinal);
        }

        public static bool ShouldReleaseCompletedLeaderTrail(
            bool usesProvider, bool trailCompleted,
            bool hasLivingFollowers)
        {
            return !usesProvider && trailCompleted &&
                   !hasLivingFollowers;
        }

        public static bool IsSupportedLongMarchTask(string pTaskId)
        {
            return string.Equals(pTaskId, VanillaAttackMarchTaskId,
                       StringComparison.Ordinal) ||
                   string.Equals(pTaskId, DeploymentMarchTaskId,
                       StringComparison.Ordinal) ||
                   string.Equals(pTaskId, RtsMissionTaskId,
                       StringComparison.Ordinal);
        }

        public static bool ShouldInspectMarchLeader(bool hasActor, bool hasArmy,
            string taskId)
        {
            return hasActor && hasArmy && IsSupportedLongMarchTask(taskId);
        }

        public static bool CanIssueFollowerCorrection(int pPendingCount,
            bool pActorAlreadyPending)
        {
            return !pActorAlreadyPending && pPendingCount >= 0 &&
                   pPendingCount < MaxConcurrentFollowerCorrectionsPerArmy;
        }

        public static bool IsFollowerCorrectionExpired(double pStartedAt,
            double pNow)
        {
            return pStartedAt >= 0d &&
                   pNow - pStartedAt >= FollowerCorrectionTimeoutSeconds;
        }

        public static bool ShouldCorrectFollower(float pDistanceSquared,
            double pNow, double pNextAllowedTime)
        {
            return pDistanceSquared >= FollowerCorrectionDistanceSquared &&
                   pNow >= pNextAllowedTime;
        }

        public static int LookAheadIndex(int pRouteCount, int pCurrentIndex,
            int pLookAhead)
        {
            if (pRouteCount <= 0) return -1;
            int index = Math.Max(0, pCurrentIndex) + Math.Max(0, pLookAhead);
            return Math.Min(pRouteCount - 1, index);
        }

        public static void SlotOffset(int pOrder, out int pX, out int pY)
        {
            int order = Math.Max(0, pOrder);
            if (order == 0)
            {
                pX = 0;
                pY = 0;
                return;
            }

            int rank = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(order + 1d)) - 1);
            int indexInRank = order - rank * rank;
            pX = -rank + indexInRank;
            pY = rank;
        }

        public static void RotateSlot(int pLateral, int pBehind, int pDirectionX,
            int pDirectionY, out int pX, out int pY)
        {
            int directionX = Math.Sign(pDirectionX);
            int directionY = Math.Sign(pDirectionY);
            if (directionX == 0 && directionY == 0) directionY = 1;
            if (directionX != 0 && directionY != 0)
            {
                if (directionX == directionY)
                    directionX = 0;
                else
                    directionY = 0;
            }
            int x = directionY * pLateral - directionX * pBehind;
            int y = -directionX * pLateral - directionY * pBehind;
            ArmyFormationRules.ClampVectorToRadius(x, y,
                ArmyFormationRules.LocalRadius, out pX, out pY);
        }
    }
}
