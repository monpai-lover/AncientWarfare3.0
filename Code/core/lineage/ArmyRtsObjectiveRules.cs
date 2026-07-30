namespace AncientWarfare3.core.lineage
{
    public enum ArmyRtsObjectiveState
    {
        Unavailable = 0,
        OpenAttack = 1,
        OpenDefense = 2,
        ClosedOccupied = 3
    }

    public enum ArmyRtsProposalKind
    {
        None = 0,
        Attack = 1,
        Defend = 2,
        FrontHold = 3,
        Retreat = 4
    }

    public sealed class ArmyRtsObjectiveFacts
    {
        public ArmyRtsObjectiveFacts(bool cityLive, bool warActive,
            bool ownerAtWar, bool controlledByParticipantSide,
            bool hostileMilitaryInside, bool hostileCaptureProgress,
            bool hostilePlanningIntent = false,
            bool externallyControlled = false,
            bool occupationLockedAgainstKingdom = false)
        {
            CityLive = cityLive;
            WarActive = warActive;
            OwnerAtWar = ownerAtWar;
            ControlledByParticipantSide = controlledByParticipantSide;
            HostileMilitaryInside = hostileMilitaryInside;
            HostileCaptureProgress = hostileCaptureProgress;
            HostilePlanningIntent = hostilePlanningIntent;
            ExternallyControlled = externallyControlled;
            OccupationLockedAgainstKingdom = occupationLockedAgainstKingdom;
        }

        public bool CityLive { get; }
        public bool WarActive { get; }
        public bool OwnerAtWar { get; }
        public bool ControlledByParticipantSide { get; }
        public bool HostileMilitaryInside { get; }
        public bool HostileCaptureProgress { get; }
        public bool HostilePlanningIntent { get; }
        public bool ExternallyControlled { get; }
        public bool OccupationLockedAgainstKingdom { get; }
    }

    public static class ArmyRtsObjectiveRules
    {
        public static ArmyRtsObjectiveState Classify(
            ArmyRtsObjectiveFacts pFacts)
        {
            if (pFacts == null || !pFacts.CityLive || !pFacts.WarActive ||
                !pFacts.OwnerAtWar || pFacts.ExternallyControlled ||
                pFacts.OccupationLockedAgainstKingdom)
                return ArmyRtsObjectiveState.Unavailable;

            if (pFacts.ControlledByParticipantSide)
                return pFacts.HostileMilitaryInside ||
                       pFacts.HostileCaptureProgress
                    ? ArmyRtsObjectiveState.OpenDefense
                    : ArmyRtsObjectiveState.ClosedOccupied;

            return ArmyRtsObjectiveState.OpenAttack;
        }

        public static bool CanCommit(ArmyRtsProposalKind pKind,
            ArmyRtsObjectiveState pState, long armyKingdomId,
            long proposalKingdomId, int openObjectiveCount)
        {
            if (armyKingdomId < 0L || armyKingdomId != proposalKingdomId)
                return false;

            switch (pKind)
            {
                case ArmyRtsProposalKind.Attack:
                    return pState == ArmyRtsObjectiveState.OpenAttack;
                case ArmyRtsProposalKind.Defend:
                    return pState == ArmyRtsObjectiveState.OpenDefense;
                case ArmyRtsProposalKind.FrontHold:
                    return openObjectiveCount <= 0;
                default:
                    return false;
            }
        }

        public static ArmyRtsProposalKind ResolveHomelandRecaptureProposal(
            ArmyRtsObjectiveState pState)
        {
            if (pState == ArmyRtsObjectiveState.OpenDefense)
                return ArmyRtsProposalKind.Defend;
            return pState == ArmyRtsObjectiveState.OpenAttack
                ? ArmyRtsProposalKind.Attack
                : ArmyRtsProposalKind.None;
        }

        public static bool ShouldUseObjectiveCompletion(
            ArmyRtsProposalKind pKind, ArmyRtsRole pRole)
        {
            return pKind != ArmyRtsProposalKind.Retreat &&
                   pKind != ArmyRtsProposalKind.FrontHold;
        }

        public static bool ShouldRetainForwardFrontHoldTarget(
            bool pNoOpenAttackObjectives, bool pTargetLive,
            bool pTargetSecuredByThisWar)
        {
            return pNoOpenAttackObjectives && pTargetLive &&
                   pTargetSecuredByThisWar;
        }

        public static bool IsRetreatAnchorValid(bool cityLive,
            bool ownedByArmyKingdom, bool hostileCaptureActive,
            bool enemyFrozenControlled)
        {
            return cityLive && ownedByArmyKingdom &&
                   !hostileCaptureActive && !enemyFrozenControlled;
        }

        public static bool CanReplaceFrozenOccupation(
            bool existingFrozenControl, long existingControllerKingdomId,
            long incomingControllerKingdomId, long legalOwnerKingdomId)
        {
            if (incomingControllerKingdomId < 0L) return false;
            return !existingFrozenControl ||
                   existingControllerKingdomId == incomingControllerKingdomId ||
                   incomingControllerKingdomId == legalOwnerKingdomId;
        }
    }
}
