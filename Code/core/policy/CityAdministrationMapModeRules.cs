using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.policy
{
    internal enum CityAdministrationMapLevel
    {
        Regions,
        Cities
    }

    internal enum CityAdministrationMapClickAction
    {
        None,
        FocusRegion,
        InspectCity,
        PopToRegions
    }

    internal sealed class CityAdministrationMapStateRules
    {
        private readonly List<long> _regionBreadcrumbs = new List<long>();

        internal bool IsRegionLevel => _regionBreadcrumbs.Count == 0;
        internal long FocusSeatCityId => IsRegionLevel
            ? -1L : _regionBreadcrumbs[_regionBreadcrumbs.Count - 1];
        internal IReadOnlyList<long> RegionBreadcrumbs => _regionBreadcrumbs;

        internal bool PushRegion(long pSeatCityId)
        {
            if (pSeatCityId < 0L) return false;
            _regionBreadcrumbs.Add(pSeatCityId);
            return true;
        }

        internal bool PopRegion()
        {
            if (IsRegionLevel) return false;
            _regionBreadcrumbs.RemoveAt(_regionBreadcrumbs.Count - 1);
            return true;
        }

        internal void Reset() => _regionBreadcrumbs.Clear();
    }

    internal static class CityAdministrationMapModeRules
    {
        internal static string CacheKey(long pWorldGeneration,
            CityAdministrationMapLevel pLevel, long pSeatCityId,
            long pEntityId)
        {
            return "city-admin:" + pWorldGeneration + ":" +
                pLevel + ":" + pSeatCityId + ":" + pEntityId;
        }

        internal static IReadOnlyList<long> OrderedMembers(
            IEnumerable<long> pCityIds)
        {
            return (pCityIds ?? Array.Empty<long>()).Distinct().OrderBy(id => id)
                .ToArray();
        }

        internal static CityAdministrationMapClickAction ResolveClick(
            bool pIsRegionLevel, long pFocusSeatCityId,
            long pClickedRegionSeatCityId, bool pClickedMapped)
        {
            if (!pClickedMapped)
                return pIsRegionLevel
                    ? CityAdministrationMapClickAction.None
                    : CityAdministrationMapClickAction.PopToRegions;
            if (pIsRegionLevel)
                return CityAdministrationMapClickAction.FocusRegion;
            return pClickedRegionSeatCityId == pFocusSeatCityId
                ? CityAdministrationMapClickAction.InspectCity
                : CityAdministrationMapClickAction.PopToRegions;
        }
    }
}
