namespace AncientWarfare3.core.lineage
{
    public static class FamilyTreePortraitFrameRules
    {
        public static bool ShouldShowRoleFrame(bool pIsKing, bool pIsCityLeader, bool pIsArmyCaptain)
        {
            return pIsKing || pIsCityLeader || pIsArmyCaptain;
        }
    }
}
