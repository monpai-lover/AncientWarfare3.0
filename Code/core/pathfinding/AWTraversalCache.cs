using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AncientWarfare3.core.asyncwork;

namespace AncientWarfare3.core.pathfinding
{
    internal sealed class AWTraversalCache
    {
        private readonly HashSet<int> _dirtyTiles = new HashSet<int>();
        private readonly Queue<int> _dirtyTileQueue = new Queue<int>();
        private readonly AWPathDiagnostics _diagnostics;
        private AWTraversalGeneration _current;
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
        private long _topologySourceRevision;
        private bool _topologyBuildScheduled;
        private bool _topologyDirty;
        // 无异步 traversal worker 时,全图拓扑重建改在这上面跑,由
        // PollBackgroundTopologyBuild 在主线程收口。见 ScheduleTopologyBuild。
        private Task<AWTraversalBuildResult> _topologyBuildTask;

        public int GenerationId => _current?.Id ?? -1;
        public long SourceRevision => _sourceRevision;
        internal long TopologySourceRevision => _topologySourceRevision;
        public int DirtyTileCount => _dirtyTiles.Count;
        public int DirtyChunkCount => DirtyTileCount;
        public int PendingOverlayChunkCount => 0;

        public int StartRegion(int pTileId)
        {
            AWTraversalGeneration generation = _current;
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
            if (_initialBuildScheduled) return;
            int capturedThisFrame = _initialCaptureCursor - batch.Start;
            int dirtyTileBudget = Math.Min(
                AWTraversalCacheBudgetRules.DirtyTileBudget,
                Math.Max(0, AWTraversalCaptureRules.InitialCaptureTileBudget -
                            capturedThisFrame));
            CaptureInitialDirtyFrame(started, budgetTicks, dirtyTileBudget);
            if (!AWTraversalRules.CanPublishInitialGeneration(
                tileCaptureComplete,
                    pendingDirtyChunkCount: _dirtyTiles.Count)) return;
            if (AWTraversalBuildRules.ShouldScheduleInitialAsync(
                AWAsyncRuntime.TraversalEnabled, _initialBuildScheduled,
                    tileCaptureComplete, _dirtyTiles.Count))
            {
                ScheduleInitialBuild();
                return;
            }
            if (AWTraversalBuildRules.ShouldPublishInitialSynchronously(
                    AWAsyncRuntime.TraversalEnabled, _initialBuildScheduled))
                PublishInitialBuild(BuildInitialSnapshot());
        }

        private bool CaptureInitialDirtyFrame(long pStarted,
            long pBudgetTicks, int pChunkBudget)
        {
            int processed = 0;
            var neighbors = new int[8];
            while (_dirtyTiles.Count > 0 &&
                   processed < Math.Max(0, pChunkBudget))
            {
                if (Stopwatch.GetTimestamp() - pStarted >= pBudgetTicks)
                    return false;
                int tileId = TakeDirtyTile();
                if (tileId < 0 || tileId >= _initialCaptureTiles.Length)
                    continue;
                AWTileTraversalSnapshot snapshot = Capture(
                    _initialCaptureTiles[tileId], neighbors);
                AWTraversalChunkPlacement placement =
                    AWTraversalCaptureRules.PlacementForCoordinates(
                        snapshot.X, snapshot.Y, _width, _height,
                        AWTraversalGeneration.DefaultChunkSize);
                EnsureInitialChunk(placement);
                if (placement.Valid)
                    _initialChunks[placement.ChunkId][placement.LocalIndex] =
                        snapshot;
                processed++;
            }
            return _dirtyTiles.Count == 0;
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
            return _current?.Retain();
        }

        public int OceanComponentOf(int pTileId)
        {
            AssertMainThread();
            return _current?.OceanComponentOf(pTileId) ?? -1;
        }

        public void MarkDirty(WorldTile pTile)
        {
            AssertMainThread();
            if (pTile?.data == null || _width <= 0) return;
            if (_initializing) IncrementSourceRevision();
            int tileId = pTile.data.tile_id;
            if (_dirtyTiles.Add(tileId)) _dirtyTileQueue.Enqueue(tileId);
        }

        private void IncrementSourceRevision()
        {
            _sourceRevision = _sourceRevision == long.MaxValue
                ? 1L
                : _sourceRevision + 1L;
        }

        private void IncrementTopologySourceRevision()
        {
            _topologySourceRevision = _topologySourceRevision == long.MaxValue
                ? 1L
                : _topologySourceRevision + 1L;
        }

        public int ProcessDirty(int pChunkBudget)
        {
            AssertMainThread();
            if (_current == null || pChunkBudget <= 0) return 0;
            if (_dirtyTiles.Count == 0) return 0;
            return ProcessDirtyTiles(pChunkBudget);
        }

        public void ProcessPendingBuild()
        {
            AssertMainThread();
            if (PollBackgroundTopologyBuild()) return;
            if (!_topologyDirty || _topologyBuildScheduled ||
                _dirtyTiles.Count > 0) return;
            ScheduleTopologyBuild();
        }

        /// <summary>
        /// 后台拓扑重建的主线程收口。返回 true 表示还在算,本帧不要再排一个。
        ///
        /// 实测这条路径造成过单帧 211.78ms(其中 sched_aw3_authority 占
        /// 207.796ms),对应 async_traversal_sync_fallback 从 0 变 1 —— 一次全图
        /// 拓扑重建同步跑在了权威帧上。
        /// </summary>
        private bool PollBackgroundTopologyBuild()
        {
            Task<AWTraversalBuildResult> task = _topologyBuildTask;
            if (task == null) return false;
            if (!task.IsCompleted) return true;
            _topologyBuildTask = null;
            if (task.IsFaulted || task.IsCanceled)
            {
                HandleTopologyBuildFault(task.Exception);
                return false;
            }

            // PublishTopologyBuild 会核对 WorldGeneration / BaseGenerationId /
            // SourceRevision,期间地形若又变过就按 stale 丢弃并复位
            // _topologyBuildScheduled,下一帧自然重排。
            PublishTopologyBuild(task.Result);
            return false;
        }

        private int ProcessDirtyTiles(int pTileBudget)
        {
            WorldTile[] worldTiles = World.world?.tiles_list;
            if (worldTiles == null || worldTiles.Length != _current.TileCount)
                return 0;
            bool compareShadow = AWAsyncRuntime.ShadowEnabled;
            int chunkCount = compareShadow
                ? AWTraversalCaptureRules.ChunkCount(_width, _height,
                    AWTraversalGeneration.DefaultChunkSize)
                : 0;
            AWTileTraversalSnapshot[][] beforeChunks = compareShadow
                ? new AWTileTraversalSnapshot[chunkCount][]
                : null;
            var changed = new List<AWTileTraversalSnapshot>(
                Math.Min(pTileBudget, _dirtyTiles.Count));
            var changedChunkIds = compareShadow
                ? new HashSet<int>()
                : null;
            var neighbors = new int[8];
            int processed = 0;
            while (processed < pTileBudget && _dirtyTiles.Count > 0)
            {
                int tileId = TakeDirtyTile();
                processed++;
                if (tileId < 0 || tileId >= worldTiles.Length) continue;
                AWTileTraversalSnapshot captured = Capture(
                    worldTiles[tileId], neighbors);
                if (!_current.TryGet(tileId,
                        out AWTileTraversalSnapshot cached) ||
                    Equivalent(cached, captured)) continue;
                bool topologyChanged = TopologyRelevantChanged(cached,
                    captured);
                if (topologyChanged)
                {
                    _topologyDirty = true;
                    IncrementTopologySourceRevision();
                }
                else
                    captured = captured.WithOceanComponent(
                        cached.OceanComponent);
                changed.Add(captured);
                if (compareShadow)
                {
                    int chunkId = ChunkId(captured.X, captured.Y);
                    if (chunkId >= 0 && chunkId < beforeChunks.Length &&
                        changedChunkIds.Add(chunkId))
                        beforeChunks[chunkId] =
                            _current.CopyChunkSnapshot(chunkId);
                }
            }
            if (changed.Count == 0) return processed;

            IncrementSourceRevision();
            _current.ApplyTileSnapshots(changed);
            _diagnostics?.AddTraversalChunksCaptured(changed.Count);
            if (compareShadow)
            {
                var afterChunks = new AWTileTraversalSnapshot[chunkCount][];
                var captures = new List<AWTraversalChunkCapture>(
                    changedChunkIds.Count);
                foreach (int chunkId in changedChunkIds)
                    if (chunkId >= 0 && chunkId < afterChunks.Length)
                    {
                        afterChunks[chunkId] =
                            _current.CopyChunkSnapshot(chunkId);
                        captures.Add(new AWTraversalChunkCapture(chunkId,
                            _sourceRevision, afterChunks[chunkId]));
                    }
                ScheduleShadowBuild(_current.Id, _sourceRevision,
                    beforeChunks, captures, afterChunks);
            }
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
                if (!_current.TryGet(captured.Id,
                        out AWTileTraversalSnapshot cached) ||
                    !Equivalent(cached, captured))
                    MarkDirty(live);
                checkedTiles++;
            }
            return checkedTiles;
        }

        public void Clear()
        {
            AssertMainThread(pCaptureIfUnset: true);
            _dirtyTiles.Clear();
            _dirtyTileQueue.Clear();
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
            _topologySourceRevision = 0L;
            _topologyBuildScheduled = false;
            _topologyDirty = false;
            // 只丢引用不等待:那个任务只读自己那份脱离的快照副本,算完没人接
            // 就自然消失;世代号也已经作废,即便被收口也会按 stale 丢掉。
            _topologyBuildTask = null;
        }

        private AWTraversalBuildResult BuildInitialSnapshot()
        {
            var input = new AWTraversalBuildInput(AWAsyncRuntime.WorldGeneration,
                baseGenerationId: 0, sourceRevision: _sourceRevision,
                width: _width, height: _height,
                chunkSize: AWTraversalGeneration.DefaultChunkSize,
                baseChunks: (AWTileTraversalSnapshot[][])_initialChunks.Clone(),
                captures: Array.Empty<AWTraversalChunkCapture>(),
                resultGenerationId: _generationId + 1);
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
                captures: Array.Empty<AWTraversalChunkCapture>(),
                resultGenerationId: _generationId + 1);
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
            AWTraversalGeneration prepared = pResult == null
                ? null
                : pResult.PreparedGeneration;
            if (!_initializing || _current != null || pResult == null ||
                pResult.WorldGeneration != AWAsyncRuntime.WorldGeneration ||
                pResult.BaseGenerationId != 0 ||
                pResult.SourceRevision > _sourceRevision ||
                pResult.Width != _width || pResult.Height != _height ||
                pResult.ChunkSize != AWTraversalGeneration.DefaultChunkSize ||
                prepared == null || prepared.Id != _generationId + 1 ||
                prepared.Width != _width || prepared.Height != _height ||
                pResult.Chunks.Length != AWTraversalCaptureRules.ChunkCount(
                    _width, _height, AWTraversalGeneration.DefaultChunkSize))
            {
                prepared?.Dispose();
                _diagnostics?.OnTraversalBuildStale();
                return;
            }
            _current = prepared;
            _generationId = prepared.Id;
            _initialCaptureTiles = null;
            _initialChunks = null;
            _initialCaptureCursor = 0;
            _initializing = false;
            _sweepCursor = 0;
            _topologyDirty = false;
            _diagnostics?.OnTraversalBuildPublished();
        }

        private void ScheduleTopologyBuild()
        {
            if (_current == null || !_topologyDirty ||
                _topologyBuildScheduled) return;
            long revision = _topologySourceRevision;
            long worldGeneration = AWAsyncRuntime.WorldGeneration;
            var execution = new AWTraversalTopologyBuildExecution(_current,
                worldGeneration, _current.Id, revision);
            var commit = new AWTraversalTopologyCommit(this);
            var request = new AWAsyncWorkRequest("traversal-topology-cache",
                AWAsyncLane.Traversal,
                new AWAsyncStamp(worldGeneration,
                    UnityEngine.Time.frameCount, revision),
                execution.Execute, commit.Commit,
                HandleTopologyBuildFault);
            if (AWAsyncRuntime.TrySchedule(request))
            {
                _topologyBuildScheduled = true;
                return;
            }
            if (!AWAsyncRuntime.TraversalEnabled)
            {
                // 这里原本是就地 Execute —— 全图海洋连通性 + 区域拓扑,实测一次
                // 205ms,直接把权威帧撑爆。改成后台算、主线程发布。
                //
                // 可以安全离线程:构造函数已经把全图 chunk 快照复制成一份脱离
                // 实时状态的数组,而 AWOceanConnectivityRules.Apply 与
                // AWRegionTopologySnapshot.Build 只读这份数组(不碰 World /
                // Unity / 缓存自身状态)。发布仍然只在主线程做,且过期校验原样
                // 保留,所以最坏情况只是白算一次。
                //
                // 复用上面那个 execution,不再 Dispose 掉重建一个:它的构造函数
                // 要把全图每个 chunk 的快照整份复制一遍,实测单次 26~28MB。
                // TrySchedule 在没有 compute worker 时必然失败,所以原来那对
                // 「Dispose + 重新 new」等于每次拓扑重建都白分配一份全图副本再
                // 扔掉。调度失败时 request 里的闭包永远不会被调用,execution 也
                // 没被 Execute 过(Execute 才会把 _chunks 取空),所以此处完好可用。
                _diagnostics?.OnTraversalSyncFallback();
                _topologyBuildScheduled = true;
                _topologyBuildTask = Task.Run(() =>
                {
                    try
                    {
                        return (AWTraversalBuildResult)execution.Execute(
                            CancellationToken.None);
                    }
                    finally { execution.Dispose(); }
                });
                return;
            }

            execution.Dispose();
        }

        private void HandleTopologyBuildFault(Exception pError)
        {
            AssertMainThread();
            _topologyBuildScheduled = false;
            _diagnostics?.OnTraversalBuildStale();
        }

        private void PublishTopologyBuild(AWTraversalBuildResult pResult)
        {
            AssertMainThread();
            bool current = pResult != null && _current != null &&
                           pResult.WorldGeneration ==
                           AWAsyncRuntime.WorldGeneration &&
                           pResult.BaseGenerationId == _current.Id &&
                           pResult.SourceRevision ==
                           _topologySourceRevision &&
                           pResult.Width == _width &&
                           pResult.Height == _height &&
                           pResult.ChunkSize ==
                           AWTraversalGeneration.DefaultChunkSize &&
                           pResult.RegionTopology != null;
            _topologyBuildScheduled = false;
            if (!current)
            {
                _diagnostics?.OnTraversalBuildStale();
                return;
            }
            _current.ReplaceTopologySnapshot(pResult.RegionTopology,
                pResult.Chunks);
            _topologyDirty = false;
            _diagnostics?.OnTraversalBuildPublished();
        }

        private sealed class AWTraversalTopologyCommit
        {
            private readonly AWTraversalCache _owner;

            public AWTraversalTopologyCommit(AWTraversalCache pOwner)
            {
                _owner = pOwner;
            }

            public void Commit(object pResult)
            {
                _owner.PublishTopologyBuild(
                    pResult as AWTraversalBuildResult);
            }
        }

        private static bool TopologyRelevantChanged(
            AWTileTraversalSnapshot pCached,
            AWTileTraversalSnapshot pCaptured)
        {
            if (pCached.Ground != pCaptured.Ground ||
                pCached.Block != pCaptured.Block ||
                pCached.Liquid != pCaptured.Liquid ||
                pCached.Ocean != pCaptured.Ocean ||
                pCached.Lava != pCaptured.Lava ||
                pCached.RegionId != pCaptured.RegionId ||
                pCached.IslandId != pCaptured.IslandId ||
                pCached.NeighborCount != pCaptured.NeighborCount)
                return true;
            for (int index = 0; index < pCached.NeighborCount; index++)
                if (pCached.GetNeighbor(index) !=
                    pCaptured.GetNeighbor(index)) return true;
            return false;
        }

        private int TakeDirtyTile()
        {
            while (_dirtyTileQueue.Count > 0)
            {
                int tileId = _dirtyTileQueue.Dequeue();
                if (_dirtyTiles.Remove(tileId)) return tileId;
            }
            return -1;
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
                pNeighbors: pNeighbors,
                hasType: type != null);
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
            if (pLeft.Id != pRight.Id || pLeft.HasType != pRight.HasType ||
                pLeft.Ground != pRight.Ground ||
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
