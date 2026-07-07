namespace AncientWarfare3.core.policy
{
    public static class MandateMapMarkerRules
    {
        public static bool ShouldReplaceSpeciesIcon(string pIconPath, bool pHasSpeciesImage)
        {
            return !string.IsNullOrEmpty(pIconPath) && pHasSpeciesImage;
        }

        public static bool ShouldUseSpecialIcon(string pIconPath, bool pHasSpecialImage)
        {
            return false;
        }

        public static bool ShouldClearSpecialIcon(string pIconPath, bool pHasSpecialImage)
        {
            return pHasSpecialImage;
        }
    }
}
