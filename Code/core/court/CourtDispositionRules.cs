using System;

namespace AncientWarfare3.core.court
{
    public static class CourtDispositionRules
    {
        public static int Cost(CourtDispositionAction pAction,
            int pNobleRank = 0)
        {
            switch (pAction)
            {
                case CourtDispositionAction.PromoteRank:
                case CourtDispositionAction.DemoteRank:
                    return 8;
                case CourtDispositionAction.DismissOffice:
                    return 10;
                case CourtDispositionAction.GrantNobleRank:
                    return 10 + Math.Max(1, Math.Min(8, pNobleRank)) * 3;
                case CourtDispositionAction.GrantFief:
                case CourtDispositionAction.RelocateFeudatory:
                    return 20;
                case CourtDispositionAction.RevokeFief:
                    return 15;
                case CourtDispositionAction.GrantSurname:
                    return 30;
                case CourtDispositionAction.ExpelLineage:
                    return 40;
                case CourtDispositionAction.ReclaimFeudatoryCity:
                    return 35;
                default:
                    return 0;
            }
        }

        public static bool CanAfford(float pPoliticalPoints,
            CourtDispositionAction pAction, int pNobleRank = 0)
        {
            return pPoliticalPoints + 0.001f >= Cost(pAction, pNobleRank);
        }

        public static bool IsReward(CourtDispositionAction pAction)
        {
            return pAction == CourtDispositionAction.PromoteRank ||
                   pAction == CourtDispositionAction.GrantNobleRank ||
                   pAction == CourtDispositionAction.GrantFief ||
                   pAction == CourtDispositionAction.GrantSurname;
        }

        public static bool IsPunishment(CourtDispositionAction pAction)
        {
            return !IsReward(pAction);
        }

        public static CourtDispositionOutcome ResolveOutcome(bool pEligible,
            bool pOperationCommitted, bool pRebellionStarted,
            bool pPersistenceKnown)
        {
            if (!pEligible) return CourtDispositionOutcome.Rejected;
            if (!pPersistenceKnown) return CourtDispositionOutcome.Unknown;
            if (pRebellionStarted) return CourtDispositionOutcome.Rebelled;
            return pOperationCommitted
                ? CourtDispositionOutcome.Committed
                : CourtDispositionOutcome.CleanFailure;
        }

        public static bool ShouldSpend(CourtDispositionOutcome pOutcome)
        {
            return pOutcome == CourtDispositionOutcome.Committed ||
                   pOutcome == CourtDispositionOutcome.Rebelled;
        }

        public static CourtDispositionResistanceRoute ResistanceRoute(
            bool pIsFeudatoryPrince, bool pIsLandedGeneral,
            bool pIsChiefMinister)
        {
            if (pIsFeudatoryPrince)
                return CourtDispositionResistanceRoute.FeudatoryJingnan;
            if (pIsChiefMinister)
                return CourtDispositionResistanceRoute.MinisterialCoup;
            if (pIsLandedGeneral)
                return CourtDispositionResistanceRoute.GeneralRebellion;
            return CourtDispositionResistanceRoute.None;
        }

        public static bool IsChiefMinisterCandidate(bool pPremierIdMatches,
            bool pCurrentOfficer)
        {
            return pPremierIdMatches && pCurrentOfficer;
        }

        public static bool RequiresIntParameter(
            CourtDispositionAction pAction)
        {
            return pAction == CourtDispositionAction.GrantNobleRank;
        }

        public static bool RequiresCityParameter(
            CourtDispositionAction pAction)
        {
            return pAction == CourtDispositionAction.GrantFief ||
                   pAction == CourtDispositionAction.ReclaimFeudatoryCity;
        }

        public static bool ShouldRefreshCourt(
            CourtDispositionAction pAction)
        {
            return pAction == CourtDispositionAction.PromoteRank ||
                   pAction == CourtDispositionAction.DemoteRank ||
                   pAction == CourtDispositionAction.DismissOffice ||
                   pAction == CourtDispositionAction.GrantNobleRank ||
                   pAction == CourtDispositionAction.GrantSurname ||
                   pAction == CourtDispositionAction.ExpelLineage;
        }
    }
}
