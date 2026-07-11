namespace AncientWarfare3.content
{
    public static class XiaAllianceNamingRules
    {
        public static bool ShouldUseXiaName(bool pFounder1IsXia, bool pFounder2IsXia)
        {
            return pFounder1IsXia || pFounder2IsXia;
        }

        public static bool ShouldRenameAfterCreation(bool pUsesXiaNaming, bool pValidName)
        {
            return pUsesXiaNaming && pValidName;
        }
    }
}
