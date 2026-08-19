using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    internal enum HierarchicalVassalLabelRefreshKind
    {
        None,
        ActiveView,
        RootCountries,
        RootCities
    }

    internal static class HierarchicalVassalMapLabelRuntime
    {
        private sealed class InFlightBuild
        {
            internal readonly LabelSource Source;
            internal readonly HierarchicalVassalLabelBuildJob Job;

            internal InFlightBuild(LabelSource pSource,
                HierarchicalVassalLabelBuildJob pJob)
            {
                Source = pSource;
                Job = pJob;
            }
        }

        private static readonly Dictionary<string,
            HierarchicalVassalLabelCacheEntry> Cache =
                new Dictionary<string, HierarchicalVassalLabelCacheEntry>();
        private static readonly Dictionary<string, long> SourceGenerations =
            new Dictionary<string, long>();
        private static readonly Dictionary<string, long> BatchSourceGenerations =
            new Dictionary<string, long>();
        private static readonly HashSet<string> DirtySourceKeys =
            new HashSet<string>();
        private static readonly Dictionary<long, HashSet<int>>
            ObservedCityZoneIds =
                new Dictionary<long, HashSet<int>>();
        private static readonly Dictionary<string, HashSet<int>>
            PendingZoneChanges =
                new Dictionary<string, HashSet<int>>();
        private static readonly Dictionary<string, HashSet<int>>
            PendingAddedZoneIds =
                new Dictionary<string, HashSet<int>>();
        private static readonly Dictionary<int, HashSet<string>>
            CacheKeysByZoneId =
                new Dictionary<int, HashSet<string>>();
        private static readonly HierarchicalVassalLabelIndexWorkQueue
            CacheIndexWork = new HierarchicalVassalLabelIndexWorkQueue();
        private static readonly Dictionary<long, HashSet<string>>
            CityKeysByEntityId =
                new Dictionary<long, HashSet<string>>();
        private static List<LabelSource> _sources;
        private static HierarchicalVassalLabelDiscoveryJob _discoveryJob;
        private static IReadOnlyList<HierarchicalVassalMapLabelCitySource>
            _pendingCitySources;
        private static IReadOnlyList<
            HierarchicalVassalMapLabelTerritorySource> _pendingTerritories;
        private static IReadOnlyList<HierarchicalVassalMapLabelRegionSource>
            _pendingRegionSources;
        private static int _sourceConversionIndex;
        private static bool _sourceConversionComplete;
        private static HashSet<string> _activeKeys;
        private static int _sourceIndex;
        private static LabelSource _currentSource;
        private static HierarchicalVassalLabelBuildJob _currentJob;
        private static readonly List<InFlightBuild> InFlightBuilds =
            new List<InFlightBuild>();
        private static HierarchicalVassalLabelRefreshKind _refreshKind;
        private static string _batchKeyPrefix = string.Empty;
        private static string _batchLayer = string.Empty;
        private static long _batchFocus;
        private static bool _mapModeActive;
        private static bool _activeViewDirty = true;
        private static bool _rootCountriesDirty = true;
        private static bool _rootCitiesDirty = true;
        private static bool _batchSuperseded;
        private static long _worldGeneration = 1L;
        private static long _layoutGeneration = 1L;
        private static long _sourceGeneration = 1L;
        private static long _batchWorldGeneration;
        private static long _batchLayoutGeneration;
        private static long _batchSourceGeneration;
        private static bool _forceActiveViewRequested;
        private static bool _forceRootCountriesRequested;
        private static bool _forceRootCitiesRequested;
        private static bool _batchForceRefresh;
        private static int _acceptedSourceResultCount;
        private static int _rejectedSourceResultCount;
        private static int _processFailureRetryCount;

        internal static bool NeedsProcessFrame => _discoveryJob != null ||
            _sources != null ||
            _currentJob != null || InFlightBuilds.Count > 0 ||
            (_mapModeActive && _activeViewDirty) ||
            _rootCountriesDirty || _rootCitiesDirty ||
            CacheIndexWork.HasPendingWork;

        internal static int CacheCountForDiagnostics => Cache.Count;

        internal static int AcceptedSourceResultCountForDiagnostics =>
            _acceptedSourceResultCount;

        internal static int RejectedSourceResultCountForDiagnostics =>
            _rejectedSourceResultCount;

        internal static string CurrentSourceKeyForDiagnostics =>
            _currentSource?.Key ?? (InFlightBuilds.Count > 0
                ? InFlightBuilds[0].Source?.Key
                : null);

        internal static int InFlightWorkerCountForDiagnostics =>
            InFlightBuilds.Count;

        internal static int SourceGenerationCountForDiagnostics =>
            SourceGenerations.Count;

        internal static int DirtySourceKeyCountForDiagnostics =>
            DirtySourceKeys.Count;


        internal static void ObserveMapModeActive(bool pActive)
        {
            if (_mapModeActive == pActive) return;
            _processFailureRetryCount = 0;
            _mapModeActive = pActive;
            if (!pActive) return;
            _activeViewDirty = true;
            if (_refreshKind != HierarchicalVassalLabelRefreshKind.None &&
                _refreshKind != HierarchicalVassalLabelRefreshKind.ActiveView)
                CancelCurrentBatch(true);
        }

        internal static void MarkDirty(bool pForceRecalculate)
        {
            _processFailureRetryCount = 0;
            _activeViewDirty = true;
            _rootCountriesDirty = true;
            _rootCitiesDirty = true;
            if (pForceRecalculate)
            {
                _forceActiveViewRequested = true;
                _forceRootCountriesRequested = true;
                _forceRootCitiesRequested = true;
            }
            if (!pForceRecalculate)
            {
                return;
            }
            CancelCurrentBatch(false);
        }

        internal static void RequestRefresh()
        {
            _processFailureRetryCount = 0;
            _activeViewDirty = true;
        }

        internal static void MarkCityDirty(City pCity)
        {
            if (pCity == null) return;
            _processFailureRetryCount = 0;
            _activeViewDirty = true;
            _rootCountriesDirty = true;
            _rootCitiesDirty = true;
            var zoneIds = CollectZoneIds(pCity?.zones);
            MarkKeysForEntityAndZones("city", pCity.id, zoneIds);
            SupersedeCurrentBatch();
        }

        internal static void MarkCityGeometryDirty(City pCity)
        {
            if (pCity == null) return;
            _processFailureRetryCount = 0;
            var zoneIds = CollectZoneIds(pCity?.zones);
            if (TryAccumulateCityGeometryChange(pCity.id, zoneIds)) return;

            // Initial precompute has no cache baseline yet. Keep one bounded
            // follow-up batch so a mutation cannot publish as the baseline.
            _activeViewDirty = _mapModeActive;
            _rootCountriesDirty = true;
            _rootCitiesDirty = true;
            SupersedeCurrentBatch();
        }

        internal static void MarkCityZoneGeometryDirty(City pCity,
            TileZone pZone)
        {
            if (pCity == null || pZone == null || pZone.id < 0) return;
            bool hasObserved = ObservedCityZoneIds.ContainsKey(pCity.id);
            bool hasCityCache = CityKeysByEntityId.ContainsKey(pCity.id);
            bool hasTerritoryCache = HasCacheForExistingCityZone(pCity,
                pZone.id);
            if (!_mapModeActive && !hasObserved && !hasCityCache &&
                !hasTerritoryCache) return;

            _processFailureRetryCount = 0;
            if (TryAccumulateCityZoneAddition(pCity, pZone.id)) return;

            // An active view with no baseline needs one normal discovery pass.
            _activeViewDirty = _mapModeActive;
            _rootCountriesDirty = true;
            _rootCitiesDirty = true;
            SupersedeCurrentBatch();
        }

        internal static void EvictCity(long pCityId)
        {
            ObservedCityZoneIds.Remove(pCityId);
            EvictKeys(key => key.MatchesEntity("city", pCityId));
        }

        internal static void EvictKingdom(long pKingdomId)
        {
            EvictKeys(key => key.MatchesEntity("country", pKingdomId) ||
                key.HasFocus(pKingdomId));
        }

        internal static void RecoverFromProcessFailure()
        {
            _processFailureRetryCount++;
            bool hadBatch = _refreshKind !=
                HierarchicalVassalLabelRefreshKind.None || _sources != null;
            CancelCurrentBatch(true);
            if (!hadBatch && _processFailureRetryCount < 3)
            {
                _activeViewDirty = _mapModeActive;
                _rootCountriesDirty = true;
                _rootCitiesDirty = true;
            }
            if (_processFailureRetryCount >= 3)
            {
                _activeViewDirty = false;
                _rootCountriesDirty = false;
                _rootCitiesDirty = false;
            }
        }

        internal static bool CanRetryProcessFailure =>
            _processFailureRetryCount < 3;

        internal static void MarkKingdomDirty(Kingdom pKingdom)
        {
            if (pKingdom == null) return;
            _processFailureRetryCount = 0;
            _activeViewDirty = true;
            _rootCountriesDirty = true;
            _rootCitiesDirty = true;
            var zoneIds = new HashSet<int>();
            try
            {
                IEnumerable<City> cities = pKingdom.getCities();
                if (cities != null)
                    foreach (City city in cities)
                        AddZoneIds(zoneIds, city?.zones);
            }
            catch { }
            MarkKeysForEntityAndZones("country", pKingdom.id, zoneIds);
            SupersedeCurrentBatch();
        }

        internal static void MarkHierarchyDirty()
        {
            _processFailureRetryCount = 0;
            _activeViewDirty = true;
            _rootCountriesDirty = true;
            var keys = new List<string>();
            foreach (string key in Cache.Keys)
                if (HierarchicalVassalLabelCacheKey.TryParse(key,
                        out HierarchicalVassalLabelCacheKey parsed) &&
                    string.Equals(parsed.Layer, "country",
                        StringComparison.Ordinal)) keys.Add(key);
            if (_sources != null)
                for (int index = 0; index < _sources.Count; index++)
                    if (_sources[index].Country) keys.Add(_sources[index].Key);
            for (int index = 0; index < keys.Count; index++)
                MarkSourceDirty(keys[index]);
            if (_refreshKind == HierarchicalVassalLabelRefreshKind.ActiveView ||
                _refreshKind == HierarchicalVassalLabelRefreshKind.RootCountries)
                CancelCurrentBatch(true);
        }

        internal static void MarkViewChanged()
        {
            _processFailureRetryCount = 0;
            _activeViewDirty = true;
            _sourceGeneration = HierarchicalVassalLabelBatchRules.
                NextSourceGeneration(_sourceGeneration);
            if (_refreshKind != HierarchicalVassalLabelRefreshKind.None)
                CancelCurrentBatch(true);
        }

        private static void SupersedeCurrentBatch()
        {
            if (_refreshKind != HierarchicalVassalLabelRefreshKind.None &&
                (_discoveryJob != null ||
                 (_sources != null && !_sourceConversionComplete)))
                _batchSuperseded = true;
        }

        internal static void CancelUnpublishedJobs()
        {
            CancelCurrentBatch(false);
        }

        internal static void Reset()
        {
            _worldGeneration++;
            _layoutGeneration++;
            _sourceGeneration = HierarchicalVassalLabelBatchRules.
                NextSourceGeneration(_sourceGeneration);
            CancelCurrentBatch(false);
            Cache.Clear();
            SourceGenerations.Clear();
            BatchSourceGenerations.Clear();
            DirtySourceKeys.Clear();
            ObservedCityZoneIds.Clear();
            PendingZoneChanges.Clear();
            PendingAddedZoneIds.Clear();
            CacheKeysByZoneId.Clear();
            CacheIndexWork.Clear();
            CityKeysByEntityId.Clear();
            _mapModeActive = false;
            _activeViewDirty = true;
            _rootCountriesDirty = true;
            _rootCitiesDirty = true;
            _forceActiveViewRequested = false;
            _forceRootCountriesRequested = false;
            _forceRootCitiesRequested = false;
            _acceptedSourceResultCount = 0;
            _rejectedSourceResultCount = 0;
            _processFailureRetryCount = 0;
            _discoveryJob = null;
        }

        internal static void ProcessFrame()
        {
            CacheIndexWork.Advance(CacheKeysByZoneId,
                _mapModeActive
                    ? HierarchicalVassalMapModeSchedulingRules.
                        MaximumLabelIndexBudget
                    : HierarchicalVassalMapModeSchedulingRules.
                        MaximumInactiveLabelIndexBudget);
            PollCompletedBuilds();
            if (_sources == null)
            {
                if (_discoveryJob == null && !TryBeginRefresh()) return;
                if (_discoveryJob != null)
                {
                    AdvanceDiscovery();
                    if (_sources == null) return;
                }
            }

            if (!_sourceConversionComplete)
            {
                AdvanceSourceConversion();
                if (!_sourceConversionComplete) return;
            }

            bool activeBatch = _refreshKind ==
                HierarchicalVassalLabelRefreshKind.ActiveView;
            int requestedSourceBudget = activeBatch
                ? HierarchicalVassalMapModeSchedulingRules.MaximumLabelBudget
                : HierarchicalVassalMapModeSchedulingRules.
                    MaximumInactiveLabelBudget;
            int tileBudget = activeBatch
                ? HierarchicalVassalMapModeSchedulingRules.
                    MaximumLabelTileCopyBudget
                : HierarchicalVassalMapModeSchedulingRules.
                    MaximumInactiveLabelTileCopyBudget;
            int remainingTileBudget = tileBudget;
            int sourceBudget = HierarchicalVassalMapModeSchedulingRules.
                ClampLabelBudget(requestedSourceBudget);

            while (sourceBudget > 0 && _sourceIndex < _sources.Count &&
                   remainingTileBudget > 0 &&
                   HierarchicalVassalLabelPipelineRules.CanSubmit(
                       InFlightBuilds.Count))
            {
                LabelSource source = _sources[_sourceIndex];
                if (_currentSource == null)
                {
                    Cache.TryGetValue(source.Key,
                        out HierarchicalVassalLabelCacheEntry cached);
                    int changedZones = cached == null
                        ? int.MaxValue
                        : CountSymmetricDifference(cached.BaselineZoneIds,
                            source.ZoneIds);
                    bool nameChanged = cached != null &&
                        !string.Equals(cached.SourceName,
                            source.DisplayName, StringComparison.Ordinal);
                    bool thresholdReached =
                        HierarchicalVassalLabelInvalidationRules.
                            ShouldRecalculate(changedZones,
                                source.ZoneIds.Count, false);
                    bool recalculate = cached == null || nameChanged ||
                        IsSourceDirty(source.Key) ||
                        _batchForceRefresh ||
                        cached.LayoutGeneration != _batchLayoutGeneration ||
                        thresholdReached;
                    if (!recalculate)
                    {
                        ClearSourceDirty(source.Key);
                        if (ShouldPublishActiveBatch)
                            PublishOrShow(source, cached);
                        _sourceIndex++;
                        sourceBudget--;
                        continue;
                    }

                    _currentSource = source;
                    _currentJob = HierarchicalVassalLabelBuildJob.
                        CreateFromZones(source.EntityId, source.DisplayName,
                            source.Zones, !source.Country);
                }

                HierarchicalVassalLabelBuildProgress progress =
                    _currentJob.Advance(
                        new HierarchicalVassalLabelBuildBudget(
                            remainingTileBudget, 1, 1, 1));
                remainingTileBudget = Math.Max(0,
                    remainingTileBudget - progress.ConsumedUnits);
                if (progress.Completed || progress.Cancelled)
                {
                    HandleBuildCompletion(source, progress);
                    _currentJob = null;
                    _currentSource = null;
                    _sourceIndex++;
                    sourceBudget--;
                    continue;
                }

                if (_currentJob.Phase ==
                    HierarchicalVassalLabelBuildPhase.ComputePureGeometry)
                {
                    InFlightBuilds.Add(new InFlightBuild(_currentSource,
                        _currentJob));
                    _currentJob = null;
                    _currentSource = null;
                    _sourceIndex++;
                    sourceBudget--;
                    continue;
                }

                return;
            }

            if (!HierarchicalVassalLabelPipelineRules.CanFinish(
                    _sourceIndex >= _sources.Count,
                    _currentJob != null, InFlightBuilds.Count)) return;
            if (ShouldPublishActiveBatch)
                HierarchicalVassalMapModeLabelLayer.
                    HideRuntimeLabelsExcept(_activeKeys);
            if (IsCurrentBatchGeneration && !_batchSuperseded)
                PruneMissingEntries();
            _processFailureRetryCount = 0;
            FinishCurrentBatch();
        }

        private static void PollCompletedBuilds()
        {
            for (int index = InFlightBuilds.Count - 1; index >= 0; index--)
            {
                InFlightBuild build = InFlightBuilds[index];
                HierarchicalVassalLabelBuildProgress progress =
                    build.Job.Advance(
                        new HierarchicalVassalLabelBuildBudget(1, 1, 1, 1));
                if (!progress.Completed && !progress.Cancelled) continue;
                InFlightBuilds.RemoveAt(index);
                HandleBuildCompletion(build.Source, progress);
            }
        }

        private static void HandleBuildCompletion(LabelSource pSource,
            HierarchicalVassalLabelBuildProgress pProgress)
        {
            if (!pProgress.Completed) return;
            if (IsCurrentBatchGeneration &&
                IsCurrentSourceGeneration(pSource.Key))
            {
                HierarchicalVassalLabelCacheEntry accepted =
                    Accept(pSource, pProgress.Result);
                _acceptedSourceResultCount++;
                ClearSourceDirty(pSource.Key);
                if (ShouldPublishActiveBatch)
                    PublishOrShow(pSource, accepted);
                return;
            }
            _rejectedSourceResultCount++;
        }

        private static void AdvanceDiscovery()
        {
            bool active = _refreshKind ==
                HierarchicalVassalLabelRefreshKind.ActiveView;
            _discoveryJob.Advance(
                active ? HierarchicalVassalMapModeSchedulingRules.
                    MaximumLabelDiscoveryKingdomBudget :
                    HierarchicalVassalMapModeSchedulingRules.
                        MaximumInactiveLabelDiscoveryKingdomBudget,
                active ? HierarchicalVassalMapModeSchedulingRules.
                    MaximumLabelDiscoveryCityBudget :
                    HierarchicalVassalMapModeSchedulingRules.
                        MaximumInactiveLabelDiscoveryCityBudget,
                active ? HierarchicalVassalMapModeSchedulingRules.
                    MaximumLabelDiscoveryZoneBudget :
                    HierarchicalVassalMapModeSchedulingRules.
                        MaximumInactiveLabelDiscoveryZoneBudget);
            if (_discoveryJob.Status ==
                HierarchicalVassalLabelDiscoveryStatus.Incomplete) return;
            if (_discoveryJob.Status !=
                HierarchicalVassalLabelDiscoveryStatus.Complete)
            {
                RequeueCurrentBatch();
                FinishCurrentBatch();
                return;
            }
            HierarchicalVassalLabelDiscoveryResult result =
                _discoveryJob.Result;
            if (IsCurrentBatchGeneration && !_batchSuperseded)
                HierarchicalVassalMapModeService.TryAcceptLabelHierarchy(
                    result.Hierarchy, result.Kingdoms, _batchFocus);
            _sources = new List<LabelSource>();
            BatchSourceGenerations.Clear();
            _activeKeys = new HashSet<string>();
            _pendingRegionSources = (_refreshKind ==
                HierarchicalVassalLabelRefreshKind.RootCities ||
                _refreshKind == HierarchicalVassalLabelRefreshKind.ActiveView) &&
                HierarchicalVassalMapModeService.IsCityRegionLayer
                ? result.RegionSources : null;
            _pendingCitySources = _refreshKind ==
                HierarchicalVassalLabelRefreshKind.RootCities ||
                (_refreshKind == HierarchicalVassalLabelRefreshKind.ActiveView &&
                 HierarchicalVassalMapModeService.IsCityLayer)
                ? result.CitySources : null;
            _pendingTerritories = _pendingCitySources == null
                ? result.Territories : null;
            _sourceConversionIndex = 0;
            _sourceConversionComplete = false;
            _discoveryJob = null;
        }

        private static void AdvanceSourceConversion()
        {
            if (_sources == null) return;
            int budget = _refreshKind ==
                HierarchicalVassalLabelRefreshKind.ActiveView
                ? HierarchicalVassalMapModeSchedulingRules.MaximumLabelBudget
                : HierarchicalVassalMapModeSchedulingRules.
                    MaximumInactiveLabelBudget;
            if (budget <= 0) budget = 1;
            bool regionBatch = _pendingRegionSources != null;
            bool cityBatch = !regionBatch && _pendingCitySources != null;
            int count = regionBatch ? _pendingRegionSources.Count :
                cityBatch ? _pendingCitySources.Count :
                (_pendingTerritories?.Count ?? 0);
            while (budget-- > 0 && _sourceConversionIndex < count)
            {
                if (regionBatch)
                    AppendRegionSource(_pendingRegionSources[
                        _sourceConversionIndex], _batchFocus);
                else if (cityBatch)
                    AppendCitySource(_pendingCitySources[
                        _sourceConversionIndex], _batchFocus);
                else
                    AppendCountrySource(_pendingTerritories[
                        _sourceConversionIndex], _batchFocus);
                _sourceConversionIndex++;
            }
            if (_sourceConversionIndex < count) return;

            _pendingCitySources = null;
            _pendingRegionSources = null;
            _pendingTerritories = null;
            _sourceConversionComplete = true;
            _discoveryJob = null;
            if (_refreshKind == HierarchicalVassalLabelRefreshKind.ActiveView &&
                _mapModeActive)
                HierarchicalVassalMapModeLabelLayer.
                    HideRuntimeLabelsExcept(_activeKeys);
        }

        private static bool ShouldPublishActiveBatch =>
            _mapModeActive && IsCurrentBatchGeneration &&
            !_batchSuperseded && _refreshKind ==
                HierarchicalVassalLabelRefreshKind.ActiveView;

        private static bool IsCurrentBatchGeneration =>
            HierarchicalVassalLabelBatchRules.CanAccept(
                _batchWorldGeneration, _worldGeneration,
                _batchSourceGeneration, _sourceGeneration,
                _batchSuperseded) &&
            _batchLayoutGeneration == _layoutGeneration;

        private static bool TryBeginRefresh()
        {
            HierarchicalVassalLabelRefreshKind kind =
                HierarchicalVassalLabelRefreshKind.None;
            if (_mapModeActive && _activeViewDirty)
                kind = HierarchicalVassalLabelRefreshKind.ActiveView;
            else if (_rootCountriesDirty)
                kind = HierarchicalVassalLabelRefreshKind.RootCountries;
            else if (_rootCitiesDirty)
                kind = HierarchicalVassalLabelRefreshKind.RootCities;
            if (kind == HierarchicalVassalLabelRefreshKind.None) return false;

            _refreshKind = kind;
            _batchWorldGeneration = _worldGeneration;
            _batchLayoutGeneration = _layoutGeneration;
            _batchSourceGeneration = _sourceGeneration;
            _batchSuperseded = false;
            _batchForceRefresh = kind ==
                HierarchicalVassalLabelRefreshKind.ActiveView
                ? _forceActiveViewRequested
                : kind == HierarchicalVassalLabelRefreshKind.RootCountries
                    ? _forceRootCountriesRequested
                    : _forceRootCitiesRequested;
            if (kind == HierarchicalVassalLabelRefreshKind.ActiveView)
                _forceActiveViewRequested = false;
            else if (kind == HierarchicalVassalLabelRefreshKind.RootCountries)
                _forceRootCountriesRequested = false;
            else
            _forceRootCitiesRequested = false;
            _sources = null;
            _pendingCitySources = null;
            _pendingRegionSources = null;
            _pendingTerritories = null;
            _sourceConversionIndex = 0;
            _sourceConversionComplete = false;
            _activeKeys = null;
            _batchKeyPrefix = ResolveBatchKeyPrefix(kind);
            _batchLayer = (kind == HierarchicalVassalLabelRefreshKind.RootCities ||
                kind == HierarchicalVassalLabelRefreshKind.ActiveView) &&
                HierarchicalVassalMapModeService.IsCityRegionLayer
                ? "region" : kind == HierarchicalVassalLabelRefreshKind.RootCities ||
                (kind == HierarchicalVassalLabelRefreshKind.ActiveView &&
                 HierarchicalVassalMapModeService.IsCityLayer)
                ? "city" : "country";
            _batchFocus = kind == HierarchicalVassalLabelRefreshKind.ActiveView
                ? HierarchicalVassalMapModeService.CurrentLabelFocusKey : -1L;
            _sourceIndex = 0;
            _currentSource = null;
            _currentJob = null;
            InFlightBuilds.Clear();
            switch (kind)
            {
                case HierarchicalVassalLabelRefreshKind.ActiveView:
                    _activeViewDirty = false;
                    break;
                case HierarchicalVassalLabelRefreshKind.RootCountries:
                    _rootCountriesDirty = false;
                    break;
                case HierarchicalVassalLabelRefreshKind.RootCities:
                    _rootCitiesDirty = false;
                    break;
            }
            _discoveryJob = HierarchicalVassalMapModeService.
                BeginLabelSourceDiscovery(
                    kind == HierarchicalVassalLabelRefreshKind.ActiveView
                        ? HierarchicalVassalLabelDiscoveryKind.ActiveView
                        : kind == HierarchicalVassalLabelRefreshKind.RootCities
                            ? HierarchicalVassalLabelDiscoveryKind.RootCities
                            : HierarchicalVassalLabelDiscoveryKind.RootCountries,
                    _batchFocus);
            return true;
        }

        private static void AppendCitySource(
            HierarchicalVassalMapLabelCitySource pSource, long pFocusKey)
        {
            if (pSource == null || !pSource.HasVisibleLand) return;
            City city = pSource.City;
            string name = SafeCityName(city);
            if (string.IsNullOrWhiteSpace(name)) return;
            var source = new LabelSource(
                BuildKey(pFocusKey, city.id, true), city.id, name,
                false, city.kingdom, city, pSource.Zones,
                pSource.ZoneIds);
            RegisterConvertedSource(source);
        }

        private static void AppendRegionSource(
            HierarchicalVassalMapLabelRegionSource pSource, long pFocusKey)
        {
            if (pSource == null || !pSource.HasVisibleLand ||
                pSource.SeatCity?.data == null) return;
            string name = pSource.Region.RegionName;
            if (string.IsNullOrWhiteSpace(name)) return;
            var source = new LabelSource(
                BuildKey(pFocusKey, pSource.Region.SeatCityId,
                    "region"), pSource.Region.SeatCityId, name, false,
                pSource.SeatCity.kingdom, pSource.SeatCity, pSource.Zones,
                pSource.ZoneIds);
            RegisterConvertedSource(source);
        }

        private static void AppendCountrySource(
            HierarchicalVassalMapLabelTerritorySource pTerritory,
            long pFocusKey)
        {
            if (pTerritory == null || !pTerritory.HasVisibleLand) return;
            Kingdom kingdom = pTerritory.Kingdom;
            string name = HierarchicalVassalMapModeService.
                GetMapDisplayName(kingdom);
            if (string.IsNullOrWhiteSpace(name)) return;
            var source = new LabelSource(
                BuildKey(pFocusKey, kingdom.id, false), kingdom.id,
                name, true, kingdom, null, pTerritory.Zones,
                pTerritory.ZoneIds);
            RegisterConvertedSource(source);
        }

        private static void RegisterConvertedSource(LabelSource pSource)
        {
            if (pSource == null) return;
            _sources.Add(pSource);
            BatchSourceGenerations[pSource.Key] =
                GetSourceGeneration(pSource.Key);
            _activeKeys.Add(pSource.Key);
        }

        private static HierarchicalVassalLabelCacheEntry Accept(
            LabelSource pSource, HierarchicalVassalLabelBuildResult pResult)
        {
            HashSet<int> previousZoneIds = null;
            if (!Cache.TryGetValue(pSource.Key,
                    out HierarchicalVassalLabelCacheEntry cached))
            {
                cached = new HierarchicalVassalLabelCacheEntry(pResult,
                    pSource.ZoneIds, pSource.DisplayName,
                    _batchLayoutGeneration);
                Cache[pSource.Key] = cached;
            }
            else
            {
                previousZoneIds = cached.BaselineZoneIds;
                cached.Accept(pResult, pSource.ZoneIds, pSource.DisplayName,
                    _batchLayoutGeneration);
            }
            CacheIndexWork.EnqueueReplace(pSource.Key, previousZoneIds,
                cached.BaselineZoneIds);
            if (!pSource.Country)
                AddIndexedKey(CityKeysByEntityId, pSource.EntityId,
                    pSource.Key);
            PendingZoneChanges.Remove(pSource.Key);
            PendingAddedZoneIds.Remove(pSource.Key);
            if (!pSource.Country)
                ObservedCityZoneIds[pSource.EntityId] = pSource.ZoneIds;
            return cached;
        }

        private static void PublishOrShow(LabelSource pSource,
            HierarchicalVassalLabelCacheEntry pEntry)
        {
            if (pEntry == null) return;
            if (pEntry.Published &&
                HierarchicalVassalMapModeLabelLayer.ShowRuntimeLabel(
                    pSource.Key))
            {
                HierarchicalVassalMapModeLabelLayer.RefreshRuntimeLabelStyle(
                    pSource.Key, pSource.Country, pSource.Kingdom,
                    pSource.City);
                return;
            }
            HierarchicalVassalLabelBuildResult result = pEntry.Result;
            HierarchicalVassalMapModeLabelLayer.ApplyRuntimeLabel(
                pSource.Key, result.DisplayText, result.Placement,
                result.CountryLabelGap, pSource.Country, pSource.Kingdom,
                pSource.City);
            pEntry.MarkPublished();
        }

        private static void PruneMissingEntries()
        {
            if (string.IsNullOrEmpty(_batchKeyPrefix) ||
                _activeKeys == null) return;
            var obsolete = new List<string>();
            foreach (string key in Cache.Keys)
            {
                if (HierarchicalVassalLabelCacheKey.TryParse(key,
                        out HierarchicalVassalLabelCacheKey parsed) &&
                    parsed.HasPrefix(_worldGeneration, _batchLayer,
                        _batchFocus) && !_activeKeys.Contains(key))
                    obsolete.Add(key);
            }
            for (int index = 0; index < obsolete.Count; index++)
            {
                string key = obsolete[index];
                if (_batchLayer == "city" && _batchFocus == -1L &&
                    HierarchicalVassalLabelCacheKey.TryParse(key,
                        out HierarchicalVassalLabelCacheKey parsed))
                {
                    EvictCity(parsed.EntityId);
                    continue;
                }
                RemoveCacheKey(key);
            }
        }

        private static string ResolveBatchKeyPrefix(
            HierarchicalVassalLabelRefreshKind pKind)
        {
            if (pKind == HierarchicalVassalLabelRefreshKind.RootCountries)
                return "world:" + _worldGeneration + ":country:-1:";
            if (pKind == HierarchicalVassalLabelRefreshKind.RootCities)
                return "world:" + _worldGeneration + ":" +
                    (HierarchicalVassalMapModeService.IsCityRegionLayer
                        ? "region" : "city") + ":-1:";
            long focus = HierarchicalVassalMapModeService.
                CurrentLabelFocusKey;
            return "world:" + _worldGeneration + ":" +
                (HierarchicalVassalMapModeService.IsCityLayer
                    ? "city:" : "country:") + focus + ":";
        }

        private static void CancelCurrentBatch(bool pRequeue)
        {
            if (pRequeue) RequeueCurrentBatch();
            _discoveryJob?.Cancel();
            _currentJob?.Cancel();
            for (int index = 0; index < InFlightBuilds.Count; index++)
                InFlightBuilds[index].Job?.Cancel();
            InFlightBuilds.Clear();
            FinishCurrentBatch();
        }

        private static void RequeueCurrentBatch()
        {
            switch (_refreshKind)
            {
                case HierarchicalVassalLabelRefreshKind.ActiveView:
                    _activeViewDirty = true;
                    break;
                case HierarchicalVassalLabelRefreshKind.RootCountries:
                    _rootCountriesDirty = true;
                    break;
                case HierarchicalVassalLabelRefreshKind.RootCities:
                    _rootCitiesDirty = true;
                    break;
            }
        }

        private static void FinishCurrentBatch()
        {
            _currentJob = null;
            _currentSource = null;
            InFlightBuilds.Clear();
            _sources = null;
            _activeKeys = null;
            _sourceIndex = 0;
            _refreshKind = HierarchicalVassalLabelRefreshKind.None;
            _batchKeyPrefix = string.Empty;
            _batchLayer = string.Empty;
            _batchFocus = 0L;
            _batchSuperseded = false;
            _batchForceRefresh = false;
            _discoveryJob = null;
            _pendingCitySources = null;
            _pendingTerritories = null;
            _sourceConversionIndex = 0;
            _sourceConversionComplete = false;
        }

        private static int CountSymmetricDifference(ISet<int> pBaseline,
            ISet<int> pCurrent)
        {
            int changed = 0;
            if (pBaseline != null)
                foreach (int id in pBaseline)
                    if (pCurrent == null || !pCurrent.Contains(id)) changed++;
            if (pCurrent != null)
                foreach (int id in pCurrent)
                    if (pBaseline == null || !pBaseline.Contains(id)) changed++;
            return changed;
        }

        private static bool IsCurrentSourceGeneration(string pKey)
        {
            long batch = BatchSourceGenerations.TryGetValue(pKey,
                out long value) ? value : 0L;
            return batch == GetSourceGeneration(pKey);
        }

        private static long GetSourceGeneration(string pKey)
        {
            return SourceGenerations.TryGetValue(pKey,
                out long value) ? value : 0L;
        }

        private static void MarkSourceDirty(string pKey)
        {
            if (string.IsNullOrEmpty(pKey)) return;
            DirtySourceKeys.Add(pKey);
            SourceGenerations[pKey] = HierarchicalVassalLabelBatchRules.
                NextSourceGeneration(GetSourceGeneration(pKey));
        }

        private static void ClearSourceDirty(string pKey)
        {
            if (IsCurrentSourceGeneration(pKey)) DirtySourceKeys.Remove(pKey);
        }

        private static bool IsSourceDirty(string pKey)
        {
            return !string.IsNullOrEmpty(pKey) &&
                DirtySourceKeys.Contains(pKey);
        }

        private static HashSet<int> CollectZoneIds(
            IReadOnlyList<TileZone> pZones)
        {
            var ids = new HashSet<int>();
            AddZoneIds(ids, pZones);
            return ids;
        }

        private static void AddZoneIds(ISet<int> pTarget,
            IReadOnlyList<TileZone> pZones)
        {
            if (pTarget == null || pZones == null) return;
            for (int index = 0; index < pZones.Count; index++)
            {
                TileZone zone = pZones[index];
                if (zone?.id >= 0) pTarget.Add(zone.id);
            }
        }

        private static void MarkKeysForEntityAndZones(string pLayer,
            long pEntityId, ISet<int> pZoneIds,
            bool pForceRecalculate = true)
        {
            var keys = new HashSet<string>();
            if (_sources != null)
            {
                for (int index = 0; index < _sources.Count; index++)
                {
                    LabelSource source = _sources[index];
                    if (source == null) continue;
                    if ((pLayer == "city" && source.City?.id == pEntityId) ||
                        (pLayer == "country" && source.Country &&
                         source.EntityId == pEntityId) ||
                         ZonesOverlap(pZoneIds, source.ZoneIds))
                        keys.Add(source.Key);
                }
            }

            foreach (KeyValuePair<string,
                HierarchicalVassalLabelCacheEntry> pair in Cache)
            {
                string key = pair.Key;
                bool entityMatch = HierarchicalVassalLabelCacheKey.TryParse(
                    key, out HierarchicalVassalLabelCacheKey parsed) &&
                    parsed.MatchesEntity(pLayer, pEntityId);
                if (entityMatch || ZonesOverlap(pZoneIds,
                        pair.Value?.BaselineZoneIds))
                    keys.Add(key);
            }
            foreach (string key in keys)
            {
                if (pForceRecalculate) MarkSourceDirty(key);
                else MarkSourceGeneration(key);
            }
        }

        private static void MarkSourceGeneration(string pKey)
        {
            if (string.IsNullOrEmpty(pKey)) return;
            SourceGenerations[pKey] = HierarchicalVassalLabelBatchRules.
                NextSourceGeneration(GetSourceGeneration(pKey));
        }

        private static void EvictKeys(
            Func<HierarchicalVassalLabelCacheKey, bool> pMatch)
        {
            if (pMatch == null) return;
            bool activeMatch = false;
            if (_sources != null)
            {
                for (int index = 0; index < _sources.Count; index++)
                {
                    if (!HierarchicalVassalLabelCacheKey.TryParse(
                            _sources[index].Key,
                            out HierarchicalVassalLabelCacheKey sourceKey) ||
                        !pMatch(sourceKey)) continue;
                    activeMatch = true;
                    break;
                }
            }
            if (activeMatch) CancelCurrentBatch(true);

            var keys = new HashSet<string>();
            foreach (string key in Cache.Keys)
                if (HierarchicalVassalLabelCacheKey.TryParse(key,
                        out HierarchicalVassalLabelCacheKey parsed) &&
                    pMatch(parsed)) keys.Add(key);
            foreach (string key in SourceGenerations.Keys)
                if (HierarchicalVassalLabelCacheKey.TryParse(key,
                        out HierarchicalVassalLabelCacheKey parsed) &&
                    pMatch(parsed)) keys.Add(key);
            foreach (string key in BatchSourceGenerations.Keys)
                if (HierarchicalVassalLabelCacheKey.TryParse(key,
                        out HierarchicalVassalLabelCacheKey parsed) &&
                    pMatch(parsed)) keys.Add(key);
            foreach (string key in DirtySourceKeys)
                if (HierarchicalVassalLabelCacheKey.TryParse(key,
                        out HierarchicalVassalLabelCacheKey parsed) &&
                    pMatch(parsed)) keys.Add(key);
            foreach (string key in keys)
                RemoveCacheKey(key);
        }

        private static void RemoveCacheKey(string pKey)
        {
            if (string.IsNullOrEmpty(pKey)) return;
            if (Cache.TryGetValue(pKey,
                    out HierarchicalVassalLabelCacheEntry cached))
            {
                CacheIndexWork.EnqueueRemove(pKey, cached.BaselineZoneIds);
                if (HierarchicalVassalLabelCacheKey.TryParse(pKey,
                        out HierarchicalVassalLabelCacheKey parsed) &&
                    string.Equals(parsed.Layer, "city",
                        StringComparison.Ordinal))
                    RemoveIndexedKey(CityKeysByEntityId, parsed.EntityId,
                        pKey);
            }
            Cache.Remove(pKey);
            SourceGenerations.Remove(pKey);
            BatchSourceGenerations.Remove(pKey);
            DirtySourceKeys.Remove(pKey);
            PendingZoneChanges.Remove(pKey);
            PendingAddedZoneIds.Remove(pKey);
            HierarchicalVassalMapModeLabelLayer.RemoveRuntimeLabel(pKey);
        }

        private static bool TryAccumulateCityGeometryChange(long pCityId,
            HashSet<int> pCurrentZoneIds)
        {
            pCurrentZoneIds ??= new HashSet<int>();
            if (!TryGetObservedCityZones(pCityId, out HashSet<int> previous))
            {
                ObservedCityZoneIds[pCityId] =
                    new HashSet<int>(pCurrentZoneIds);
                return false;
            }
            var changedZoneIds = new HashSet<int>(previous);
            changedZoneIds.SymmetricExceptWith(pCurrentZoneIds);
            ObservedCityZoneIds[pCityId] =
                new HashSet<int>(pCurrentZoneIds);
            if (changedZoneIds.Count == 0) return true;

            var affectedScope = new HashSet<int>(previous);
            affectedScope.UnionWith(pCurrentZoneIds);
            HashSet<string> keys = CollectGeometryKeys(pCityId,
                affectedScope);
            if (keys.Count == 0) return false;

            foreach (string key in keys)
            {
                if (!Cache.TryGetValue(key,
                        out HierarchicalVassalLabelCacheEntry cached))
                {
                    MarkSourceDirty(key);
                    ScheduleRefreshForKey(key);
                    continue;
                }

                if (!PendingZoneChanges.TryGetValue(key,
                        out HashSet<int> pending))
                {
                    pending = new HashSet<int>();
                    PendingZoneChanges[key] = pending;
                }
                if (!PendingAddedZoneIds.TryGetValue(key,
                        out HashSet<int> pendingAdded))
                {
                    pendingAdded = new HashSet<int>();
                    PendingAddedZoneIds[key] = pendingAdded;
                }
                foreach (int zoneId in changedZoneIds)
                {
                    bool baselineContains = cached.BaselineZoneIds.Contains(
                        zoneId);
                    bool currentContains = pCurrentZoneIds.Contains(zoneId);
                    if (baselineContains != currentContains)
                    {
                        pending.Add(zoneId);
                        if (currentContains) pendingAdded.Add(zoneId);
                        else pendingAdded.Remove(zoneId);
                    }
                    else
                    {
                        pending.Remove(zoneId);
                        pendingAdded.Remove(zoneId);
                    }
                }

                int removedCount = pending.Count - pendingAdded.Count;
                int currentZoneCount = cached.BaselineZoneIds.Count +
                    pendingAdded.Count - removedCount;
                currentZoneCount = Math.Max(0, currentZoneCount);
                if (!HierarchicalVassalLabelInvalidationRules.
                        ShouldRecalculate(pending.Count, currentZoneCount,
                            false)) continue;

                MarkSourceDirty(key);
                ScheduleRefreshForKey(key);
            }
            return true;
        }

        private static bool TryAccumulateCityZoneAddition(City pCity,
            int pZoneId)
        {
            if (pCity == null || pZoneId < 0) return true;
            if (ObservedCityZoneIds.TryGetValue(pCity.id,
                    out HashSet<int> observed))
                observed.Add(pZoneId);

            var keys = new HashSet<string>();
            AddIndexedKeys(keys, CityKeysByEntityId, pCity.id);
            AddIndexedKeys(keys, CacheKeysByZoneId, pZoneId);
            int anchorZoneId = FindExistingCityZoneId(pCity, pZoneId);
            if (anchorZoneId >= 0)
                AddIndexedKeys(keys, CacheKeysByZoneId, anchorZoneId);
            if (keys.Count == 0) return false;

            foreach (string key in keys)
            {
                if (!Cache.TryGetValue(key,
                        out HierarchicalVassalLabelCacheEntry cached))
                {
                    MarkSourceDirty(key);
                    ScheduleRefreshForKey(key);
                    continue;
                }

                if (!PendingZoneChanges.TryGetValue(key,
                        out HashSet<int> pending))
                {
                    pending = new HashSet<int>();
                    PendingZoneChanges[key] = pending;
                }
                if (!PendingAddedZoneIds.TryGetValue(key,
                        out HashSet<int> pendingAdded))
                {
                    pendingAdded = new HashSet<int>();
                    PendingAddedZoneIds[key] = pendingAdded;
                }

                if (cached.BaselineZoneIds.Contains(pZoneId))
                {
                    pending.Remove(pZoneId);
                    pendingAdded.Remove(pZoneId);
                    continue;
                }
                pending.Add(pZoneId);
                pendingAdded.Add(pZoneId);
                int removedCount = pending.Count - pendingAdded.Count;
                int currentZoneCount = Math.Max(0,
                    cached.BaselineZoneIds.Count + pendingAdded.Count -
                    removedCount);
                if (!HierarchicalVassalLabelInvalidationRules.
                        ShouldRecalculate(pending.Count, currentZoneCount,
                            false)) continue;
                MarkSourceDirty(key);
                ScheduleRefreshForKey(key);
            }
            return true;
        }

        private static bool HasCacheForExistingCityZone(City pCity,
            int pAddedZoneId)
        {
            int zoneId = FindExistingCityZoneId(pCity, pAddedZoneId);
            return zoneId >= 0 && CacheKeysByZoneId.ContainsKey(zoneId);
        }

        private static int FindExistingCityZoneId(City pCity,
            int pAddedZoneId)
        {
            if (pCity?.zones == null || pCity.zones.Count <= 1) return -1;
            TileZone first = pCity.zones[0];
            if (first != null && first.id >= 0 && first.id != pAddedZoneId)
                return first.id;
            TileZone second = pCity.zones[1];
            return second != null && second.id >= 0 &&
                   second.id != pAddedZoneId ? second.id : -1;
        }

        private static bool TryGetObservedCityZones(long pCityId,
            out HashSet<int> pObserved)
        {
            if (ObservedCityZoneIds.TryGetValue(pCityId,
                    out HashSet<int> observed))
            {
                pObserved = new HashSet<int>(observed);
                return true;
            }
            foreach (KeyValuePair<string,
                HierarchicalVassalLabelCacheEntry> pair in Cache)
            {
                if (!HierarchicalVassalLabelCacheKey.TryParse(pair.Key,
                        out HierarchicalVassalLabelCacheKey parsed) ||
                    !parsed.MatchesEntity("city", pCityId)) continue;
                pObserved = new HashSet<int>(pair.Value.BaselineZoneIds);
                return true;
            }
            pObserved = null;
            return false;
        }

        private static HashSet<string> CollectGeometryKeys(long pCityId,
            ISet<int> pAffectedZoneIds)
        {
            var keys = new HashSet<string>();
            AddIndexedKeys(keys, CityKeysByEntityId, pCityId);
            if (_sources != null)
            {
                for (int index = 0; index < _sources.Count; index++)
                {
                    LabelSource source = _sources[index];
                    if (source != null && ZonesOverlap(pAffectedZoneIds,
                            source.ZoneIds)) keys.Add(source.Key);
                }
            }
            if (pAffectedZoneIds != null)
                foreach (int zoneId in pAffectedZoneIds)
                {
                    if (!CacheKeysByZoneId.TryGetValue(zoneId,
                            out HashSet<string> indexedKeys)) continue;
                    foreach (string key in indexedKeys)
                        if (Cache.TryGetValue(key,
                                out HierarchicalVassalLabelCacheEntry entry) &&
                            ZonesOverlap(pAffectedZoneIds,
                                entry.BaselineZoneIds)) keys.Add(key);
                }
            foreach (KeyValuePair<string,
                HierarchicalVassalLabelCacheEntry> pair in Cache)
                if (CacheIndexWork.IsKeyPending(pair.Key) &&
                    ZonesOverlap(pAffectedZoneIds,
                        pair.Value.BaselineZoneIds)) keys.Add(pair.Key);
            return keys;
        }

        private static void AddIndexedKey<TKey>(
            IDictionary<TKey, HashSet<string>> pIndex, TKey pIndexKey,
            string pLabelKey)
        {
            if (!pIndex.TryGetValue(pIndexKey, out HashSet<string> keys))
            {
                keys = new HashSet<string>();
                pIndex[pIndexKey] = keys;
            }
            keys.Add(pLabelKey);
        }

        private static void RemoveIndexedKey<TKey>(
            IDictionary<TKey, HashSet<string>> pIndex, TKey pIndexKey,
            string pLabelKey)
        {
            if (!pIndex.TryGetValue(pIndexKey, out HashSet<string> keys))
                return;
            keys.Remove(pLabelKey);
            if (keys.Count == 0) pIndex.Remove(pIndexKey);
        }

        private static void AddIndexedKeys<TKey>(ISet<string> pTarget,
            IDictionary<TKey, HashSet<string>> pIndex, TKey pIndexKey)
        {
            if (!pIndex.TryGetValue(pIndexKey, out HashSet<string> keys))
                return;
            pTarget.UnionWith(keys);
        }

        private static void ScheduleRefreshForKey(string pKey)
        {
            if (!HierarchicalVassalLabelCacheKey.TryParse(pKey,
                    out HierarchicalVassalLabelCacheKey parsed)) return;
            string activeLayer = HierarchicalVassalMapModeService.IsCityLayer
                ? (HierarchicalVassalMapModeService.IsCityRegionLayer
                    ? "region" : "city") : "country";
            bool activeMatch = _mapModeActive &&
                string.Equals(parsed.Layer, activeLayer,
                    StringComparison.Ordinal) &&
                parsed.HierarchyFocus == HierarchicalVassalMapModeService.
                    CurrentLabelFocusKey;
            if (activeMatch)
            {
                _activeViewDirty = true;
                return;
            }
            if (parsed.HierarchyFocus != -1L) return;
            if (string.Equals(parsed.Layer, "city", StringComparison.Ordinal) ||
                string.Equals(parsed.Layer, "region", StringComparison.Ordinal))
                _rootCitiesDirty = true;
            else if (string.Equals(parsed.Layer, "country",
                         StringComparison.Ordinal))
                _rootCountriesDirty = true;
        }

        private static bool ZonesOverlap(ISet<int> pLeft,
            ISet<int> pRight)
        {
            if (pLeft == null || pRight == null || pLeft.Count == 0 ||
                pRight.Count == 0) return false;
            foreach (int id in pLeft)
                if (pRight.Contains(id)) return true;
            return false;
        }

        private static string BuildKey(long pFocusId, long pEntityId,
            bool pCity)
        {
            return new HierarchicalVassalLabelCacheKey(_worldGeneration,
                pCity ? "city" : "country", pFocusId, pEntityId).ToString();
        }

        private static string BuildKey(long pFocusId, long pEntityId,
            string pLayer)
        {
            return new HierarchicalVassalLabelCacheKey(_worldGeneration,
                pLayer, pFocusId, pEntityId).ToString();
        }

        private static string SafeCityName(City pCity)
        {
            try { return pCity?.data?.name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private sealed class LabelSource
        {
            internal readonly string Key;
            internal readonly long EntityId;
            internal readonly string DisplayName;
            internal readonly bool Country;
            internal readonly Kingdom Kingdom;
            internal readonly City City;
            internal readonly IReadOnlyList<TileZone> Zones;
            internal readonly HashSet<int> ZoneIds;

            internal LabelSource(string pKey, long pEntityId,
                string pDisplayName, bool pCountry, Kingdom pKingdom,
                City pCity, IReadOnlyList<TileZone> pZones,
                HashSet<int> pZoneIds)
            {
                Key = pKey;
                EntityId = pEntityId;
                DisplayName = pDisplayName ?? string.Empty;
                Country = pCountry;
                Kingdom = pKingdom;
                City = pCity;
                Zones = pZones ?? Array.Empty<TileZone>();
                ZoneIds = pZoneIds ?? new HashSet<int>();
            }
        }
    }
}
