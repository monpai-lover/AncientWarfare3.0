using System;

namespace AncientWarfare3.core.lineage
{
    public static class ArmyNativeNameRules
    {
        public static string BuildOrdinaryName(string pKingdomName,
            string pAnchorCityName, int pOrdinal)
        {
            string kingdom = (pKingdomName ?? string.Empty).Trim();
            string city = (pAnchorCityName ?? string.Empty).Trim();
            int ordinal = Math.Max(1, pOrdinal);
            string prefix = city.Length == 0
                ? kingdom
                : kingdom + "-" + city;
            return prefix + "-" + ordinal + "军";
        }

        public static string ResolveName(bool isSpecialArmy,
            string currentNativeName, string kingdomName,
            string anchorCityName, int ordinal)
        {
            return isSpecialArmy
                ? currentNativeName ?? string.Empty
                : BuildOrdinaryName(kingdomName, anchorCityName, ordinal);
        }
    }
}
