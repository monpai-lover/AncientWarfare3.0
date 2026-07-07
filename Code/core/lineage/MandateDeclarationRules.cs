namespace AncientWarfare3.core.lineage
{
    public static class MandateDeclarationRules
    {
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
    }
}
