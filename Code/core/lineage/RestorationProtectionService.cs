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
    }
}
