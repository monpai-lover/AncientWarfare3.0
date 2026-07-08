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
            return pRole == AWArmyRole.RoyalGuard;
        }

        public static int MaxArmiesPerCity(string pRole)
        {
            return pRole == AWArmyRole.SlaveArmy ? 1 : int.MaxValue;
        }

        public static int MaxArmiesPerKingdom(string pRole)
        {
            if (pRole == AWArmyRole.RoyalGuard) return 1;
            return pRole == AWArmyRole.BorderArmy ? 3 : int.MaxValue;
        }

        public static bool ShouldMatchArmyAnchor(string pRole, long pRequestedAnchorId, long pArmyAnchorId)
        {
            if (MaxArmiesPerKingdom(pRole) == 1) return true;
            if (pRequestedAnchorId < 0) return true;
            return pArmyAnchorId == pRequestedAnchorId;
        }

        public static bool ShouldCleanupDuplicateArmy(string pRole, long pRequestedAnchorId, long pArmyAnchorId)
        {
            if (MaxArmiesPerKingdom(pRole) == 1) return true;
            return MaxArmiesPerCity(pRole) == 1 &&
                   pRequestedAnchorId >= 0 &&
                   pArmyAnchorId == pRequestedAnchorId;
        }

        public static bool ShouldSetCaptain(long pCurrentCaptainId, long pNewCaptainId)
        {
            return pNewCaptainId >= 0 && pCurrentCaptainId != pNewCaptainId;
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
