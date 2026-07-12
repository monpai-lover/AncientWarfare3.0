using System;

namespace AncientWarfare3.core.pathfinding
{
    internal static class AWPathfindingBootstrap
    {
        private static readonly AWTraversalCache TraversalCache = new AWTraversalCache();
        private static readonly AWPathRecoveryManager Recovery = new AWPathRecoveryManager();
        private static readonly AWPathDiagnostics Diagnostics = new AWPathDiagnostics();
        private static AWPathFinder _finder;
        private static AWPathOwnerState _lastState = AWPathOwnerState.Pending;
        private static bool _running;

        internal static AWPathFinder Finder => _finder;
        internal static AWTraversalCache Cache => TraversalCache;
        internal static AWPathRecoveryManager RecoveryManager => Recovery;
        internal static AWPathDiagnostics PathDiagnostics => Diagnostics;

        public static void PrepareOwnership()
        {
            PathfindingOwnershipService.Prepare();
        }

        public static void AfterPatchesRegistered()
        {
            PathfindingOwnershipService.BeginStabilization();
        }

        public static void ProcessFrame()
        {
            AWPathOwnerState state = PathfindingOwnershipService.ProcessMainThreadTick();
            if (state != _lastState)
            {
                AncientWarfare3.ModClass.LogInfo("AW3 pathfinding owner: " + state);
                _lastState = state;
            }

            if (state == AWPathOwnerState.Suspending || state == AWPathOwnerState.Cultiway)
            {
                StopOwnedPathfinder();
                return;
            }
            if (state != AWPathOwnerState.Aw3) return;
            EnsureStarted();
            if (!_running) return;
            TraversalCache.ProcessDirty(2);
            TraversalCache.ConsistencySweep(64);
        }

        public static void ClearWorld()
        {
            StopOwnedPathfinder();
            Recovery.Clear();
            PathfindingOwnershipService.ResetWorld();
            _lastState = AWPathOwnerState.Pending;
        }

        private static void EnsureStarted()
        {
            if (_running) return;
            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null || tiles.Length == 0 || MapBox.width <= 0 || MapBox.height <= 0) return;
            TraversalCache.Initialize();
            _finder = new AWPathFinder(new AWStreamingPathGenerator(), Diagnostics);
            _finder.Start(AWPathfindingConfig.WorkerCount(Environment.ProcessorCount));
            _running = true;
        }

        private static void StopOwnedPathfinder()
        {
            if (_finder != null)
            {
                _finder.Clear(AWPathFailureReason.WorldCleared);
                _finder.StopAndDrain();
                _finder.Dispose();
                _finder = null;
            }
            TraversalCache.Clear();
            _running = false;
        }
    }
}
