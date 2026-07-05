namespace AncientWarfare3.core.lineage
{
    public static class FiefMilitaryRules
    {
        public const float ActiveFiefManpowerMultiplier = 1.35f;
        public const float ActiveFiefTaxMultiplier = 0.88f;
        public const float ActiveFiefUnrestBonus = 1.5f;
        private static readonly string[] FiefArmyTitles =
        {
            "虎贲",
            "鹰扬",
            "武卫",
            "骁骑",
            "锐士",
            "陷阵",
            "强弩",
            "武锋"
        };

        public static bool ShouldApplyFiefSoldierTrait(bool activeFief, bool isWarrior,
            bool alreadyHasTrait, bool isSlave, bool isRoyalGuard)
        {
            return activeFief && isWarrior && !alreadyHasTrait && !isSlave && !isRoyalGuard;
        }

        public static bool ShouldRenameFiefArmy(bool activeFief, bool isSlaveArmy, bool isRoyalGuardArmy)
        {
            return activeFief && !isSlaveArmy && !isRoyalGuardArmy;
        }

        public static bool ShouldEnforceFiefCommand(bool activeFief, bool generalAlive,
            bool generalInKingdom, bool generalIsSlave, bool generalIsKing)
        {
            return activeFief && generalAlive && generalInKingdom && !generalIsSlave && !generalIsKing;
        }

        public static string BuildFiefArmyName(string cityName, long cityId)
        {
            string prefix = string.IsNullOrWhiteSpace(cityName) ? "封地" : cityName.Trim();
            return prefix + SelectFiefArmyTitle(cityId) + "军";
        }

        private static string SelectFiefArmyTitle(long cityId)
        {
            long normalized = cityId < 0 ? 0 : cityId;
            int index = (int)(normalized % FiefArmyTitles.Length);
            return FiefArmyTitles[index];
        }
    }
}
