namespace AncientWarfare3.core.court
{
    public enum CourtDispositionAction
    {
        PromoteRank,
        DemoteRank,
        DismissOffice,
        GrantNobleRank,
        GrantFief,
        RevokeFief,
        GrantSurname,
        ExpelLineage,
        RelocateFeudatory,
        ReclaimFeudatoryCity
    }

    public enum CourtDispositionOutcome
    {
        Rejected,
        Committed,
        Rebelled,
        CleanFailure,
        Unknown
    }

    public enum CourtDispositionResistanceRoute
    {
        None,
        FeudatoryJingnan,
        GeneralRebellion,
        MinisterialCoup
    }

    public enum CourtDispositionResistanceResult
    {
        Accepted,
        Rebelled,
        FailedToStart
    }

    public sealed class CourtDispositionResistanceResolution
    {
        public CourtDispositionResistanceResolution(
            CourtDispositionResistanceRoute pRoute,
            CourtDispositionResistanceResult pResult,
            bool pDomainCommitted = false)
        {
            Route = pRoute;
            Result = pResult;
            DomainCommitted = pDomainCommitted;
        }

        public CourtDispositionResistanceRoute Route { get; }
        public CourtDispositionResistanceResult Result { get; }
        public bool DomainCommitted { get; }
    }

    public sealed class CourtDispositionLedgerEntry
    {
        public CourtDispositionLedgerEntry(long pActionId,
            CourtDispositionOutcome? pOutcome, string pReason, int pCost)
        {
            ActionId = pActionId;
            Outcome = pOutcome;
            Reason = pReason ?? "";
            Cost = pCost;
        }

        public long ActionId { get; }
        public CourtDispositionOutcome? Outcome { get; }
        public string Reason { get; }
        public int Cost { get; }
    }

    public sealed class CourtDispositionCommand
    {
        public CourtDispositionCommand(long pKingdomId, long pRulerActorId,
            long pTargetActorId, CourtDispositionAction pAction,
            int pIntParameter = 0, long pLongParameter = -1L,
            string pOperationKey = "")
        {
            KingdomId = pKingdomId;
            RulerActorId = pRulerActorId;
            TargetActorId = pTargetActorId;
            Action = pAction;
            IntParameter = pIntParameter;
            LongParameter = pLongParameter;
            OperationKey = pOperationKey ?? "";
        }

        public long KingdomId { get; }
        public long RulerActorId { get; }
        public long TargetActorId { get; }
        public CourtDispositionAction Action { get; }
        public int IntParameter { get; }
        public long LongParameter { get; }
        public string OperationKey { get; }
    }

    public sealed class CourtDispositionPreview
    {
        public CourtDispositionPreview(bool pAllowed, string pReason,
            int pCost)
        {
            Allowed = pAllowed;
            Reason = pReason ?? "";
            Cost = pCost;
        }

        public bool Allowed { get; }
        public string Reason { get; }
        public int Cost { get; }
    }

    public sealed class CourtDispositionResult
    {
        public CourtDispositionResult(CourtDispositionOutcome pOutcome,
            string pReason, long pActionId, int pCost,
            bool pShouldRefreshCourt)
        {
            Outcome = pOutcome;
            Reason = pReason ?? "";
            ActionId = pActionId;
            Cost = pCost;
            ShouldRefreshCourt = pShouldRefreshCourt;
        }

        public CourtDispositionOutcome Outcome { get; }
        public string Reason { get; }
        public long ActionId { get; }
        public int Cost { get; }
        public bool ShouldRefreshCourt { get; }
    }
}
