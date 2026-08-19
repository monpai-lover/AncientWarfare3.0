using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolXiaAccessService
    {
        public static bool CanHostAcademy(City pCity)
        {
            Resolve(pCity, out bool cityValid, out bool ownerValid,
                out bool nativeXiaOwner, out bool fullyXiaizedCity);
            return HistoricalSchoolXiaAccessRules.CanHostAcademy(cityValid,
                ownerValid, nativeXiaOwner, fullyXiaizedCity);
        }

        public static bool CanReceiveSchoolTravel(City pCity)
        {
            Resolve(pCity, out bool cityValid, out bool ownerValid,
                out bool nativeXiaOwner, out bool fullyXiaizedCity);
            return HistoricalSchoolXiaAccessRules.CanReceiveSchoolTravel(
                cityValid, ownerValid, nativeXiaOwner, fullyXiaizedCity);
        }

        public static bool CanHostLecture(City pCity)
        {
            Resolve(pCity, out bool cityValid, out bool ownerValid,
                out bool nativeXiaOwner, out bool fullyXiaizedCity);
            return HistoricalSchoolXiaAccessRules.CanHostLecture(cityValid,
                ownerValid, nativeXiaOwner, fullyXiaizedCity);
        }

        public static void NotifyAccessChanged(City pCity)
        {
            long cityId = pCity?.data?.id ?? -1L;
            if (cityId < 0) return;
            HistoricalSchoolRuntime.RefreshLivingXiaCity(pCity);
            HistoricalSchoolAcademyConstructionService.InvalidateCity(cityId);
            HistoricalSchoolVenueService.ReleaseCityClaims(cityId);
            HistoricalSchoolVenueService.InvalidateCity(cityId);
            HistoricalSchoolRecruitCandidateCache.InvalidateCity(cityId);
            HistoricalSchoolTravelService.InvalidateCityIndex();
            SchoolLandmarkService.MarkDirty(cityId);
            AncientWarfare3.core.court.CitySchoolSnapshotService.MarkDirty(pCity);
        }

        private static void Resolve(City pCity, out bool pCityValid,
            out bool pOwnerValid, out bool pNativeXiaOwner,
            out bool pFullyXiaizedCity)
        {
            pCityValid = pCity?.data != null && pCity.isAlive() &&
                         !pCity.isRekt();
            Kingdom owner = pCityValid ? pCity.kingdom : null;
            pOwnerValid = owner?.data != null && !owner.isRekt() &&
                          !owner.isNeutral();
            pNativeXiaOwner = pOwnerValid &&
                              LineageService.IsXiaKingdom(owner);
            pFullyXiaizedCity = pCityValid && pOwnerValid &&
                                XiaizationService.IsFullyXiaizedCity(pCity);
        }
    }
}
