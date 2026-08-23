using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal enum HierarchicalVassalLabelBuildPhase
    {
        CollectTiles,
        ComputePureGeometry,
        Complete,
        Cancelled
    }

    internal readonly struct HierarchicalVassalLabelBuildBudget
    {
        internal readonly int TileUnits;
        internal readonly int ComponentUnits;
        internal readonly int AnchorUnits;
        internal readonly int EnvelopeSampleUnits;

        internal HierarchicalVassalLabelBuildBudget(int pTileUnits,
            int pComponentUnits, int pAnchorUnits,
            int pEnvelopeSampleUnits)
        {
            TileUnits = Clamp(pTileUnits);
            ComponentUnits = Clamp(pComponentUnits);
            AnchorUnits = Clamp(pAnchorUnits);
            EnvelopeSampleUnits = Clamp(pEnvelopeSampleUnits);
        }

        internal static HierarchicalVassalLabelBuildBudget TestUnlimited =>
            new HierarchicalVassalLabelBuildBudget(4096, 4096, 24, 4096);

        private static int Clamp(int pValue)
        {
            return pValue <= 0 ? 1 : pValue;
        }
    }

    internal readonly struct HierarchicalVassalLabelBuildProgress
    {
        internal readonly bool Completed;
        internal readonly bool Cancelled;
        internal readonly int ConsumedUnits;
        internal readonly HierarchicalVassalLabelBuildResult Result;

        internal HierarchicalVassalMapModeLabelPlacement Placement =>
            Result.Placement;

        internal HierarchicalVassalLabelBuildProgress(bool pCompleted,
            bool pCancelled, int pConsumedUnits,
            HierarchicalVassalLabelBuildResult pResult)
        {
            Completed = pCompleted;
            Cancelled = pCancelled;
            ConsumedUnits = pConsumedUnits;
            Result = pResult;
        }
    }

    internal readonly struct HierarchicalVassalLabelBuildResult
    {
        internal readonly HierarchicalVassalMapModeLabelPlacement Placement;
        internal readonly string DisplayText;
        internal readonly int CountryLabelGap;

        internal HierarchicalVassalLabelBuildResult(
            HierarchicalVassalMapModeLabelPlacement pPlacement,
            string pDisplayText, int pCountryLabelGap)
        {
            Placement = pPlacement;
            DisplayText = pDisplayText ?? string.Empty;
            CountryLabelGap = pCountryLabelGap;
        }
    }

    /// <summary>
    /// Copies live-derived tile coordinates in bounded main-thread slices,
    /// then runs the existing value-only geometry calculation away from the
    /// render thread. The worker never receives WorldBox or scene objects.
    /// </summary>
    internal sealed class HierarchicalVassalLabelBuildJob
    {
        private readonly long _realmId;
        private readonly string _displayName;
        private readonly int _countryLabelGap;
        private readonly IReadOnlyList<Vector2Int> _sourceTiles;
        private readonly IReadOnlyList<TileZone> _sourceZones;
        private readonly bool _cityLabel;
        private readonly bool _deriveCountryFormat;
        private readonly Vector2 _regionAnchor;
        private readonly bool _hasRegionAnchor;
        private readonly HashSet<Vector2Int> _uniqueTileSet =
            new HashSet<Vector2Int>();
        private readonly List<Vector2Int> _uniqueTiles =
            new List<Vector2Int>();
        private int _sourceIndex;
        private int _sourceZoneIndex;
        private int _sourceZoneTileIndex;
        private Task<HierarchicalVassalLabelBuildResult>
            _geometryTask;
        private readonly CancellationTokenSource _cancellation =
            new CancellationTokenSource();
        private HierarchicalVassalLabelBuildResult _result;

        private HierarchicalVassalLabelBuildJob(long pRealmId,
            string pDisplayName, IReadOnlyList<Vector2Int> pSourceTiles,
            int pCountryLabelGap)
        {
            _realmId = pRealmId;
            _displayName = pDisplayName ?? string.Empty;
            _sourceTiles = pSourceTiles ?? Array.Empty<Vector2Int>();
            _sourceZones = null;
            _countryLabelGap = pCountryLabelGap;
            _deriveCountryFormat = false;
            _regionAnchor = Vector2.zero;
            _hasRegionAnchor = false;
            Phase = HierarchicalVassalLabelBuildPhase.CollectTiles;
        }

        private HierarchicalVassalLabelBuildJob(long pRealmId,
            string pDisplayName, IReadOnlyList<TileZone> pSourceZones,
            bool pCityLabel, Vector2? pRegionAnchor)
        {
            _realmId = pRealmId;
            _displayName = pDisplayName ?? string.Empty;
            _sourceTiles = null;
            _sourceZones = pSourceZones ?? Array.Empty<TileZone>();
            _cityLabel = pCityLabel;
            _deriveCountryFormat = !pCityLabel;
            _regionAnchor = pRegionAnchor ?? Vector2.zero;
            _hasRegionAnchor = pRegionAnchor.HasValue;
            Phase = HierarchicalVassalLabelBuildPhase.CollectTiles;
        }

        internal long RealmId => _realmId;

        internal HierarchicalVassalLabelBuildPhase Phase { get; private set; }

        internal int UniqueTileCount => _uniqueTiles.Count;

        internal bool WorkerStoppedForDiagnostics => _geometryTask == null ||
            _geometryTask.IsCompleted;

        internal static HierarchicalVassalLabelBuildJob CreateForTest(
            long pRealmId, string pDisplayName,
            IReadOnlyList<Vector2Int> pTiles, int pCountryLabelGap)
        {
            return new HierarchicalVassalLabelBuildJob(pRealmId,
                pDisplayName, pTiles, pCountryLabelGap);
        }

        internal static HierarchicalVassalLabelBuildJob Create(
            long pRealmId, string pDisplayName,
            IReadOnlyList<Vector2Int> pTiles, int pCountryLabelGap)
        {
            return new HierarchicalVassalLabelBuildJob(pRealmId,
                pDisplayName, pTiles, pCountryLabelGap);
        }

        internal static HierarchicalVassalLabelBuildJob CreateFromZones(
            long pRealmId, string pDisplayName,
            IReadOnlyList<TileZone> pZones, bool pCityLabel,
            Vector2? pRegionAnchor = null)
        {
            return new HierarchicalVassalLabelBuildJob(pRealmId,
                pDisplayName, pZones, pCityLabel, pRegionAnchor);
        }

        internal void Cancel()
        {
            if (Phase == HierarchicalVassalLabelBuildPhase.Complete) return;
            _cancellation.Cancel();
            Phase = HierarchicalVassalLabelBuildPhase.Cancelled;
        }

        internal HierarchicalVassalLabelBuildProgress Advance(
            HierarchicalVassalLabelBuildBudget pBudget)
        {
            if (Phase == HierarchicalVassalLabelBuildPhase.Cancelled)
                return Progress(0);
            if (Phase == HierarchicalVassalLabelBuildPhase.Complete)
                return Progress(0);

            int consumed = 0;
            if (Phase == HierarchicalVassalLabelBuildPhase.CollectTiles)
            {
                int remaining = pBudget.TileUnits;
                if (_sourceZones != null)
                    CollectZoneTiles(ref remaining, ref consumed);
                else
                    CollectDirectTiles(ref remaining, ref consumed);
                if (!CollectionComplete)
                    return Progress(consumed);
                StartGeometryTask();
                return Progress(consumed);
            }

            if (Phase ==
                    HierarchicalVassalLabelBuildPhase.ComputePureGeometry &&
                _geometryTask != null && _geometryTask.IsCompleted)
            {
                if (_geometryTask.IsCanceled || _geometryTask.IsFaulted)
                {
                    Phase = HierarchicalVassalLabelBuildPhase.Cancelled;
                    _geometryTask = null;
                    return Progress(consumed);
                }
                _result = _geometryTask.GetAwaiter().GetResult();
                _geometryTask = null;
                Phase = HierarchicalVassalLabelBuildPhase.Complete;
            }
            return Progress(consumed);
        }

        private void StartGeometryTask()
        {
            if (_geometryTask != null || Phase ==
                    HierarchicalVassalLabelBuildPhase.Cancelled) return;
            IReadOnlyList<Vector2Int> frozenTiles = _uniqueTiles;
            string frozenName = _displayName;
            int frozenGap = _countryLabelGap;
            bool cityLabel = _cityLabel;
            bool deriveCountryFormat = _deriveCountryFormat;
            CancellationToken cancellationToken = _cancellation.Token;
            Phase = HierarchicalVassalLabelBuildPhase.ComputePureGeometry;
            _geometryTask = Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                HierarchicalVassalMapModeGeometryMetrics metrics =
                    HierarchicalVassalMapModeGeometry.CalculateMetrics(
                        frozenTiles, cancellationToken);
                if (!cityLabel)
                {
                    int gap = deriveCountryFormat
                        ? HierarchicalVassalMapModeRules.
                            CalculateCountryLabelGapLevel(frozenName,
                                metrics.SpanX)
                        : frozenGap;
                    string text = deriveCountryFormat
                        ? HierarchicalVassalMapModeRules.FormatCountryLabel(
                            frozenName, metrics.SpanX)
                        : frozenName;
                    HierarchicalVassalMapModeLabelPlacement placement =
                        HierarchicalVassalMapModeGeometry.
                            CalculateLabelPlacement(frozenTiles, text, gap,
                                cancellationToken);
                    return new HierarchicalVassalLabelBuildResult(placement,
                        text, gap);
                }
                var cityPlacement =
                    new HierarchicalVassalMapModeLabelPlacement
                    {
                        Centroid = _hasRegionAnchor
                            ? _regionAnchor : metrics.Centroid,
                        Angle = metrics.Angle,
                        Size = HierarchicalVassalMapModeGeometry.
                            CalculateCityLabelSize(metrics.Area)
                    };
                return new HierarchicalVassalLabelBuildResult(cityPlacement,
                    frozenName, 0);
            });
        }

        private bool CollectionComplete => _sourceZones != null
            ? _sourceZoneIndex >= _sourceZones.Count
            : _sourceIndex >= _sourceTiles.Count;

        private void CollectDirectTiles(ref int pRemaining,
            ref int pConsumed)
        {
            while (pRemaining-- > 0 &&
                   _sourceIndex < _sourceTiles.Count)
            {
                AddTile(_sourceTiles[_sourceIndex++]);
                pConsumed++;
            }
        }

        private void CollectZoneTiles(ref int pRemaining,
            ref int pConsumed)
        {
            while (pRemaining > 0 && _sourceZoneIndex < _sourceZones.Count)
            {
                TileZone zone = _sourceZones[_sourceZoneIndex];
                WorldTile[] tiles = zone?.tiles;
                if (tiles == null || _sourceZoneTileIndex >= tiles.Length)
                {
                    _sourceZoneIndex++;
                    _sourceZoneTileIndex = 0;
                    pRemaining--;
                    pConsumed++;
                    continue;
                }
                WorldTile tile = tiles[_sourceZoneTileIndex++];
                pRemaining--;
                pConsumed++;
                if (IsVisibleLand(tile))
                    AddTile(new Vector2Int(tile.x, tile.y));
            }
        }

        private void AddTile(Vector2Int pTile)
        {
            if (_uniqueTileSet.Add(pTile)) _uniqueTiles.Add(pTile);
        }

        private static bool IsVisibleLand(WorldTile pTile)
        {
            TileTypeBase type = pTile?.Type;
            return pTile?.data != null && type != null && type.ground &&
                   !type.liquid && !type.ocean && !type.lava;
        }

        private HierarchicalVassalLabelBuildProgress Progress(int pConsumed)
        {
            return new HierarchicalVassalLabelBuildProgress(
                Phase == HierarchicalVassalLabelBuildPhase.Complete,
                Phase == HierarchicalVassalLabelBuildPhase.Cancelled,
                pConsumed, _result);
        }
    }
}
