namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolXiaAccessRules
    {
        public static bool CanHostAcademy(bool pCityValid,
            bool pOwnerValid, bool pNativeXiaOwner,
            bool pFullyXiaizedCity)
        {
            return HasAccess(pCityValid, pOwnerValid, pNativeXiaOwner,
                pFullyXiaizedCity);
        }

        public static bool CanReceiveSchoolTravel(bool pCityValid,
            bool pOwnerValid, bool pNativeXiaOwner,
            bool pFullyXiaizedCity)
        {
            return HasAccess(pCityValid, pOwnerValid, pNativeXiaOwner,
                pFullyXiaizedCity);
        }

        public static bool CanHostLecture(bool pCityValid,
            bool pOwnerValid, bool pNativeXiaOwner,
            bool pFullyXiaizedCity)
        {
            return HasAccess(pCityValid, pOwnerValid, pNativeXiaOwner,
                pFullyXiaizedCity);
        }

        private static bool HasAccess(bool pCityValid, bool pOwnerValid,
            bool pNativeXiaOwner, bool pFullyXiaizedCity)
        {
            return pCityValid && pOwnerValid &&
                   (pNativeXiaOwner || pFullyXiaizedCity);
        }
    }
}
