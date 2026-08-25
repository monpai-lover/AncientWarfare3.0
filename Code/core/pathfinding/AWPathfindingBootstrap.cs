using System;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.pathfinding
{
    internal static class AWPathfindingBootstrap
    {
        private static readonly AWPathDiagnostics Diagnostics = new AWPathDiagnostics();
        private static readonly AWTraversalCache TraversalCache =
            new AWTraversalCache(Diagnostics);
        private static readonly AWPathRecoveryManager Recovery = new AWPathRecoveryManager();
        private static AWPathFinder _finder;
        private static bool _running;
        private static bool _traversalRunning;
        private static long _maintenanceFrame;
        private static int _lastMaintenanceRenderFrame = -1;

        internal static AWPathFinder Finder => _finder;
        internal static bool ReadyToIntercept =>
            AWTraversalCaptureRules.ReadyToIntercept(_traversalRunning,
                _running, _finder != null);
        internal static AWTraversalCache Cache => TraversalCache;
        internal static AWPathRecoveryManager RecoveryManager => Recovery;
        internal static AWPathDiagnostics PathDiagnostics => Diagnostics;

        public static void PrepareOwnership()
        {
            if (!AWPathfindingRuntimeMode.IsAw3) return;
            PathfindingOwnershipService.Prepare();
        }

        public static void AfterPatchesRegistered()
        {
            if (!AWPathfindingRuntimeMode.IsAw3) return;
            PathfindingOwnershipService.BeginStabilization();
        }

        public static void ProcessFrame()
        {
            if (!AWPathfindingRuntimeMode.IsAw3) return;
            long diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            AWPathOwnerState state;
            try
            {
                state = PathfindingOwnershipService.ProcessMainThreadTick();
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "path_owner_audit", diagnostic);
            }
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try
            {
                EnsureTraversalStarted();
                EnsureNavigationGrid();
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "path_traversal_ensure_started", diagnostic);
            }
            if (!_traversalRunning) return;
            if (state == AWPathOwnerState.Suspending ||
                state == AWPathOwnerState.Cultiway)
            {
                diagnostic = RuntimePerformanceDiagnostic.BeginScope();
                try { StopOwnedPathfinder(); }
                finally
                {
                    RuntimePerformanceDiagnostic.EndDetail(
                        "path_owner_stop", diagnostic);
                }
            }
            else if (state == AWPathOwnerState.Aw3)
            {
                diagnostic = RuntimePerformanceDiagnostic.BeginScope();
                try { EnsureStarted(); }
                finally
                {
                    RuntimePerformanceDiagnostic.EndDetail(
                        "path_ensure_started", diagnostic);
                }
            }
            int renderFrame = UnityEngine.Time.frameCount;
            if (_lastMaintenanceRenderFrame == renderFrame) return;
            _lastMaintenanceRenderFrame = renderFrame;
            if (state == AWPathOwnerState.Aw3)
                TraversalCache.ProcessPendingBuild();
            if (_maintenanceFrame < long.MaxValue) _maintenanceFrame++;
            int dirtyTileCount = TraversalCache.DirtyTileCount;
            if (AWTraversalCacheBudgetRules.ShouldProcessDirty(
                    _maintenanceFrame, dirtyTileCount))
            {
                diagnostic = RuntimePerformanceDiagnostic.BeginScope();
                try
                {
                    TraversalCache.ProcessDirty(
                        AWTraversalCacheBudgetRules.DirtyTileBudgetForFrame(
                            dirtyTileCount));
                }
                finally
                {
                    RuntimePerformanceDiagnostic.EndDetail(
                        "path_dirty_chunks", diagnostic);
                }
            }
            if (AWTraversalCacheBudgetRules.ShouldRunConsistencySweep(
                    _maintenanceFrame))
            {
                diagnostic = RuntimePerformanceDiagnostic.BeginScope();
                try
                {
                    TraversalCache.ConsistencySweep(
                        AWTraversalCacheBudgetRules.ConsistencyTileBudget);
                }
                finally
                {
                    RuntimePerformanceDiagnostic.EndDetail(
                        "path_consistency_sweep", diagnostic);
                }
            }
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try
            {
                Diagnostics.DrainAndMaybeLog(32,
                    _finder?.QueueDepth ?? 0,
                    _finder?.ActiveCount ?? 0,
                    _finder?.WorkerCount ?? 0,
                    _finder?.StaleWorkCount ?? 0, null);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "path_diagnostics_drain", diagnostic);
            }
        }

        /// <summary>
        /// Completes the Cultiway-style per-simulation-tick path lifecycle.
        /// Dirty traversal work remains budgeted in ProcessFrame; retry
        /// activation is centralized in the shared Finder and runs once at
        /// the simulation completion boundary.
        /// </summary>
        internal static void Tick()
        {
            if (!AWPathfindingRuntimeMode.IsAw3 || !_running) return;
            _finder?.Tick();
            TraversalCache.ProcessPendingBuild();
            AWPathNavigationGridService.FlushDirty();
        }

        public static void ClearWorld()
        {
            AWArmyMarchService.Clear();
            ArmyRouteProviderService.ClearWorld();
            if (!AWPathfindingRuntimeMode.IsAw3) return;
            StopOwnedPathfinder();
            TraversalCache.Clear();
            AWPathNavigationGridService.Clear();
            _traversalRunning = false;
            _maintenanceFrame = 0;
            _lastMaintenanceRenderFrame = -1;
            Recovery.Clear();
            AWDockTransportService.Clear();
            PathfindingOwnershipService.ResetWorld();
        }

        private static void EnsureStarted()
        {
            if (_running) return;
            EnsureTraversalStarted();
            if (!_traversalRunning) return;
            int workerCount =
                AWPerformanceSettings.ActorPathfindingWorkerCount;
            if (workerCount <= 0) return;
            _finder = new AWPathFinder(new AWStreamingPathGenerator(),
                Diagnostics, AWPathMovementBridge.CreateRecoveryRequest);
            _finder.Start(workerCount);
            _running = true;
        }

        private static void EnsureTraversalStarted()
        {
            if (_traversalRunning) return;
            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null || tiles.Length == 0 || MapBox.width <= 0 ||
                MapBox.height <= 0) return;
            TraversalCache.Initialize();
            _traversalRunning = TraversalCache.GenerationId >= 0;
        }

        private static void EnsureNavigationGrid()
        {
            if (AWPathNavigationGridService.Current != null) return;
            AWTraversalGeneration generation = TraversalCache.CurrentGeneration;
            if (generation != null)
                AWPathNavigationGridService.BuildFromTraversal(generation);
        }

        private static void StopOwnedPathfinder()
        {
            if (!_running && _finder == null) return;
            AWPathMovementBridge.Clear();
            if (_finder != null)
            {
                _finder.Clear(AWPathFailureReason.WorldCleared);
                _finder.StopAndDrain();
                _finder.Dispose();
                _finder = null;
            }
            _running = false;
        }
    }
}
