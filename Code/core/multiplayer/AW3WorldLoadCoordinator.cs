using System;
using System.IO;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;
using AncientWarfare3.core.county;
using AncientWarfare3.core.lineage;

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
        private static bool _worldLoadedHandled;

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
                _worldLoadedHandled = false;
                _strictDispatchDepth = 0;
            }
            CityReservePoolService.EndWorldLoadRestore();
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
                _worldLoadedHandled = false;
            }

            try
            {
                using (EnterStrictDispatch())
                    World.world.save_manager.loadWorld(canonicalDirectory, false);
            }
            catch (Exception error)
            {
                lock (Gate)
                {
                    AW3MultiplayerWorldLoadSnapshot before = operation.Snapshot;
                    if (operation.FailWorldLoadFallback(
                            "WorldBox load invocation failed: " + error.Message) &&
                        ReferenceEquals(_operation, operation))
                        PreservePendingWorldLoad(before);
                }
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
                if (cancelled) PreservePendingWorldLoad(before);
                return cancelled;
            }
        }

        internal static void Tick()
        {
            Tick(DateTime.UtcNow);
        }

        internal static void Tick(DateTime utcNow)
        {
            lock (Gate)
            {
                if (_operation == null) return;
                AW3MultiplayerWorldLoadSnapshot before = _operation.Snapshot;
                if (_operation.TryTimeout(utcNow)) PreservePendingWorldLoad(before);
            }
        }

        private static void PreservePendingWorldLoad(AW3MultiplayerWorldLoadSnapshot before)
        {
            // Cancelling/timing out the AW3 operation cannot cancel WorldBox's queue.
            // Restore its archive normally if that already dispatched world arrives later.
            if (before.State != AW3MultiplayerWorldLoadState.AwaitingWorldData &&
                before.State != AW3MultiplayerWorldLoadState.LoadingWorld) return;
            _normalLoadDirectory = before.GenerationDirectory;
            _normalWorldDataQueued = before.State == AW3MultiplayerWorldLoadState.LoadingWorld;
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

                _operation?.FailWorldLoadFallback(
                    "WorldBox started a different world-load request.");
                _normalLoadDirectory = canonical;
                _normalWorldDataQueued = false;
                _generatedWorldQueued = false;
                _worldLoadedHandled = false;
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
                _worldLoadedHandled = false;
            }
            CityReservePoolService.EndWorldLoadRestore();
        }

        private static void OnWorldLoaded()
        {
            AW3MultiplayerWorldLoadOperation operation = null;
            string strictDirectory = string.Empty;
            string normalDirectory = string.Empty;
            bool initializeGeneratedWorld = false;
            bool startUntrackedWorld = false;

            lock (Gate)
            {
                if (_worldLoadedHandled) return;
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
                else if (HasLiveOperation(candidate) ||
                         !string.IsNullOrEmpty(_normalLoadDirectory))
                    return;
                else if (AWAsyncRuntime.State == AWAsyncLifecycleState.Stopped)
                    startUntrackedWorld = true;
                _worldLoadedHandled = true;
            }

            try
            {
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

                if (initializeGeneratedWorld ||
                    !string.IsNullOrEmpty(normalDirectory))
                    RestoreNormalWorld(normalDirectory, initializeGeneratedWorld);
                else if (startUntrackedWorld)
                {
                    try
                    {
                        AWAsyncWorldLifecycle.StartWorld();
                    }
                    catch (Exception error)
                    {
                        ModClass.LogWarning(
                            "AW3 untracked world async start failed: " + error);
                    }
                }
            }
            finally
            {
                RunPostLoadRepair("reserve_pool_cleanup",
                    CityReservePoolService.EndWorldLoadRestore);
                RunPostLoadRepair("de_jure_regions",
                    DeJureRegionStore.RepairAfterWorldLoaded);
                RunPostLoadRepair("county_administration",
                    CountyAdministrationStore.RepairAfterWorldLoaded);
                // County reconstruction can create seats that the earlier restore
                // pipeline could not see. Register vacancies before identity sync.
                RunPostLoadRepair("county_vacancies",
                    CourtVacancyReconciliationService.InitializeCountyVacancies);
                RunPostLoadRepair("social_identity", () =>
                    SocialIdentityMigrationService.RepairAfterWorldLoaded());
                RunPostLoadRepair("clan_membership", () =>
                    ClanMembershipSyncService.RepairAfterWorldLoaded());
                RunPostLoadRepair("family_identity",
                    FamilyIdentitySyncService.RepairAfterWorldLoaded);
            }
        }

        private static void RestoreNormalWorld(string directory, bool generated)
        {
            string context = generated ? "generated world" : "save load";
            string stage = "runtime_restore";
            try
            {
                AW3RestoreResult result = generated
                    ? AW3RuntimeRestorePipeline.TryInitializeGeneratedWorld(
                        strict: false)
                    : AW3RuntimeRestorePipeline.TryRestoreFromDirectory(
                        directory, strict: false);
                if (result.Success)
                {
                    stage = "async_world_start";
                    AWAsyncWorldLifecycle.StartWorld();
                }
                LogNormalFailure(context, result);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("AW3 " + context + " failed at " +
                                    stage + ": " + error);
            }
        }

        private static void RunPostLoadRepair(string stage, Action repair)
        {
            try
            {
                repair();
            }
            catch (Exception error)
            {
                ModClass.LogWarning("AW3 post-load repair failed at " +
                                    stage + ": " + error);
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
