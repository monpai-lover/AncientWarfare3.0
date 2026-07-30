namespace AncientWarfare3.core.lineage
{
    public static class MandateDeclarationRules
    {
        public static bool RequiresMandateRitesForOrigin(string pDeclarationReason,
            string pOriginType, string pClaimantKind)
        {
            return MandateRitesRules.RequiresOrdinaryGate(
                MandateRitesRules.ResolveSource(pDeclarationReason, pOriginType,
                    pClaimantKind));
        }

        public static bool CanCreateNewPeriod(bool pMandateActive, long pCurrentKingdomId,
            long pCandidateKingdomId)
        {
            return !pMandateActive || pCurrentKingdomId != pCandidateKingdomId;
        }

        public static bool ShouldEndDestroyedMandate(bool mandateActive,
            long mandateKingdomId, long destroyedKingdomId)
        {
            return mandateActive && mandateKingdomId >= 0 &&
                   mandateKingdomId == destroyedKingdomId;
        }

        public static long ResolveHostileMandateCityConqueror(bool mandateActive,
            long mandateKingdomId, long losingKingdomId, long gainingKingdomId,
            bool gainingKingdomValid, bool hostileTransfer)
        {
            if (!mandateActive || mandateKingdomId < 0 ||
                mandateKingdomId != losingKingdomId ||
                !gainingKingdomValid || gainingKingdomId < 0 ||
                gainingKingdomId == losingKingdomId || !hostileTransfer)
                return -1L;
            return gainingKingdomId;
        }

        public static long ResolveHostileMandateFinalCityConqueror(
            bool mandateActive, long mandateKingdomId,
            long losingKingdomId, long gainingKingdomId,
            bool gainingKingdomValid, bool hostileTransfer,
            int losingCityCountBeforeTransfer)
        {
            if (losingCityCountBeforeTransfer != 1) return -1L;
            return ResolveHostileMandateCityConqueror(mandateActive,
                mandateKingdomId, losingKingdomId, gainingKingdomId,
                gainingKingdomValid, hostileTransfer);
        }

        public static bool CanTransferDestroyedMandate(bool mandateActive,
            long mandateKingdomId, long destroyedKingdomId, long candidateKingdomId,
            bool candidateValid, bool candidateHasKing)
        {
            return ShouldEndDestroyedMandate(
                       mandateActive, mandateKingdomId, destroyedKingdomId) &&
                   candidateKingdomId >= 0 &&
                   candidateKingdomId != destroyedKingdomId &&
                   candidateValid && candidateHasKing;
        }

        public static bool HasEnoughRealmToDeclare(int pCityCount, int pTitle,
            bool pHistoricalFigureKing, int pMinimumCities, int pKingTitleValue)
        {
            if (pHistoricalFigureKing) return pCityCount >= pMinimumCities;
            return pTitle >= pKingTitleValue || pCityCount >= pMinimumCities;
        }

        public static bool NeedsLegalCoreControl(int pPreviousCoreCount, bool pPreviousMandateActive)
        {
            return !pPreviousMandateActive && pPreviousCoreCount > 0;
        }

        public static bool HasEnoughLegalCoreControl(float pControlRatio, float pThreshold)
        {
            return pControlRatio + 0.0001f >= pThreshold;
        }

        public static bool CanDeclareForeignPseudo(bool pIsXiaKingdom, bool pWonMandateWar,
            bool pHasEnoughLegalCoreControl, bool pMandateAlreadyExists, out string pReason)
        {
            if (pIsXiaKingdom)
            {
                pReason = "xia_not_foreign";
                return false;
            }
            if (pMandateAlreadyExists)
            {
                pReason = "already_exists";
                return false;
            }
            if (!pWonMandateWar)
            {
                pReason = "requires_mandate_war";
                return false;
            }
            if (!pHasEnoughLegalCoreControl)
            {
                pReason = "core_control";
                return false;
            }

            pReason = "";
            return true;
        }

        public static bool CanPlayerGrant(bool pValidTarget, bool pHasKing,
            bool pSameHolder, out string pReason)
        {
            if (!pValidTarget)
            {
                pReason = "invalid";
                return false;
            }
            if (!pHasKing)
            {
                pReason = "no_king";
                return false;
            }
            if (pSameHolder)
            {
                pReason = "already_holder";
                return false;
            }
            pReason = "";
            return true;
        }
    }
}
