namespace AncientWarfare3.core.court
{
    internal static class CustomLocalGovernmentCityService
    {
        internal static bool HasForeignLandBorder(City pCity,
            Kingdom pOwner)
        {
            if (pCity?.data == null || pOwner?.data == null ||
                pCity.kingdom != pOwner) return false;
            try
            {
                foreach (Kingdom neighbour in pCity.neighbours_kingdoms)
                {
                    if (neighbour?.data == null || neighbour == pOwner ||
                        neighbour.isNeutral() || neighbour.isRekt()) continue;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
