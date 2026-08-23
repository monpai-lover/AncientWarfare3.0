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
        private long _focusedKingdomId = -1L;
        private readonly List<long> _regionBreadcrumbs = new List<long>();

        internal bool IsCountryLevel => _focusedKingdomId < 0L;
        internal bool IsRegionLevel => _focusedKingdomId >= 0L &&
            _regionBreadcrumbs.Count == 0;
        internal bool IsCityLevel => _focusedKingdomId >= 0L &&
            _regionBreadcrumbs.Count > 0;
        internal long FocusKingdomId => _focusedKingdomId;
        internal long FocusSeatCityId => IsCityLevel
            ? _regionBreadcrumbs[_regionBreadcrumbs.Count - 1] : -1L;
        internal IReadOnlyList<long> RegionBreadcrumbs => _regionBreadcrumbs;

        internal bool PushKingdom(long pKingdomId)
        {
            if (pKingdomId < 0L || !IsCountryLevel) return false;
            _focusedKingdomId = pKingdomId;
            return true;
        }

        internal bool PopKingdom()
        {
            if (IsCountryLevel) return false;
            _focusedKingdomId = -1L;
            _regionBreadcrumbs.Clear();
            return true;
        }

        internal bool PushRegion(long pSeatCityId)
        {
            if (pSeatCityId < 0L || !IsRegionLevel) return false;
            _regionBreadcrumbs.Add(pSeatCityId);
            return true;
        }

        internal bool PopRegion()
        {
            if (IsRegionLevel) return false;
            _regionBreadcrumbs.RemoveAt(_regionBreadcrumbs.Count - 1);
            return true;
        }

        internal void Reset()
        {
            _focusedKingdomId = -1L;
            _regionBreadcrumbs.Clear();
        }
    }

    internal static class CityAdministrationMapModeRules
    {
        private static readonly string[] RegionPalette =
        {
            "#4A6FA5", "#A55B4A", "#5C8A57", "#8064A5",
            "#B17A3A", "#438A8A", "#A34B70", "#667A3F"
        };

        internal static string RegionColorHex(long pSeatCityId)
        {
            long normalized = pSeatCityId < 0L ? -pSeatCityId : pSeatCityId;
            return RegionPalette[(int)(normalized % RegionPalette.Length)];
        }

        internal static string CacheKey(long pWorldGeneration,
            CityAdministrationMapLevel pLevel, long pSeatCityId,
            long pEntityId)
        {
            return "city-admin:" + pWorldGeneration + ":" +
                pLevel + ":" + pSeatCityId + ":" + pEntityId;
        }

        internal static bool IsGlobalRegionOverview(bool pCityLayer,
            bool pCountryLevel)
        {
            return pCityLayer && pCountryLevel;
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
