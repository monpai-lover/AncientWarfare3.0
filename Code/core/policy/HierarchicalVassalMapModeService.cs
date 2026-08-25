using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.county;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal sealed class HierarchicalVassalMapLabelTerritorySource
    {
        internal readonly Kingdom Kingdom;
        internal readonly IReadOnlyList<TileZone> Zones;
        internal readonly HashSet<int> ZoneIds;
        internal readonly bool HasVisibleLand;

        internal HierarchicalVassalMapLabelTerritorySource(Kingdom pKingdom,
            IReadOnlyList<TileZone> pZones)
            : this(pKingdom, pZones, null, false)
        {
            if (Zones == null) return;
            for (int index = 0; index < Zones.Count; index++)
            {
                TileZone zone = Zones[index];
                if (zone?.id >= 0)
                {
                    ZoneIds.Add(zone.id);
                    if (zone.tiles_with_ground > 0) HasVisibleLand = true;
                }
            }
        }

        internal HierarchicalVassalMapLabelTerritorySource(Kingdom pKingdom,
            IReadOnlyList<TileZone> pZones, HashSet<int> pZoneIds,
            bool pHasVisibleLand)
        {
            Kingdom = pKingdom;
            Zones = pZones ?? Array.Empty<TileZone>();
            ZoneIds = pZoneIds ?? new HashSet<int>();
            HasVisibleLand = pHasVisibleLand;
        }
    }

    internal sealed class HierarchicalVassalMapLabelCitySource
    {
        internal readonly City City;
        internal readonly IReadOnlyList<TileZone> Zones;
        internal readonly HashSet<int> ZoneIds;
        internal readonly bool HasVisibleLand;

        internal HierarchicalVassalMapLabelCitySource(City pCity,
            IReadOnlyList<TileZone> pZones, HashSet<int> pZoneIds,
            bool pHasVisibleLand)
        {
            City = pCity;
            Zones = pZones ?? Array.Empty<TileZone>();
            ZoneIds = pZoneIds ?? new HashSet<int>();
            HasVisibleLand = pHasVisibleLand;
        }
    }

    internal sealed class HierarchicalVassalMapLabelRegionSource
    {
        internal readonly RegionalGovernmentReadModel Region;
        internal readonly City SeatCity;
        internal readonly IReadOnlyList<TileZone> Zones;
        internal readonly HashSet<int> ZoneIds;
        internal readonly bool HasVisibleLand;

        internal HierarchicalVassalMapLabelRegionSource(
            RegionalGovernmentReadModel pRegion, City pSeatCity,
            IReadOnlyList<TileZone> pZones, HashSet<int> pZoneIds,
            bool pHasVisibleLand)
        {
            Region = pRegion;
            SeatCity = pSeatCity;
            Zones = pZones ?? Array.Empty<TileZone>();
            ZoneIds = pZoneIds ?? new HashSet<int>();
            HasVisibleLand = pHasVisibleLand;
        }
    }

    internal static class HierarchicalVassalMapModeService
    {
        public const string POWER_ID =
            HierarchicalVassalMapModeRules.POWER_ID;

        private static readonly HierarchicalVassalMapModeState State =
            new HierarchicalVassalMapModeState();
        private static readonly CityAdministrationMapStateRules
            CityAdministrationState = new CityAdministrationMapStateRules();
        private static Dictionary<long, Kingdom> KingdomIndex =
            new Dictionary<long, Kingdom>();
        private static readonly Dictionary<int, NativeZoneMetaCacheEntry>
            NativeDrawMetaCache =
                new Dictionary<int, NativeZoneMetaCacheEntry>();
        private static readonly Dictionary<int,
            List<HierarchicalVassalLabelTile>> NativeLandTileCache =
                new Dictionary<int, List<HierarchicalVassalLabelTile>>();
        private static readonly Dictionary<long, NativeCountryLabelEntry>
            NativeCountryLabels =
                new Dictionary<long, NativeCountryLabelEntry>();
        private static readonly Dictionary<long, NativeCityLabelEntry>
            NativeCityLabels = new Dictionary<long, NativeCityLabelEntry>();
        private static readonly Dictionary<long, AWMapModeMetaObject>
            CountyMetaCache = new Dictionary<long, AWMapModeMetaObject>();
        private static readonly Dictionary<long, AWMapModeMetaObject>
            CityRegionMetaCache = new Dictionary<long, AWMapModeMetaObject>();
        private static readonly Stack<NativeCountryLabelEntry>
            NativeCountryLabelPool = new Stack<NativeCountryLabelEntry>();
        private static readonly Stack<NativeCityLabelEntry>
            NativeCityLabelPool = new Stack<NativeCityLabelEntry>();
        private static readonly List<NativeCountryLabelEntry>
            NativeCountryPublishEntries = new List<NativeCountryLabelEntry>();
        private static readonly List<NativeCityLabelEntry>
            NativeCityPublishEntries = new List<NativeCityLabelEntry>();
        private static readonly HashSet<string> NativeActiveLabelKeys =
            new HashSet<string>();
        private static readonly Dictionary<Type, SelectAndInspectInvoker>
            SelectAndInspectByAssetType =
                new Dictionary<Type, SelectAndInspectInvoker>();
        private static HierarchicalVassalHierarchyIndex _hierarchyIndex;
        private static bool _nativeDrawPassActive;
        private static HierarchicalVassalMapModeLayer _selectedLayer =
            HierarchicalVassalMapModeLayer.Countries;
        private static bool _nativeDrawPassUsingCache;
        private static bool _nativeDrawCacheValid;
        private static bool _nativeDrawCacheCityLayer;
        private static long _nativeDrawCacheFocusKey = long.MinValue;

        private delegate bool SelectAndInspectInvoker(
            object pAsset, object pObject);

        public static bool IsActive()
        {
            bool coordinatorActive = false;
            bool cachedAssetMatches = false;
            try
            {
                coordinatorActive = AWMapModeCoordinator.IsActive(POWER_ID);
            }
            catch { }
            try
            {
                MetaTypeAsset cached = World.world?.getCachedMapMetaAsset();
                cachedAssetMatches = cached != null &&
                    (ReferenceEquals(cached,
                         AWMapModeMetaLibrary.HierarchicalVassalAsset) ||
                     cached.map_mode ==
                         AWMapModeMetaTypes.HierarchicalVassal);
            }
            catch { }
            return HierarchicalVassalMapActivationRules.ShouldOwnRenderer(
                coordinatorActive, cachedAssetMatches);
        }

        public static HierarchicalVassalMapModeLayer GetSelectedLayer()
        {
            return _selectedLayer;
        }

        internal static void SetSelectedLayerFromOption(int pZoneOption)
        {
            HierarchicalVassalMapModeLayer nextLayer =
                HierarchicalVassalMapModeOptionRules.ResolveLayer(pZoneOption);
            bool changed = nextLayer != _selectedLayer;
            if (changed)
            {
                CityAdministrationState.Reset();
                HierarchicalVassalMapModeLabelLayer.
                    HideRuntimeLabelsExcept(null);
                _selectedLayer = nextLayer;
                InvalidateNativeLabelCache();
                RequestNativeRedraw();
            }
            else
            {
                RequestNativeRedraw();
            }
        }

        internal static void PrepareForDeJureInteraction(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt() ||
                pCity.kingdom?.data == null || pCity.kingdom.isRekt()) return;
            _selectedLayer = HierarchicalVassalMapModeLayer.Cities;
            EnsureHierarchyIndex();
            long focusKingdomId = _hierarchyIndex?.ResolveRepresentative(
                pCity.kingdom.id) ?? pCity.kingdom.id;
            if (focusKingdomId < 0L) focusKingdomId = pCity.kingdom.id;
            CityAdministrationState.Reset();
            CityAdministrationState.PushKingdom(focusKingdomId);
            InvalidateNativeLabelCache();
            HierarchicalVassalMapModeLabelLayer.HideRuntimeLabelsExcept(null);
            RequestNativeRedraw();
        }

        internal static void RefreshAfterDeJureMutation()
        {
            RegionalGovernmentAggregationService.Clear();
            CityRegionMetaCache.Clear();
            CountyMetaCache.Clear();
            InvalidateNativeLabelCache();
            HierarchicalVassalMapModeLabelLayer.MarkDirty();
            HierarchicalVassalMapModeLabelLayer.RequestRefresh();
            RequestNativeRedraw();
        }

        public static bool IsCityLayer =>
            GetSelectedLayer() == HierarchicalVassalMapModeLayer.Cities;

        internal static bool IsCityGlobalRegionLayer => IsCityLayer &&
            CityAdministrationMapModeRules.IsGlobalRegionOverview(
                IsCityLayer, CityAdministrationState.IsCountryLevel);

        internal static bool IsCityCountryLayer => IsCityLayer &&
            CityAdministrationState.IsCountryLevel &&
            !IsCityGlobalRegionLayer;

        internal static bool IsCityRegionLayer => IsCityLayer &&
            (IsCityGlobalRegionLayer || CityAdministrationState.IsRegionLevel);

        internal static bool IsCityMemberLayer => IsCityLayer &&
            CityAdministrationState.IsCityLevel;

        internal static bool IsCityCountyLayer => IsCityLayer &&
            CityAdministrationState.IsCountyLevel;

        internal static long CityAdministrationFocusSeatCityId =>
            CityAdministrationState.FocusSeatCityId;

        public static IMetaObject GetMetaForZone(TileZone pZone)
        {
            if (pZone == null || pZone.id < 0 ||
                !ContainsVisibleLand(pZone)) return null;
            City city = pZone.city;
            Kingdom physicalKingdom = city?.kingdom;
            if (_nativeDrawPassActive &&
                NativeDrawMetaCache.TryGetValue(pZone.id,
                    out NativeZoneMetaCacheEntry cached) &&
                ReferenceEquals(cached.Zone, pZone) &&
                ReferenceEquals(cached.City, city) &&
                ReferenceEquals(cached.PhysicalKingdom,
                    physicalKingdom)) return cached.Meta;

            IMetaObject resolved = ResolveMetaForZone(
                pZone, city, physicalKingdom);
            if (_nativeDrawPassActive)
            {
                NativeDrawMetaCache[pZone.id] =
                    new NativeZoneMetaCacheEntry(pZone, city,
                        physicalKingdom, resolved);
                RecordNativeDrawZone(pZone);
            }
            return resolved;
        }

        private static IMetaObject ResolveMetaForZone(TileZone pZone, City pCity,
            Kingdom pPhysicalKingdom)
        {
            if (pCity?.data == null || pCity.isRekt()) return null;
            if (!IsValidKingdom(pPhysicalKingdom)) return null;
            if (IsCityCountyLayer)
            {
                CountyRecord county = CountyAdministrationService.FindForZone(
                    pZone.id);
                if (county == null || county.CountyId !=
                    CityAdministrationState.FocusCountyId) return null;
                if (!CountyMetaCache.TryGetValue(county.CountyId,
                        out AWMapModeMetaObject countyMeta))
                {
                    ColorAsset countryColor = null;
                    try { countryColor = pPhysicalKingdom.getColor(); }
                    catch { }
                    countryColor?.initColor();
                    countyMeta = new AWMapModeMetaObject(county.CountyId,
                        county.Name, AWMapModeMetaTypes.HierarchicalVassal,
                        countryColor);
                    CountyMetaCache[county.CountyId] = countyMeta;
                }
                else if (countyMeta.data != null)
                    countyMeta.data.name = county.Name ?? string.Empty;
                return countyMeta;
            }
            if (IsCityRegionLayer)
            {
                EnsureHierarchyIndex();
                long regionKingdomId = _hierarchyIndex?.ResolveRepresentative(
                    pPhysicalKingdom.id) ?? pPhysicalKingdom.id;
                if (!IsCityGlobalRegionLayer &&
                    regionKingdomId != CityAdministrationState.FocusKingdomId)
                {
                    Kingdom outsideKingdom = GetKingdom(regionKingdomId);
                    return IsValidKingdom(outsideKingdom)
                        ? outsideKingdom : pPhysicalKingdom;
                }
                if (!RegionalGovernmentAggregationService.TryFindRegion(
                        pPhysicalKingdom, pCity.data.id,
                        out RegionalGovernmentReadModel region)) return null;
                City seat = FindCity(region.SeatCityId) ??
                    FindCity(pPhysicalKingdom, region.SeatCityId);
                return GetCityRegionMeta(region, seat ?? pCity);
            }
            if (IsCityCountryLayer || IsCityMemberLayer)
            {
                EnsureHierarchyIndex();
                long cityRepresentativeId = _hierarchyIndex?.ResolveRepresentative(
                    pPhysicalKingdom.id) ?? -1L;
                Kingdom cityRepresentative = GetKingdom(cityRepresentativeId);
                return IsValidKingdom(cityRepresentative)
                    ? cityRepresentative : pPhysicalKingdom;
            }

            EnsureHierarchyIndex();
            long representativeId = _hierarchyIndex?.ResolveRepresentative(
                pPhysicalKingdom.id) ?? -1L;
            if (representativeId < 0L) return null;
            Kingdom representative = GetKingdom(representativeId);
            return IsValidKingdom(representative)
                ? representative
                : pPhysicalKingdom;
        }

        internal static void BeginNativeDrawPass()
        {
            _nativeDrawPassUsingCache = CanReuseNativeLabels();
            _nativeDrawPassActive = true;
            if (_nativeDrawPassUsingCache) return;
            InvalidateNativeLabelCache();
            ReleaseNativeDrawEntries();
            ClearNativePublishBuffers();
        }

        internal static void RecordNativeDrawZone(TileZone pZone)
        {
            if (!_nativeDrawPassActive || pZone == null ||
                pZone.id < 0 || !ContainsVisibleLand(pZone)) return;
            if (!NativeDrawMetaCache.TryGetValue(pZone.id,
                    out NativeZoneMetaCacheEntry cached) ||
                !ReferenceEquals(cached.Zone, pZone)) return;

            if (IsCityRegionLayer)
            {
                City city = cached.City;
                if (city?.data == null || city.isRekt() ||
                    (!IsCityGlobalRegionLayer &&
                     !IsCityInFocusedKingdom(city)) ||
                    !RegionalGovernmentAggregationService.TryFindRegion(
                        city.kingdom, city.id,
                        out RegionalGovernmentReadModel region)) return;
                City seat = FindCity(region.SeatCityId) ??
                    FindCity(city.kingdom, region.SeatCityId) ?? city;
                if (!NativeCityLabels.TryGetValue(seat.id,
                        out NativeCityLabelEntry regionEntry))
                {
                    regionEntry = AcquireNativeCityLabelEntry(seat);
                    regionEntry.SetDisplayName(
                        RegionalGovernmentRules.AdministrativeLabel(
                            region.RegionName, region.RegionTitle));
                    NativeCityLabels.Add(seat.id, regionEntry);
                }
                regionEntry.Add(pZone);
                return;
            }
            if (IsCityMemberLayer)
            {
                City city = cached.City;
                if (city?.data == null || city.isRekt()) return;
                if (!NativeCityLabels.TryGetValue(city.id,
                        out NativeCityLabelEntry cityEntry))
                {
                    cityEntry = AcquireNativeCityLabelEntry(city);
                    NativeCityLabels.Add(city.id, cityEntry);
                }
                cityEntry.Add(pZone);
                return;
            }
            if (IsCityCountyLayer)
            {
                CountyRecord county = CountyAdministrationService.FindForZone(
                    pZone.id);
                if (county == null || county.CountyId !=
                    CityAdministrationState.FocusCountyId) return;
                City city = cached.City;
                if (city?.data == null || city.isRekt()) return;
                if (!NativeCityLabels.TryGetValue(county.CountyId,
                        out NativeCityLabelEntry countyEntry))
                {
                    countyEntry = AcquireNativeCityLabelEntry(city);
                    countyEntry.SetDisplayName(county.Name);
                    NativeCityLabels.Add(county.CountyId, countyEntry);
                }
                countyEntry.Add(pZone);
                return;
            }

            Kingdom representative = cached.Meta as Kingdom;
            if (!IsValidKingdom(representative)) return;
            if (!NativeCountryLabels.TryGetValue(representative.id,
                    out NativeCountryLabelEntry entry))
            {
                entry = AcquireNativeCountryLabelEntry(representative);
                NativeCountryLabels.Add(representative.id, entry);
            }
            entry.Add(pZone);
        }

        internal static void EndNativeDrawPass()
        {
            bool published = false;
            try
            {
                if (_nativeDrawPassActive && !_nativeDrawPassUsingCache)
                {
                    if (IsCityCountryLayer) PublishNativeCountryLabels();
                    else if (IsCityMemberLayer || IsCityCountyLayer)
                        PublishNativeCityLabels();
                    else if (IsCityRegionLayer && !IsCityGlobalRegionLayer)
                        PublishNativeCityLabels();
                    else if (!IsCityGlobalRegionLayer)
                        PublishNativeCountryLabels();
                    published = true;
                }
                if (IsCityGlobalRegionLayer)
                    HierarchicalVassalMapModeLabelLayer.
                        HideNativeLabelsExcept(null);
            }
            catch (Exception error)
            {
                try
                {
                    HierarchicalVassalMapModeLabelLayer.
                        HideRuntimeLabelsExcept(NativeActiveLabelKeys);
                }
                catch { }
                try
                {
                    ModClass.LogWarning(
                        "Hierarchical native labels failed: " +
                        error.Message);
                }
                catch { }
            }
            finally
            {
                if (_nativeDrawPassActive && !_nativeDrawPassUsingCache &&
                    published)
                {
                    _nativeDrawCacheValid = true;
                    _nativeDrawCacheCityLayer = IsCityLayer;
                    _nativeDrawCacheFocusKey = CurrentLabelFocusKey;
                }
                _nativeDrawPassActive = false;
                if (!_nativeDrawPassUsingCache)
                    ClearNativePublishBuffers();
                _nativeDrawPassUsingCache = false;
            }
        }

        private static bool CanReuseNativeLabels()
        {
            return _nativeDrawCacheValid &&
                   _nativeDrawCacheCityLayer == IsCityLayer &&
                   _nativeDrawCacheFocusKey == CurrentLabelFocusKey;
        }

        private static void InvalidateNativeLabelCache()
        {
            _nativeDrawCacheValid = false;
            NativeDrawMetaCache.Clear();
            CountyMetaCache.Clear();
        }

        private static void PublishNativeCityLabels()
        {
            NativeCityPublishEntries.Clear();
            foreach (NativeCityLabelEntry entry in NativeCityLabels.Values)
                NativeCityPublishEntries.Add(entry);
            NativeCityPublishEntries.Sort((pLeft, pRight) =>
                pLeft.City.id.CompareTo(pRight.City.id));
            NativeActiveLabelKeys.Clear();
            for (int index = 0; index < NativeCityPublishEntries.Count;
                 index++)
            {
                NativeCityLabelEntry entry = NativeCityPublishEntries[index];
                City city = entry.City;
                string displayName = entry.DisplayName ??
                    city?.data?.name?.Trim();
                if (string.IsNullOrWhiteSpace(displayName)) continue;
                Vector3 center = city.city_center;
                var placement = new HierarchicalVassalMapModeLabelPlacement
                {
                    Centroid = new Vector2(center.x, center.y),
                    Angle = 0f,
                    Size = HierarchicalVassalMapModeGeometry.
                        CalculateCityLabelSize(entry.LandArea)
                };
                long labelId = IsCityCountyLayer
                    ? NativeCityPublishEntries[index].City?.data?.id ?? city.id
                    : city.id;
                if (IsCityCountyLayer)
                {
                    foreach (KeyValuePair<long, NativeCityLabelEntry> pair in
                             NativeCityLabels)
                        if (ReferenceEquals(pair.Value, entry))
                            labelId = pair.Key;
                }
                string key = HierarchicalVassalMapModeLabelLayer.
                    GetNativeLabelKey(false, CurrentLabelFocusKey, labelId);
                HierarchicalVassalMapModeLabelLayer.ApplyRuntimeLabel(
                    key, displayName, placement, 0, false, city.kingdom,
                    city);
                NativeActiveLabelKeys.Add(key);
            }
            HierarchicalVassalMapModeLabelLayer.
                HideRuntimeLabelsExcept(NativeActiveLabelKeys);
        }

        private static void PublishNativeCountryLabels()
        {
            NativeCountryPublishEntries.Clear();
            foreach (NativeCountryLabelEntry entry in
                     NativeCountryLabels.Values)
                NativeCountryPublishEntries.Add(entry);
            NativeCountryPublishEntries.Sort((pLeft, pRight) => CompareKingdoms(
                pLeft.Kingdom, pRight.Kingdom));
            NativeActiveLabelKeys.Clear();
            for (int index = 0; index < NativeCountryPublishEntries.Count;
                 index++)
            {
                NativeCountryLabelEntry entry =
                    NativeCountryPublishEntries[index];
                Kingdom kingdom = entry.Kingdom;
                string displayName = GetMapDisplayName(kingdom)?.Trim();
                if (string.IsNullOrWhiteSpace(displayName)) continue;
                if (!TryBuildCountryPlacement(entry, displayName,
                        out HierarchicalVassalMapModeLabelPlacement placement,
                        out int gap)) continue;

                string key = HierarchicalVassalMapModeLabelLayer.
                    GetNativeLabelKey(true, CurrentLabelFocusKey, kingdom.id);
                HierarchicalVassalMapModeLabelLayer.ApplyRuntimeLabel(
                    key, HierarchicalVassalMapModeRules.FormatCountryLabel(
                        displayName, entry.HorizontalSpan), placement, gap,
                    true, kingdom, null);
                NativeActiveLabelKeys.Add(key);
            }
            HierarchicalVassalMapModeLabelLayer.
                HideRuntimeLabelsExcept(NativeActiveLabelKeys);
        }

        private static NativeCountryLabelEntry
            AcquireNativeCountryLabelEntry(Kingdom pKingdom)
        {
            NativeCountryLabelEntry entry = NativeCountryLabelPool.Count > 0
                ? NativeCountryLabelPool.Pop()
                : new NativeCountryLabelEntry();
            entry.Reset(pKingdom);
            return entry;
        }

        private static NativeCityLabelEntry AcquireNativeCityLabelEntry(
            City pCity)
        {
            NativeCityLabelEntry entry = NativeCityLabelPool.Count > 0
                ? NativeCityLabelPool.Pop()
                : new NativeCityLabelEntry();
            entry.Reset(pCity);
            return entry;
        }

        private static void ReleaseNativeDrawEntries()
        {
            foreach (NativeCountryLabelEntry entry in
                     NativeCountryLabels.Values)
            {
                entry.Reset(null);
                NativeCountryLabelPool.Push(entry);
            }
            NativeCountryLabels.Clear();
            foreach (NativeCityLabelEntry entry in NativeCityLabels.Values)
            {
                entry.Reset(null);
                NativeCityLabelPool.Push(entry);
            }
            NativeCityLabels.Clear();
        }

        private static void ClearNativePublishBuffers()
        {
            NativeCountryPublishEntries.Clear();
            NativeCityPublishEntries.Clear();
            NativeActiveLabelKeys.Clear();
        }

        private static bool TryBuildCountryPlacement(
            NativeCountryLabelEntry pEntry, string pDisplayName,
            out HierarchicalVassalMapModeLabelPlacement pPlacement,
            out int pGap)
        {
            pPlacement = default(HierarchicalVassalMapModeLabelPlacement);
            pGap = 0;
            bool hasMetrics = pEntry.Accumulator.TryBuild(
                out HierarchicalVassalZoneLabelMetrics metrics);
            if (pEntry.HasLandTileSnapshot &&
                !pEntry.Accumulator.HasLandTiles) hasMetrics = false;
            if (hasMetrics)
            {
                pEntry.HorizontalSpan = metrics.SpanX;
                var geometry = new HierarchicalVassalMapModeGeometryMetrics
                {
                    Area = metrics.LandArea,
                    Centroid = new Vector2((float)metrics.AnchorX,
                        (float)metrics.AnchorY),
                    SpanX = metrics.SpanX,
                    SpanY = metrics.SpanY,
                    Angle = metrics.Angle
                };
                pGap = HierarchicalVassalMapModeRules.
                    CalculateCountryLabelGapLevel(pDisplayName,
                        metrics.SpanX);
                pPlacement = new HierarchicalVassalMapModeLabelPlacement
                {
                    Centroid = geometry.Centroid,
                    Angle = geometry.Angle,
                    Size = HierarchicalVassalMapModeGeometry.
                        CalculateLabelSize(geometry, pDisplayName, pGap)
                };
                return true;
            }

            if (!TryResolveCountryFallback(pEntry,
                    out Vector2 fallback)) return false;
            pPlacement = new HierarchicalVassalMapModeLabelPlacement
            {
                Centroid = fallback,
                Angle = 0f,
                Size = HierarchicalVassalMapModeRules.
                    SmallTerritoryMinimumLabelSize
            };
            return true;
        }

        private static bool TryResolveCountryFallback(
            NativeCountryLabelEntry pEntry, out Vector2 pPosition)
        {
            pPosition = Vector2.zero;
            Kingdom kingdom = pEntry?.Kingdom;
            try
            {
                if (kingdom?.hasCapital() == true &&
                    kingdom.capital?.data != null &&
                    IsVisibleLand(kingdom.capital.getTile()))
                {
                    pPosition = kingdom.capital.city_center;
                    return true;
                }
                if (kingdom != null)
                {
                    foreach (City city in kingdom.getCities())
                    {
                        if (city?.data == null || city.isRekt() ||
                            !IsVisibleLand(city.getTile())) continue;
                        pPosition = city.city_center;
                        return true;
                    }
                }
            }
            catch { }
            if (!pEntry.HasFallbackZoneCenter) return false;
            pPosition = pEntry.FallbackZoneCenter;
            return true;
        }

        internal static bool NativeDrawPassActive => _nativeDrawPassActive;

        internal static void RebuildHierarchyIndex()
        {
            InvalidateNativeLabelCache();
            var rawSuzerainIds = new Dictionary<long, long>();
            KingdomIndex.Clear();
            try
            {
                if (World.world?.kingdoms != null)
                {
                    foreach (Kingdom kingdom in World.world.kingdoms)
                    {
                        if (!IsValidKingdom(kingdom)) continue;
                        KingdomIndex[kingdom.id] = kingdom;
                        rawSuzerainIds[kingdom.id] = SafeSuzerainId(kingdom);
                    }
                }
            }
            catch { }
            _hierarchyIndex = HierarchicalVassalHierarchyIndex.Build(
                rawSuzerainIds, State.IsRoot ? -1L : State.FocusKingdomId);
        }

        private static void EnsureHierarchyIndex()
        {
            if (_hierarchyIndex == null) RebuildHierarchyIndex();
        }

        internal static long CurrentLabelFocusKey =>
            IsCityLayer ? (IsCityCountryLayer ? -1L :
                IsCityRegionLayer ? CityAdministrationState.FocusKingdomId :
                IsCityCountyLayer ? CityAdministrationState.FocusCountyId :
                CityAdministrationState.FocusSeatCityId) :
            (State.IsRoot ? -1L : State.FocusKingdomId);

        internal static IReadOnlyList<Kingdom> LabelDiscoveryKingdoms
        {
            get
            {
                try
                {
                    return World.world?.kingdoms?.list ??
                           (IReadOnlyList<Kingdom>)Array.Empty<Kingdom>();
                }
                catch { return Array.Empty<Kingdom>(); }
            }
        }

        internal static IReadOnlyList<City> LabelDiscoveryCities(
            Kingdom pKingdom) => pKingdom?.cities;

        internal static IReadOnlyList<HierarchicalVassalMapLabelRegionSource>
            BuildCityAdministrationRegionSources(IReadOnlyList<City> pCities,
                long pKingdomId = -1L)
        {
            if (pKingdomId < 0L)
                return BuildGlobalDeJureRegionSources(pCities);
            var result = new List<HierarchicalVassalMapLabelRegionSource>();
            foreach (IGrouping<long, City> group in (pCities ??
                     Array.Empty<City>()).Where(city =>
                         IsValidKingdom(city?.kingdom) &&
                         (pKingdomId < 0L || city.kingdom.id == pKingdomId))
                     .GroupBy(city => city.kingdom.id))
            {
                Kingdom kingdom = GetKingdom(group.Key);
                foreach (RegionalGovernmentReadModel region in
                         RegionalGovernmentAggregationService.Build(kingdom))
                {
                    List<City> members = group.Where(city => region.MemberCityIds
                        .Contains(city.id)).ToList();
                    if (members.Count == 0) continue;
                    var zones = new List<TileZone>();
                    var zoneIds = new HashSet<int>();
                    foreach (City city in members)
                        foreach (TileZone zone in city.zones ??
                                 new List<TileZone>())
                            if (zone?.id >= 0 && zoneIds.Add(zone.id))
                                zones.Add(zone);
                    if (!zones.Any(zone => zone.tiles_with_ground > 0))
                        continue;
                    City seat = members.FirstOrDefault(city => city.id ==
                        region.SeatCityId) ?? members[0];
                    result.Add(new HierarchicalVassalMapLabelRegionSource(
                        region, seat, zones, zoneIds, true));
                }
            }
            return result.OrderBy(source => source.Region.SeatCityId).ToArray();
        }

        private static IReadOnlyList<HierarchicalVassalMapLabelRegionSource>
            BuildGlobalDeJureRegionSources(IReadOnlyList<City> pCities)
        {
            var available = (pCities ?? Array.Empty<City>())
                .Where(city => city?.data != null && !city.isRekt())
                .GroupBy(city => city.id)
                .ToDictionary(group => group.Key, group => group.First());
            var result = new List<HierarchicalVassalMapLabelRegionSource>();
            foreach (DeJureRegion legal in DeJureRegionStore.ActiveRegions())
            {
                if (legal?.MemberCityIds == null) continue;
                City seat = FindCity(legal.SeatCityId);
                if (seat?.data == null || seat.isRekt()) continue;
                var members = legal.MemberCityIds
                    .Select(id => available.TryGetValue(id, out City city)
                        ? city : FindCity(id))
                    .Where(city => city?.data != null && !city.isRekt() &&
                        !PeasantRebelBanditStrongholdService.IsStrongholdCity(
                            city))
                    .ToList();
                if (members.Count == 0) continue;
                var zones = new List<TileZone>();
                var zoneIds = new HashSet<int>();
                foreach (City city in members)
                    foreach (TileZone zone in city.zones ??
                             new List<TileZone>())
                        if (zone?.id >= 0 && zoneIds.Add(zone.id))
                            zones.Add(zone);
                if (!zones.Any(zone => zone.tiles_with_ground > 0)) continue;
                CustomCourtRuntime.RegionalTitles(seat.kingdom,
                    out string regionTitle, out _);
                var model = new RegionalGovernmentReadModel
                {
                    RegionId = legal.RegionId,
                    SeatCityId = legal.SeatCityId,
                    LegalSeatCityId = legal.SeatCityId,
                    EffectiveSeatCityId = legal.SeatCityId,
                    RegionName = DeJureRegionStore.ResolveDisplayName(legal),
                    RegionTitle = regionTitle,
                    MemberCityIds = members.Select(city => city.id).Distinct()
                        .ToList(),
                    LocalGovernmentCityIds = members.Select(city => city.id)
                        .Distinct().ToList()
                };
                result.Add(new HierarchicalVassalMapLabelRegionSource(
                    model, seat, zones, zoneIds, true));
            }
            return result.OrderBy(source => source.Region.SeatCityId)
                .ThenBy(source => source.Region.RegionId).ToArray();
        }

        internal static bool IsLabelDiscoveryKingdom(Kingdom pKingdom) =>
            IsValidKingdom(pKingdom);

        internal static long LabelDiscoverySuzerainId(Kingdom pKingdom) =>
            SafeSuzerainId(pKingdom);

        internal static bool TryGetCachedLabelHierarchy(
            out HierarchicalVassalHierarchyIndex pIndex)
        {
            pIndex = _hierarchyIndex;
            return pIndex != null;
        }

        internal static bool TryAcceptLabelHierarchy(
            HierarchicalVassalHierarchyIndex pIndex,
            Dictionary<long, Kingdom> pKingdoms, long pFocus)
        {
            long expectedHierarchyFocus = IsCityMemberLayer
                ? CityAdministrationState.FocusKingdomId : pFocus;
            if (pIndex == null || pKingdoms == null ||
                pFocus != CurrentLabelFocusKey ||
                pIndex.FocusKingdomId != expectedHierarchyFocus) return false;
            _hierarchyIndex = pIndex;
            KingdomIndex = pKingdoms;
            InvalidateNativeLabelCache();
            return true;
        }

        internal static HierarchicalVassalLabelDiscoveryJob
            BeginLabelSourceDiscovery(
                HierarchicalVassalLabelDiscoveryKind pKind, long pFocus)
        {
            long discoveryFocus = IsCityMemberLayer
                ? CityAdministrationState.FocusKingdomId : pFocus;
            return new HierarchicalVassalLabelDiscoveryJob(pKind,
                discoveryFocus);
        }

        internal static long ResolveVisibleRepresentativeId(
            Kingdom pPhysicalKingdom)
        {
            if (!IsValidKingdom(pPhysicalKingdom)) return -1L;
            EnsureHierarchyIndex();
            return _hierarchyIndex?.ResolveRepresentative(
                pPhysicalKingdom.id) ?? -1L;
        }

        internal static IReadOnlyList<City> GetVisibleCities()
        {
            EnsureHierarchyIndex();
            var cities = new List<City>();
            var seenCityIds = new HashSet<long>();
            try
            {
                if (World.world?.kingdoms == null) return cities;
                foreach (Kingdom container in World.world.kingdoms)
                {
                    if (!IsValidKingdom(container)) continue;
                    foreach (City city in container.getCities())
                    {
                        Kingdom currentOwner = city?.kingdom;
                        if (city?.data != null && !city.isRekt() &&
                            IsValidKingdom(currentOwner) &&
                            _hierarchyIndex.ResolveRepresentative(
                                currentOwner.id) >= 0L &&
                            seenCityIds.Add(city.id))
                            cities.Add(city);
                    }
                }
            }
            catch { }
            cities.Sort((pLeft, pRight) => pLeft.id.CompareTo(pRight.id));
            return cities;
        }

        internal static IReadOnlyList<City> GetAllLabelCities()
        {
            var cities = new List<City>();
            var seenCityIds = new HashSet<long>();
            try
            {
                if (World.world?.kingdoms == null) return cities;
                foreach (Kingdom container in World.world.kingdoms)
                {
                    if (!IsValidKingdom(container)) continue;
                    foreach (City city in container.getCities())
                    {
                        Kingdom currentOwner = city?.kingdom;
                        if (city?.data != null && !city.isRekt() &&
                            IsValidKingdom(currentOwner) &&
                            seenCityIds.Add(city.id))
                            cities.Add(city);
                    }
                }
            }
            catch { }
            cities.Sort((pLeft, pRight) => pLeft.id.CompareTo(pRight.id));
            return cities;
        }

        internal static IReadOnlyList<HierarchicalVassalMapLabelTerritorySource>
            GetVisibleLabelTerritories()
        {
            EnsureHierarchyIndex();
            return BuildLabelTerritories(_hierarchyIndex);
        }

        internal static IReadOnlyList<HierarchicalVassalMapLabelTerritorySource>
            GetRootLabelTerritories()
        {
            var rawSuzerainIds = new Dictionary<long, long>();
            try
            {
                if (World.world?.kingdoms == null)
                    return Array.Empty<
                        HierarchicalVassalMapLabelTerritorySource>();
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (!IsValidKingdom(kingdom)) continue;
                    rawSuzerainIds[kingdom.id] = SafeSuzerainId(kingdom);
                }
            }
            catch { }
            HierarchicalVassalHierarchyIndex rootIndex =
                HierarchicalVassalHierarchyIndex.Build(rawSuzerainIds, -1L);
            return BuildLabelTerritories(rootIndex);
        }

        private static IReadOnlyList<HierarchicalVassalMapLabelTerritorySource>
            BuildLabelTerritories(HierarchicalVassalHierarchyIndex pIndex)
        {
            var builders = new Dictionary<long, LabelTerritoryBuilder>();
            var seenCityIds = new HashSet<long>();
            try
            {
                if (World.world?.kingdoms == null)
                    return Array.Empty<
                        HierarchicalVassalMapLabelTerritorySource>();
                foreach (Kingdom container in World.world.kingdoms)
                {
                    if (!IsValidKingdom(container)) continue;
                    foreach (City city in container.getCities())
                    {
                        Kingdom currentOwner = city?.kingdom;
                        if (city?.data == null || city.isRekt() ||
                            city.zones == null ||
                            !seenCityIds.Add(city.id) ||
                            !IsValidKingdom(currentOwner)) continue;
                        long representativeId = pIndex.
                            ResolveRepresentative(currentOwner.id);
                        if (representativeId < 0L) continue;
                        if (!builders.TryGetValue(representativeId,
                                out LabelTerritoryBuilder builder))
                        {
                            Kingdom representative = GetKingdom(
                                representativeId);
                            if (!IsValidKingdom(representative)) continue;
                            builder = new LabelTerritoryBuilder(representative);
                            builders.Add(representativeId, builder);
                        }
                        builder.AddZones(city.zones);
                    }
                }
            }
            catch { }

            var result = new List<
                HierarchicalVassalMapLabelTerritorySource>(builders.Count);
            foreach (LabelTerritoryBuilder builder in builders.Values)
                result.Add(builder.Build());
            result.Sort((pLeft, pRight) => CompareKingdoms(
                pLeft.Kingdom, pRight.Kingdom));
            return result;
        }

        internal static string GetMapDisplayName(Kingdom pKingdom)
        {
            return SafeDisplayName(pKingdom);
        }

        internal static bool TryGetDisplayedRealm(TileZone pZone,
            out Kingdom pKingdom, out List<TileZone> pZones)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                pKingdom = null;
                pZones = null;
                Kingdom physical = pZone?.city?.kingdom;
                if (pZone == null || pZone.id < 0 ||
                    !IsValidKingdom(physical)) return false;
                EnsureHierarchyIndex();
                long representativeId = _hierarchyIndex?.
                    ResolveRepresentative(physical.id) ?? -1L;
                Kingdom representative = GetKingdom(representativeId);
                if (!IsValidKingdom(representative)) return false;

                var zones = new List<TileZone>();
                var seenZoneIds = new HashSet<int>();
                var seenCityIds = new HashSet<long>();
                try
                {
                    IReadOnlyList<long> memberIds = _hierarchyIndex.
                        GetRepresentativeMembers(representativeId);
                    for (int memberIndex = 0; memberIndex < memberIds.Count;
                         memberIndex++)
                    {
                        Kingdom container = GetKingdom(memberIds[memberIndex]);
                        if (!IsValidKingdom(container)) continue;
                        foreach (City city in container.getCities())
                        {
                            if (city?.data == null || city.isRekt() ||
                                city.zones == null ||
                                !seenCityIds.Add(city.id)) continue;
                            Kingdom currentOwner = city.kingdom;
                            if (!IsValidKingdom(currentOwner) ||
                                _hierarchyIndex.ResolveRepresentative(
                                    currentOwner.id) != representativeId)
                                continue;
                            for (int zoneIndex = 0;
                                 zoneIndex < city.zones.Count; zoneIndex++)
                            {
                                TileZone zone = city.zones[zoneIndex];
                                if (zone?.id >= 0 &&
                                    seenZoneIds.Add(zone.id) &&
                                    ContainsVisibleLand(zone)) zones.Add(zone);
                            }
                        }
                    }
                }
                catch { }
                pKingdom = representative;
                pZones = zones;
                return zones.Count > 0;
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.HierarchicalHoverIndex,
                    benchmark);
            }
        }

        internal static bool IsFocused => !State.IsRoot;

        internal static long FocusKingdomId => State.FocusKingdomId;

        public static bool HandleZoneClick(WorldTile pTile, string pPowerId)
        {
            if (!IsActive() && !string.Equals(pPowerId, POWER_ID,
                    StringComparison.Ordinal)) return false;
            TileZone clickedZone = pTile?.zone;
            if (clickedZone == null || clickedZone.id < 0)
                return ReturnToRootFromUnmappedClick();
            Kingdom physical = clickedZone.city?.kingdom;
            if (!IsValidKingdom(physical))
                return ReturnToRootFromUnmappedClick();

            if (IsCityLayer)
            {
                if (IsCityCountyLayer)
                {
                    CountyRecord county = CountyAdministrationService.FindForZone(
                        clickedZone.id);
                    if (county == null || county.CountyId !=
                        CityAdministrationState.FocusCountyId)
                    {
                        bool popped = CityAdministrationState.PopRegion();
                        if (popped) RefreshView();
                        return popped;
                    }
                    return TryInspectCity(pTile, pPowerId);
                }
                if (IsCityCountryLayer)
                {
                    EnsureHierarchyIndex();
                    long countryKingdomId = _hierarchyIndex?.
                        ResolveRepresentative(physical.id) ?? -1L;
                    Kingdom clickedKingdom = GetKingdom(countryKingdomId);
                    if (!IsValidKingdom(clickedKingdom)) return false;
                    if (!CityAdministrationState.PushKingdom(
                            clickedKingdom.id)) return false;
                    RefreshView();
                    return true;
                }
                City city = clickedZone.city;
                if (city?.data == null || city.isRekt())
                    return ReturnToRootFromUnmappedClick();
                if (IsCityGlobalRegionLayer)
                    return HandleGlobalCityRegionClick(city);
                if (IsCityMemberLayer)
                {
                    CountyRecord county = CountyAdministrationService.FindForZone(
                        clickedZone.id);
                    if (county != null && CityAdministrationState.PushCounty(
                            county.CountyId))
                    {
                        RefreshView();
                        return true;
                    }
                }
                if (IsCityRegionLayer)
                {
                    EnsureHierarchyIndex();
                    long clickedRepresentativeId = _hierarchyIndex?.
                        ResolveRepresentative(city.kingdom.id) ?? -1L;
                    if (clickedRepresentativeId != CityAdministrationState.
                            FocusKingdomId)
                    {
                        bool popped = CityAdministrationState.PopKingdom();
                        if (popped) RefreshView();
                        return popped;
                    }
                }
                bool mapped = RegionalGovernmentAggregationService.
                    TryFindRegion(city.kingdom, city.id,
                        out RegionalGovernmentReadModel clickedRegion);
                CityAdministrationMapClickAction action =
                    CityAdministrationMapModeRules.ResolveClick(
                        CityAdministrationState.IsRegionLevel,
                        CityAdministrationState.FocusSeatCityId,
                        mapped ? clickedRegion.SeatCityId : -1L,
                        mapped);
                if (action == CityAdministrationMapClickAction.FocusRegion)
                    return HandleCityRegionClick(city);
                if (action == CityAdministrationMapClickAction.InspectCity)
                    return TryInspectCity(pTile, pPowerId);
                if (action == CityAdministrationMapClickAction.PopToRegions)
                {
                    bool popped = CityAdministrationState.PopRegion();
                    if (!popped && IsCityRegionLayer)
                        popped = CityAdministrationState.PopKingdom();
                    if (popped) RefreshView();
                    return popped;
                }
                return false;
            }

            EnsureHierarchyIndex();
            long clickedKingdomId = _hierarchyIndex?.ResolveRepresentative(
                physical.id) ?? -1L;
            if (clickedKingdomId < 0L)
                return SwitchToPhysicalRealm(clickedZone, pTile, pPowerId);
            Kingdom clicked = GetKingdom(clickedKingdomId);
            if (!IsValidKingdom(clicked)) return false;

            if (!State.IsRoot && clicked.id == State.FocusKingdomId)
            {
                bool popped = State.PopFocus();
                if (popped) RefreshView();
                bool inspected = TryInspectKingdom(clicked, pTile, pPowerId);
                return inspected || popped;
            }

            bool hasChildren = _hierarchyIndex.GetDirectChildren(clicked.id).
                Count > 0;
            if (!State.IsRoot)
            {
                if (!ContainsKingdomId(_hierarchyIndex.GetDirectChildren(
                        State.FocusKingdomId), clicked.id)) return false;
                bool advanced = State.TryPushFocus(clicked.id,
                    (int)KingdomTitleService.GetTitle(clicked), hasChildren);
                if (advanced) RefreshView();
                bool inspected = TryInspectKingdom(clicked, pTile, pPowerId);
                return inspected || advanced;
            }

            if (!hasChildren)
                return TryInspectKingdom(clicked, pTile, pPowerId);
            if (!State.TryPushFocus(clicked.id,
                    (int)KingdomTitleService.GetTitle(clicked), true))
                return false;
            RefreshView();
            return true;
        }

        private static bool ReturnToRootFromUnmappedClick()
        {
            if (IsCityCountyLayer)
            {
                bool popped = CityAdministrationState.PopRegion();
                if (popped) RefreshView();
                return popped;
            }
            if (IsCityMemberLayer)
            {
                CityAdministrationState.PopRegion();
                RefreshView();
                return true;
            }
            if (IsCityRegionLayer)
            {
                CityAdministrationState.PopKingdom();
                RefreshView();
                return true;
            }
            if (IsCityLayer)
            {
                // An empty/water tile is not a navigation action. The native
                // zone redraw can clear the world-space labels, so keep the
                // current global state view and requeue its label batch.
                if (IsCityGlobalRegionLayer)
                {
                    HierarchicalVassalMapModeLabelLayer.RequestRefresh();
                    RequestNativeRedraw();
                    return true;
                }
                return false;
            }
            if (State.IsRoot) return false;
            State.Reset();
            RefreshView();
            return true;
        }

        private static bool HandleCityRegionClick(City pCity)
        {
            if (pCity?.kingdom == null ||
                !RegionalGovernmentAggregationService.TryFindRegion(
                    pCity.kingdom, pCity.id,
                    out RegionalGovernmentReadModel region) ||
                !CityAdministrationState.PushRegion(region.SeatCityId))
                return false;
            RefreshView();
            return true;
        }

        private static bool HandleGlobalCityRegionClick(City pCity)
        {
            if (pCity?.kingdom == null ||
                !RegionalGovernmentAggregationService.TryFindRegion(
                    pCity.kingdom, pCity.id,
                    out RegionalGovernmentReadModel region)) return false;
            if (!CityAdministrationState.PushKingdom(pCity.kingdom.id))
                return false;
            RefreshView();
            return true;
        }

        internal static bool IsCityInFocusedRegion(City pCity)
        {
            if (pCity?.kingdom == null ||
                CityAdministrationState.IsRegionLevel) return false;
            return RegionalGovernmentAggregationService.TryFindRegion(
                pCity.kingdom, pCity.id,
                out RegionalGovernmentReadModel region) &&
                region.SeatCityId == CityAdministrationState.FocusSeatCityId;
        }

        private static bool IsCityInFocusedKingdom(City pCity)
        {
            if (pCity?.kingdom == null || IsCityCountryLayer) return false;
            EnsureHierarchyIndex();
            return _hierarchyIndex?.ResolveRepresentative(
                pCity.kingdom.id) == CityAdministrationState.FocusKingdomId;
        }

        private static IMetaObject GetCityRegionMeta(
            RegionalGovernmentReadModel pRegion, City pSeatCity)
        {
            if (pRegion == null || pSeatCity?.data == null) return null;
            long seatCityId = pRegion.SeatCityId >= 0L
                ? pRegion.SeatCityId : pSeatCity.data.id;
            string name = RegionalGovernmentRules.AdministrativeLabel(
                pRegion.RegionName, pRegion.RegionTitle);
            if (!CityRegionMetaCache.TryGetValue(seatCityId,
                    out AWMapModeMetaObject meta) || meta == null)
            {
                ColorAsset color = ResolveDeJureRegionColor(pRegion.RegionId,
                    seatCityId);
                color?.initColor();
                meta = new AWMapModeMetaObject(seatCityId, name,
                    AWMapModeMetaTypes.HierarchicalVassal, color);
                CityRegionMetaCache[seatCityId] = meta;
            }
            else if (meta.data != null && meta.data.name != name)
                meta.data.name = name ?? string.Empty;
            return meta;
        }

        private static ColorAsset ResolveDeJureRegionColor(long pRegionId,
            long pSeatCityId)
        {
            try
            {
                ColorLibrary palette = AssetManager.kingdom_colors_library;
                if (palette?.list != null && palette.list.Count > 0)
                {
                    long normalized = pRegionId >= 0L ? pRegionId : pSeatCityId;
                    if (normalized < 0L) normalized = -normalized;
                    int index = (int)(normalized % palette.list.Count);
                    ColorAsset color = palette.getColorByIndex(index);
                    if (color != null) return color;
                }
            }
            catch { }
            return ColorAsset.tryMakeNewColorAsset(
                CityAdministrationMapModeRules.RegionColorHex(pSeatCityId));
        }

        private static bool SwitchToPhysicalRealm(TileZone pClickedZone,
            WorldTile pTile, string pPowerId)
        {
            if (State.IsRoot) return false;
            Kingdom physicalKingdom = pClickedZone?.city?.kingdom;
            if (!IsValidKingdom(physicalKingdom))
                return ReturnToRootFromUnmappedClick();
            Kingdom root = ResolveHierarchyRoot(physicalKingdom);
            if (!IsValidKingdom(root))
                return ReturnToRootFromUnmappedClick();

            State.Reset();
            RebuildHierarchyIndex();
            bool hasChildren = _hierarchyIndex.GetDirectChildren(root.id).
                Count > 0;
            bool focused = State.TryPushFocus(root.id,
                (int)KingdomTitleService.GetTitle(root), hasChildren);
            RefreshView();
            if (focused) return true;
            TryInspectKingdom(root, pTile, pPowerId);
            return true;
        }

        private static Kingdom ResolveHierarchyRoot(Kingdom pKingdom)
        {
            if (!IsValidKingdom(pKingdom)) return null;
            Kingdom current = pKingdom;
            var visited = new HashSet<long>();
            while (IsValidKingdom(current) && visited.Add(current.id))
            {
                Kingdom suzerain = GetKingdom(SafeSuzerainId(current));
                if (!IsValidKingdom(suzerain)) return current;
                current = suzerain;
            }
            return pKingdom;
        }

        private static bool ContainsKingdomId(IReadOnlyList<long> pKingdomIds,
            long pKingdomId)
        {
            if (pKingdomIds == null) return false;
            for (int index = 0; index < pKingdomIds.Count; index++)
                if (pKingdomIds[index] == pKingdomId) return true;
            return false;
        }

        public static void DirtyMap()
        {
            InvalidateNativeLabelCache();
            RequestNativeRedraw();
        }

        public static void MarkCityDirty(City pCity)
        {
            if (pCity?.data == null) return;
            RegionalGovernmentAggregationService.Invalidate(pCity.kingdom);
            _nativeDrawCacheValid = false;
            InvalidateCityMeta(pCity);
            HierarchicalVassalMapModeLabelLayer.MarkCityDirty(pCity);
            RequestNativeRedraw();
        }

        internal static void MarkCityGeometryDirty(City pCity)
        {
            if (pCity?.data == null) return;
            RegionalGovernmentAggregationService.Invalidate(pCity.kingdom);
            _nativeDrawCacheValid = false;
            InvalidateCityMeta(pCity);
            // City.addZone already invalidates WorldBox's native zone layer.
            // AW3 only accumulates the changed zone for label placement.
            HierarchicalVassalMapModeLabelLayer.MarkCityGeometryDirty(pCity);
        }

        internal static void MarkCityZoneGeometryDirty(City pCity, TileZone pZone)
        {
            if (pCity?.data == null || pZone == null || pZone.id < 0) return;
            RegionalGovernmentAggregationService.Invalidate(pCity.kingdom);
            _nativeDrawCacheValid = false;
            InvalidateZoneMeta(pZone);
            HierarchicalVassalMapModeLabelLayer.MarkCityZoneGeometryDirty(
                pCity, pZone);
        }

        internal static void RemoveCity(City pCity,
            Kingdom pFormerKingdom = null)
        {
            if (pCity == null) return;
            RegionalGovernmentAggregationService.Invalidate(pCity.kingdom);
            RegionalGovernmentAggregationService.Invalidate(pFormerKingdom);
            _nativeDrawCacheValid = false;
            InvalidateCityMeta(pCity);
            HierarchicalVassalMapModeLabelLayer.EvictCity(pCity.id);
            HierarchicalVassalMapModeLabelLayer.EvictNativeCity(pCity.id);
            if (pCity.kingdom != null)
                HierarchicalVassalMapModeLabelLayer.MarkKingdomDirty(
                    pCity.kingdom);
            if (pFormerKingdom != null && pFormerKingdom != pCity.kingdom)
                HierarchicalVassalMapModeLabelLayer.MarkKingdomDirty(
                    pFormerKingdom);
        }

        public static void MarkKingdomDirty(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            _nativeDrawCacheValid = false;
            HierarchicalVassalMapModeLabelLayer.MarkKingdomDirty(pKingdom);
            RequestNativeRedraw();
        }

        public static void MarkHierarchyDirty(params Kingdom[] pAffectedKingdoms)
        {
            _hierarchyIndex = null;
            RegionalGovernmentAggregationService.Clear();
            InvalidateNativeLabelCache();
            HierarchicalVassalMapModeLabelLayer.MarkHierarchyDirty();
            RequestNativeRedraw();
        }

        internal static void MarkCityOwnershipChanged(City pCity,
            Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (pCity?.data == null) return;
            RegionalGovernmentAggregationService.Invalidate(pOldKingdom);
            RegionalGovernmentAggregationService.Invalidate(pNewKingdom);
            long oldKingdomId = pOldKingdom?.id ?? -1L;
            long newKingdomId = pNewKingdom?.id ?? -1L;
            if (oldKingdomId == newKingdomId) return;
            if (!HierarchicalVassalMapModeRules.ShouldUseLocalOwnershipRefresh(
                    oldKingdomId, newKingdomId)) return;
            MarkCityDirty(pCity);
        }

        public static void Reset()
        {
            State.Reset();
            CityAdministrationState.Reset();
            CityRegionMetaCache.Clear();
            _hierarchyIndex = null;
            KingdomIndex.Clear();
            InvalidateNativeLabelCache();
            NativeLandTileCache.Clear();
            ClearNativePublishBuffers();
            ReleaseNativeDrawEntries();
            NativeCountryLabelPool.Clear();
            NativeCityLabelPool.Clear();
            _nativeDrawPassActive = false;
            _nativeDrawPassUsingCache = false;
            _nativeDrawCacheValid = false;
            _nativeDrawCacheFocusKey = long.MinValue;
            HierarchicalVassalMapModeLabelLayer.Reset();
        }

        internal static void OnKingdomDestroying(Kingdom pKingdom)
        {
            long pKingdomId = pKingdom?.id ?? -1L;
            if (pKingdomId < 0L) return;
            bool wasFocused = false;
            IReadOnlyList<long> breadcrumbs = State.Breadcrumbs;
            for (int index = 0; index < breadcrumbs.Count; index++)
            {
                if (breadcrumbs[index] != pKingdomId) continue;
                wasFocused = true;
                break;
            }
            if (wasFocused) State.Reset();
            KingdomIndex.Remove(pKingdomId);
            _hierarchyIndex = null;
            InvalidateNativeLabelCache();
            try
            {
                if (pKingdom?.cities != null)
                    for (int index = 0; index < pKingdom.cities.Count; index++)
                        if (pKingdom.cities[index] != null)
                        {
                            HierarchicalVassalMapModeLabelLayer.EvictCity(
                                pKingdom.cities[index].id);
                            HierarchicalVassalMapModeLabelLayer.
                                EvictNativeCity(pKingdom.cities[index].id);
                        }
            }
            catch { }
            HierarchicalVassalMapModeLabelLayer.EvictKingdom(pKingdomId);
            HierarchicalVassalMapModeLabelLayer.
                EvictNativeKingdom(pKingdomId);
            RequestNativeRedraw();
        }

        private static void RefreshView()
        {
            _hierarchyIndex = null;
            InvalidateNativeLabelCache();
            HierarchicalVassalMapModeLabelLayer.
                HideRuntimeLabelsExcept(null);
            RequestNativeRedraw();
        }

        private static void RequestNativeRedraw()
        {
            try
            {
                World.world?.zone_calculator?.setDrawnZonesDirty();
            }
            catch { }
        }

        private static void InvalidateCityMeta(City pCity)
        {
            if (pCity?.zones == null) return;
            for (int index = 0; index < pCity.zones.Count; index++)
            {
                TileZone zone = pCity.zones[index];
                if (zone?.id >= 0)
                {
                    NativeDrawMetaCache.Remove(zone.id);
                    NativeLandTileCache.Remove(zone.id);
                }
            }
        }

        private static void InvalidateZoneMeta(TileZone pZone)
        {
            if (pZone == null || pZone.id < 0) return;
            NativeDrawMetaCache.Remove(pZone.id);
            NativeLandTileCache.Remove(pZone.id);
        }

        internal static bool IsVisibleLand(WorldTile pTile)
        {
            TileTypeBase type = pTile?.Type;
            return pTile?.data != null && type != null && type.ground &&
                   !type.liquid && !type.ocean && !type.lava;
        }

        internal static bool ContainsVisibleLand(TileZone pZone)
        {
            return pZone != null && pZone.tiles_with_ground > 0;
        }

        private static IReadOnlyList<HierarchicalVassalLabelTile>
            GetNativeLandTiles(TileZone pZone)
        {
            if (pZone == null || pZone.id < 0)
                return Array.Empty<HierarchicalVassalLabelTile>();
            if (NativeLandTileCache.TryGetValue(pZone.id,
                    out List<HierarchicalVassalLabelTile> cached))
                return cached;

            var result = new List<HierarchicalVassalLabelTile>();
            WorldTile[] tiles = pZone.tiles;
            if (tiles != null)
            {
                for (int index = 0; index < tiles.Length; index++)
                {
                    WorldTile tile = tiles[index];
                    if (IsVisibleLand(tile))
                        result.Add(new HierarchicalVassalLabelTile(
                            tile.x, tile.y));
                }
            }
            NativeLandTileCache[pZone.id] = result;
            return result;
        }

        private static bool IsValidKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   pKingdom.isCiv() && !pKingdom.isNeutral();
        }

        private static bool TryInspectCity(WorldTile pTile, string pPowerId)
        {
            try
            {
                City city = pTile?.zone?.city;
                if (city?.data != null && !city.isRekt() &&
                    TrySelectAndInspect(city, "city")) return true;
                return ActionLibrary.inspectCity(pTile, pPowerId);
            }
            catch { return false; }
        }

        private static bool TryInspectKingdom(Kingdom pKingdom,
            WorldTile pTile, string pPowerId)
        {
            try
            {
                if (pKingdom?.data != null && !pKingdom.isRekt() &&
                    !pKingdom.isNeutral() &&
                    TrySelectAndInspect(pKingdom, "kingdom")) return true;
                return ActionLibrary.inspectKingdom(pTile, pPowerId);
            }
            catch { return false; }
        }

        private static bool TrySelectAndInspect(object pNanoObject,
            string pAssetField)
        {
            if (pNanoObject == null || string.IsNullOrWhiteSpace(pAssetField))
                return false;
            try
            {
                Type libraryType = typeof(Kingdom).Assembly.GetType(
                    "MetaTypeLibrary");
                object asset = libraryType?.GetField(pAssetField,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static)?.GetValue(null);
                if (asset == null) return false;
                Type assetType = asset.GetType();
                if (!SelectAndInspectByAssetType.TryGetValue(assetType,
                        out SelectAndInspectInvoker invoke))
                {
                    invoke = ResolveSelectAndInspectInvoker(assetType);
                    SelectAndInspectByAssetType[assetType] = invoke;
                }
                return invoke != null && invoke(asset, pNanoObject);
            }
            catch { return false; }
        }

        private static SelectAndInspectInvoker ResolveSelectAndInspectInvoker(
            Type pAssetType)
        {
            System.Reflection.MethodInfo method = pAssetType?.GetMethod(
                "selectAndInspect");
            if (method == null) return null;
            int count = method.GetParameters().Length;
            if (count == 4)
            {
                var call = ReflectionDelegateFactory.TryCreate<
                    Action<object, object, bool, bool, bool>>(method);
                if (call != null)
                    return (asset, value) =>
                    {
                        call(asset, value, false, false, false);
                        return true;
                    };
                return (asset, value) =>
                {
                    method.Invoke(asset, new object[]
                    {
                        value, false, false, false
                    });
                    return true;
                };
            }
            if (count == 3)
            {
                var call = ReflectionDelegateFactory.TryCreate<
                    Action<object, object, bool, bool>>(method);
                if (call != null)
                    return (asset, value) =>
                    {
                        call(asset, value, false, false);
                        return true;
                    };
                return (asset, value) =>
                {
                    method.Invoke(asset, new object[]
                    {
                        value, false, false
                    });
                    return true;
                };
            }
            if (count != 1) return null;
            var single = ReflectionDelegateFactory.TryCreate<
                Action<object, object>>(method);
            if (single != null)
                return (asset, value) =>
                {
                    single(asset, value);
                    return true;
                };
            return (asset, value) =>
            {
                method.Invoke(asset, new[] { value });
                return true;
            };
        }

        private static Kingdom GetKingdom(long pKingdomId)
        {
            if (pKingdomId < 0L || World.world?.kingdoms == null) return null;
            if (KingdomIndex.TryGetValue(pKingdomId, out Kingdom indexed) &&
                IsValidKingdom(indexed)) return indexed;
            try
            {
                foreach (Kingdom kingdom in World.world.kingdoms)
                    if (kingdom?.id == pKingdomId) return kingdom;
            }
            catch { }
            return null;
        }

        private static City FindCity(Kingdom pKingdom, long pCityId)
        {
            if (pKingdom?.data == null || pCityId < 0L) return null;
            try
            {
                return pKingdom.getCities()?.FirstOrDefault(city =>
                    city?.data?.id == pCityId && !city.isRekt());
            }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0L || World.world?.cities == null) return null;
            try
            {
                City city = World.world.cities.get(pCityId);
                return city?.data != null && !city.isRekt() ? city : null;
            }
            catch { return null; }
        }

        private static long SafeSuzerainId(Kingdom pKingdom)
        {
            try { return VassalService.GetSuzerainId(pKingdom); }
            catch { return -1L; }
        }

        private static string SafeDisplayName(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return string.Empty;
            try
            {
                string projected = RulerAppellationService.
                    GetProjectedStateName(pKingdom);
                if (!string.IsNullOrWhiteSpace(projected)) return projected;
            }
            catch { }
            return pKingdom.name ?? string.Empty;
        }

        private static int CompareKingdoms(Kingdom pLeft, Kingdom pRight)
        {
            int titleOrder = HierarchicalVassalMapModeRules.CompareTitles(
                KingdomTitleService.GetTitle(pLeft),
                KingdomTitleService.GetTitle(pRight));
            return titleOrder != 0
                ? titleOrder
                : pLeft.id.CompareTo(pRight.id);
        }

        private sealed class LabelTerritoryBuilder
        {
            private readonly Kingdom _kingdom;
            private readonly List<TileZone> _zones = new List<TileZone>();
            private readonly HashSet<int> _zoneIds = new HashSet<int>();

            internal LabelTerritoryBuilder(Kingdom pKingdom)
            {
                _kingdom = pKingdom;
            }

            internal void AddZones(IReadOnlyList<TileZone> pZones)
            {
                if (pZones == null) return;
                for (int index = 0; index < pZones.Count; index++)
                {
                    TileZone zone = pZones[index];
                    if (zone?.id >= 0 && _zoneIds.Add(zone.id))
                        _zones.Add(zone);
                }
            }

            internal HierarchicalVassalMapLabelTerritorySource Build()
            {
                _zones.Sort((pLeft, pRight) =>
                    pLeft.id.CompareTo(pRight.id));
                return new HierarchicalVassalMapLabelTerritorySource(
                    _kingdom, _zones);
            }
        }

        private sealed class NativeCountryLabelEntry
        {
            internal Kingdom Kingdom { get; private set; }
            internal readonly HierarchicalVassalZoneLabelAccumulator
                Accumulator = new HierarchicalVassalZoneLabelAccumulator();
            internal bool HasLandTileSnapshot;
            internal bool HasFallbackZoneCenter;
            internal Vector2 FallbackZoneCenter;
            internal int HorizontalSpan { get; set; } = 1;

            internal void Reset(Kingdom pKingdom)
            {
                Kingdom = pKingdom;
                Accumulator.Reset();
                HasLandTileSnapshot = false;
                HasFallbackZoneCenter = false;
                FallbackZoneCenter = Vector2.zero;
                HorizontalSpan = 1;
                try
                {
                    WorldTile capital = pKingdom?.hasCapital() == true
                        ? pKingdom.capital?.getTile()
                        : null;
                    if (IsVisibleLand(capital))
                        Accumulator.SetCapital(capital.x, capital.y);
                }
                catch { }
            }

            internal void Add(TileZone pZone)
            {
                WorldTile center = pZone?.centerTile;
                if (center == null) return;
                Vector3 position = center.posV;
                if (!HasFallbackZoneCenter)
                {
                    IReadOnlyList<HierarchicalVassalLabelTile> landTiles =
                        GetNativeLandTiles(pZone);
                    if (landTiles.Count > 0)
                    {
                        HasFallbackZoneCenter = true;
                        FallbackZoneCenter = new Vector2(
                            landTiles[0].X, landTiles[0].Y);
                    }
                }
                HasLandTileSnapshot = true;
                Accumulator.Add(pZone.id, position.x, position.y,
                    pZone.tiles_with_ground, GetNativeLandTiles(pZone));
            }
        }

        private sealed class NativeCityLabelEntry
        {
            private readonly HashSet<int> _zoneIds = new HashSet<int>();
            internal City City { get; private set; }
            internal string DisplayName { get; private set; }
            internal int LandArea { get; private set; }

            internal void Reset(City pCity)
            {
                City = pCity;
                DisplayName = null;
                _zoneIds.Clear();
                LandArea = 0;
            }

            internal void SetDisplayName(string pName)
            {
                DisplayName = pName;
            }

            internal void Add(TileZone pZone)
            {
                if (pZone == null || pZone.id < 0 ||
                    !_zoneIds.Add(pZone.id)) return;
                LandArea += Math.Max(0, pZone.tiles_with_ground);
            }
        }

        private readonly struct NativeZoneMetaCacheEntry
        {
            internal readonly TileZone Zone;
            internal readonly City City;
            internal readonly Kingdom PhysicalKingdom;
            internal readonly IMetaObject Meta;

            internal NativeZoneMetaCacheEntry(TileZone pZone, City pCity,
                Kingdom pPhysicalKingdom, IMetaObject pMeta)
            {
                Zone = pZone;
                City = pCity;
                PhysicalKingdom = pPhysicalKingdom;
                Meta = pMeta;
            }
        }
    }
}
