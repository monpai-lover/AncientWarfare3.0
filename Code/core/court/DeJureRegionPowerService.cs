using System;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal static class DeJureRegionPowerService
    {
        internal const int CreateMode = 0;
        internal const int AssignMode = 1;
        private static long _targetRegionId = -1L;

        internal static void ClearRuntime()
        {
            _targetRegionId = -1L;
        }

        internal static string Click(WorldTile pTile, int pMode,
            out bool pSuccess)
        {
            pSuccess = false;
            City city = pTile?.zone?.city;
            if (city?.data == null || city.isRekt())
                return "aw_de_jure_region_invalid_city";

            if (pMode == CreateMode)
            {
                HierarchicalVassalMapModeService.PrepareForDeJureInteraction(
                    city);
                if (!DeJureRegionStore.CreateState(city, "power_create",
                        out DeJureRegion created, out _))
                    return "aw_de_jure_region_create_failed";
                _targetRegionId = created.RegionId;
                pSuccess = true;
                HierarchicalVassalMapModeService.MarkHierarchyDirty(
                    city.kingdom);
                HierarchicalVassalMapModeService.RefreshAfterDeJureMutation();
                return "aw_de_jure_region_created";
            }

            if (pMode != AssignMode) return "aw_de_jure_region_invalid_mode";
            HierarchicalVassalMapModeService.PrepareForDeJureInteraction(city);
            if (_targetRegionId < 0L)
            {
                if (!DeJureRegionStore.TryGetForCity(city.data.id,
                        out DeJureRegion selected) ||
                    selected.SeatCityId != city.data.id)
                    return "aw_de_jure_region_select_capital";
                _targetRegionId = selected.RegionId;
                pSuccess = true;
                return "aw_de_jure_region_target_selected";
            }

            if (DeJureRegionStore.TryGetForCity(city.data.id,
                    out DeJureRegion clickedRegion) &&
                clickedRegion.SeatCityId == city.data.id &&
                clickedRegion.RegionId != _targetRegionId)
            {
                _targetRegionId = clickedRegion.RegionId;
                pSuccess = true;
                return "aw_de_jure_region_target_selected";
            }

            if (!DeJureRegionStore.AssignCity(_targetRegionId, city,
                    out _))
                return "aw_de_jure_region_assign_failed";
            pSuccess = true;
            HierarchicalVassalMapModeService.MarkHierarchyDirty(
                city.kingdom);
            HierarchicalVassalMapModeService.RefreshAfterDeJureMutation();
            return "aw_de_jure_region_assigned";
        }
    }
}
