using System;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.policy
{
    public sealed class BoundaryWorkerCompletion
    {
        internal BoundaryWorkerCompletion(
            HierarchicalVassalBoundaryChunkSnapshot pSnapshot,
            BoundaryChunkDraftSet pDraft,
            string pFailureReason)
        {
            WorldGeneration = pSnapshot.WorldGeneration;
            ChunkKey = pSnapshot.ChunkKey;
            Revision = pSnapshot.Revision;
            Layer = pSnapshot.Layer;
            Fingerprint = pSnapshot.Fingerprint;
            Draft = pDraft;
            FailureReason = pFailureReason ?? string.Empty;
        }

        public long WorldGeneration { get; }
        public BoundaryChunkKey ChunkKey { get; }
        public long Revision { get; }
        public BoundaryDisplayLayer Layer { get; }
        public ulong Fingerprint { get; }
        public BoundaryChunkDraftSet Draft { get; }
        public string FailureReason { get; }
        public bool IsFailure { get { return Draft == null; } }
    }

    public sealed class HierarchicalVassalBoundaryTopologyWorker : IDisposable
    {
        private const int JoinTimeoutMilliseconds = 1000;
        private const int MaximumFailureLength = 256;

        private readonly object _gate = new object();
        private readonly AutoResetEvent _workSignal = new AutoResetEvent(false);
        private readonly Dictionary<WorkKey,
            HierarchicalVassalBoundaryChunkSnapshot> _pending =
            new Dictionary<WorkKey, HierarchicalVassalBoundaryChunkSnapshot>();
        private readonly Dictionary<WorkKey, long> _latestRevisions =
            new Dictionary<WorkKey, long>();
        private readonly Queue<WorkKey> _pendingOrder = new Queue<WorkKey>();
        private readonly Queue<BoundaryWorkerCompletion> _completions =
            new Queue<BoundaryWorkerCompletion>();
        private readonly Func<HierarchicalVassalBoundaryChunkSnapshot,
            BoundaryChunkDraftSet> _processor;
        private readonly Thread _workerThread;

        private int _worldChunkCount;
        private long _worldGeneration;
        private bool _needsRescan;
        private bool _stopping;
        private bool _disposed;

        public HierarchicalVassalBoundaryTopologyWorker(int pWorldChunkCount)
            : this(pWorldChunkCount, ProcessSnapshot)
        {
        }

        internal HierarchicalVassalBoundaryTopologyWorker(
            int pWorldChunkCount,
            Func<HierarchicalVassalBoundaryChunkSnapshot,
                BoundaryChunkDraftSet> pProcessor)
        {
            if (pWorldChunkCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(pWorldChunkCount));
            _worldChunkCount = pWorldChunkCount;
            _processor = pProcessor ??
                         throw new ArgumentNullException(nameof(pProcessor));
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "AW3 Hierarchical Boundary Topology"
            };
            _workerThread.Start();
        }

        public long WorldGeneration
        {
            get { lock (_gate) return _worldGeneration; }
        }

        public int PendingCount
        {
            get { lock (_gate) return _pending.Count; }
        }

        public int CompletionCount
        {
            get { lock (_gate) return _completions.Count; }
        }

        public bool NeedsRescan
        {
            get { lock (_gate) return _needsRescan; }
        }

        public long ResetWorld(int pWorldChunkCount)
        {
            if (pWorldChunkCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(pWorldChunkCount));
            lock (_gate)
            {
                ThrowIfDisposed();
                _worldGeneration = checked(_worldGeneration + 1L);
                _worldChunkCount = pWorldChunkCount;
                _pending.Clear();
                _pendingOrder.Clear();
                _completions.Clear();
                _latestRevisions.Clear();
                _needsRescan = false;
                return _worldGeneration;
            }
        }

        public bool Submit(HierarchicalVassalBoundaryChunkSnapshot pSnapshot)
        {
            if (pSnapshot == null)
                throw new ArgumentNullException(nameof(pSnapshot));
            lock (_gate)
            {
                if (_disposed || _stopping ||
                    pSnapshot.WorldGeneration != _worldGeneration)
                    return false;
                var key = new WorkKey(
                    pSnapshot.WorldGeneration, pSnapshot.ChunkKey);
                if (_latestRevisions.TryGetValue(key, out long latestRevision) &&
                    pSnapshot.Revision <= latestRevision)
                    return false;
                if (_pending.TryGetValue(key, out
                        HierarchicalVassalBoundaryChunkSnapshot previous))
                {
                    if (pSnapshot.Revision <= previous.Revision)
                        return false;
                    _latestRevisions[key] = pSnapshot.Revision;
                    _pending[key] = pSnapshot;
                    RemoveOlderCompletions(key, pSnapshot.Revision);
                    return true;
                }
                if (_pending.Count >= _worldChunkCount)
                {
                    _needsRescan = true;
                    return false;
                }
                if (!_latestRevisions.ContainsKey(key) &&
                    _latestRevisions.Count >= _worldChunkCount)
                {
                    _needsRescan = true;
                    return false;
                }
                _latestRevisions[key] = pSnapshot.Revision;
                RemoveOlderCompletions(key, pSnapshot.Revision);
                _pending.Add(key, pSnapshot);
                _pendingOrder.Enqueue(key);
                _workSignal.Set();
                return true;
            }
        }

        public bool TryTakeCompletion(out BoundaryWorkerCompletion pCompletion)
        {
            lock (_gate)
            {
                while (_completions.Count > 0)
                {
                    BoundaryWorkerCompletion candidate =
                        _completions.Dequeue();
                    var key = new WorkKey(
                        candidate.WorldGeneration, candidate.ChunkKey);
                    if (candidate.WorldGeneration != _worldGeneration ||
                        _latestRevisions.TryGetValue(
                            key, out long latestRevision) &&
                        candidate.Revision < latestRevision)
                        continue;
                    pCompletion = candidate;
                    return true;
                }
                pCompletion = null;
                return false;
            }
        }

        public bool TryConsumeRescanMarker(long pWorldGeneration)
        {
            lock (_gate)
            {
                if (!_needsRescan || pWorldGeneration != _worldGeneration)
                    return false;
                _needsRescan = false;
                return true;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _stopping = true;
                _pending.Clear();
                _pendingOrder.Clear();
                _completions.Clear();
                _latestRevisions.Clear();
                _needsRescan = false;
            }
            _workSignal.Set();
            _workerThread.Join(JoinTimeoutMilliseconds);
            _workSignal.Dispose();
        }

        private void WorkerLoop()
        {
            while (true)
            {
                if (!TryTakeWork(out
                        HierarchicalVassalBoundaryChunkSnapshot snapshot))
                {
                    lock (_gate)
                    {
                        if (_stopping) return;
                    }
                    _workSignal.WaitOne();
                    continue;
                }

                BoundaryWorkerCompletion completion;
                try
                {
                    BoundaryChunkDraftSet draft = _processor(snapshot);
                    if (draft == null)
                        throw new InvalidOperationException(
                            "Boundary processor returned no draft.");
                    completion = new BoundaryWorkerCompletion(
                        snapshot, draft, string.Empty);
                }
                catch (Exception exception)
                {
                    completion = new BoundaryWorkerCompletion(
                        snapshot, null, BoundedFailure(exception));
                }
                EnqueueCompletion(snapshot, completion);
            }
        }

        private bool TryTakeWork(
            out HierarchicalVassalBoundaryChunkSnapshot pSnapshot)
        {
            lock (_gate)
            {
                while (_pendingOrder.Count > 0)
                {
                    WorkKey key = _pendingOrder.Dequeue();
                    if (!_pending.TryGetValue(key, out pSnapshot))
                        continue;
                    _pending.Remove(key);
                    return true;
                }
                pSnapshot = null;
                return false;
            }
        }

        private void EnqueueCompletion(
            HierarchicalVassalBoundaryChunkSnapshot pSnapshot,
            BoundaryWorkerCompletion pCompletion)
        {
            lock (_gate)
            {
                if (_stopping ||
                    pSnapshot.WorldGeneration != _worldGeneration)
                    return;
                var key = new WorkKey(
                    pSnapshot.WorldGeneration, pSnapshot.ChunkKey);
                if (!_latestRevisions.TryGetValue(
                        key, out long latestRevision) ||
                    latestRevision != pSnapshot.Revision)
                    return;
                if (_pending.TryGetValue(key, out
                        HierarchicalVassalBoundaryChunkSnapshot newer) &&
                    newer.Revision > pSnapshot.Revision)
                    return;
                if (_completions.Count >= _worldChunkCount)
                {
                    _completions.Dequeue();
                    _needsRescan = true;
                }
                _completions.Enqueue(pCompletion);
            }
        }

        private void RemoveOlderCompletions(WorkKey pKey, long pRevision)
        {
            int count = _completions.Count;
            for (int i = 0; i < count; i++)
            {
                BoundaryWorkerCompletion completion = _completions.Dequeue();
                var completionKey = new WorkKey(
                    completion.WorldGeneration, completion.ChunkKey);
                if (completionKey.Equals(pKey) &&
                    completion.Revision < pRevision)
                    continue;
                _completions.Enqueue(completion);
            }
        }

        private static BoundaryChunkDraftSet ProcessSnapshot(
            HierarchicalVassalBoundaryChunkSnapshot pSnapshot)
        {
            if (!pSnapshot.ColorAssignment.IsValid)
                throw new InvalidOperationException(
                    "Invalid global color assignment: " +
                    pSnapshot.ColorAssignment.FailureReason);

            BoundaryCellRaster raster = pSnapshot.Cells;
            BoundaryChunkBounds bounds = InteriorBounds(raster);

            BoundaryTopologyDraft countryTopology =
                HierarchicalVassalBoundaryTopologyRules.Extract(
                    raster, BoundaryDisplayLayer.Countries);
            BoundaryTopologyDraft cityTopology =
                HierarchicalVassalBoundaryTopologyRules.Extract(
                    raster, BoundaryDisplayLayer.Cities);
            BoundaryRiverDraft countryRivers =
                HierarchicalVassalBoundaryRiverRules.Analyze(
                    raster, BoundaryDisplayLayer.Countries);
            BoundaryRiverDraft cityRivers =
                HierarchicalVassalBoundaryRiverRules.Analyze(
                    raster, BoundaryDisplayLayer.Cities);

            IReadOnlyList<BoundaryRibbonInput> countryInputs = BuildInputs(
                countryTopology, countryRivers, raster, bounds,
                pSnapshot.ColorAssignment);
            IReadOnlyList<BoundaryRibbonInput> cityInputs = BuildInputs(
                cityTopology, cityRivers, raster, bounds,
                pSnapshot.ColorAssignment);

            BoundaryHeightDraft height =
                HierarchicalVassalBoundaryHeightRules.Pack(
                    raster, HierarchicalVassalBoundaryChunkRules.ChunkSize,
                    HierarchicalVassalBoundaryChunkRules.Halo,
                    pSnapshot.Revision);
            BoundaryMeshDraft countryFill =
                HierarchicalVassalBoundaryMeshDraftRules
                    .BuildFillAuthoritative(raster,
                        BoundaryDisplayLayer.Countries, bounds,
                        pSnapshot.ColorAssignment);
            BoundaryMeshDraft cityFill =
                HierarchicalVassalBoundaryMeshDraftRules
                    .BuildFillAuthoritative(raster,
                        BoundaryDisplayLayer.Cities, bounds,
                        pSnapshot.ColorAssignment);
            if (countryFill.FailureCount != 0 || cityFill.FailureCount != 0)
                throw new InvalidOperationException(
                    "Authoritative boundary fill failed.");
            BoundaryMeshDraft countryRibbons =
                HierarchicalVassalBoundaryMeshDraftRules.BuildRibbons(
                    countryInputs, raster);
            BoundaryMeshDraft cityRibbons =
                HierarchicalVassalBoundaryMeshDraftRules.BuildRibbons(
                    cityInputs, raster);
            return new BoundaryChunkDraftSet(height,
                countryFill, cityFill, countryRibbons, cityRibbons,
                pSnapshot.ColorAssignment);
        }

        private static IReadOnlyList<BoundaryRibbonInput> BuildInputs(
            BoundaryTopologyDraft pTopology,
            BoundaryRiverDraft pRivers,
            BoundaryCellRaster pRaster,
            BoundaryChunkBounds pBounds,
            HierarchyColorAssignment pAssignment)
        {
            var result = new List<BoundaryRibbonInput>();
            AddTopologyInputs(result, pTopology.OpenChains,
                pTopology.ProtectedVertices, pRivers,
                pRaster, pBounds, pAssignment);
            AddTopologyInputs(result, pTopology.ClosedChains,
                pTopology.ProtectedVertices, pRivers,
                pRaster, pBounds, pAssignment);
            for (int i = 0; i < pRivers.PoliticalChains.Count; i++)
            {
                BoundaryPoliticalRiverChain river = pRivers.PoliticalChains[i];
                var raw = new List<BoundaryGridPoint>(river.Points.Count);
                for (int point = 0; point < river.Points.Count; point++)
                    raw.Add(new BoundaryGridPoint(
                        (int)Math.Round(river.Points[point].X),
                        (int)Math.Round(river.Points[point].Y)));
                if (raw.Count < 2) continue;
                var curve = new BoundaryCurveDraft(
                    river.Points, false, false, 1f);
                result.Add(CreateInput(curve, raw, river.Tier,
                    river.LeftOwnerId, river.RightOwnerId, true, pAssignment));
            }
            return result;
        }

        private static void AddTopologyInputs(
            List<BoundaryRibbonInput> pResult,
            IReadOnlyList<BoundaryChain> pChains,
            ISet<BoundaryGridPoint> pProtectedVertices,
            BoundaryRiverDraft pRivers,
            BoundaryCellRaster pRaster,
            BoundaryChunkBounds pBounds,
            HierarchyColorAssignment pAssignment)
        {
            for (int chainIndex = 0; chainIndex < pChains.Count; chainIndex++)
            {
                BoundaryChain chain = pChains[chainIndex];
                var run = new List<BoundaryGridPoint>();
                BoundaryTier runTier = BoundaryTier.None;
                long runLeft = -1L;
                long runRight = -1L;
                for (int edgeIndex = 0; edgeIndex < chain.Edges.Count; edgeIndex++)
                {
                    BoundaryRawEdge edge = chain.Edges[edgeIndex];
                    BoundaryGridPoint start = chain.Points[edgeIndex];
                    BoundaryGridPoint end = chain.Points[edgeIndex + 1];
                    if (pRivers.ShoreEdgesToSuppress.Contains(
                            new BoundaryGridEdgeKey(edge.Start, edge.End)))
                    {
                        AddRun(pResult, run, runTier, runLeft, runRight,
                            pProtectedVertices, pRaster, pBounds, pAssignment);
                        run.Clear();
                        continue;
                    }
                    long left = edge.Start.Equals(start)
                        ? edge.LeftOwnerId : edge.RightOwnerId;
                    long right = edge.Start.Equals(start)
                        ? edge.RightOwnerId : edge.LeftOwnerId;
                    if (run.Count > 0 &&
                        (runTier != edge.Tier || runLeft != left ||
                         runRight != right))
                    {
                        AddRun(pResult, run, runTier, runLeft, runRight,
                            pProtectedVertices, pRaster, pBounds, pAssignment);
                        run.Clear();
                    }
                    if (run.Count == 0)
                    {
                        run.Add(start);
                        runTier = edge.Tier;
                        runLeft = left;
                        runRight = right;
                    }
                    run.Add(end);
                }
                AddRun(pResult, run, runTier, runLeft, runRight,
                    pProtectedVertices, pRaster, pBounds, pAssignment);
            }
        }

        private static void AddRun(
            List<BoundaryRibbonInput> pResult,
            IReadOnlyList<BoundaryGridPoint> pRun,
            BoundaryTier pTier,
            long pLeftOwnerId,
            long pRightOwnerId,
            ISet<BoundaryGridPoint> pProtectedVertices,
            BoundaryCellRaster pRaster,
            BoundaryChunkBounds pBounds,
            HierarchyColorAssignment pAssignment)
        {
            if (pRun.Count < 2) return;
            if (pRun[0].Equals(pRun[pRun.Count - 1]))
            {
                int middle = pRun.Count / 2;
                AddOpenRun(pResult, CopyRange(pRun, 0, middle), pTier,
                    pLeftOwnerId, pRightOwnerId, pProtectedVertices,
                    pRaster, pBounds, pAssignment);
                AddOpenRun(pResult,
                    CopyRange(pRun, middle, pRun.Count - 1), pTier,
                    pLeftOwnerId, pRightOwnerId, pProtectedVertices,
                    pRaster, pBounds, pAssignment);
                return;
            }
            AddOpenRun(pResult, pRun, pTier, pLeftOwnerId, pRightOwnerId,
                pProtectedVertices, pRaster, pBounds, pAssignment);
        }

        private static void AddOpenRun(
            List<BoundaryRibbonInput> pResult,
            IReadOnlyList<BoundaryGridPoint> pRaw,
            BoundaryTier pTier,
            long pLeftOwnerId,
            long pRightOwnerId,
            ISet<BoundaryGridPoint> pProtectedVertices,
            BoundaryCellRaster pRaster,
            BoundaryChunkBounds pBounds,
            HierarchyColorAssignment pAssignment)
        {
            if (pRaw.Count < 2) return;
            var options = new BoundaryCurveOptions(pTier,
                pLeftOwnerId, pRightOwnerId, 0.45f,
                closed: false, allowRiverWater: false);
            BoundaryCurveDraft curve =
                HierarchicalVassalBoundaryCurveRules.Fit(
                    pRaw, pProtectedVertices, pRaster, options);
            if (pLeftOwnerId >= 0 && pRightOwnerId >= 0)
            {
                BoundaryVisualPairDraft pair =
                    HierarchicalVassalBoundaryPolygonRules.BuildVisualPair(
                        pRaster, pLeftOwnerId, pRightOwnerId, pTier,
                        ToFloat(pRaw), curve.Points, pBounds);
                if (!pair.IsValid)
                    curve = new BoundaryCurveDraft(
                        ToFloat(pRaw), false, true, 0f);
            }
            pResult.Add(CreateInput(curve, pRaw, pTier,
                pLeftOwnerId, pRightOwnerId, false, pAssignment));
        }

        private static IReadOnlyList<BoundaryGridPoint> CopyRange(
            IReadOnlyList<BoundaryGridPoint> pPoints,
            int pStart,
            int pEnd)
        {
            var result = new BoundaryGridPoint[pEnd - pStart + 1];
            for (int i = 0; i < result.Length; i++)
                result[i] = pPoints[pStart + i];
            return result;
        }


        private static BoundaryRibbonInput CreateInput(
            BoundaryCurveDraft pCurve,
            IReadOnlyList<BoundaryGridPoint> pRaw,
            BoundaryTier pTier,
            long pLeftOwnerId,
            long pRightOwnerId,
            bool pIsRiver,
            HierarchyColorAssignment pAssignment)
        {
            uint left = ColorFor(pAssignment, pTier, pLeftOwnerId);
            uint right = ColorFor(pAssignment, pTier, pRightOwnerId);
            BoundaryRibbonCoastSide coast = pLeftOwnerId < 0
                ? BoundaryRibbonCoastSide.Left
                : pRightOwnerId < 0
                    ? BoundaryRibbonCoastSide.Right
                    : BoundaryRibbonCoastSide.None;
            return new BoundaryRibbonInput(pCurve, pRaw, pTier,
                pLeftOwnerId, pRightOwnerId, left, right, pIsRiver, coast);
        }

        private static uint ColorFor(
            HierarchyColorAssignment pAssignment,
            BoundaryTier pTier,
            long pOwnerId)
        {
            if (pOwnerId < 0) return 0u;
            if (!pAssignment.TryGetColor(pTier, pOwnerId, out uint color))
                throw new InvalidOperationException(
                    "Global color assignment is missing " +
                    pTier + ":" + pOwnerId + ".");
            return color;
        }

        private static BoundaryChunkBounds InteriorBounds(
            BoundaryCellRaster pRaster)
        {
            int halo = HierarchicalVassalBoundaryChunkRules.Halo;
            return new BoundaryChunkBounds(
                pRaster.OriginX, pRaster.OriginY,
                pRaster.MaxXExclusive, pRaster.MaxYExclusive,
                pRaster.OriginX + halo, pRaster.OriginY + halo,
                pRaster.MaxXExclusive - halo,
                pRaster.MaxYExclusive - halo);
        }

        private static IReadOnlyList<BoundaryFloatPoint> ToFloat(
            IReadOnlyList<BoundaryGridPoint> pPoints)
        {
            var result = new BoundaryFloatPoint[pPoints.Count];
            for (int i = 0; i < pPoints.Count; i++)
                result[i] = new BoundaryFloatPoint(pPoints[i].X, pPoints[i].Y);
            return result;
        }

        private static string BoundedFailure(Exception pException)
        {
            string result = pException.GetType().Name + ": " + pException.Message;
            return result.Length <= MaximumFailureLength
                ? result
                : result.Substring(0, MaximumFailureLength);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        private readonly struct WorkKey : IEquatable<WorkKey>
        {
            public WorkKey(long pGeneration, BoundaryChunkKey pChunkKey)
            {
                Generation = pGeneration;
                ChunkKey = pChunkKey;
            }

            public long Generation { get; }
            public BoundaryChunkKey ChunkKey { get; }

            public bool Equals(WorkKey pOther)
            {
                return Generation == pOther.Generation &&
                       ChunkKey.Equals(pOther.ChunkKey);
            }

            public override bool Equals(object pValue)
            {
                return pValue is WorkKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return unchecked((Generation.GetHashCode() * 397) ^
                                 ChunkKey.GetHashCode());
            }
        }
    }
}
