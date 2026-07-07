namespace AncientWarfare3.core.lineage
{
    public static class ArmySaveSafetyRules
    {
        public static bool ShouldRemoveInvalidSpecialArmy(
            bool pIsSpecialArmy,
            bool pHasKingdom,
            bool pHasCity,
            bool pHasCaptain,
            int pUnitCount)
        {
            if (!pIsSpecialArmy) return false;
            if (pHasKingdom) return false;
            if (pHasCity) return false;
            if (pHasCaptain) return false;
            return pUnitCount <= 0;
        }

        public static bool ShouldUseSafeSpecialArmySave(bool pIsSpecialArmy, bool pHasKingdom)
        {
            return pIsSpecialArmy;
        }
    }
}
