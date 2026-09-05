using System;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    /// <summary>
    /// Resolves a complete official title from its durable appointment
    /// jurisdiction instead of the actor's current residence.
    /// </summary>
    internal static class CourtOfficialTitleResolver
    {
        internal static string Resolve(Actor pActor, Kingdom pCourtKingdom,
            string pOfficeId, string pOfficeName)
        {
            if (pActor?.data == null) return pOfficeName ?? string.Empty;
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_CITY_ID, out long cityId, -1L);
            pActor.data.get(LineageKeys.COURT_COUNTY_ID, out long countyId,
                -1L);
            return Resolve(pCourtKingdom, layer, cityId, countyId, pOfficeId,
                pOfficeName, "");
        }

        internal static string Resolve(Kingdom pCourtKingdom, string pLayer,
            long pCityId, long pCountyId, string pOfficeId,
            string pOfficeName, string pFallbackCityName)
        {
            string layer = InferLegacyLayer(pLayer, pCityId, pCountyId,
                pOfficeId);
            if (!CourtOfficeDisplayRules.IsJurisdictionalLayer(layer))
                return (pOfficeName ?? string.Empty).Trim();
            string jurisdiction = ResolveJurisdiction(pCourtKingdom, layer,
                pCityId, pCountyId, pFallbackCityName);
            return CourtOfficeDisplayRules.ComposeJurisdictionalTitle(layer,
                jurisdiction, pOfficeName);
        }

        private static string InferLegacyLayer(string pLayer, long pCityId,
            long pCountyId, string pOfficeId)
        {
            if (!string.IsNullOrWhiteSpace(pLayer)) return pLayer;
            if (pCountyId >= 0L || string.Equals(pOfficeId,
                    CourtOfficeId.CountyMagistrate, StringComparison.Ordinal))
                return CourtOfficeLayer.County;
            return pCityId >= 0L ? CourtOfficeLayer.City : string.Empty;
        }

        private static string ResolveJurisdiction(Kingdom pCourtKingdom,
            string pLayer, long pCityId, long pCountyId,
            string pFallbackCityName)
        {
            if (pLayer == CourtOfficeLayer.County)
            {
                try
                {
                    return AncientWarfare3.core.county.CountyAdministrationStore
                        .FindById(pCountyId)?.Name ?? string.Empty;
                }
                catch { return string.Empty; }
            }

            City city = null;
            try { city = World.world?.cities?.get(pCityId); }
            catch { }

            if (pLayer == CourtOfficeLayer.Feudatory)
            {
                try
                {
                    if (FeudatoryService.TryGetByCity(pCityId,
                            out FeudatorySnapshot feudatory))
                        return string.IsNullOrWhiteSpace(
                            feudatory.FeudatoryName)
                            ? feudatory.SeatName
                            : feudatory.FeudatoryName;
                }
                catch { }
                return CityName(city, pFallbackCityName);
            }

            if (pLayer == CourtOfficeLayer.Regional)
            {
                try
                {
                    if (DeJureRegionStore.TryGetForCity(pCityId,
                            out DeJureRegion region))
                    {
                        CustomCourtRuntime.RegionalTitles(pCourtKingdom,
                            out string regionTitle, out _);
                        return RegionalGovernmentRules.AdministrativeLabel(
                            region.RegionName, regionTitle);
                    }
                }
                catch { }
                return CityName(city, pFallbackCityName);
            }

            string cityName = CityName(city, pFallbackCityName);
            if (cityName.Length == 0) return string.Empty;
            try
            {
                CustomCourtRuntime.RegionalTitles(pCourtKingdom, out _, out _,
                    out string localLevelTitle);
                return RegionalGovernmentRules.AdministrativeLabel(cityName,
                    localLevelTitle);
            }
            catch { return cityName; }
        }

        private static string CityName(City pCity, string pFallback)
        {
            string name = string.Empty;
            try
            {
                name = DeJureRegionStore.ResolveCountyNameForPresentation(
                    pCity);
            }
            catch { }
            return string.IsNullOrWhiteSpace(name)
                ? (pFallback ?? string.Empty).Trim()
                : name.Trim();
        }
    }
}
