using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;
using UnityEngine;

namespace AncientWarfare3.core.presentation
{
    internal static class ArmyRtsVisualizationService
    {
        private const float SelectionRefreshSeconds = 2f;
        private const float LineWidth = 0.075f;
        private const float MarkerScale = 0.22f;
        private const float MarkerHeight = 0.7f;
        private const int RouteSortingOrder = 12;
        private const int MaximumCandidatesReadPerFrame = 32;

        private sealed class PoolEntry
        {
            public long ArmyId = -1L;
            public GameObject Root;
            public LineRenderer[] Lines = new LineRenderer[2];
            public SpriteRenderer Marker;

            public void Hide()
            {
                ArmyId = -1L;
                if (Root != null && Root.activeSelf)
                    Root.SetActive(false);
            }
        }

        private static readonly List<PoolEntry> Pool =
            new List<PoolEntry>(
                ArmyRtsVisualizationRules.MaximumVisibleArmies);
        private static readonly List<ArmyRtsVisualizationCandidate>
            CandidateScratch =
                new List<ArmyRtsVisualizationCandidate>(
                    ArmyRtsVisualizationRules.MaximumVisibleArmies);
        private static readonly List<long> ArmyIdScratch = new List<long>();
        private static readonly long[] VisibleArmyIds =
            new long[ArmyRtsVisualizationRules.MaximumVisibleArmies];

        private static GameObject _root;
        private static Material _lineMaterial;
        private static Sprite _markerSprite;
        private static bool _reportedFailure;
        private static bool _initializationFailed;
        private static long _selectedKingdomId = -1L;
        private static int _visibleCount;
        private static int _refreshCursor;
        private static float _nextSelectionRefresh;
        private static long _selectionKingdomId = -1L;
        private static long _selectionAfterArmyId = -1L;
        private static bool _selectionInProgress;

        public static void SetEnabled(bool pEnabled)
        {
            if (AWPerformanceSettings.ShowArmyRtsVisuals == pEnabled) return;
            AWPerformanceSettings.SwitchArmyRtsVisuals(pEnabled);
            _reportedFailure = false;
            if (pEnabled) _initializationFailed = false;
            else ClearDisplayState();
        }

        public static void ProcessFrame()
        {
            try
            {
                Kingdom selected = SelectedMetas.selected_kingdom;
                long selectedId = selected?.data?.id ?? -1L;
                bool worldReady = Config.game_loaded &&
                                  !SmoothLoader.isLoading();
                if (!worldReady || _initializationFailed ||
                    !ArmyRtsVisualizationRules.ShouldDisplay(
                        ArmyRtsRuntimeMode.Current,
                        AWPerformanceSettings.ShowArmyRtsVisuals,
                        selectedId) ||
                    selected == null || selected.isRekt())
                {
                    ClearDisplayState();
                    return;
                }

                EnsurePool();
                float now = Time.unscaledTime;
                bool selectingCurrent = _selectionInProgress &&
                                        _selectionKingdomId == selectedId;
                if (selectedId != _selectedKingdomId && !selectingCurrent)
                    BeginSelection(selected, pClearVisible: true);
                else if (!_selectionInProgress &&
                         now >= _nextSelectionRefresh)
                    BeginSelection(selected, pClearVisible: false);
                if (_selectionInProgress)
                    ProcessSelectionBatch(selected, now);
                RefreshNextEntries(selected);
            }
            catch (Exception error)
            {
                ClearDisplayState();
                DestroyVisualizationObjects();
                _initializationFailed = true;
                if (_reportedFailure) return;
                _reportedFailure = true;
                ModClass.LogWarning(
                    "Army RTS visualization failed: " + error.Message);
            }
        }

        public static void ClearRuntime()
        {
            ClearDisplayState();
            _reportedFailure = false;
            _initializationFailed = false;
        }

        public static void Shutdown()
        {
            ClearRuntime();
            DestroyVisualizationObjects();
            for (var index = 0; index < VisibleArmyIds.Length; index++)
                VisibleArmyIds[index] = -1L;
        }

        private static void BeginSelection(Kingdom pKingdom,
            bool pClearVisible)
        {
            if (pClearVisible)
            {
                for (int i = 0; i < Pool.Count; i++) Pool[i].Hide();
                for (int i = 0; i < _visibleCount; i++)
                    VisibleArmyIds[i] = -1L;
                _selectedKingdomId = -1L;
                _visibleCount = 0;
                _refreshCursor = 0;
            }
            CandidateScratch.Clear();
            ArmyIdScratch.Clear();
            _selectionKingdomId = pKingdom.id;
            _selectionAfterArmyId = -1L;
            _selectionInProgress = true;
        }

        private static void ProcessSelectionBatch(Kingdom pKingdom,
            float pNow)
        {
            if (!_selectionInProgress ||
                _selectionKingdomId != pKingdom.id) return;
            ArmyStrategicIndexService.CopyArmyIdsAfter(pKingdom,
                _selectionAfterArmyId, MaximumCandidatesReadPerFrame,
                ArmyIdScratch, out bool complete);
            for (int i = 0; i < ArmyIdScratch.Count; i++)
            {
                Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                    ArmyIdScratch[i], pKingdom.id);
                if (army?.data == null ||
                    !ArmyRtsControllerService.TryGetProjection(army,
                        out ArmyRtsStrategicProjection projection) ||
                    !ArmyRtsControllerService.TryGetMission(army,
                        out ArmyRtsMission mission)) continue;
                ArmyRtsVisualizationRules.TryAddVisibleCandidate(
                    CandidateScratch,
                    new ArmyRtsVisualizationCandidate(army.id,
                        pKingdom.id, projection.State, mission.Role,
                        projection.PlayerOrder), pKingdom.id);
            }
            if (ArmyIdScratch.Count > 0)
                _selectionAfterArmyId =
                    ArmyIdScratch[ArmyIdScratch.Count - 1];
            if (complete) CommitSelection(pKingdom, pNow);
        }

        private static void CommitSelection(Kingdom pKingdom, float pNow)
        {
            bool changed = CandidateScratch.Count != _visibleCount;
            for (int i = 0; i < CandidateScratch.Count; i++)
            {
                long armyId = CandidateScratch[i].ArmyId;
                if (VisibleArmyIds[i] != armyId) changed = true;
                VisibleArmyIds[i] = armyId;
            }
            for (int i = CandidateScratch.Count; i < _visibleCount; i++)
                VisibleArmyIds[i] = -1L;

            _selectedKingdomId = pKingdom.id;
            _visibleCount = CandidateScratch.Count;
            _nextSelectionRefresh = pNow + SelectionRefreshSeconds;
            _selectionKingdomId = -1L;
            _selectionAfterArmyId = -1L;
            _selectionInProgress = false;
            if (!changed) return;

            _refreshCursor = 0;
            for (int i = 0; i < Pool.Count; i++) Pool[i].Hide();
            for (int i = 0; i < _visibleCount; i++)
                Pool[i].ArmyId = VisibleArmyIds[i];
        }

        private static void RefreshNextEntries(Kingdom pKingdom)
        {
            if (_visibleCount <= 0) return;
            int budget = Math.Min(
                ArmyRtsVisualizationRules.MaximumEntriesRefreshedPerFrame,
                _visibleCount);
            for (int i = 0; i < budget; i++)
            {
                int index = (_refreshCursor + i) % _visibleCount;
                PoolEntry entry = Pool[index];
                entry.ArmyId = VisibleArmyIds[index];
                RefreshEntry(entry, pKingdom);
            }
            _refreshCursor = (_refreshCursor + budget) % _visibleCount;
        }

        private static void RefreshEntry(PoolEntry pEntry,
            Kingdom pKingdom)
        {
            Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                pEntry.ArmyId, pKingdom.id);
            if (army?.data == null ||
                !ArmyRtsControllerService.TryGetProjection(army,
                    out ArmyRtsStrategicProjection projection) ||
                !ArmyRtsControllerService.TryGetMission(army,
                    out ArmyRtsMission mission))
            {
                pEntry.Hide();
                return;
            }

            Actor captain;
            try { captain = army.getCaptain(); }
            catch { captain = null; }
            bool hasTarget =
                ArmyRtsControllerService.TryGetMissionTarget(army,
                    out WorldTile targetTile);
            if (captain?.data == null || !captain.isAlive() ||
                captain.isRekt() || !hasTarget || targetTile == null)
            {
                pEntry.Hide();
                return;
            }

            Vector3 captainPosition = captain.current_position;
            Vector3 targetPosition = targetTile.posV3;
            bool hasAnchor = ArmyRtsControllerService.TryGetCaptainTarget(
                captain, out WorldTile anchorTile) && anchorTile != null &&
                anchorTile != targetTile;
            Vector3 anchorPosition = hasAnchor
                ? anchorTile.posV3
                : targetPosition;
            Color color = ResolveColor(
                ArmyRtsVisualizationRules.ColorFor(projection.State,
                    mission.Role));

            pEntry.Root.SetActive(true);
            SetSegment(pEntry.Lines[0], captainPosition, anchorPosition,
                color);
            if (hasAnchor)
                SetSegment(pEntry.Lines[1], anchorPosition,
                    targetPosition, color);
            else
                pEntry.Lines[1].enabled = false;
            pEntry.Marker.enabled = true;
            pEntry.Marker.color = color;
            pEntry.Marker.transform.position = targetPosition +
                Vector3.up * MarkerHeight;
        }

        private static void EnsurePool()
        {
            if (_root != null &&
                Pool.Count == ArmyRtsVisualizationRules.
                    MaximumVisibleArmies) return;

            GameObject root = null;
            Material material = null;
            var entries = new List<PoolEntry>(
                ArmyRtsVisualizationRules.MaximumVisibleArmies);
            try
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    throw new InvalidOperationException(
                        "Sprites/Default shader is unavailable.");
                material = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                Sprite markerSprite = SpriteTextureLoader.getSprite(
                                          "ui/Icons/iconArrowAttackTarget") ??
                                      SpriteTextureLoader.getSprite(
                                          "ui/Icons/iconAttack");
                root = new GameObject("AW3_ArmyRtsVisualization")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                UnityEngine.Object.DontDestroyOnLoad(root);
                string sortingLayer = ResolveSortingLayer();
                for (int i = 0;
                     i < ArmyRtsVisualizationRules.MaximumVisibleArmies; i++)
                {
                    PoolEntry entry = CreateEntry(i, root, material,
                        markerSprite, sortingLayer);
                    entry.Hide();
                    entries.Add(entry);
                }
            }
            catch
            {
                if (root != null) UnityEngine.Object.Destroy(root);
                if (material != null) UnityEngine.Object.Destroy(material);
                throw;
            }

            DestroyVisualizationObjects();
            _root = root;
            _lineMaterial = material;
            _markerSprite = entries.Count > 0
                ? entries[0].Marker.sprite
                : null;
            Pool.AddRange(entries);
        }

        private static PoolEntry CreateEntry(int pIndex, GameObject pRoot,
            Material pMaterial, Sprite pMarkerSprite,
            string pSortingLayer)
        {
            var entry = new PoolEntry
            {
                Root = new GameObject("ArmyRoute_" + pIndex)
            };
            entry.Root.transform.SetParent(pRoot.transform, false);
            for (int i = 0; i < entry.Lines.Length; i++)
            {
                var lineObject = new GameObject("Segment_" + i,
                    typeof(LineRenderer));
                lineObject.transform.SetParent(entry.Root.transform, false);
                LineRenderer line = lineObject.GetComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.startWidth = LineWidth;
                line.endWidth = LineWidth;
                line.numCapVertices = 2;
                line.sharedMaterial = pMaterial;
                line.sortingLayerName = pSortingLayer;
                line.sortingOrder = RouteSortingOrder;
                line.enabled = false;
                entry.Lines[i] = line;
            }

            var markerObject = new GameObject("TargetMarker",
                typeof(SpriteRenderer));
            markerObject.transform.SetParent(entry.Root.transform, false);
            entry.Marker = markerObject.GetComponent<SpriteRenderer>();
            entry.Marker.sprite = pMarkerSprite;
            entry.Marker.sortingLayerName = pSortingLayer;
            entry.Marker.sortingOrder = RouteSortingOrder + 1;
            entry.Marker.transform.localScale = Vector3.one * MarkerScale;
            entry.Marker.enabled = false;
            return entry;
        }

        private static void SetSegment(LineRenderer pLine,
            Vector3 pStart, Vector3 pEnd, Color pColor)
        {
            if (pLine == null ||
                (pStart - pEnd).sqrMagnitude < 0.01f)
            {
                if (pLine != null) pLine.enabled = false;
                return;
            }
            pLine.enabled = true;
            pLine.startColor = pColor;
            pLine.endColor = pColor;
            pLine.SetPosition(0, pStart);
            pLine.SetPosition(1, pEnd);
        }

        private static string ResolveSortingLayer()
        {
            string layer = "Default";
            try
            {
                SpriteRenderer worldRenderer =
                    World.world?.GetComponent<SpriteRenderer>();
                if (worldRenderer != null)
                    layer = worldRenderer.sortingLayerName;
            }
            catch { }
            return layer;
        }

        private static Color ResolveColor(ArmyRtsRouteColor pColor)
        {
            return pColor switch
            {
                ArmyRtsRouteColor.Red => new Color(0.95f, 0.18f,
                    0.14f, 0.88f),
                ArmyRtsRouteColor.Gold => new Color(1f, 0.72f,
                    0.12f, 0.9f),
                ArmyRtsRouteColor.Blue => new Color(0.18f, 0.55f,
                    1f, 0.9f),
                _ => new Color(1f, 1f, 1f, 0.82f)
            };
        }

        private static void ClearDisplayState()
        {
            if (_visibleCount > 0 || _selectedKingdomId >= 0L)
                for (int i = 0; i < Pool.Count; i++) Pool[i].Hide();
            for (int i = 0; i < _visibleCount; i++)
                VisibleArmyIds[i] = -1L;
            CandidateScratch.Clear();
            ArmyIdScratch.Clear();
            _selectedKingdomId = -1L;
            _visibleCount = 0;
            _refreshCursor = 0;
            _nextSelectionRefresh = 0f;
            _selectionKingdomId = -1L;
            _selectionAfterArmyId = -1L;
            _selectionInProgress = false;
        }

        private static void DestroyVisualizationObjects()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_lineMaterial != null)
                UnityEngine.Object.Destroy(_lineMaterial);
            Pool.Clear();
            _root = null;
            _lineMaterial = null;
            _markerSprite = null;
        }
    }
}
