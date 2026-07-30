using System;
using System.IO;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;

namespace AncientWarfare3.core.multiplayer
{
    internal static class AW3WorldLoadCoordinator
    {
        private static readonly object Gate = new object();
        private static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(2);

        private static bool _initialized;
        private static AW3MultiplayerWorldLoadOperation _operation;
        private static string _normalLoadDirectory = string.Empty;
        private static bool _normalWorldDataQueued;
        private static bool _generatedWorldQueued;

        [ThreadStatic]
        private static int _strictDispatchDepth;

        internal static void Initialize()
        {
            lock (Gate)
            {
                if (_initialized) return;
                MapBox.on_world_loaded += OnWorldLoaded;
                _initialized = true;
            }
        }

        internal static void Shutdown()
        {
            lock (Gate)
            {
                if (_initialized)
                    MapBox.on_world_loaded -= OnWorldLoaded;
                _initialized = false;
                _operation = null;
                _normalLoadDirectory = string.Empty;
                _normalWorldDataQueued = false;
                _generatedWorldQueued = false;
                _strictDispatchDepth = 0;
            }
        }

        internal static AW3MultiplayerWorldLoadStartResult
            TryBeginGenerationLoad(string canonicalDirectory)
        {
            AW3MultiplayerWorldLoadOperation operation;
            lock (Gate)
            {
                if (HasLiveOperation(_operation))
                    return AW3MultiplayerWorldLoadStartResult.Failure(
                        AW3MultiplayerWorldLoadError.Busy,
                        "Another world-load operation is active.");

                Guid operationId = Guid.NewGuid();
                operation = new AW3MultiplayerWorldLoadOperation(operationId,
                    canonicalDirectory, DateTime.UtcNow + LoadTimeout);
                canonicalDirectory = operation.Snapshot.GenerationDirectory;
                _operation = operation;
                _normalLoadDirectory = string.Empty;
                _normalWorldDataQueued = false;
                _generatedWorldQueued = false;
            }

            try
            {
                using (EnterStrictDispatch())
                    World.world.save_manager.loadWorld(canonicalDirectory, false);
            }
            catch (Exception error)
            {
                operation.FailWorldLoadFallback(
                    "WorldBox load invocation failed: " + error.Message);
            }

            return AW3MultiplayerWorldLoadStartResult.Success(
                operation.Snapshot.OperationId);
        }

        internal static AW3MultiplayerWorldLoadSnapshot GetStatus(
            Guid operationId)
        {
            lock (Gate)
            {
                if (_operation == null ||
                    _operation.Snapshot.OperationId != operationId)
                    return AW3MultiplayerWorldLoadSnapshot.Unknown(operationId);
                return _operation.Snapshot;
            }
        }

        internal static bool Cancel(Guid operationId)
        {
            lock (Gate)
            {
                if (_operation == null ||
                    _operation.Snapshot.OperationId != operationId)
                    return false;

                AW3MultiplayerWorldLoadSnapshot before = _operation.Snapshot;
                bool cancelled = _operation.Cancel();
                if (cancelled &&
                    before.State == AW3MultiplayerWorldLoadState.LoadingWorld)
                {
                    _normalLoadDirectory = before.GenerationDirectory;
                    _normalWorldDataQueued = true;
                }
                return cancelled;
            }
        }

        internal static void Tick()
        {
            lock (Gate)
                _operation?.TryTimeout(DateTime.UtcNow);
        }

        internal static void ObserveLoadWorldStarted(string path)
        {
            if (!TryCanonicalize(path, out string canonical)) return;

            lock (Gate)
            {
                if (_strictDispatchDepth > 0 && _operation != null &&
                    string.Equals(_operation.Snapshot.GenerationDirectory,
                        canonical, StringComparison.OrdinalIgnoreCase))
                    return;

                _normalLoadDirectory = canonical;
                _normalWorldDataQueued = false;
                _generatedWorldQueued = false;
            }
        }

        internal static void ObserveWorldDataQueued(string path)
        {
            if (!TryCanonicalize(path, out string canonical)) return;

            lock (Gate)
            {
                if (_operation != null &&
                    _operation.ObserveWorldDataQueued(canonical))
                {
                    _normalLoadDirectory = string.Empty;
                    _normalWorldDataQueued = false;
                    return;
                }

                if (string.Equals(_normalLoadDirectory, canonical,
                        StringComparison.OrdinalIgnoreCase))
                    _normalWorldDataQueued = true;
            }
        }

        internal static void ObserveGeneratedWorldQueued()
        {
            lock (Gate)
            {
                AW3MultiplayerWorldLoadOperation operation = _operation;
                operation?.FailWorldLoadFallback(
                    "WorldBox generated a fallback world.");
                _normalLoadDirectory = string.Empty;
                _normalWorldDataQueued = false;
                _generatedWorldQueued = true;
            }
        }

        private static void OnWorldLoaded()
        {
            AW3MultiplayerWorldLoadOperation operation = null;
            string strictDirectory = string.Empty;
            string normalDirectory = string.Empty;
            bool initializeGeneratedWorld = false;

            lock (Gate)
            {
                AW3MultiplayerWorldLoadOperation candidate = _operation;
                if (candidate != null && candidate.ObserveWorldLoaded())
                {
                    operation = candidate;
                    strictDirectory = candidate.Snapshot.GenerationDirectory;
                    _normalLoadDirectory = string.Empty;
                    _normalWorldDataQueued = false;
                    _generatedWorldQueued = false;
                }
                else if (_generatedWorldQueued)
                {
                    initializeGeneratedWorld = true;
                    _generatedWorldQueued = false;
                }
                else if (_normalWorldDataQueued)
                {
                    normalDirectory = _normalLoadDirectory;
                    _normalLoadDirectory = string.Empty;
                    _normalWorldDataQueued = false;
                }
            }

            if (operation != null)
            {
                AW3RestoreResult result;
                try
                {
                    result = AW3RuntimeRestorePipeline.TryRestoreFromDirectory(
                        strictDirectory, strict: true);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning(
                        "AW3 strict restore failed for operation " +
                        operation.Snapshot.OperationId + ": " + error);
                    operation.FailAw3Restore(
                        "runtime_restore: " + error.Message);
                    return;
                }
                if (result.Success)
                {
                    lock (Gate)
                    {
                        if (!ReferenceEquals(_operation, operation) ||
                            operation.Snapshot.State !=
                            AW3MultiplayerWorldLoadState.RestoringAw3)
                            return;
                        try
                        {
                            AWAsyncWorldLifecycle.StartWorld();
                            operation.CompleteRestore();
                        }
                        catch (Exception error)
                        {
                            operation.FailAw3Restore(
                                "async_world_start: " + error.Message);
                        }
                    }
                }
                else
                    operation.FailAw3Restore(RestoreFailureDetail(result));
                return;
            }

            if (initializeGeneratedWorld)
            {
                AW3RestoreResult result =
                    AW3RuntimeRestorePipeline.TryInitializeGeneratedWorld(
                        strict: false);
                if (result.Success) AWAsyncWorldLifecycle.StartWorld();
                LogNormalFailure("generated world", result);
                return;
            }

            if (!string.IsNullOrEmpty(normalDirectory))
            {
                AW3RestoreResult result =
                    AW3RuntimeRestorePipeline.TryRestoreFromDirectory(
                        normalDirectory, strict: false);
                if (result.Success) AWAsyncWorldLifecycle.StartWorld();
                LogNormalFailure("save load", result);
            }
        }

        private static void LogNormalFailure(string context,
            AW3RestoreResult result)
        {
            if (result.Success) return;
            ModClass.LogWarning("AW3 " + context + " restore failed at " +
                                RestoreFailureDetail(result));
        }

        private static string RestoreFailureDetail(AW3RestoreResult result)
        {
            return result.FailedStage + ": " + result.Detail;
        }

        private static bool HasLiveOperation(
            AW3MultiplayerWorldLoadOperation operation)
        {
            if (operation == null) return false;
            AW3MultiplayerWorldLoadState state = operation.Snapshot.State;
            return state != AW3MultiplayerWorldLoadState.Completed &&
                   state != AW3MultiplayerWorldLoadState.Failed &&
                   state != AW3MultiplayerWorldLoadState.Cancelled;
        }

        private static IDisposable EnterStrictDispatch()
        {
            _strictDispatchDepth++;
            return new StrictDispatchScope();
        }

        private sealed class StrictDispatchScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_strictDispatchDepth > 0) _strictDispatchDepth--;
            }
        }

        private static bool TryCanonicalize(string path,
            out string canonical)
        {
            canonical = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                return false;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string root = Path.GetPathRoot(fullPath) ?? string.Empty;
                while (fullPath.Length > root.Length &&
                       IsDirectorySeparator(fullPath[fullPath.Length - 1]))
                    fullPath = fullPath.Substring(0, fullPath.Length - 1);
                canonical = fullPath;
                return true;
            }
            catch (Exception error) when (error is ArgumentException ||
                                          error is NotSupportedException ||
                                          error is PathTooLongException)
            {
                return false;
            }
        }

        private static bool IsDirectorySeparator(char value)
        {
            return value == Path.DirectorySeparatorChar ||
                   value == Path.AltDirectorySeparatorChar;
        }
    }
}
