namespace AncientWarfare3.core.lineage
{
    public static class SoldierRetirementRules
    {
        public static bool CanConsiderForRetirement(bool isSupportedActor, bool isRekt, bool isWarrior,
            bool alreadyRetired, bool isGeneral, bool isFiefHolder)
        {
            if (!isSupportedActor) return false;
            if (isRekt || !isWarrior) return false;
            if (alreadyRetired) return false;
            return !isGeneral && !isFiefHolder;
        }
    }
}
