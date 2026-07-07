namespace AncientWarfare3.core.lineage
{
    public static class AWArmyRoleRules
    {
        public static bool IsSpecialRole(string pRole)
        {
            return pRole == AWArmyRole.RoyalGuard ||
                   pRole == AWArmyRole.SlaveArmy ||
                   pRole == AWArmyRole.BorderArmy;
        }

        public static bool ShouldUseDetachedArmy(string pRole)
        {
            return IsSpecialRole(pRole);
        }

        public static string DisplayName(string pRole, string pKingdomName, int pIndex)
        {
            string prefix = string.IsNullOrEmpty(pKingdomName) ? "" : pKingdomName + " ";
            string role = RoleLabel(pRole);
            string suffix = pIndex > 1 ? " " + pIndex.ToString() : "";
            return prefix + role + suffix;
        }

        private static string RoleLabel(string pRole)
        {
            if (pRole == AWArmyRole.RoyalGuard) return "禁卫军";
            if (pRole == AWArmyRole.SlaveArmy) return "奴隶军";
            if (pRole == AWArmyRole.BorderArmy) return "边军";
            return "军";
        }
    }
}
