using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using AncientWarfare3.core.asyncwork;

namespace AncientWarfare3.core.pathfinding
{
    internal sealed class AWTraversalCache
    {
        private readonly Queue<int> _dirtyChunks = new Queue<int>();
        private readonly HashSet<int> _queuedChunks = new HashSet<int>();
        private readonly Dictionary<int, AWTraversalOverlayEntry> _overlay =
            new Dictionary<int, AWTraversalOverlayEntry>();
        private readonly AWPathDiagnostics _diagnostics;
        private AWTraversalGeneration _current;
        private AWTraversalGeneration _overlayGeneration;
        private WorldTile[] _initialCaptureTiles;
        private AWTileTraversalSnapshot[][] _initialChunks;
        private bool _initializing;
        private bool _initialBuildScheduled;
        private int _initialCaptureCursor;
        private int _generationId;
        private int _mainThreadId;
        private int _sweepCursor;
        private int _width;
        private int _height;
        private int _chunksWide;
        private long _sourceRevision;
        private bool _overlayBuildScheduled;
        private bool _regionTopologyDirty;

        public int GenerationId => (_overlayGeneration ?? _current)?.Id ?? -1;
        public long SourceRevision => _sourceRevision;
        public int DirtyChunkCount => _dirtyChunks.Count;
        public int PendingOverlayChunkCount => _overlay.Count;

        public int StartRegion(int pTileId)
        {
            AWTraversalGeneration generation = _overlayGeneration ?? _current;
            if (generation == null || pTileId < 0 ||
                pTileId >= generation.TileCount) return -1;
            int x = pTileId % generation.Width;
            int y = pTileId / generation.Width;
            return x / generation.ChunkSize +
                   y / generation.ChunkSize * generation.ChunksWide;
        }

        public AWTraversalCache(AWPathDiagnostics pDiagnostics = null)
        {
            _diagnostics = pDiagnostics;
        }

        public void Initialize()
        {
            AssertMainThread(pCaptureIfUnset: true);
            WorldTile[] tiles = World.world?.tiles_list;
            int width = MapBox.width;
            int height = MapBox.height;
            int tileCount = tiles?.Length ?? 0;
            if (!AWTraversalCaptureRules.MatchesMapSize(width, height,
                    tileCount))
            {
                if (_initializing) Clear();
                return;
            }
            if (_current != null) return;
            if (!_initializing || !ReferenceEquals(_initialCaptureTiles,
                    tiles) || _width != width || _height != height)
                BeginInitialCapture(tiles, width, height);
            CaptureInitialFrame();
        }

        private void BeginInitialCapture(WorldTile[] pTiles, int pWidth,
            int pHeight)
        {
            Clear();
            _width = pWidth;
            _height = pHeight;
            _chunksWide = Math.Max(1,
                (_width + AWTraversalGeneration.DefaultChunkSize - 1) /
                AWTraversalGeneration.DefaultChunkSize);
            _initialCaptureTiles = pTiles;
            _initialChunks = new AWTileTraversalSnapshot[
                AWTraversalCaptureRules.ChunkCount(_width, _height,
                    AWTraversalGeneration.DefaultChunkSize)][];
            _initialCaptureCursor = 0;
            _initializing = true;
        }

        private void CaptureInitialFrame()
        {
            if (!_initializing || _initialCaptureTiles == null ||
                _initialChunks == null) return;
            WorldTile[] liveTiles = World.world?.tiles_list;
            if (!ReferenceEquals(liveTiles, _initialCaptureTiles) ||
                !AWTraversalCaptureRules.MatchesMapSize(MapBox.width,
                    MapBox.height, liveTiles?.Length ?? 0) ||
                MapBox.width != _width || MapBox.height != _height)
            {
                Clear();
                return;
            }

            AWTraversalCaptureBatch batch = AWTraversalCaptureRules.NextBatch(
                _initialCaptureTiles.Length, _initialCaptureCursor,
                AWTraversalCaptureRules.InitialCaptureTileBudget);
            long started = Stopwatch.GetTimestamp();
            long budgetTicks = Math.Max(1L, (long)(Stopwatch.Frequency *
                AWTraversalCaptureRules.InitialCaptureBudgetMilliseconds /
                1000d));
            int end = batch.Start + batch.Count;
            var neighbors = new int[8];
            while (_initialCaptureCursor < end)
            {
                if (_initialCaptureCursor > batch.Start &&
                    Stopwatch.GetTimestamp() - started >= budgetTicks) break;
                AWTraversalChunkPlacement expected =
                    AWTraversalCaptureRules.PlacementForIndex(
                        _initialCaptureCursor, _width, _height,
                        AWTraversalGeneration.DefaultChunkSize);
                EnsureInitialChunk(expected);
                AWTileTraversalSnapshot snapshot = Capture(
                    _initialCaptureTiles[_initialCaptureCursor], neighbors);
                if (snapshot.Exists && snapshot.Id >= 0 &&
                    snapshot.Id < _initialCaptureTiles.Length)
                {
                    AWTraversalChunkPlacement placement =
                        AWTraversalCaptureRules.PlacementForCoordinates(
                            snapshot.X, snapshot.Y, _width, _height,
                            AWTraversalGeneration.DefaultChunkSize);
                    EnsureInitialChunk(placement);
                    if (placement.Valid)
                        _initialChunks[placement.ChunkId][
                            placement.LocalIndex] = snapshot;
                }
                _initialCaptureCursor++;
            }
            bool tileCaptureComplete =
                _initialCaptureCursor >= _initialCaptureTiles.Length;
            if (!tileCaptureComplete) return;
            int capturedThisFrame = _initialCaptureCursor - batch.Start;
            int dirtyChunkBudget = Math.Min(
                AWTraversalCacheBudgetRules.DirtyChunkBudget,
                AWTraversalCaptureRules.InitialDirtyChunkBudget(
                    capturedThisFrame,
                    AWTraversalCaptureRules.InitialCaptureTileBudget,
                    AWTraversalGeneration.DefaultChunkSize));
            CaptureInitialDirtyFrame(started, budgetTicks, dirtyChunkBudget);
            if (!AWTraversalRules.CanPublishInitialGeneration(
                    tileCaptureComplete,
                    pendingDirtyChunkCount: _dirtyChunks.Count)) return;
            if (AWTraversalBuildRules.ShouldScheduleInitialAsync(
                    AWAsyncRuntime.TraversalEnabled, _initialBuildScheduled,
                    tileCaptureComplete, _dirtyChunks.Count))
            {
                ScheduleInitialBuild();
                return;
            }
            if (_initialBuildScheduled) return;
            if (AWTraversalBuildRules.ShouldPublishInitialSynchronously(
                    AWAsyncRuntime.TraversalEnabled, _initialBuildScheduled))
                PublishInitialBuild(BuildInitialSnapshot());
        }

        private bool CaptureInitialDirtyFrame(long pStarted,
            long pBudgetTicks, int pChunkBudget)
        {
            int processed = 0;
            int chunkCount = _initialChunks?.Length ?? 0;
            var neighbors = new int[8];
            while (_dirtyChunks.Count > 0 &&
                   processed < Math.Max(0, pChunkBudget))
            {
                if (Stopwatch.GetTimestamp() - pStarted >= pBudgetTicks)
                    return false;
                int chunkId = _dirtyChunks.Dequeue();
                _queuedChunks.Remove(chunkId);
                if (chunkId < 0 || chunkId >= chunkCount) continue;
                _initialChunks[chunkId] = CaptureChunk(chunkId,
                    _initialCaptureTiles, neighbors);
                processed++;
            }
            return _dirtyChunks.Count == 0;
        }

        private void EnsureInitialChunk(AWTraversalChunkPlacement pPlacement)
        {
            if (!pPlacement.Valid || _initialChunks == null ||
                pPlacement.ChunkId < 0 ||
                pPlacement.ChunkId >= _initialChunks.Length ||
                _initialChunks[pPlacement.ChunkId] != null) return;
            int size = AWTraversalGeneration.DefaultChunkSize;
            _initialChunks[pPlacement.ChunkId] =
                new AWTileTraversalSnapshot[size * size];
        }

        public AWTraversalGeneration Pin()
        {
            AssertMainThread();
            return (_overlayGeneration ?? _current)?.Retain();
        }

        public void MarkDirty(WorldTile pTile)
        {
            AssertMainThread();
            if (pTile?.data == null || _width <= 0) return;
            if (_initializing)
            {
                IncrementSourceRevision();
                EnqueueDirtyChunk(pTile);
                return;
            }
            if (_current == null) return;
            if (RegionTopologyChanged(pTile)) _regionTopologyDirty = true;
            IncrementSourceRevision();
            EnqueueDirtyChunk(pTile);
        }

        private void IncrementSourceRevision()
        {
            _sourceRevision = _sourceRevision == long.MaxValue
                ? 1L
                : _sourceRevision + 1L;
        }

        private void EnqueueDirtyChunk(WorldTile pTile)
        {
            int chunkId = ChunkId(pTile.x, pTile.y);
            if (chunkId < 0 || !_queuedChunks.Add(chunkId)) return;
            _dirtyChunks.Enqueue(chunkId);
        }

        public int ProcessDirty(int pChunkBudget)
        {
            AssertMainThread();
            if (_current == null || pChunkBudget <= 0) return 0;
            if (AWAsyncRuntime.TraversalEnabled)
            {
                // Shadow is a comparison mode, not permission to rebuild the
                // complete traversal generation on the simulation thread.
                // Capture the small live-tile delta here and let the worker
                // assemble the generation and water components.
                if (_overlay.Count > 0 && !_overlayBuildScheduled)
                    ScheduleOverlayBuild();
                if (_dirtyChunks.Count == 0) return 0;
                return CaptureDirtyForAsyncBuild(pChunkBudget);
            }
            if (_dirtyChunks.Count == 0) return 0;
            return ProcessDirtySynchronously(pChunkBudget,
                pCompareShadow: AWAsyncRuntime.ShadowEnabled);
        }

        public void ProcessPendingBuild()
        {
            AssertMainThread();
            if (!AWAsyncRuntime.TraversalEnabled ||
                _overlay.Count == 0 || _overlayBuildScheduled) return;
            ScheduleOverlayBuild();
        }

        private int ProcessDirtySynchronously(int pChunkBudget,
            bool pCompareShadow)
        {
            WorldTile[] worldTiles = World.world?.tiles_list;
            if (worldTiles == null || worldTiles.Length != _current.TileCount) return 0;

            AWTileTraversalSnapshot[][] baseChunks =
                _current.CopyChunkReferences();
            AWTileTraversalSnapshot[][] chunks =
                (AWTileTraversalSnapshot[][])baseChunks.Clone();
            List<AWTraversalChunkCapture> captures = pCompareShadow
                ? new List<AWTraversalChunkCapture>()
                : null;
            int baseGenerationId = _current.Id;
            long revision = _sourceRevision;
            int processed = 0;
            var neighbors = new int[8];
            while (processed < pChunkBudget && _dirtyChunks.Count > 0)
            {
                int chunkId = _dirtyChunks.Dequeue();
                _queuedChunks.Remove(chunkId);
                if (chunkId < 0 || chunkId >= chunks.Length) continue;
                AWTileTraversalSnapshot[] captured = CaptureChunk(chunkId,
                    worldTiles, neighbors);
                chunks[chunkId] = captured;
                captures?.Add(new AWTraversalChunkCapture(chunkId,
                    revision, captured));
                processed++;
            }

            if (processed <= 0) return 0;
            AWTraversalGeneration previous = _current;
            _current = new AWTraversalGeneration(++_generationId, _width, _height,
                AWTraversalGeneration.DefaultChunkSize, chunks);
            if (pCompareShadow)
                ScheduleShadowBuild(baseGenerationId, revision, baseChunks,
                    captures, chunks);
            previous.Dispose();
            return processed;
        }

        private void ScheduleShadowBuild(int pBaseGenerationId,
            long pRevision, AWTileTraversalSnapshot[][] pBaseChunks,
            IReadOnlyList<AWTraversalChunkCapture> pCaptures,
            AWTileTraversalSnapshot[][] pSynchronousChunks)
        {
            if (pCaptures == null || pCaptures.Count == 0) return;
            var captures = pCaptures.ToArray();
            var chunkIds = captures.Select(pCapture => pCapture.ChunkId)
                .ToArray();
            long worldGeneration = AWAsyncRuntime.WorldGeneration;
            var input = new AWTraversalBuildInput(worldGeneration,
                pBaseGenerationId, pRevision, _width, _height,
                AWTraversalGeneration.DefaultChunkSize, pBaseChunks,
                captures);
            var synchronous = new AWTraversalBuildResult(worldGeneration,
                pBaseGenerationId, pRevision, _width, _height,
                AWTraversalGeneration.DefaultChunkSize,
                pSynchronousChunks);
            string expected = AWTraversalShadowRules.SummarizeChunks(
                synchronous, chunkIds);
            string key = "base=" + pBaseGenerationId + ",revision=" +
                         pRevision + ",chunks=" + string.Join(",", chunkIds);
            var execution = new AWTraversalBuildExecution(input);
            var commit = new AWTraversalShadowCommit(key, expected, chunkIds);
            var request = new AWAsyncWorkRequest("traversal-shadow-cache",
                AWAsyncLane.Traversal,
                new AWAsyncStamp(worldGeneration,
                    UnityEngine.Time.frameCount, pRevision),
                execution.Execute, commit.Commit,
                pCommitMode: AWAsyncCommitMode.Background);
            // A rejected shadow comparison must never execute its full build
            // synchronously. It is diagnostic-only and may be skipped when
            // the async worker is saturated.
            AWAsyncRuntime.TrySchedule(request);
        }

        private int CaptureDirtyForAsyncBuild(int pChunkBudget)
        {
            WorldTile[] worldTiles = World.world?.tiles_list;
            if (worldTiles == null || worldTiles.Length != _current.TileCount)
                return 0;
            long started = Stopwatch.GetTimestamp();
            long budgetTicks = Math.Max(1L,
                (long)(Stopwatch.Frequency * 0.75d / 1000d));
            int chunkSize = AWTraversalGeneration.DefaultChunkSize;
            int chunksHigh = Math.Max(1,
                (_height + chunkSize - 1) / chunkSize);
            int chunkCount = _chunksWide * chunksHigh;
            int processed = 0;
            var neighbors = new int[8];
            while (processed < pChunkBudget && _dirtyChunks.Count > 0)
            {
                if (processed > 0 && Stopwatch.GetTimestamp() - started >=
                    budgetTicks) break;
                int chunkId = _dirtyChunks.Dequeue();
                _queuedChunks.Remove(chunkId);
                if (chunkId < 0 || chunkId >= chunkCount) continue;
                AWTileTraversalSnapshot[] captured = CaptureChunk(chunkId,
                    worldTiles, neighbors);
                _overlay[chunkId] = new AWTraversalOverlayEntry(chunkId,
                    _sourceRevision, captured);
                processed++;
            }
            if (processed <= 0) return 0;
            _diagnostics?.AddTraversalChunksCaptured(processed);
            RefreshOverlayGeneration();
            ScheduleOverlayBuild();
            return processed;
        }

        public int ConsistencySweep(int pTileBudget)
        {
            AssertMainThread();
            if (_current == null || pTileBudget <= 0) return 0;
            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null || tiles.Length != _current.TileCount) return 0;

            int checkedTiles = 0;
            var neighbors = new int[8];
            while (checkedTiles < pTileBudget && tiles.Length > 0)
            {
                if (_sweepCursor >= tiles.Length) _sweepCursor = 0;
                WorldTile live = tiles[_sweepCursor++];
                AWTileTraversalSnapshot captured = Capture(live, neighbors);
                AWTraversalGeneration effective = _overlayGeneration ?? _current;
                if (!effective.TryGet(captured.Id, out AWTileTraversalSnapshot cached) ||
                    !Equivalent(cached, captured))
                    MarkDirty(live);
                checkedTiles++;
            }
            return checkedTiles;
        }

        public void Clear()
        {
            AssertMainThread(pCaptureIfUnset: true);
            _dirtyChunks.Clear();
            _queuedChunks.Clear();
            _overlay.Clear();
            _overlayGeneration?.Dispose();
            _overlayGeneration = null;
            _current?.Dispose();
            _current = null;
            _initialCaptureTiles = null;
            _initialChunks = null;
            _initializing = false;
            _initialBuildScheduled = false;
            _initialCaptureCursor = 0;
            _generationId = 0;
            _sweepCursor = 0;
            _width = 0;
            _height = 0;
            _chunksWide = 0;
            _sourceRevision = 0L;
            _overlayBuildScheduled = false;
            _regionTopologyDirty = false;
        }

        private AWTraversalBuildResult BuildInitialSnapshot()
        {
            var input = new AWTraversalBuildInput(AWAsyncRuntime.WorldGeneration,
                baseGenerationId: 0, sourceRevision: _sourceRevision,
                width: _width, height: _height,
                chunkSize: AWTraversalGeneration.DefaultChunkSize,
                baseChunks: (AWTileTraversalSnapshot[][])_initialChunks.Clone(),
                captures: Array.Empty<AWTraversalChunkCapture>());
            return AWTraversalBuildRules.Build(input);
        }

        private void ScheduleInitialBuild()
        {
            if (!_initializing || _initialBuildScheduled ||
                _initialChunks == null) return;
            long worldGeneration = AWAsyncRuntime.WorldGeneration;
            var input = new AWTraversalBuildInput(worldGeneration,
                baseGenerationId: 0, sourceRevision: _sourceRevision,
                width: _width, height: _height,
                chunkSize: AWTraversalGeneration.DefaultChunkSize,
                baseChunks: (AWTileTraversalSnapshot[][])_initialChunks.Clone(),
                captures: Array.Empty<AWTraversalChunkCapture>());
            var execution = new AWTraversalBuildExecution(input);
            var request = new AWAsyncWorkRequest("traversal-initial-cache",
                AWAsyncLane.Traversal,
                new AWAsyncStamp(worldGeneration, UnityEngine.Time.frameCount,
                    input.SourceRevision),
                execution.Execute, new AWInitialTraversalBuildCommit(this).Commit,
                HandleInitialBuildFault);
            _initialBuildScheduled = AWAsyncRuntime.TrySchedule(request);
        }

        private void HandleInitialBuildFault(Exception pError)
        {
            AssertMainThread();
            _initialBuildScheduled = false;
            _diagnostics?.OnTraversalBuildStale();
        }

        private void PublishInitialBuild(AWTraversalBuildResult pResult)
        {
            AssertMainThread();
            _initialBuildScheduled = false;
            if (!_initializing || _current != null || pResult == null ||
                pResult.WorldGeneration != AWAsyncRuntime.WorldGeneration ||
                pResult.BaseGenerationId != 0 ||
                pResult.SourceRevision > _sourceRevision ||
                pResult.Width != _width || pResult.Height != _height ||
                pResult.ChunkSize != AWTraversalGeneration.DefaultChunkSize ||
                pResult.Chunks.Length != AWTraversalCaptureRules.ChunkCount(
                    _width, _height, AWTraversalGeneration.DefaultChunkSize))
            {
                _diagnostics?.OnTraversalBuildStale();
                return;
            }
            _current = new AWTraversalGeneration(++_generationId,
                _width, _height, AWTraversalGeneration.DefaultChunkSize,
                pResult.Chunks, pResult.RegionTopology);
            _initialCaptureTiles = null;
            _initialChunks = null;
            _initialCaptureCursor = 0;
            _initializing = false;
            _sweepCursor = 0;
            _regionTopologyDirty = false;
            _diagnostics?.OnTraversalBuildPublished();
        }

        private void ScheduleOverlayBuild()
        {
            if (_current == null || _overlay.Count == 0) return;
            long revision = _sourceRevision;
            AWTraversalChunkCapture[] captures = _overlay.Values
                .OrderBy(pEntry => pEntry.ChunkId)
                .Select(pEntry => new AWTraversalChunkCapture(
                    pEntry.ChunkId, pEntry.SourceRevision, pEntry.Tiles))
                .ToArray();
            var input = new AWTraversalBuildInput(
                AWAsyncRuntime.WorldGeneration, _current.Id, revision,
                _width, _height, AWTraversalGeneration.DefaultChunkSize,
                _current.CopyChunkReferences(), captures,
                rebuildWaterConnectivity: _regionTopologyDirty);
            var execution = new AWTraversalBuildExecution(input);
            var commit = new AWTraversalBuildCommit(this);
            var request = new AWAsyncWorkRequest("traversal-cache",
                AWAsyncLane.Traversal,
                new AWAsyncStamp(input.WorldGeneration,
                    UnityEngine.Time.frameCount, input.SourceRevision),
                execution.Execute, commit.Commit,
                error => HandleBuildFault(error));
            if (AWAsyncRuntime.TrySchedule(request))
            {
                _overlayBuildScheduled = true;
                return;
            }
            if (!AWAsyncRuntime.TraversalEnabled)
            {
                _diagnostics?.OnTraversalSyncFallback();
                PublishBuild((AWTraversalBuildResult)execution.Execute(
                    CancellationToken.None));
            }
        }

        private void HandleBuildFault(Exception pError)
        {
            AssertMainThread();
            _overlayBuildScheduled = false;
            _diagnostics?.OnTraversalBuildStale();
            if (_current != null && _overlay.Count > 0)
                ScheduleOverlayBuild();
        }

        private void PublishBuild(AWTraversalBuildResult pResult)
        {
            AssertMainThread();
            int currentChunkCount = AWTraversalCaptureRules.ChunkCount(
                _width, _height, AWTraversalGeneration.DefaultChunkSize);
            if (!AWTraversalBuildRules.CanPublish(pResult,
                    AWAsyncRuntime.WorldGeneration, _current?.Id ?? -1,
                    _sourceRevision, currentWidth: _width,
                    currentHeight: _height,
                    currentChunkSize:
                    AWTraversalGeneration.DefaultChunkSize,
                    currentChunkCount: currentChunkCount))
            {
                _overlayBuildScheduled = false;
                _diagnostics?.OnTraversalBuildStale();
                if (_dirtyChunks.Count == 0 && _overlay.Count > 0)
                    ScheduleOverlayBuild();
                return;
            }
            bool reuseTopology = AWTraversalBuildRules.ShouldReuseRegionTopology(
                pResult.BaseGenerationId, _regionTopologyDirty);
            var next = new AWTraversalGeneration(++_generationId,
                pResult.Width, pResult.Height, pResult.ChunkSize,
                pResult.Chunks,
                reuseTopology
                    ? _current?.RegionTopology
                    : pResult.RegionTopology);
            AWTraversalGeneration previous = _current;
            _overlayBuildScheduled = false;
            _current = next;
            previous?.Dispose();
            _diagnostics?.OnTraversalBuildPublished();

            IReadOnlyList<AWTraversalOverlayEntry> remaining =
                AWTraversalBuildRules.RemoveCommittedOverlay(
                    _overlay.Values, pResult.SourceRevision);
            _overlay.Clear();
            foreach (AWTraversalOverlayEntry entry in remaining)
                _overlay[entry.ChunkId] = entry;
            if (remaining.Count == 0) _regionTopologyDirty = false;
            _overlayGeneration?.Dispose();
            _overlayGeneration = null;
            if (_overlay.Count > 0)
            {
                RefreshOverlayGeneration();
                ScheduleOverlayBuild();
            }
        }

        private void RefreshOverlayGeneration()
        {
            _overlayGeneration?.Dispose();
            _overlayGeneration = null;
            if (_current == null || _overlay.Count == 0) return;
            var chunks = new Dictionary<int, AWTileTraversalSnapshot[]>(
                _overlay.Count);
            foreach (AWTraversalOverlayEntry entry in _overlay.Values)
                if (entry.ChunkId >= 0)
                    chunks[entry.ChunkId] = entry.Tiles;
            _overlayGeneration = AWTraversalGeneration.FromOverlay(
                ++_generationId, _current, chunks);
        }

        private sealed class AWTraversalBuildCommit
        {
            private readonly AWTraversalCache _owner;

            public AWTraversalBuildCommit(AWTraversalCache pOwner)
            {
                _owner = pOwner;
            }

            public void Commit(object pResult)
            {
                _owner.PublishBuild(pResult as AWTraversalBuildResult);
            }
        }

        private bool RegionTopologyChanged(WorldTile pTile)
        {
            AWTraversalGeneration generation = _overlayGeneration ?? _current;
            if (generation == null || pTile?.data == null ||
                !generation.TryGet(pTile.data.tile_id,
                    out AWTileTraversalSnapshot cached)) return true;

            if (cached.RegionId != (pTile.region?.id ?? -1)) return true;
            WorldTile[] neighbours = pTile.neighboursAll;
            int liveCount = Math.Min(8, neighbours?.Length ?? 0);
            if (cached.NeighborCount != liveCount) return true;
            for (int i = 0; i < liveCount; i++)
            {
                int liveId = neighbours[i]?.data?.tile_id ?? -1;
                if (cached.GetNeighbor(i) != liveId) return true;
            }
            return false;
        }

        private sealed class AWInitialTraversalBuildCommit
        {
            private readonly AWTraversalCache _owner;

            public AWInitialTraversalBuildCommit(AWTraversalCache pOwner)
            {
                _owner = pOwner;
            }

            public void Commit(object pResult)
            {
                _owner.PublishInitialBuild(pResult as AWTraversalBuildResult);
            }
        }

        private sealed class AWTraversalShadowCommit
        {
            private readonly string _key;
            private readonly string _expected;
            private readonly int[] _chunkIds;

            public AWTraversalShadowCommit(string pKey, string pExpected,
                int[] pChunkIds)
            {
                _key = pKey ?? string.Empty;
                _expected = pExpected ?? string.Empty;
                _chunkIds = pChunkIds ?? Array.Empty<int>();
            }

            public void Commit(object pResult)
            {
                string actual = AWTraversalShadowRules.SummarizeChunks(
                    pResult as AWTraversalBuildResult, _chunkIds);
                string key = _key;
                AWAsyncShadowRuntime.CompareSummary("traversal", key,
                    _expected, actual);
            }
        }

        private AWTileTraversalSnapshot[] CaptureChunk(int pChunkId, WorldTile[] pTiles,
            int[] pNeighbors)
        {
            int size = AWTraversalGeneration.DefaultChunkSize;
            var result = new AWTileTraversalSnapshot[size * size];
            int chunkX = pChunkId % _chunksWide;
            int chunkY = pChunkId / _chunksWide;
            int startX = chunkX * size;
            int startY = chunkY * size;
            int endX = Math.Min(_width, startX + size);
            int endY = Math.Min(_height, startY + size);
            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    int tileId = x + y * _width;
                    int local = x - startX + (y - startY) * size;
                    result[local] = Capture(pTiles[tileId], pNeighbors);
                }
            }
            return result;
        }

        private static AWTileTraversalSnapshot Capture(WorldTile pTile, int[] pNeighbors)
        {
            if (pTile?.data == null) return default;
            for (int i = 0; i < pNeighbors.Length; i++) pNeighbors[i] = -1;
            WorldTile[] liveNeighbors = pTile.neighboursAll;
            int neighborCount = Math.Min(pNeighbors.Length, liveNeighbors?.Length ?? 0);
            for (int i = 0; i < neighborCount; i++)
                pNeighbors[i] = liveNeighbors[i]?.data?.tile_id ?? -1;

            TileTypeBase type = pTile.Type;
            bool fire;
            bool goodForBoat;
            try { fire = pTile.isOnFire(); }
            catch { fire = false; }
            try { goodForBoat = pTile.isGoodForBoat(); }
            catch { goodForBoat = false; }
            return new AWTileTraversalSnapshot(pTile.data.tile_id, pTile.x, pTile.y,
                ground: type?.ground ?? false,
                block: type?.block ?? false,
                liquid: type?.liquid ?? false,
                ocean: type?.ocean ?? false,
                lava: type?.lava ?? false,
                fire: fire,
                damageUnits: type?.damage_units ?? false,
                terrainDamage: type?.damage ?? 0f,
                walkMultiplier: type?.walk_multiplier ?? 1f,
                goodForBoat: goodForBoat,
                oceanComponent: -1,
                regionId: pTile.region?.id ?? -1,
                islandId: pTile.region?.island?.id ?? -1,
                pNeighbors: pNeighbors);
        }

        private int ChunkId(int pX, int pY)
        {
            if (pX < 0 || pY < 0 || pX >= _width || pY >= _height || _chunksWide <= 0) return -1;
            int size = AWTraversalGeneration.DefaultChunkSize;
            return pX / size + pY / size * _chunksWide;
        }

        private static bool Equivalent(AWTileTraversalSnapshot pLeft,
            AWTileTraversalSnapshot pRight)
        {
            if (pLeft.Id != pRight.Id || pLeft.Ground != pRight.Ground ||
                pLeft.Block != pRight.Block || pLeft.Liquid != pRight.Liquid ||
                pLeft.Ocean != pRight.Ocean || pLeft.Lava != pRight.Lava ||
                pLeft.Fire != pRight.Fire || pLeft.DamageUnits != pRight.DamageUnits ||
                pLeft.TerrainDamage != pRight.TerrainDamage ||
                pLeft.WalkMultiplier != pRight.WalkMultiplier ||
                pLeft.GoodForBoat != pRight.GoodForBoat ||
                pLeft.RegionId != pRight.RegionId ||
                pLeft.IslandId != pRight.IslandId ||
                pLeft.NeighborCount != pRight.NeighborCount) return false;
            for (int i = 0; i < pLeft.NeighborCount; i++)
                if (pLeft.GetNeighbor(i) != pRight.GetNeighbor(i)) return false;
            return true;
        }

        private void AssertMainThread(bool pCaptureIfUnset = false)
        {
            int current = Thread.CurrentThread.ManagedThreadId;
            if (_mainThreadId == 0 && pCaptureIfUnset) _mainThreadId = current;
            if (_mainThreadId != 0 && _mainThreadId != current)
                throw new InvalidOperationException("AW traversal cache must run on the main thread");
        }
    }
}
