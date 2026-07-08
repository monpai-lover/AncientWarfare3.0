namespace AncientWarfare3.core.lineage
{
    public enum PeaceSettlementAction
    {
        None,
        TransferCity,
        ForceVassal,
        ReleaseVassal,
        RestoreKingdom,
        ApplyNoCbOutcome,
        WhitePeace,
        DefenderVictory
    }

    public static class PeaceSettlementRules
    {
        public static PeaceSettlementAction ResolveAction(string pGoalType, string pWinnerKey)
        {
            switch (pWinnerKey ?? "")
            {
                case "peace":
                    return PeaceSettlementAction.WhitePeace;
                case "defenders":
                    return PeaceSettlementAction.DefenderVictory;
                case "attackers":
                    return ResolveAttackerVictory(pGoalType);
                default:
                    return PeaceSettlementAction.None;
            }
        }

        private static PeaceSettlementAction ResolveAttackerVictory(string pGoalType)
        {
            switch (pGoalType ?? "")
            {
                case WarTerritoryService.GOAL_TAKE_CORE_CITY:
                case WarTerritoryService.GOAL_PRESS_CLAIM_CITY:
                case WarTerritoryService.GOAL_MANDATE_CONQUEST:
                    return PeaceSettlementAction.TransferCity;
                case WarTerritoryService.GOAL_FORCE_VASSAL:
                    return PeaceSettlementAction.ForceVassal;
                case WarTerritoryService.GOAL_INDEPENDENCE:
                    return PeaceSettlementAction.ReleaseVassal;
                case WarTerritoryService.GOAL_RESTORE_KINGDOM:
                    return PeaceSettlementAction.RestoreKingdom;
                case WarTerritoryService.GOAL_TAKE_MANDATE:
                case WarTerritoryService.GOAL_NO_CB:
                    return PeaceSettlementAction.ApplyNoCbOutcome;
                default:
                    return PeaceSettlementAction.None;
            }
        }
    }
}
