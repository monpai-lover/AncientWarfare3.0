namespace AncientWarfare3.core.court
{
    public enum CustomLocalGovernmentDefaultKind
    {
        Manual = 0,
        Civil = 1,
        Military = 2
    }

    public static class CustomLocalGovernmentRules
    {
        public static CustomLocalGovernmentDefaultKind SelectDefault(
            bool pManualBinding, bool pHasForeignLandBorder,
            bool pFrontierMilitaryRole)
        {
            if (pManualBinding)
                return CustomLocalGovernmentDefaultKind.Manual;
            return pHasForeignLandBorder || pFrontierMilitaryRole
                ? CustomLocalGovernmentDefaultKind.Military
                : CustomLocalGovernmentDefaultKind.Civil;
        }
    }
}
