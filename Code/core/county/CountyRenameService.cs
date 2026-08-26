using System;
using System.Linq;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.county
{
    internal enum CountyRenameResult
    {
        Success = 0,
        CountyNotFound = 1,
        Unauthorized = 2,
        EmptyName = 3,
        DuplicateName = 4,
        InvalidRegion = 5,
        PersistenceFailed = 6
    }

    internal static class CountyRenameService
    {
        internal static event Action<long, long> Changed;

        internal static CountyRenameResult TryApply(long pKingdomId,
            long pCountyId, string pName, bool pRestoreHistorical,
            out CountyRecord pUpdated)
        {
            pUpdated = null;
            CountyRecord county = CountyAdministrationStore.FindById(
                pCountyId);
            if (county == null) return CountyRenameResult.CountyNotFound;
            City city = World.world?.cities?.FirstOrDefault(item =>
                item?.data?.id == county.CityId);
            if (city?.data == null || city.isRekt())
                return CountyRenameResult.CountyNotFound;
            if (city.kingdom?.data == null || city.kingdom.id != pKingdomId)
                return CountyRenameResult.Unauthorized;

            long regionId = county.RegionId;
            if (regionId < 0L && DeJureRegionStore.TryGetForCity(
                    county.CityId, out DeJureRegion region))
                regionId = region.RegionId;
            if (regionId < 0L) return CountyRenameResult.InvalidRegion;

            try
            {
                if (pRestoreHistorical)
                {
                    county.RegionId = regionId;
                    county.ManualName = false;
                    CountyAdministrationStore.Upsert(county);
                    CountyAdministrationService.ReconcileCity(city);
                    pUpdated = CountyAdministrationStore.FindById(pCountyId);
                }
                else
                {
                    CountyRenameEntry[] entries = CountyAdministrationStore.
                        ForRegion(regionId).Select(item =>
                            new CountyRenameEntry(item.CountyId,
                                item.RegionId, item.Name, item.Active)).ToArray();
                    CountyRenameValidationResult validation =
                        CountyRenameRules.Validate(pName, county.CountyId,
                            regionId, entries, out string normalized);
                    if (validation == CountyRenameValidationResult.Empty)
                        return CountyRenameResult.EmptyName;
                    if (validation == CountyRenameValidationResult.Duplicate)
                        return CountyRenameResult.DuplicateName;
                    county.RegionId = regionId;
                    county.Name = normalized;
                    county.ManualName = true;
                    CountyAdministrationStore.Upsert(county);
                    pUpdated = CountyAdministrationStore.FindById(pCountyId);
                }
                if (pUpdated == null)
                    return CountyRenameResult.PersistenceFailed;
                try
                {
                    HierarchicalVassalMapModeService.MarkCityDirty(city);
                    Changed?.Invoke(pKingdomId, city.id);
                }
                catch (Exception refreshError)
                {
                    ModClass.LogWarning("County rename refresh failed: " +
                        refreshError.Message);
                }
                return CountyRenameResult.Success;
            }
            catch (Exception error)
            {
                ModClass.LogError("County rename failed: " + error.Message);
                return CountyRenameResult.PersistenceFailed;
            }
        }
    }
}
