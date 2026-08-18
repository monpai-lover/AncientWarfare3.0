namespace AncientWarfare3.core.lineage
{
    internal static class RestorationProtectionService
    {
        internal static void StartProtection(Kingdom pKingdom,
            int pRestorationYear)
        {
            if (pKingdom?.data == null) return;
            int protectionUntilYear = RestorationProtectionRules.
                ProtectionUntil(pRestorationYear);
            pKingdom.data.set(
                LineageKeys.RESTORATION_PROTECTION_UNTIL_YEAR,
                protectionUntilYear);
        }

        internal static int GetProtectionUntilYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return -1;
            pKingdom.data.get(
                LineageKeys.RESTORATION_PROTECTION_UNTIL_YEAR,
                out int protectionUntilYear, -1);
            return protectionUntilYear;
        }

        internal static bool IsActive(Kingdom pKingdom, int pCurrentYear)
        {
            return RestorationProtectionRules.IsActive(pCurrentYear,
                GetProtectionUntilYear(pKingdom));
        }

        internal static bool ShouldBlockIncomingWar(Kingdom pAttacker,
            Kingdom pDefender, string pWarType, bool pInternalSystemWar)
        {
            if (pAttacker?.data == null || pDefender?.data == null ||
                pAttacker == pDefender) return false;
            bool internalWar = pInternalSystemWar ||
                               RestorationProtectionRules.
                                   IsInternalWarType(pWarType) ||
                               PeasantRebelRouteService.
                                   IsOriginSuppressionPair(
                                       pAttacker, pDefender);
            return RestorationProtectionRules.ShouldBlockIncoming(
                protectionActive: IsActive(pDefender,
                    Date.getCurrentYear()),
                protectedDefender: true,
                internalWar: internalWar,
                protectedKingdomIsAttacker: false);
        }
    }
}
