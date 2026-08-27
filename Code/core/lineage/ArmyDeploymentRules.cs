namespace AncientWarfare3.core.lineage
{
    public static class ArmyDeploymentRules
    {
        public const float OrdinaryReadyRatio = 0.70f;
        public const int MaxCitiesDiscoveredPerWorkItem = 1;
        public const int MaxArmiesReviewedPerWorkItem = 8;

        public static bool IsOrdinaryArmyReady(int pLivingWarriors, int pWarriorSlots)
        {
            if (pLivingWarriors <= 0 || pWarriorSlots <= 0) return false;
            return (float)pLivingWarriors / pWarriorSlots >= OrdinaryReadyRatio;
        }

        public static bool BlocksDeclarationGate(bool hasLivingWarriors, bool isRoyalGuard,
            bool ready, bool arrived)
        {
            if (!hasLivingWarriors || isRoyalGuard) return false;
            return !ready || !arrived;
        }

        public static bool ShouldCreateSideProjection(bool actorIsAttacker,
            bool actorIsDefender)
        {
            return actorIsAttacker != actorIsDefender;
        }

        public static bool BlocksDeclarationGateForSide(bool isDefenderSide,
            bool hasLivingWarriors, bool isRoyalGuard, bool ready,
            bool arrived)
        {
            return BlocksDeclarationGate(
                hasLivingWarriors, isRoyalGuard, ready, arrived);
        }

        public static bool AreBothSidesReady(bool attackerBypassed,
            bool attackerReady, bool defenderBypassed,
            bool defenderReady)
        {
            return (attackerBypassed || attackerReady) &&
                   (defenderBypassed || defenderReady);
        }

        public static bool CanUseDeclarationProjection(
            string requestedSignature, string primarySignature,
            bool projectionExists, bool projectionClosing)
        {
            return projectionExists && !projectionClosing &&
                   !string.IsNullOrEmpty(requestedSignature);
        }

        public static bool IsFacingFrontierTile(bool ownedBySide,
            bool ground, bool liquid, bool lava, bool blocked,
            bool touchesOpponent)
        {
            return ownedBySide && ground && !liquid && !lava && !blocked &&
                   touchesOpponent;
        }

        public static int StableFrontierIndex(long armyId,
            int frontierCount)
        {
            if (frontierCount <= 0) return -1;
            return (int)(unchecked((ulong)armyId) %
                         (ulong)frontierCount);
        }

        public static int StableNoticeIndex(long pArmyId,
            int pNoticeCount)
        {
            if (pNoticeCount <= 0) return -1;
            return (int)(unchecked((ulong)pArmyId) %
                         (ulong)pNoticeCount);
        }

        public static bool ShouldResetAssignment(string currentSignature, string nextSignature,
            long currentCityId, long nextCityId)
        {
            return currentSignature != nextSignature || currentCityId != nextCityId;
        }

        public static bool ShouldClearForClosingNotice(string currentSignature, string closingSignature)
        {
            return !string.IsNullOrEmpty(closingSignature) && currentSignature == closingSignature;
        }

        public static bool ShouldMarkArmyArrived(bool actorArrived, bool actorIsCaptain)
        {
            return actorArrived && actorIsCaptain;
        }

        public static bool ShouldUseFrontierAnchor(bool captainAtTarget,
            bool targetValid)
        {
            return captainAtTarget && targetValid;
        }

        public static bool ShouldUseFormationQuorum(ArmyRtsMode pMode)
        {
            return pMode == ArmyRtsMode.On;
        }

        public static bool ShouldObserveFormation(bool useFormationMovement,
            bool anchorValid)
        {
            return useFormationMovement && anchorValid;
        }

        public static bool ShouldUseFormationFollowerJob(
            ArmyRtsMode pMode, bool actorIsCaptain)
        {
            return !actorIsCaptain && ShouldUseFormationQuorum(pMode);
        }

        public static bool ShouldAssignDeploymentActor(ArmyRtsMode pMode,
            bool actorIsCaptain)
        {
            return actorIsCaptain || ShouldUseFormationQuorum(pMode);
        }

        public static bool CanClaimDeploymentActor(bool actorIsCaptain,
            bool actorIsWarrior, bool isRoyalGuard, bool isLiving)
        {
            return isLiving && !isRoyalGuard &&
                   (actorIsCaptain || actorIsWarrior);
        }

        public static bool ShouldReassertDeploymentControl(
            bool hasExpectedJob, bool hasExpectedTask)
        {
            return !hasExpectedJob || !hasExpectedTask;
        }

        public static bool CanBeginDeployment(bool canSendArmy,
            bool discoveryComplete)
        {
            return canSendArmy && discoveryComplete;
        }

        public static bool CanAssignPrewarDeployment(
            bool pHasLivingWarriors, bool pDiscoveryComplete)
        {
            return pHasLivingWarriors && pDiscoveryComplete;
        }

        public static bool ShouldRestoreLegacyJob(bool restoreRequested,
            bool ownedByLiveRts)
        {
            return restoreRequested && !ownedByLiveRts;
        }

        public static int CompareNoticePriority(
            int pLeftEarliestWarYear,
            int pLeftNoticeYear,
            string pLeftSignature,
            int pRightEarliestWarYear,
            int pRightNoticeYear,
            string pRightSignature)
        {
            int earliest = pLeftEarliestWarYear.CompareTo(pRightEarliestWarYear);
            if (earliest != 0) return earliest;
            int issued = pLeftNoticeYear.CompareTo(pRightNoticeYear);
            if (issued != 0) return issued;
            return string.CompareOrdinal(pLeftSignature ?? "", pRightSignature ?? "");
        }

        public static bool ShouldBypassPrewarDeployment(bool pDefenderAlreadyAtWar)
        {
            return pDefenderAlreadyAtWar;
        }
    }
}
