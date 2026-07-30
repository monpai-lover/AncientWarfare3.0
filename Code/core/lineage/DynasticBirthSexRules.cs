namespace AncientWarfare3.core.lineage
{
    public static class DynasticBirthSexRules
    {
        public const int MalePreferencePercent = 70;

        public static bool ShouldPreferMale(bool pUsesDynasticSystem,
            bool pIsKing, bool pIsCurrentHeir, bool pIsFeudatoryPrince,
            bool pHoldsPrinceTitle, bool pHasLivingSon)
        {
            return ShouldPreferMale(pUsesDynasticSystem, pIsKing,
                pIsCurrentHeir, pIsFeudatoryPrince,
                false, pHoldsPrinceTitle,
                pHasLivingSon);
        }

        public static bool ShouldPreferMale(bool pUsesDynasticSystem,
            bool pIsKing, bool pIsCurrentHeir, bool pIsFeudatoryPrince,
            bool pIsFeudatorySuccessor, bool pHoldsPrinceTitle,
            bool pHasLivingSon)
        {
            return pUsesDynasticSystem &&
                   (pIsKing || pIsCurrentHeir || pIsFeudatoryPrince ||
                    pIsFeudatorySuccessor || pHoldsPrinceTitle) &&
                   !pHasLivingSon;
        }

        public static bool RollMakesMale(int pRoll)
        {
            return pRoll >= 0 && pRoll < MalePreferencePercent;
        }
    }
}
