using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AncientWarfare3.core.policy
{
    internal enum HierarchicalVassalLabelDiscoveryKind
    {
        ActiveView,
        RootCountries,
        RootCities
    }

    internal enum HierarchicalVassalLabelDiscoveryStatus
    {
        Incomplete,
        Complete,
        Failed,
        RestartRequired,
        Cancelled
    }

    internal sealed class HierarchicalVassalLabelDiscoveryResult
    {
        internal readonly IReadOnlyList<City> Cities;
        internal readonly IReadOnlyList<HierarchicalVassalMapLabelCitySource>
            CitySources;
        internal readonly IReadOnlyList<
            HierarchicalVassalMapLabelTerritorySource> Territories;
        internal readonly HierarchicalVassalHierarchyIndex Hierarchy;
        internal readonly Dictionary<long, Kingdom> Kingdoms;

        internal HierarchicalVassalLabelDiscoveryResult(
            IReadOnlyList<City> pCities,
            IReadOnlyList<HierarchicalVassalMapLabelTerritorySource>
                pTerritories,
            IReadOnlyList<HierarchicalVassalMapLabelCitySource> pCitySources,
            HierarchicalVassalHierarchyIndex pHierarchy,
            Dictionary<long, Kingdom> pKingdoms)
        {
            Cities = pCities ?? Array.Empty<City>();
            CitySources = pCitySources ?? Array.Empty<
                HierarchicalVassalMapLabelCitySource>();
            Territories = pTerritories ?? Array.Empty<
                HierarchicalVassalMapLabelTerritorySource>();
            Hierarchy = pHierarchy;
            Kingdoms = pKingdoms;
        }
    }

    /// <summary>
    /// Main-thread cursor over live kingdom/city/zone collections. It never
    /// hands WorldBox objects to a worker and never exposes partial results.
    /// </summary>
    internal sealed class HierarchicalVassalLabelDiscoveryJob
    {
        private readonly HierarchicalVassalLabelDiscoveryKind _kind;
        private readonly long _focus;
        private readonly IReadOnlyList<Kingdom> _kingdoms;
        private readonly int _initialKingdomCount;
        private readonly List<long> _seenKingdomIds = new List<long>();
        private readonly Dictionary<long, long> _rawSuzerainIds =
            new Dictionary<long, long>();
        private readonly Dictionary<long, Kingdom> _kingdomById =
            new Dictionary<long, Kingdom>();
        private readonly List<City> _cities = new List<City>();
        private readonly Dictionary<long, List<TileZone>> _territoryZones =
            new Dictionary<long, List<TileZone>>();
        private readonly List<long> _territoryOrder = new List<long>();
        private readonly Dictionary<long, HashSet<int>> _territoryZoneIds =
            new Dictionary<long, HashSet<int>>();
        private readonly List<HierarchicalVassalMapLabelCitySource>
            _citySources = new List<HierarchicalVassalMapLabelCitySource>();
        private readonly List<long> _capturedCityIds = new List<long>();
        private readonly List<int> _capturedZoneIds = new List<int>();
        private readonly HashSet<long> _territoryVisible =
            new HashSet<long>();
        private readonly List<HierarchicalVassalMapLabelTerritorySource>
            _finalizedTerritories =
                new List<HierarchicalVassalMapLabelTerritorySource>();
        private HierarchicalVassalHierarchyIndex _hierarchy;
        private Task<HierarchicalVassalHierarchyIndex> _hierarchyBuildTask;
        private readonly CancellationTokenSource _cancellation =
            new CancellationTokenSource();
        private bool _hierarchySnapshotCaptured;
        private int _kingdomIndex;
        private int _validationIndex;
        private int _cityIndex;
        private City _pendingCity;
        private Kingdom _cityContainer;
        private IReadOnlyList<City> _containerCities;
        private int _containerCityCount;
        private int _containerCityValidationIndex;
        private long _pendingRepresentative;
        private bool _pendingVisible;
        private List<TileZone> _pendingCityZones;
        private HashSet<int> _pendingCityZoneIds;
        private IReadOnlyList<TileZone> _pendingZoneCollection;
        private int _pendingZoneCount;
        private int _pendingZoneValidationIndex;
        private HierarchicalVassalLabelDiscoveryStatus _status;
        private HierarchicalVassalLabelDiscoveryResult _result;
        private int _finalizationIndex;
        private bool _kingdomMapCaptured;
        private bool _entityContainerActive;

        internal HierarchicalVassalLabelDiscoveryJob(
            HierarchicalVassalLabelDiscoveryKind pKind, long pFocus)
        {
            _kind = pKind;
            _focus = pFocus;
            _kingdoms = HierarchicalVassalMapModeService.
                LabelDiscoveryKingdoms;
            _initialKingdomCount = _kingdoms?.Count ?? 0;
            _status = _kingdoms == null
                ? HierarchicalVassalLabelDiscoveryStatus.Failed
                : HierarchicalVassalLabelDiscoveryStatus.Incomplete;
            if (_kind == HierarchicalVassalLabelDiscoveryKind.ActiveView &&
                HierarchicalVassalMapModeService.TryGetCachedLabelHierarchy(
                    out HierarchicalVassalHierarchyIndex cached))
                _hierarchy = cached;
        }

        internal HierarchicalVassalLabelDiscoveryStatus Status => _status;

        internal HierarchicalVassalLabelDiscoveryResult Result => _result;

        internal int KingdomsConsumedForDiagnostics => _kingdomIndex;

        internal bool IsComplete => _status ==
            HierarchicalVassalLabelDiscoveryStatus.Complete;

        internal bool WorkerStoppedForDiagnostics =>
            _hierarchyBuildTask == null || _hierarchyBuildTask.IsCompleted;

        internal void Cancel()
        {
            if (_status == HierarchicalVassalLabelDiscoveryStatus.Complete ||
                _status == HierarchicalVassalLabelDiscoveryStatus.Cancelled)
                return;
            _cancellation.Cancel();
            _status = HierarchicalVassalLabelDiscoveryStatus.Cancelled;
            _result = null;
        }

        internal void Advance(int pKingdomBudget, int pCityBudget,
            int pZoneBudget)
        {
            if (_status != HierarchicalVassalLabelDiscoveryStatus.Incomplete)
                return;
            try
            {
                if (!ValidateKingdomCollection(Math.Max(1, pKingdomBudget)))
                    return;
                if (_kind != HierarchicalVassalLabelDiscoveryKind.RootCities &&
                    _hierarchy == null && !_hierarchySnapshotCaptured)
                {
                    CaptureHierarchy(Math.Max(1, pKingdomBudget));
                    if (!_hierarchySnapshotCaptured) return;
                    // Let the value-only worker run at least until the next
                    // frame; this keeps hierarchy construction off the live
                    // discovery slice even when the task completes quickly.
                    return;
                }
                if (_kind != HierarchicalVassalLabelDiscoveryKind.RootCities &&
                    _hierarchy == null)
                {
                    if (!PollHierarchyBuild()) return;
                }
                else if (_kind != HierarchicalVassalLabelDiscoveryKind.RootCities &&
                         !_kingdomMapCaptured)
                {
                    CaptureKingdomMap(Math.Max(1, pKingdomBudget));
                    if (!_kingdomMapCaptured) return;
                    _kingdomIndex = 0;
                }
                CaptureEntities(Math.Max(1, pKingdomBudget),
                    Math.Max(1, pCityBudget),
                    Math.Max(1, pZoneBudget));
            }
            catch
            {
                _status = HierarchicalVassalLabelDiscoveryStatus.Failed;
                _result = null;
            }
        }

        private bool ValidateKingdomCollection(int pBudget)
        {
            if (_kingdoms == null || _kingdoms.Count != _initialKingdomCount)
            {
                _status = HierarchicalVassalLabelDiscoveryStatus.
                    RestartRequired;
                return false;
            }
            int checkedCount = _seenKingdomIds.Count;
            int checks = Math.Max(1, pBudget);
            while (checkedCount > 0 && checks-- > 0)
            {
                if (_validationIndex >= checkedCount) _validationIndex = 0;
                Kingdom kingdom = _kingdoms[_validationIndex];
                long kingdomId = kingdom?.id ?? -1L;
                if (kingdomId != _seenKingdomIds[_validationIndex])
                {
                    _status = HierarchicalVassalLabelDiscoveryStatus.
                        RestartRequired;
                    return false;
                }
                _validationIndex++;
            }
            return true;
        }

        private void CaptureHierarchy(int pBudget)
        {
            while (pBudget-- > 0 && _kingdomIndex < _initialKingdomCount)
            {
                Kingdom kingdom = _kingdoms[_kingdomIndex];
                _seenKingdomIds.Add(kingdom?.id ?? -1L);
                if (kingdom != null && HierarchicalVassalMapModeService.
                    IsLabelDiscoveryKingdom(kingdom))
                {
                    _kingdomById[kingdom.id] = kingdom;
                    _rawSuzerainIds[kingdom.id] =
                        HierarchicalVassalMapModeService.
                            LabelDiscoverySuzerainId(kingdom);
                }
                _kingdomIndex++;
            }
            if (_kingdomIndex < _initialKingdomCount) return;
            var snapshot = new Dictionary<long, long>(_rawSuzerainIds);
            long focus = _kind ==
                HierarchicalVassalLabelDiscoveryKind.RootCountries ||
                _kind == HierarchicalVassalLabelDiscoveryKind.RootCities
                ? -1L : _focus;
            CancellationToken cancellationToken = _cancellation.Token;
            _hierarchyBuildTask = Task.Run(() =>
                HierarchicalVassalHierarchyIndex.Build(snapshot, focus,
                    cancellationToken), cancellationToken);
            _hierarchySnapshotCaptured = true;
        }

        private bool PollHierarchyBuild()
        {
            if (_hierarchyBuildTask == null) return false;
            if (!_hierarchyBuildTask.IsCompleted) return false;
            try
            {
                if (_hierarchyBuildTask.IsCanceled ||
                    _hierarchyBuildTask.IsFaulted)
                {
                    _status = _cancellation.IsCancellationRequested
                        ? HierarchicalVassalLabelDiscoveryStatus.Cancelled
                        : HierarchicalVassalLabelDiscoveryStatus.Failed;
                    return false;
                }
                _hierarchy = _hierarchyBuildTask.GetAwaiter().GetResult();
            }
            catch
            {
                _status = HierarchicalVassalLabelDiscoveryStatus.Failed;
                return false;
            }
            _hierarchyBuildTask = null;
            _kingdomMapCaptured = true;
            _kingdomIndex = 0;
            _cityIndex = 0;
            return _hierarchy != null;
        }

        private void CaptureKingdomMap(int pBudget)
        {
            while (pBudget-- > 0 && _kingdomIndex < _initialKingdomCount)
            {
                Kingdom kingdom = _kingdoms[_kingdomIndex++];
                _seenKingdomIds.Add(kingdom?.id ?? -1L);
                if (kingdom != null && HierarchicalVassalMapModeService.
                    IsLabelDiscoveryKingdom(kingdom))
                    _kingdomById[kingdom.id] = kingdom;
            }
            if (_kingdomIndex >= _initialKingdomCount)
                _kingdomMapCaptured = true;
        }

        private void CaptureEntities(int pKingdomBudget, int pCityBudget,
            int pZoneBudget)
        {
            while (pCityBudget > 0 && _kingdomIndex <
                   _initialKingdomCount)
            {
                Kingdom container = _kingdoms[_kingdomIndex];
                if (!_entityContainerActive)
                {
                    if (pKingdomBudget <= 0) return;
                    pKingdomBudget--;
                    _entityContainerActive = true;
                }
                long containerId = container?.id ?? -1L;
                if (_seenKingdomIds.Count <= _kingdomIndex)
                    _seenKingdomIds.Add(containerId);
                else if (containerId != _seenKingdomIds[_kingdomIndex])
                {
                    _status = HierarchicalVassalLabelDiscoveryStatus.
                        RestartRequired;
                    return;
                }
                if (container == null)
                {
                    _kingdomIndex++;
                    _cityIndex = 0;
                    _cityContainer = null;
                    _containerCities = null;
                    _entityContainerActive = false;
                    continue;
                }
                IReadOnlyList<City> cities =
                    HierarchicalVassalMapModeService.
                        LabelDiscoveryCities(container);
                if (cities == null)
                {
                    _status = HierarchicalVassalLabelDiscoveryStatus.Failed;
                    return;
                }
                if (!ReferenceEquals(_cityContainer, container))
                {
                    _cityContainer = container;
                    _containerCities = cities;
                    _containerCityCount = cities.Count;
                    _containerCityValidationIndex = 0;
                    _capturedCityIds.Clear();
                }
                else if (!ValidateCityCollection(cities,
                             Math.Max(1, pCityBudget))) return;
                if (_pendingCity == null && _cityIndex >= cities.Count)
                {
                    _kingdomIndex++;
                    _cityIndex = 0;
                    _cityContainer = null;
                    _containerCities = null;
                    _entityContainerActive = false;
                    continue;
                }
                if (_pendingCity == null)
                {
                    City city = cities[_cityIndex++];
                    pCityBudget--;
                    _capturedCityIds.Add(city?.id ?? -1L);
                    if (!IsCandidate(city)) continue;
                    _pendingCity = city;
                    _pendingZoneIndex = 0;
                    _pendingVisible = false;
                    _pendingCityZones = new List<TileZone>();
                    _pendingCityZoneIds = new HashSet<int>();
                    _pendingZoneCollection = city.zones == null
                        ? (IReadOnlyList<TileZone>)Array.Empty<TileZone>()
                        : city.zones;
                    _pendingZoneCount = _pendingZoneCollection.Count;
                    _pendingZoneValidationIndex = 0;
                    _capturedZoneIds.Clear();
                    _pendingRepresentative = ResolveRepresentative(city);
                    if (_pendingRepresentative < 0L) _pendingCity = null;
                }
                if (_pendingCity == null) continue;
                IReadOnlyList<TileZone> zones = _pendingZoneCollection;
                if (!ValidatePendingZoneCollection(zones,
                        Math.Max(1, pZoneBudget))) return;
                while (_pendingZoneIndex < zones.Count && pZoneBudget > 0)
                {
                    TileZone zone = zones[_pendingZoneIndex++];
                    pZoneBudget--;
                    _capturedZoneIds.Add(zone?.id ?? -1);
                    if (zone == null || zone.id < 0) continue;
                    if (zone != null && _pendingCityZoneIds.Add(zone.id))
                        _pendingCityZones.Add(zone);
                    if (zone.tiles_with_ground > 0) _pendingVisible = true;
                    if (_kind != HierarchicalVassalLabelDiscoveryKind.
                        ActiveView || !HierarchicalVassalMapModeService.
                        IsCityLayer)
                        AddTerritoryZone(_pendingRepresentative, zone);
                }
                if (_pendingZoneIndex < zones.Count) return;
                if (_pendingVisible && _kind ==
                    HierarchicalVassalLabelDiscoveryKind.ActiveView &&
                    HierarchicalVassalMapModeService.IsCityLayer)
                    _cities.Add(_pendingCity);
                else if (_pendingVisible && _kind ==
                    HierarchicalVassalLabelDiscoveryKind.RootCities)
                    _cities.Add(_pendingCity);
                if (_pendingVisible && (_kind ==
                        HierarchicalVassalLabelDiscoveryKind.ActiveView &&
                        HierarchicalVassalMapModeService.IsCityLayer ||
                    _kind == HierarchicalVassalLabelDiscoveryKind.RootCities))
                    _citySources.Add(new HierarchicalVassalMapLabelCitySource(
                        _pendingCity, _pendingCityZones, _pendingCityZoneIds,
                        _pendingVisible));
                _pendingCity = null;
                _pendingCityZones = null;
                _pendingCityZoneIds = null;
                _pendingZoneCollection = null;
                _capturedZoneIds.Clear();
            }
            if (_kingdomIndex < _initialKingdomCount || _pendingCity != null)
                return;
            FinalizeSources(Math.Max(1, pCityBudget));
        }

        private bool ValidateCityCollection(IReadOnlyList<City> pCities,
            int pBudget)
        {
            if (!ReferenceEquals(pCities, _containerCities) ||
                pCities.Count != _containerCityCount)
            {
                _status = HierarchicalVassalLabelDiscoveryStatus.
                    RestartRequired;
                return false;
            }
            int checks = Math.Max(1, pBudget);
            while (_capturedCityIds.Count > 0 && checks-- > 0)
            {
                if (_containerCityValidationIndex >=
                    _capturedCityIds.Count)
                    _containerCityValidationIndex = 0;
                City city = pCities[_containerCityValidationIndex];
                long cityId = city?.id ?? -1L;
                if (cityId != _capturedCityIds[
                        _containerCityValidationIndex])
                {
                    _status = HierarchicalVassalLabelDiscoveryStatus.
                        RestartRequired;
                    return false;
                }
                _containerCityValidationIndex++;
            }
            return true;
        }

        private bool ValidatePendingZoneCollection(
            IReadOnlyList<TileZone> pZones, int pBudget)
        {
            if (!ReferenceEquals(pZones, _pendingZoneCollection) ||
                pZones.Count != _pendingZoneCount)
            {
                _status = HierarchicalVassalLabelDiscoveryStatus.
                    RestartRequired;
                return false;
            }
            int checks = Math.Max(1, pBudget);
            while (_capturedZoneIds.Count > 0 && checks-- > 0)
            {
                if (_pendingZoneValidationIndex >= _capturedZoneIds.Count)
                    _pendingZoneValidationIndex = 0;
                TileZone zone = pZones[_pendingZoneValidationIndex];
                int zoneId = zone?.id ?? -1;
                if (zoneId != _capturedZoneIds[
                        _pendingZoneValidationIndex])
                {
                    _status = HierarchicalVassalLabelDiscoveryStatus.
                        RestartRequired;
                    return false;
                }
                _pendingZoneValidationIndex++;
            }
            return true;
        }

        private bool IsCandidate(City pCity)
        {
            return pCity?.data != null && !pCity.isRekt() &&
                HierarchicalVassalMapModeService.
                    IsLabelDiscoveryKingdom(pCity.kingdom);
        }

        private long ResolveRepresentative(City pCity)
        {
            if (_kind == HierarchicalVassalLabelDiscoveryKind.RootCities)
                return pCity.kingdom.id;
            return _hierarchy?.ResolveRepresentative(pCity.kingdom.id) ?? -1L;
        }

        private void AddTerritoryZone(long pRepresentative, TileZone pZone)
        {
            if (!_territoryZones.TryGetValue(pRepresentative,
                    out List<TileZone> zones))
            {
                zones = new List<TileZone>();
                _territoryZones[pRepresentative] = zones;
                _territoryZoneIds[pRepresentative] = new HashSet<int>();
                _territoryOrder.Add(pRepresentative);
            }
            if (pZone?.id >= 0 && _territoryZoneIds[pRepresentative].Add(
                    pZone.id)) zones.Add(pZone);
            if (pZone?.tiles_with_ground > 0)
                _territoryVisible.Add(pRepresentative);
        }

        private void FinalizeSources(int pBudget)
        {
            if (_kind == HierarchicalVassalLabelDiscoveryKind.ActiveView &&
                HierarchicalVassalMapModeService.IsCityLayer ||
                _kind == HierarchicalVassalLabelDiscoveryKind.RootCities)
            {
                _result = new HierarchicalVassalLabelDiscoveryResult(_cities,
                    null, _citySources, _hierarchy, _kingdomById);
                _status = HierarchicalVassalLabelDiscoveryStatus.Complete;
                return;
            }
            while (pBudget-- > 0 && _finalizationIndex <
                   _territoryOrder.Count)
            {
                long key = _territoryOrder[_finalizationIndex++];
                if (!_territoryVisible.Contains(key) ||
                    !_kingdomById.TryGetValue(key, out Kingdom kingdom))
                    continue;
                _finalizedTerritories.Add(
                    new HierarchicalVassalMapLabelTerritorySource(
                    kingdom, _territoryZones[key], _territoryZoneIds[key],
                    true));
            }
            if (_finalizationIndex < _territoryOrder.Count)
                return;
            _result = new HierarchicalVassalLabelDiscoveryResult(null,
                _finalizedTerritories, null, _hierarchy, _kingdomById);
            _status = HierarchicalVassalLabelDiscoveryStatus.Complete;
        }

        private int _pendingZoneIndex;
    }
}
