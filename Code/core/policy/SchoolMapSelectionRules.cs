namespace AncientWarfare3.core.policy
{
    public static class SchoolMapSelectionRules
    {
        public static bool CanSelectCity(bool pCityValid, bool pWindowActive)
        {
            return pCityValid && !pWindowActive;
        }
    }
}
