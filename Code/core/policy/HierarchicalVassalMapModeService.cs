using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalMapModeService
    {
        public const string POWER_ID =
            HierarchicalVassalMapModeRules.POWER_ID;
        private static readonly HierarchicalVassalMapModeState State =
            new HierarchicalVassalMapModeState();
        private static HierarchicalVassalMapModeSnapshot _rootSnapshot;
        private static readonly Dictionary<long,
            HierarchicalVassalMapModeSnapshot> FocusedSnapshots =
                new Dictionary<long, HierarchicalVassalMapModeSnapshot>();
        private static HierarchicalVassalMapModeSnapshot _visibleSnapshot;
        private static long _visibleSnapshotRevision = long.MinValue;
        private static SnapshotBuildState _snapshotBuild;
        private static bool _zoneRenderDirty = true;
        private static HierarchicalVassalMapModeSnapshot _lastDrawnSnapshot;
        private static readonly HierarchicalVassalMapModeChangeTracker
            ChangeTracker = new HierarchicalVassalMapModeChangeTracker();
        private static HierarchicalVassalMapModeLayer _selectedLayer =
            HierarchicalVassalMapModeLayer.Countries;

        private const int FallbackKingdomBudget =
            HierarchicalVassalMapModeInvalidationRules.
                MaximumFallbackItemsPerFrame;

        public static bool IsActive()
        {
            return AWMapModeCoordinator.IsActive(POWER_ID);
        }

        public static HierarchicalVassalMapModeLayer GetSelectedLayer()
        {
            return _selectedLayer;
        }

        internal static void SetSelectedLayerFromOption(int pZoneOption)
        {
            _selectedLayer = HierarchicalVassalMapModeOptionRules.ResolveLayer(
                pZoneOption);
            _zoneRenderDirty = true;
            HierarchicalVassalMapModeLabelLayer.MarkDirty();
        }

        public static bool IsCityLayer =>
            GetSelectedLayer() == HierarchicalVassalMapModeLayer.Cities;

        public static IMetaObject GetMetaForZone(TileZone pZone)
        {
            if (!IsActive()) return null;
            HierarchicalVassalMapModeSnapshot snapshot =
                BuildVisibleSnapshot();
            if (pZone == null || snapshot == null || pZone.id < 0 ||
                !snapshot.ZoneToKingdomId.TryGetValue(pZone.id,
                    out long mappedKingdomId)) return null;
            if (IsCityLayer)
                return IsVisibleCity(pZone.city, snapshot)
                    ? (IMetaObject)(object)pZone.city
                    : null;
            Kingdom kingdom = GetKingdom(mappedKingdomId);
            return IsValidKingdom(kingdom) ? kingdom : null;
        }

        internal static bool TryGetDisplayedRealm(TileZone pZone,
            out Kingdom pKingdom, out List<TileZone> pZones)
        {
            pKingdom = null;
            pZones = null;
            if (pZone == null || pZone.id < 0) return false;
            HierarchicalVassalMapModeSnapshot snapshot =
                BuildVisibleSnapshot();
            if (snapshot?.ZoneToKingdomId == null ||
                !snapshot.ZoneToKingdomId.TryGetValue(pZone.id,
                    out long kingdomId)) return false;
            Kingdom kingdom = GetKingdom(kingdomId);
            if (!IsValidKingdom(kingdom)) return false;
            IReadOnlyList<HierarchicalVassalKingdomSnapshot> entries =
                snapshot.Entries;
            for (int index = 0; index < entries.Count; index++)
            {
                HierarchicalVassalKingdomSnapshot entry = entries[index];
                if (entry == null || entry.KingdomId != kingdomId) continue;
                pKingdom = kingdom;
                pZones = entry.DrawableZones;
                return pZones != null && pZones.Count > 0;
            }
            return false;
        }

        internal static bool IsFocused => !State.IsRoot;

        internal static long FocusKingdomId => State.FocusKingdomId;

        internal static bool IsSnapshotReady => _visibleSnapshot != null;

        internal static void MarkZoneRenderDirty()
        {
            _zoneRenderDirty = true;
        }

        internal static void ProcessFrame()
        {
            if (!Config.game_loaded || !IsActive())
            {
                _snapshotBuild = null;
                return;
            }

            if (_visibleSnapshot != null) return;
            if (_snapshotBuild == null)
                _snapshotBuild = CreateSnapshotBuild();
            AdvanceSnapshotBuild();
        }

        internal static HierarchicalVassalMapModeSnapshot BuildVisibleSnapshot()
        {
            if (_visibleSnapshot != null) return _visibleSnapshot;
            if (State.IsRoot)
            {
                // Runtime callers (zone drawing, hover and labels) must never
                // synchronously build the world snapshot. MapBox.Update owns
                // the bounded builder and will publish it when complete.
                if (_rootSnapshot == null) return null;
                _visibleSnapshot = _rootSnapshot;
                return _visibleSnapshot;
            }

            long focusKingdomId = State.FocusKingdomId;
            if (!FocusedSnapshots.TryGetValue(focusKingdomId,
                    out _visibleSnapshot)) return null;
            return _visibleSnapshot;
        }

        internal static void RefreshIfWorldChanged()
        {
            if (World.world?.kingdoms == null) return;
            bool changed = ChangeTracker.AdvanceFallback(
                World.world.kingdoms.list, FallbackKingdomBudget);
            long revision = ChangeTracker.Generation;
            if (revision == _visibleSnapshotRevision) return;

            _visibleSnapshotRevision = revision;
            InvalidateSnapshotCaches();
            if (changed) HierarchicalVassalMapModeLabelLayer.MarkDirty();
        }

        public static void DrawZones(MetaTypeAsset pAsset)
        {
            ZoneCalculator calculator = World.world?.zone_calculator;
            if (pAsset == null || calculator == null ||
                World.world?.kingdoms == null) return;
            HierarchicalVassalMapModeSnapshot snapshot =
                BuildVisibleSnapshot();
            IReadOnlyList<TileZone> drawableZones = snapshot?.DrawableZones;
            if (drawableZones == null) return;
            bool fullDraw = !HierarchicalVassalMapModeSchedulingRules.
                ShouldKeepCachedZones(
                    snapshotUnchanged: ReferenceEquals(snapshot,
                        _lastDrawnSnapshot),
                    renderStateDirty: _zoneRenderDirty);
            for (int index = 0; index < drawableZones.Count; index++)
            {
                TileZone zone = drawableZones[index];
                if (zone == null || zone.id < 0) continue;
                if (fullDraw)
                {
                    calculator.drawBegin();
                    calculator.drawZoneMeta(zone, pAsset, GetMetaForZone);
                }
                calculator.drawEnd(zone);
            }
            _lastDrawnSnapshot = snapshot;
            _zoneRenderDirty = false;
        }

        public static bool HandleZoneClick(WorldTile pTile, string pPowerId)
        {
            // MetaTypeAsset.click_action_zone invokes this delegate without a
            // power id, while the selected god-power path supplies one. The
            // active map mode is the authoritative guard for both routes.
            if (!IsActive()) return false;
            HierarchicalVassalMapModeSnapshot visible =
                BuildVisibleSnapshot();
            if (visible == null) return false;
            TileZone clickedZone = pTile?.zone;
            if (clickedZone == null || clickedZone.id < 0)
                return ReturnToRootFromUnmappedClick();
            if (!visible.ZoneToKingdomId.TryGetValue(clickedZone.id,
                    out long clickedKingdomId))
                return SwitchToPhysicalRealm(clickedZone, pTile, pPowerId);
            Kingdom clicked = GetKingdom(clickedKingdomId);
            if (!IsValidKingdom(clicked)) return false;

            if (IsCityLayer)
            {
                City city = clickedZone.city;
                if (!IsVisibleCity(city, visible)) return false;
                try
                {
                    return TryInspectCity(pTile, pPowerId);
                }
                catch { return false; }
            }

            if (!State.IsRoot && clicked.id == State.FocusKingdomId)
            {
                bool popped = State.PopFocus();
                if (popped) RefreshView();
                bool inspected = TryInspectKingdom(clicked, pTile, pPowerId);
                return inspected || popped;
            }

            HierarchyContext context = BuildContext();
            if (!context.Kingdoms.ContainsKey(clicked.id)) return false;

            bool hasChildren = context.DirectVassalsBySuzerainId.TryGetValue(
                                   clicked.id,
                                   out List<Kingdom> clickedChildren) &&
                               clickedChildren.Count > 0;

            if (!State.IsRoot)
            {
                if (!context.DirectVassalsBySuzerainId.TryGetValue(
                        State.FocusKingdomId, out List<Kingdom> children) ||
                    !ContainsKingdom(children, clicked.id))
                    return false;

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
            if (State.IsRoot) return false;
            State.Reset();
            RefreshView();
            return true;
        }

        private static bool SwitchToPhysicalRealm(TileZone pClickedZone,
            WorldTile pTile, string pPowerId)
        {
            if (State.IsRoot) return false;
            Kingdom physicalKingdom = pClickedZone?.city?.kingdom;
            if (!IsValidKingdom(physicalKingdom))
                return ReturnToRootFromUnmappedClick();

            HierarchyContext context = BuildContext();
            Kingdom root = ResolveHierarchyRoot(context, physicalKingdom);
            if (!IsValidKingdom(root))
                return ReturnToRootFromUnmappedClick();

            State.Reset();
            bool hasChildren = context.DirectVassalsBySuzerainId.TryGetValue(
                                   root.id, out List<Kingdom> children) &&
                               children.Count > 0;
            bool focused = State.TryPushFocus(root.id,
                (int)KingdomTitleService.GetTitle(root), hasChildren);
            RefreshView();
            if (focused) return true;
            TryInspectKingdom(root, pTile, pPowerId);
            return true;
        }

        private static Kingdom ResolveHierarchyRoot(HierarchyContext pContext,
            Kingdom pKingdom)
        {
            if (pContext == null || !IsValidKingdom(pKingdom) ||
                !pContext.Kingdoms.ContainsKey(pKingdom.id)) return null;
            Kingdom current = pKingdom;
            var visited = new HashSet<long>();
            while (IsValidKingdom(current) && visited.Add(current.id))
            {
                long suzerainId = EffectiveSuzerainId(pContext, current.id);
                if (suzerainId < 0L ||
                    !pContext.Kingdoms.TryGetValue(suzerainId,
                        out Kingdom suzerain))
                    return current;
                current = suzerain;
            }
            return current;
        }

        public static void DirtyMap()
        {
            InvalidateSnapshotCaches();
            _visibleSnapshotRevision = long.MinValue;
            ChangeTracker.MarkAll();
            HierarchicalVassalMapModeCityCache.Clear();
            HierarchicalVassalMapModeLabelLayer.MarkDirty();
            try { AWMapModeMetaLibrary.ClearDynamicMetaCache(); }
            catch { }
            try { World.world?.zone_calculator?.dirtyAndClear(); }
            catch { }
        }

        public static void Reset()
        {
            State.Reset();
            ChangeTracker.Reset();
            HierarchicalVassalMapModeCityCache.Clear();
            HierarchicalVassalMapModeLabelLayer.Reset();
            DirtyMap();
        }

        internal static void MarkKingdomDirty(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            ChangeTracker.MarkKingdom(pKingdom.id);
            HierarchicalVassalMapModeLabelLayer.MarkDirty();
        }

        internal static void MarkCityDirty(City pCity)
        {
            if (pCity?.data == null) return;
            ChangeTracker.MarkCity(pCity.id);
            HierarchicalVassalMapModeCityCache.MarkDirty(pCity.id);
            HierarchicalVassalMapModeLabelLayer.MarkDirty();
        }

        internal static void MarkHierarchyDirty()
        {
            ChangeTracker.MarkHierarchy();
            HierarchicalVassalMapModeLabelLayer.MarkDirty();
        }

        private static void RefreshView()
        {
            if (State.IsRoot)
                _visibleSnapshot = _rootSnapshot;
            else if (!FocusedSnapshots.TryGetValue(State.FocusKingdomId,
                         out _visibleSnapshot))
                _visibleSnapshot = null;
            _zoneRenderDirty = true;
            HierarchicalVassalMapModeLabelLayer.MarkDirty();
            try { World.world?.zone_calculator?.dirtyAndClear(); }
            catch { }
        }

        private static void InvalidateSnapshotCaches()
        {
            _rootSnapshot = null;
            FocusedSnapshots.Clear();
            _visibleSnapshot = null;
            _snapshotBuild = null;
            _lastDrawnSnapshot = null;
            _zoneRenderDirty = true;
        }

        public static HierarchicalVassalMapModeSnapshot BuildRootSnapshot()
        {
            HierarchyContext context = BuildContext();
            return BuildRootSnapshot(context);
        }

        private static SnapshotBuildState CreateSnapshotBuild()
        {
            var context = BuildContext();
            var state = new SnapshotBuildState
            {
                Context = context,
                IsRoot = State.IsRoot,
                FocusKingdomId = State.FocusKingdomId,
                Snapshot = new HierarchicalVassalMapModeSnapshot
                {
                    FocusKingdomId = State.IsRoot ? -1L : State.FocusKingdomId
                }
            };

            if (State.IsRoot)
            {
                var roots = new List<Kingdom>();
                foreach (KeyValuePair<long, Kingdom> pair in context.Kingdoms)
                    if (EffectiveSuzerainId(context, pair.Key) < 0L)
                        roots.Add(pair.Value);
                roots.Sort(CompareKingdoms);
                for (int index = 0; index < roots.Count; index++)
                {
                    var territory = new List<Kingdom>();
                    CollectSubtree(context, roots[index], territory,
                        new HashSet<long>());
                    state.Work.Add(new SnapshotBuildWork(
                        roots[index], territory));
                }
                return state;
            }

            if (!context.Kingdoms.TryGetValue(State.FocusKingdomId,
                    out Kingdom focus))
            {
                state.IsRoot = true;
                state.FocusKingdomId = -1L;
                state.Snapshot = new HierarchicalVassalMapModeSnapshot();
                foreach (KeyValuePair<long, Kingdom> pair in context.Kingdoms)
                    if (EffectiveSuzerainId(context, pair.Key) < 0L)
                    {
                        var territory = new List<Kingdom>();
                        CollectSubtree(context, pair.Value, territory,
                            new HashSet<long>());
                        state.Work.Add(new SnapshotBuildWork(pair.Value,
                            territory));
                    }
                return state;
            }

            state.Work.Add(new SnapshotBuildWork(focus,
                new List<Kingdom> { focus }));
            IReadOnlyList<Kingdom> children = GetChildren(context, focus);
            for (int index = 0; index < children.Count; index++)
            {
                var territory = new List<Kingdom>();
                CollectSubtree(context, children[index], territory,
                    new HashSet<long>());
                state.Work.Add(new SnapshotBuildWork(children[index],
                    territory));
            }
            return state;
        }

        private static void AdvanceSnapshotBuild()
        {
            if (_snapshotBuild == null) return;
            int budget = HierarchicalVassalMapModeSchedulingRules.
                ClampEntryBudget(
                    HierarchicalVassalMapModeSchedulingRules.MaximumEntryBudget);
            while (budget-- > 0 && _snapshotBuild.Index <
                       _snapshotBuild.Work.Count)
            {
                SnapshotBuildWork work =
                    _snapshotBuild.Work[_snapshotBuild.Index++];
                try
                {
                    HierarchicalVassalKingdomSnapshot entry = BuildEntry(
                        _snapshotBuild.Context, work.Kingdom,
                        work.Territory, _snapshotBuild.Snapshot);
                    if (_snapshotBuild.IsRoot)
                        _snapshotBuild.Snapshot.AddRootEntry(entry);
                    else
                        _snapshotBuild.Snapshot.AddFocusedEntry(entry);
                }
                catch
                {
                    // A stale kingdom is skipped; the rest of the snapshot
                    // remains usable and will be rebuilt on the next dirty.
                }
            }

            if (_snapshotBuild.Index < _snapshotBuild.Work.Count) return;

            if (_snapshotBuild.IsRoot)
                _rootSnapshot = _snapshotBuild.Snapshot;
            else
                FocusedSnapshots[_snapshotBuild.FocusKingdomId] =
                    _snapshotBuild.Snapshot;
            _visibleSnapshot = _snapshotBuild.Snapshot;
            _snapshotBuild = null;
            _zoneRenderDirty = true;
            HierarchicalVassalMapModeLabelLayer.MarkDirty();
            try { World.world?.zone_calculator?.dirtyAndClear(); }
            catch { }
        }

        public static HierarchicalVassalMapModeSnapshot BuildFocusedSnapshot(
            long pFocusKingdomId)
        {
            HierarchyContext context = BuildContext();
            if (!context.Kingdoms.TryGetValue(pFocusKingdomId,
                    out Kingdom focus))
                return BuildRootSnapshot(context);

            var snapshot = new HierarchicalVassalMapModeSnapshot
            {
                FocusKingdomId = pFocusKingdomId
            };
            snapshot.AddFocusedEntry(BuildEntry(context, focus,
                new List<Kingdom> { focus }, snapshot));
            IReadOnlyList<Kingdom> children = GetChildren(context, focus);
            for (int index = 0; index < children.Count; index++)
            {
                var territory = new List<Kingdom>();
                CollectSubtree(context, children[index], territory,
                    new HashSet<long>());
                snapshot.AddFocusedEntry(BuildEntry(context,
                    children[index], territory, snapshot));
            }
            return snapshot;
        }

        private static HierarchicalVassalMapModeSnapshot BuildRootSnapshot(
            HierarchyContext pContext)
        {
            var snapshot = new HierarchicalVassalMapModeSnapshot();
            var roots = new List<Kingdom>();
            foreach (KeyValuePair<long, Kingdom> pair in pContext.Kingdoms)
                if (EffectiveSuzerainId(pContext, pair.Key) < 0L)
                    roots.Add(pair.Value);
            roots.Sort(CompareKingdoms);

            for (int index = 0; index < roots.Count; index++)
            {
                var territory = new List<Kingdom>();
                CollectSubtree(pContext, roots[index], territory,
                    new HashSet<long>());
                snapshot.AddRootEntry(BuildEntry(pContext, roots[index],
                    territory, snapshot));
            }
            return snapshot;
        }

        private static HierarchyContext BuildContext()
        {
            var context = new HierarchyContext();
            try
            {
                if (World.world?.kingdoms == null) return context;
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (!IsValidKingdom(kingdom)) continue;
                    context.Kingdoms[kingdom.id] = kingdom;
                }
            }
            catch
            {
                return context;
            }

            foreach (KeyValuePair<long, Kingdom> pair in context.Kingdoms)
                context.RawSuzerainIds[pair.Key] = SafeSuzerainId(pair.Value);
            var resolutionStates = new Dictionary<long, byte>();
            var resolutionPath = new List<long>();
            var cyclicKingdomIds = new HashSet<long>();
            foreach (long kingdomId in context.Kingdoms.Keys)
                ResolveEffectiveSuzerainId(context, kingdomId,
                    resolutionStates, resolutionPath, cyclicKingdomIds);
            foreach (KeyValuePair<long, long> pair in
                     context.EffectiveSuzerainIds)
            {
                if (pair.Value < 0L ||
                    !context.Kingdoms.TryGetValue(pair.Key,
                        out Kingdom child)) continue;
                if (!context.DirectVassalsBySuzerainId.TryGetValue(pair.Value,
                        out List<Kingdom> children))
                {
                    children = new List<Kingdom>();
                    context.DirectVassalsBySuzerainId[pair.Value] = children;
                }
                children.Add(child);
            }
            foreach (List<Kingdom> children in
                     context.DirectVassalsBySuzerainId.Values)
                children.Sort(CompareKingdoms);
            return context;
        }

        private static void ResolveEffectiveSuzerainId(
            HierarchyContext pContext, long pKingdomId,
            Dictionary<long, byte> pStates, List<long> pPath,
            HashSet<long> pCyclicKingdomIds)
        {
            if (!pContext.Kingdoms.ContainsKey(pKingdomId)) return;
            if (pStates.TryGetValue(pKingdomId, out byte state))
            {
                if (state == 1)
                    MarkCyclePathInvalid(pPath, pCyclicKingdomIds);
                return;
            }

            pStates[pKingdomId] = 1;
            pPath.Add(pKingdomId);
            long suzerainId = RawSuzerainId(pContext, pKingdomId);
            if (suzerainId >= 0L &&
                pContext.Kingdoms.ContainsKey(suzerainId))
                ResolveEffectiveSuzerainId(pContext, suzerainId, pStates,
                    pPath, pCyclicKingdomIds);
            if (suzerainId >= 0L &&
                pCyclicKingdomIds.Contains(suzerainId))
                pCyclicKingdomIds.Add(pKingdomId);

            pContext.EffectiveSuzerainIds[pKingdomId] =
                pCyclicKingdomIds.Contains(pKingdomId) ||
                suzerainId < 0L ||
                !pContext.Kingdoms.ContainsKey(suzerainId)
                    ? -1L
                    : suzerainId;
            pPath.RemoveAt(pPath.Count - 1);
            pStates[pKingdomId] = 2;
        }

        private static void MarkCyclePathInvalid(IList<long> pPath,
            HashSet<long> pCyclicKingdomIds)
        {
            for (int index = 0; index < pPath.Count; index++)
                pCyclicKingdomIds.Add(pPath[index]);
        }

        private static long RawSuzerainId(HierarchyContext pContext,
            long pKingdomId)
        {
            return pContext.RawSuzerainIds.TryGetValue(pKingdomId,
                out long suzerainId) ? suzerainId : -1L;
        }

        private static long EffectiveSuzerainId(HierarchyContext pContext,
            long pKingdomId)
        {
            return pContext.EffectiveSuzerainIds.TryGetValue(pKingdomId,
                out long suzerainId) ? suzerainId : -1L;
        }

        private static IReadOnlyList<Kingdom> GetChildren(
            HierarchyContext pContext,
            Kingdom pSuzerain)
        {
            if (!IsValidKingdom(pSuzerain)) return EmptyKingdoms;
            return pContext.DirectVassalsBySuzerainId.TryGetValue(
                pSuzerain.id, out List<Kingdom> children)
                ? children
                : EmptyKingdoms;
        }

        private static void CollectSubtree(HierarchyContext pContext,
            Kingdom pRoot, List<Kingdom> pResult, HashSet<long> pVisited)
        {
            if (!IsValidKingdom(pRoot) || !pVisited.Add(pRoot.id)) return;
            pResult.Add(pRoot);
            IReadOnlyList<Kingdom> children = GetChildren(pContext, pRoot);
            for (int index = 0; index < children.Count; index++)
                CollectSubtree(pContext, children[index], pResult, pVisited);
        }

        private static HierarchicalVassalKingdomSnapshot BuildEntry(
            HierarchyContext pContext, Kingdom pKingdom,
            IList<Kingdom> pTerritoryKingdoms,
            HierarchicalVassalMapModeSnapshot pOwner)
        {
            var entry = new HierarchicalVassalKingdomSnapshot
            {
                KingdomId = pKingdom.id,
                Title = KingdomTitleService.GetTitle(pKingdom),
                SuzerainId = EffectiveSuzerainId(pContext, pKingdom.id),
                DisplayName = SafeDisplayName(pKingdom),
                ColorKey = SafeColorKey(pKingdom)
            };
            IReadOnlyList<Kingdom> direct = GetChildren(pContext, pKingdom);
            for (int index = 0; index < direct.Count; index++)
                entry.AddDirectVassal(direct[index].id);

            var seenTiles = new HashSet<Vector2Int>();
            for (int index = 0; index < pTerritoryKingdoms.Count; index++)
                AddKingdomTerritory(pTerritoryKingdoms[index], entry,
                    pOwner, seenTiles);
            entry.SortLandTiles(CompareTiles);
            HierarchicalVassalMapModeGeometryMetrics metrics =
                HierarchicalVassalMapModeGeometry.CalculateMetrics(
                    entry.LandTiles);
            entry.LabelDisplayName = HierarchicalVassalMapModeRules.
                FormatCountryLabel(entry.DisplayName, metrics.SpanX);
            entry.CountryLabelGap = HierarchicalVassalMapModeRules.
                CalculateCountryLabelGapLevel(entry.LabelDisplayName,
                    metrics.SpanX);
            HierarchicalVassalMapModeLabelPlacement placement =
                HierarchicalVassalMapModeGeometry.CalculateLabelPlacement(
                    entry.LandTiles, entry.LabelDisplayName,
                    entry.CountryLabelGap);
            entry.Area = metrics.Area;
            entry.Centroid = placement.Centroid;
            entry.LabelSize = placement.Size;
            entry.LabelAngle = placement.Angle;
            return entry;
        }

        private static void AddKingdomTerritory(Kingdom pTerritoryKingdom,
            HierarchicalVassalKingdomSnapshot pEntry,
            HierarchicalVassalMapModeSnapshot pOwner,
            HashSet<Vector2Int> pSeenTiles)
        {
            if (!IsValidKingdom(pTerritoryKingdom)) return;
            try
            {
                foreach (City city in pTerritoryKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pTerritoryKingdom ||
                        city.zones == null) continue;
                    HierarchicalVassalMapModeCityCacheEntry cached =
                        HierarchicalVassalMapModeCityCache.Get(city);
                    if (cached == null) continue;
                    for (int zoneIndex = 0;
                         zoneIndex < cached.VisibleZones.Count; zoneIndex++)
                    {
                        TileZone zone = cached.VisibleZones[zoneIndex];
                        if (zone.id >= 0)
                        {
                            pOwner.MapZone(zone.id, pEntry.KingdomId);
                            pOwner.AddDrawableZone(zone);
                            pEntry.AddDrawableZone(zone);
                        }
                    }
                    for (int tileIndex = 0;
                         tileIndex < cached.LandTiles.Count; tileIndex++)
                    {
                        Vector2Int position = cached.LandTiles[tileIndex];
                        if (pSeenTiles.Add(position))
                            pEntry.AddLandTile(position);
                    }
                }
            }
            catch
            {
                // A stale city or zone is skipped without invalidating the
                // rest of the deterministic snapshot.
            }
        }

        internal static bool IsVisibleLand(WorldTile pTile)
        {
            TileTypeBase type = pTile?.Type;
            return pTile?.data != null && type != null && type.ground &&
                   !type.liquid && !type.ocean && !type.lava;
        }

        internal static bool ContainsVisibleLand(TileZone pZone)
        {
            if (pZone?.tiles == null) return false;
            WorldTile[] tiles = pZone.tiles;
            for (int index = 0; index < tiles.Length; index++)
                if (IsVisibleLand(tiles[index])) return true;
            return false;
        }

        private static bool IsValidKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   pKingdom.isCiv() && !pKingdom.isNeutral();
        }

        private static bool IsVisibleCity(City pCity,
            HierarchicalVassalMapModeSnapshot pSnapshot)
        {
            if (pCity?.data == null || pCity.isRekt() ||
                pSnapshot?.ZoneToKingdomId == null || pCity.zones == null)
                return false;
            for (int index = 0; index < pCity.zones.Count; index++)
            {
                TileZone zone = pCity.zones[index];
                if (zone != null && pSnapshot.ZoneToKingdomId.ContainsKey(zone.id))
                    return true;
            }
            return false;
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
            catch { }
            return false;
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
            catch { }
            return false;
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
                System.Reflection.MethodInfo select = asset?.GetType()
                    .GetMethod("selectAndInspect");
                if (select == null) return false;
                System.Reflection.ParameterInfo[] parameters =
                    select.GetParameters();
                if (parameters.Length == 4)
                {
                    select.Invoke(asset, new object[] { pNanoObject,
                        false, false, false });
                    return true;
                }
                if (parameters.Length == 3)
                {
                    select.Invoke(asset, new object[] { pNanoObject,
                        false, false });
                    return true;
                }
                select.Invoke(asset, new object[] { pNanoObject });
                return true;
            }
            catch { return false; }
        }

        private static Kingdom GetKingdom(long pKingdomId)
        {
            if (pKingdomId < 0L || World.world?.kingdoms == null) return null;
            try
            {
                foreach (Kingdom kingdom in World.world.kingdoms)
                    if (kingdom?.id == pKingdomId) return kingdom;
            }
            catch { }
            return null;
        }

        private static bool ContainsKingdom(IReadOnlyList<Kingdom> pKingdoms,
            long pKingdomId)
        {
            if (pKingdoms == null) return false;
            for (int index = 0; index < pKingdoms.Count; index++)
                if (pKingdoms[index]?.id == pKingdomId) return true;
            return false;
        }

        private static long SafeSuzerainId(Kingdom pKingdom)
        {
            try { return VassalService.GetSuzerainId(pKingdom); }
            catch { return -1L; }
        }

        private static string SafeColorKey(Kingdom pKingdom)
        {
            try { return HistoryColors.FromKingdom(pKingdom) ?? string.Empty; }
            catch { return string.Empty; }
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

        private static int CompareTiles(Vector2Int pLeft, Vector2Int pRight)
        {
            int xOrder = pLeft.x.CompareTo(pRight.x);
            return xOrder != 0 ? xOrder : pLeft.y.CompareTo(pRight.y);
        }

        private sealed class SnapshotBuildWork
        {
            public readonly Kingdom Kingdom;
            public readonly List<Kingdom> Territory;

            public SnapshotBuildWork(Kingdom pKingdom,
                List<Kingdom> pTerritory)
            {
                Kingdom = pKingdom;
                Territory = pTerritory;
            }
        }

        private sealed class SnapshotBuildState
        {
            public readonly List<SnapshotBuildWork> Work =
                new List<SnapshotBuildWork>();
            public HierarchyContext Context;
            public HierarchicalVassalMapModeSnapshot Snapshot;
            public bool IsRoot;
            public long FocusKingdomId;
            public int Index;
        }

        private sealed class HierarchyContext
        {
            public readonly Dictionary<long, Kingdom> Kingdoms =
                new Dictionary<long, Kingdom>();
            public readonly Dictionary<long, long> EffectiveSuzerainIds =
                new Dictionary<long, long>();
            public readonly Dictionary<long, long> RawSuzerainIds =
                new Dictionary<long, long>();
            public readonly Dictionary<long, List<Kingdom>>
                DirectVassalsBySuzerainId =
                    new Dictionary<long, List<Kingdom>>();
        }

        private static readonly IReadOnlyList<Kingdom> EmptyKingdoms =
            new List<Kingdom>().AsReadOnly();
    }
}
