namespace AncientWarfare3.core.lineage
{
    public static class ArmyCaptainContinuityRules
    {
        public static bool ShouldOwnMaintenance(ArmyRtsMode pMode,
            bool replicaApplying, bool armyLive)
        {
            return pMode == ArmyRtsMode.On && !replicaApplying && armyLive;
        }

        public static bool IsCurrentCaptainStable(bool captainExists,
            bool captainAlive, bool captainIsMember,
            bool captainIsCivilAuthority = false)
        {
            return captainExists && captainAlive && captainIsMember;
        }

        public static bool ShouldPreserveAssignedCaptain(bool captainExists,
            bool captainAlive, bool captainIsMember)
        {
            _ = captainIsMember;
            return captainExists && captainAlive;
        }

        public static bool ShouldRetainCaptain(bool structurallyStable,
            bool leaseEligible)
        {
            return structurallyStable && leaseEligible;
        }

        public static bool ShouldRejectCaptainMutation(ArmyRtsMode pMode,
            bool replicaApplying, bool armyLive,
            bool currentCaptainExists, bool currentCaptainAlive,
            bool currentCaptainIsMember, bool requestedSameCaptain,
            bool currentCaptainIsCivilAuthority = false,
            bool royalGuardRoleOwnsCaptain = false)
        {
            return ShouldOwnMaintenance(pMode, replicaApplying, armyLive) &&
                   ShouldPreserveAssignedCaptain(currentCaptainExists,
                       currentCaptainAlive, currentCaptainIsMember) &&
                   !requestedSameCaptain &&
                   !royalGuardRoleOwnsCaptain;
        }

        public static bool ShouldRejectCaptainDetachment(ArmyRtsMode pMode,
            bool replicaApplying, bool armyLive,
            bool actorIsCurrentCaptain, bool actorAlive,
            bool leavingCurrentArmy,
            bool actorIsCivilAuthority = false)
        {
            return leavingCurrentArmy && actorIsCurrentCaptain &&
                   actorAlive &&
                   ShouldOwnMaintenance(pMode,
                       replicaApplying, armyLive);
        }

        public static bool CanCreateNewArmyWithRequestedCaptain(
            bool actorExists, bool actorAlive, bool currentArmyLive,
            bool actorIsCurrentCaptain)
        {
            return actorExists && actorAlive &&
                   (!currentArmyLive || !actorIsCurrentCaptain);
        }

        public static bool ShouldRepairCaptainMembership(
            bool captainExists, bool captainAlive,
            bool captainIsMember, bool armyDisposalActive)
        {
            return captainExists && captainAlive && !captainIsMember &&
                   !armyDisposalActive;
        }

        public static bool ShouldReleaseForeignCaptainLease(
            bool captainExists, bool captainAlive,
            bool captainMatchesArmyKingdom,
            bool armyDisposalActive)
        {
            return captainExists && captainAlive &&
                   !captainMatchesArmyKingdom &&
                   !armyDisposalActive;
        }

        public static bool CanTransferCaptainLease(ArmyRtsMode pMode,
            bool replicaApplying, bool currentArmyLive,
            bool actorIsCurrentCaptain, bool actorAlive,
            bool requestedArmyIsCurrentArmy,
            bool actorIsCivilAuthority = false)
        {
            if (requestedArmyIsCurrentArmy) return true;
            return !ShouldRejectCaptainDetachment(
                pMode, replicaApplying, currentArmyLive,
                actorIsCurrentCaptain, actorAlive,
                leavingCurrentArmy: true,
                actorIsCivilAuthority: actorIsCivilAuthority);
        }

        public static bool ShouldRejectCaptainRetirement(ArmyRtsMode pMode,
            bool replicaApplying, bool armyLive,
            bool actorIsCurrentCaptain, bool actorAlive,
            bool becomingAuthority)
        {
            if (becomingAuthority) return false;
            return ShouldRejectCaptainDetachment(
                pMode, replicaApplying, armyLive,
                actorIsCurrentCaptain, actorAlive,
                leavingCurrentArmy: true,
                actorIsCivilAuthority: becomingAuthority);
        }

        public static bool IsCareerStandingCaptainCandidate(
            bool actorAlive, bool currentProfessionIsWarrior,
            bool hasArmyIndex, bool temporaryLevy,
            bool wartimeGarrison, bool temporarySlaveVanguard,
            bool enslaved)
        {
            return actorAlive && currentProfessionIsWarrior && hasArmyIndex &&
                   !temporaryLevy && !wartimeGarrison &&
                   !temporarySlaveVanguard && !enslaved;
        }

        public static bool ShouldRetainCareerCaptain(bool actorAlive,
            bool actorIsCurrentCaptain, bool armyDisposalActive)
        {
            return actorAlive && actorIsCurrentCaptain &&
                   !armyDisposalActive;
        }

        public static bool ShouldPreferReplacement(long currentBestActorId,
            long candidateActorId)
        {
            return candidateActorId >= 0L &&
                   (currentBestActorId < 0L ||
                    candidateActorId < currentBestActorId);
        }

        public static bool ShouldPreferLevyPromotion(
            float currentBestScore, long currentBestActorId,
            float candidateScore, long candidateActorId)
        {
            if (candidateActorId < 0L) return false;
            if (currentBestActorId < 0L) return true;
            int scoreOrder = candidateScore.CompareTo(currentBestScore);
            return scoreOrder > 0 ||
                   scoreOrder == 0 && candidateActorId < currentBestActorId;
        }
    }
}
