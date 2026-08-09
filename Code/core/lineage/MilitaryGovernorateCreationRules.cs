using System;

namespace AncientWarfare3.core.lineage
{
    public enum MilitaryGovernorateCreationStage
    {
        None = 0,
        KingdomCreated = 1,
        CityTransferred = 2,
        CapitalAssigned = 3,
        RelationCreated = 4,
        StateCreated = 5,
        Completed = 6
    }

    [Flags]
    public enum MilitaryGovernorateRollbackAction
    {
        None = 0,
        RemoveKingdom = 1,
        RestoreCity = 2,
        EndRelation = 4,
        EndState = 8
    }

    public static class MilitaryGovernorateCreationRules
    {
        private const int FrontierBonus = 2000000;

        public static int SeatScore(bool pExternalFrontier,
            int pPopulation, int pZones)
        {
            int population = Math.Max(0, Math.Min(100000, pPopulation));
            int zones = Math.Max(0, Math.Min(10000, pZones));
            return (pExternalFrontier ? FrontierBonus : 0) +
                   population * 10 + zones * 10;
        }

        public static int GeneralScore(int pMerit, int pLoyalty,
            int pAmbition, int pLocalServiceYears)
        {
            int merit = Math.Max(0, Math.Min(999, pMerit));
            int loyalty = Math.Max(0, Math.Min(100, pLoyalty));
            int ambition = Math.Max(0, Math.Min(100, pAmbition));
            int localService = Math.Max(0,
                Math.Min(100, pLocalServiceYears));
            return merit * 4 + loyalty * 2 - ambition * 2 +
                   localService;
        }

        public static int CompareCandidate(int pLeftScore, long pLeftId,
            int pRightScore, long pRightId)
        {
            int score = pRightScore.CompareTo(pLeftScore);
            return score != 0 ? score : pLeftId.CompareTo(pRightId);
        }

        public static MilitaryGovernorateRollbackAction RollbackFor(
            MilitaryGovernorateCreationStage pStage)
        {
            if (pStage == MilitaryGovernorateCreationStage.Completed)
                return MilitaryGovernorateRollbackAction.None;
            if (pStage < MilitaryGovernorateCreationStage.KingdomCreated)
                return MilitaryGovernorateRollbackAction.None;
            MilitaryGovernorateRollbackAction actions =
                MilitaryGovernorateRollbackAction.RemoveKingdom;
            if (pStage >= MilitaryGovernorateCreationStage.CityTransferred)
                actions |= MilitaryGovernorateRollbackAction.RestoreCity;
            if (pStage >= MilitaryGovernorateCreationStage.RelationCreated)
                actions |= MilitaryGovernorateRollbackAction.EndRelation;
            if (pStage >= MilitaryGovernorateCreationStage.StateCreated)
                actions |= MilitaryGovernorateRollbackAction.EndState;
            return actions;
        }
    }
}
