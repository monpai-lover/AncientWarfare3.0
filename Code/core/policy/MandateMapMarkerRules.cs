namespace AncientWarfare3.core.policy
{
    public static class MandateMapMarkerRules
    {
        public static bool ShouldReplaceSpeciesIcon(string pIconPath, bool pHasSpeciesImage)
        {
            return false;
        }

        public static bool ShouldUseSpecialIcon(string pIconPath, bool pHasSpecialImage)
        {
            return !string.IsNullOrEmpty(pIconPath) && pHasSpecialImage;
        }
    }
}
