using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;
using AncientWarfare3.core.county;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.multiplayer;

public static class MapBox
{
    public static event Action on_world_loaded;
    public static void RaiseWorldLoaded() => on_world_loaded?.Invoke();
    public static void generateNewMap() { }
}

public static class World
{
    public static readonly TestWorld world = new TestWorld();
}

public sealed class TestWorld
{
    public TestSaveManager save_manager { get; } = new TestSaveManager();
    public TestBuildingManager buildings = new();
    public List<Kingdom> kingdoms = new();
    public List<Kingdom> kingdoms_wild = new();
}

public sealed class TestSaveManager
{
    public Action<string> LoadAction;
    public void loadWorld(string directory, bool allowFallback)
    {
        AW3WorldLoadCoordinator.ObserveLoadWorldStarted(directory);
        LoadAction?.Invoke(directory);
    }
}

public static class ModClass
{
    public static readonly List<string> Warnings = new List<string>();
    public static void LogWarning(string message) => Warnings.Add(message);
    public static void LogInfo(string message) { }
}

internal static class PostLoadRepairTrace
{
    public static readonly List<string> Calls = new();
}

namespace AncientWarfare3.core.asyncwork
{
    internal enum AWAsyncLifecycleState { Stopped, Running }

    internal static class AWAsyncRuntime
    {
        public static AWAsyncLifecycleState State;
    }

    internal static partial class AWAsyncWorldLifecycle
    {
        public static int StartCalls;
        public static Exception StartError;

        public static long StartWorld()
        {
            StartCalls++;
            if (StartError != null) throw StartError;
            AWAsyncRuntime.State = AWAsyncLifecycleState.Running;
            return StartCalls;
        }
    }
}

namespace AncientWarfare3.core.court
{
    internal static partial class DeJureRegionStore
    {
        public static int RepairCalls;
        internal static void RepairAfterWorldLoaded()
        { RepairCalls++; PostLoadRepairTrace.Calls.Add("de_jure"); }
    }
    internal static class CourtVacancyReconciliationService
    {
        internal static void InitializeCountyVacancies() =>
            PostLoadRepairTrace.Calls.Add("vacancies");
    }
}

namespace AncientWarfare3.core.county
{
    internal static partial class CountyAdministrationStore
    {
        public static int RepairCalls;
        public static Exception RepairError;
        internal static void RepairAfterWorldLoaded()
        {
            RepairCalls++;
            PostLoadRepairTrace.Calls.Add("county");
            if (RepairError != null) throw RepairError;
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    internal static class CityReservePoolService
    {
        public static int EndCalls;
        internal static void BeginWorldLoadRestore() { }
        internal static void EndWorldLoadRestore() => EndCalls++;
    }

    internal static class SocialIdentityMigrationService
    {
        public static int RepairCalls;
        internal static int RepairAfterWorldLoaded()
        { PostLoadRepairTrace.Calls.Add("social"); return ++RepairCalls; }
        internal static void ResetForNewWorld() { }
    }
    internal static class ClanMembershipSyncService
    {
        public static int ClearCalls;
        public static Exception RepairError;
        internal static int RepairAfterWorldLoaded()
        {
            PostLoadRepairTrace.Calls.Add("clan");
            if (RepairError != null) throw RepairError;
            return 0;
        }
        internal static void ClearRuntime() => ClearCalls++;
    }
    internal static class FamilyIdentitySyncService
    {
        public static int ClearCalls;
        internal static void RepairAfterWorldLoaded() => PostLoadRepairTrace.Calls.Add("family");
        internal static void ClearRuntime() => ClearCalls++;
    }
}

namespace AncientWarfare3.core.multiplayer
{
    internal sealed class AW3RestoreResult
    {
        private AW3RestoreResult(bool success, string stage, string detail)
        {
            Success = success;
            FailedStage = stage;
            Detail = detail;
        }

        internal bool Success { get; }
        internal string FailedStage { get; }
        internal string Detail { get; }
        internal static AW3RestoreResult Succeeded() =>
            new AW3RestoreResult(true, string.Empty, string.Empty);
        internal static AW3RestoreResult Failed(string stage, string detail) =>
            new AW3RestoreResult(false, stage, detail);
    }

    internal static class AW3RuntimeRestorePipeline
    {
        public static ManualResetEventSlim RestoreEntered;
        public static ManualResetEventSlim RestoreRelease;
        public static Exception RestoreError;
        public static AW3RestoreResult Result;
        public static readonly List<(string Directory, bool Strict)> RestoreCalls = new();
        public static int GeneratedCalls;

        internal static AW3RestoreResult TryRestoreFromDirectory(
            string directory, bool strict)
        {
            RestoreCalls.Add((directory, strict));
            RestoreEntered?.Set();
            RestoreRelease?.Wait(5000);
            if (RestoreError != null) throw RestoreError;
            return Result ?? AW3RestoreResult.Succeeded();
        }

        internal static AW3RestoreResult TryInitializeGeneratedWorld(bool strict)
        {
            GeneratedCalls++;
            if (RestoreError != null) throw RestoreError;
            return Result ?? AW3RestoreResult.Succeeded();
        }
    }
}

internal static class Program
{
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "aw3-world-load-coordinator-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            AW3WorldLoadCoordinator.Initialize();
            CancelDuringRestoreDoesNotRestartAsyncWorld(root);
            RestorePipelineExceptionBecomesRestoreFailure(root);
            StartFailureBecomesRestoreFailure(root);
            foreach (bool generated in new[] { false, true })
            {
                NormalLoadFailureDoesNotSkipRepairs(root, generated,
                    failStart: false);
                NormalLoadFailureDoesNotSkipRepairs(root, generated,
                    failStart: true);
                FailedMigrationDoesNotStartAsyncWorld(root, generated);
            }
            NormalLoadSupersedesStrictLoad(root, queued: false);
            NormalLoadSupersedesStrictLoad(root, queued: true);
            CancelBeforeWorldDataStillRestoresLoadedWorld(root);
            DuplicateWorldLoadedDoesNotRestartDuringRestore(root);
            RepairFailureDoesNotSkipRemainingRepairs(root);
            GeneratedFallbackInitializesTheGeneratedWorld(root);
            DuplicateCallbackDoesNotBypassFailedRestore(root, strict: false);
            DuplicateCallbackDoesNotBypassFailedRestore(root, strict: true);
            PendingOrdinaryLoadDoesNotStartBeforeWorldData(root);
            InvocationExceptionKeepsAlreadyQueuedWorld(root);
            DuplicateUntrackedCallbackDoesNotRepeatRepairs();
            TimedOutLoadStillRestoresArrivingWorld(root, queued: false);
            TimedOutLoadStillRestoresArrivingWorld(root, queued: true);
            BranchRepairsRunInOrder(root, failClan: false);
            BranchRepairsRunInOrder(root, failClan: true);
            Console.WriteLine("AW3 world-load coordinator isolated tests passed (24 scenarios).");
            SavePatchTests.Run();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            AW3RuntimeRestorePipeline.RestoreRelease?.Set();
            AW3WorldLoadCoordinator.Shutdown();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void CancelDuringRestoreDoesNotRestartAsyncWorld(string root)
    {
        Reset();
        AW3RuntimeRestorePipeline.RestoreEntered =
            new ManualResetEventSlim(false);
        AW3RuntimeRestorePipeline.RestoreRelease =
            new ManualResetEventSlim(false);
        AW3MultiplayerWorldLoadStartResult begin =
            AW3WorldLoadCoordinator.TryBeginGenerationLoad(root);
        True(begin.Accepted, "strict generation load starts");
        AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);

        Exception callbackError = null;
        var loadedThread = new Thread(() =>
        {
            try { MapBox.RaiseWorldLoaded(); }
            catch (Exception error) { callbackError = error; }
        }) { IsBackground = true };
        loadedThread.Start();
        True(AW3RuntimeRestorePipeline.RestoreEntered.Wait(2000),
            "strict restore enters before cancellation");
        True(AW3WorldLoadCoordinator.Cancel(begin.OperationId),
            "restoring generation can be cancelled");
        AW3RuntimeRestorePipeline.RestoreRelease.Set();
        True(loadedThread.Join(2000), "restore callback returns after release");

        True(callbackError == null,
            "cancelled restore does not leak an event callback failure");
        Equal(0, AncientWarfare3.core.asyncwork.AWAsyncWorldLifecycle.StartCalls,
            "cancelled restore never starts async world services");
        Equal(AW3MultiplayerWorldLoadState.Cancelled,
            AW3WorldLoadCoordinator.GetStatus(begin.OperationId).State,
            "cancelled restore remains terminally cancelled");
        AssertRepairsCompleted();
    }

    private static void StartFailureBecomesRestoreFailure(string root)
    {
        Reset();
        AncientWarfare3.core.asyncwork.AWAsyncWorldLifecycle.StartError =
            new InvalidOperationException("injected async start failure");
        AW3MultiplayerWorldLoadStartResult begin =
            AW3WorldLoadCoordinator.TryBeginGenerationLoad(root);
        True(begin.Accepted, "second strict generation load starts");
        AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);

        Exception callbackError = null;
        try { MapBox.RaiseWorldLoaded(); }
        catch (Exception error) { callbackError = error; }

        True(callbackError == null,
            "async start failure is contained by the load coordinator");
        AW3MultiplayerWorldLoadSnapshot status =
            AW3WorldLoadCoordinator.GetStatus(begin.OperationId);
        Equal(AW3MultiplayerWorldLoadState.Failed, status.State,
            "async start failure transitions restore to failed");
        Equal(AW3MultiplayerWorldLoadError.Aw3RestoreFailed, status.Error,
            "async start failure uses the AW3 restore error category");
        Contains(status.Detail, "injected async start failure",
            "async start failure preserves diagnostic detail");
        AssertRepairsCompleted();
    }

    private static void RestorePipelineExceptionBecomesRestoreFailure(
        string root)
    {
        Reset();
        AW3RuntimeRestorePipeline.RestoreError =
            new InvalidOperationException("injected restore failure");
        AW3MultiplayerWorldLoadStartResult begin =
            AW3WorldLoadCoordinator.TryBeginGenerationLoad(root);
        True(begin.Accepted, "strict generation load starts before restore fault");
        AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);

        Exception callbackError = null;
        try { MapBox.RaiseWorldLoaded(); }
        catch (Exception error) { callbackError = error; }

        True(callbackError == null,
            "restore pipeline failure is contained by the load coordinator");
        Equal(0, AncientWarfare3.core.asyncwork.AWAsyncWorldLifecycle.StartCalls,
            "failed restore pipeline never starts async world services");
        AW3MultiplayerWorldLoadSnapshot status =
            AW3WorldLoadCoordinator.GetStatus(begin.OperationId);
        Equal(AW3MultiplayerWorldLoadState.Failed, status.State,
            "restore pipeline failure transitions restore to failed");
        Equal(AW3MultiplayerWorldLoadError.Aw3RestoreFailed, status.Error,
            "restore pipeline failure uses the AW3 restore error category");
        Contains(status.Detail, "injected restore failure",
            "restore pipeline failure preserves diagnostic detail");
        string warnings = string.Join(Environment.NewLine, ModClass.Warnings);
        Contains(warnings, begin.OperationId.ToString(),
            "strict restore exception log identifies the operation");
        Contains(warnings, "System.InvalidOperationException",
            "strict restore exception log preserves the exception type and stack");
        AssertRepairsCompleted();
    }

    private static void Reset()
    {
        AW3WorldLoadCoordinator.Shutdown();
        AW3WorldLoadCoordinator.Initialize();
        AWAsyncRuntime.State = AWAsyncLifecycleState.Stopped;
        CityReservePoolService.EndCalls = 0;
        DeJureRegionStore.RepairCalls = 0;
        CountyAdministrationStore.RepairCalls = 0;
        CountyAdministrationStore.RepairError = null;
        SocialIdentityMigrationService.RepairCalls = 0;
        ClanMembershipSyncService.RepairError = null;
        PostLoadRepairTrace.Calls.Clear();
        AncientWarfare3.core.asyncwork.AWAsyncWorldLifecycle.StartCalls = 0;
        AncientWarfare3.core.asyncwork.AWAsyncWorldLifecycle.StartError = null;
        AW3RuntimeRestorePipeline.RestoreEntered = null;
        AW3RuntimeRestorePipeline.RestoreRelease = null;
        AW3RuntimeRestorePipeline.RestoreError = null;
        AW3RuntimeRestorePipeline.Result = null;
        AW3RuntimeRestorePipeline.RestoreCalls.Clear();
        AW3RuntimeRestorePipeline.GeneratedCalls = 0;
        World.world.save_manager.LoadAction = null;
        ModClass.Warnings.Clear();
    }

    private static void BranchRepairsRunInOrder(string root, bool failClan)
    {
        Reset();
        if (failClan) ClanMembershipSyncService.RepairError =
            new InvalidOperationException("injected clan repair failure");
        string directory = Path.Combine(root, "branch-repairs");
        AW3WorldLoadCoordinator.ObserveLoadWorldStarted(directory);
        AW3WorldLoadCoordinator.ObserveWorldDataQueued(directory);
        MapBox.RaiseWorldLoaded();
        Equal("de_jure,county,vacancies,social,clan,family",
            string.Join(",", PostLoadRepairTrace.Calls),
            "b branch repairs follow reconstructed counties and retain identity order even after failure");
        ClanMembershipSyncService.RepairError = null;
    }

    private static void NormalLoadFailureDoesNotSkipRepairs(string root,
        bool generated, bool failStart)
    {
        Reset();
        var error = new InvalidOperationException("injected normal load failure");
        if (failStart) AWAsyncWorldLifecycle.StartError = error;
        else AW3RuntimeRestorePipeline.RestoreError = error;
        if (generated)
            AW3WorldLoadCoordinator.ObserveGeneratedWorldQueued();
        else
        {
            AW3WorldLoadCoordinator.ObserveLoadWorldStarted(root);
            AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);
        }
        CityReservePoolService.EndCalls = 0;

        MapBox.RaiseWorldLoaded();

        Equal(failStart ? 1 : 0, AWAsyncWorldLifecycle.StartCalls,
            "normal world only starts when restoration succeeds");
        AssertRepairsCompleted();
        string warnings = string.Join(Environment.NewLine, ModClass.Warnings);
        Contains(warnings, "injected normal load failure",
            "normal load failures preserve diagnostics");
    }

    private static void NormalLoadSupersedesStrictLoad(string root, bool queued)
    {
        Reset();
        AW3MultiplayerWorldLoadStartResult begin =
            AW3WorldLoadCoordinator.TryBeginGenerationLoad(root);
        if (queued) AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);
        string replacement = Path.Combine(root, "replacement");

        AW3WorldLoadCoordinator.ObserveLoadWorldStarted(replacement);
        AW3WorldLoadCoordinator.ObserveWorldDataQueued(replacement);
        MapBox.RaiseWorldLoaded();

        Equal(1, AW3RuntimeRestorePipeline.RestoreCalls.Count,
            "replacement world is restored once");
        Equal((replacement, false), AW3RuntimeRestorePipeline.RestoreCalls[0],
            "ordinary replacement load cannot restore the strict world's archive");
        Equal(AW3MultiplayerWorldLoadError.WorldLoadFallback,
            AW3WorldLoadCoordinator.GetStatus(begin.OperationId).Error,
            "superseded strict load is terminally failed");
        Equal(1, AWAsyncWorldLifecycle.StartCalls,
            "replacement world starts its async services");
    }

    private static void FailedMigrationDoesNotStartAsyncWorld(string root, bool generated)
    {
        Reset();
        AW3RuntimeRestorePipeline.Result = AW3RestoreResult.Failed(
            "lineage_family_archive_migration", "injected migration failure");
        if (generated) AW3WorldLoadCoordinator.ObserveGeneratedWorldQueued();
        else
        {
            AW3WorldLoadCoordinator.ObserveLoadWorldStarted(root);
            AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);
        }
        CityReservePoolService.EndCalls = 0;

        MapBox.RaiseWorldLoaded();

        Equal(0, AWAsyncWorldLifecycle.StartCalls,
            "failed migration never starts the async writer for saved or generated worlds");
        AssertRepairsCompleted();
        Contains(string.Join(Environment.NewLine, ModClass.Warnings),
            "lineage_family_archive_migration", "failed migration stage is logged");
    }

    private static void CancelBeforeWorldDataStillRestoresLoadedWorld(string root)
    {
        Reset();
        AW3MultiplayerWorldLoadStartResult begin =
            AW3WorldLoadCoordinator.TryBeginGenerationLoad(root);
        True(AW3WorldLoadCoordinator.Cancel(begin.OperationId),
            "pending strict load can be cancelled");

        AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);
        MapBox.RaiseWorldLoaded();

        Equal(1, AW3RuntimeRestorePipeline.RestoreCalls.Count,
            "late loaded world still restores its AW3 archive");
        Equal((root, false), AW3RuntimeRestorePipeline.RestoreCalls[0],
            "cancelled pending strict load falls back to ordinary restoration");
        Equal(AW3MultiplayerWorldLoadState.Cancelled,
            AW3WorldLoadCoordinator.GetStatus(begin.OperationId).State,
            "ordinary restoration does not reopen the cancelled operation");
    }

    private static void DuplicateWorldLoadedDoesNotRestartDuringRestore(string root)
    {
        Reset();
        AW3RuntimeRestorePipeline.RestoreEntered = new ManualResetEventSlim(false);
        AW3RuntimeRestorePipeline.RestoreRelease = new ManualResetEventSlim(false);
        AW3MultiplayerWorldLoadStartResult begin =
            AW3WorldLoadCoordinator.TryBeginGenerationLoad(root);
        AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);
        Exception callbackError = null;
        var loadedThread = new Thread(() =>
        {
            try { MapBox.RaiseWorldLoaded(); }
            catch (Exception error) { callbackError = error; }
        }) { IsBackground = true };
        loadedThread.Start();
        try
        {
            True(AW3RuntimeRestorePipeline.RestoreEntered.Wait(2000),
                "restore enters before duplicate callback");
            MapBox.RaiseWorldLoaded();
            Equal(0, AWAsyncWorldLifecycle.StartCalls,
                "duplicate callback cannot start async services during restoration");
            Equal(0, CountyAdministrationStore.RepairCalls,
                "duplicate callback cannot repair a partially restored world");
        }
        finally
        {
            AW3RuntimeRestorePipeline.RestoreRelease.Set();
            True(loadedThread.Join(2000), "restore completes after release");
        }
        True(callbackError == null, "strict restoration succeeds");
        Equal(1, AWAsyncWorldLifecycle.StartCalls,
            "strict world starts exactly once after restoration");
        Equal(AW3MultiplayerWorldLoadState.Completed,
            AW3WorldLoadCoordinator.GetStatus(begin.OperationId).State,
            "strict load completes after restoration");
        AssertRepairsCompleted();
    }

    private static void RepairFailureDoesNotSkipRemainingRepairs(string root)
    {
        Reset();
        CountyAdministrationStore.RepairError =
            new InvalidOperationException("injected county repair failure");
        AW3WorldLoadCoordinator.ObserveLoadWorldStarted(root);
        AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);

        MapBox.RaiseWorldLoaded();

        AssertRepairsCompleted();
        Contains(string.Join(Environment.NewLine, ModClass.Warnings),
            "injected county repair failure", "failed repair is logged");
    }

    private static void GeneratedFallbackInitializesTheGeneratedWorld(string root)
    {
        Reset();
        AW3MultiplayerWorldLoadStartResult begin =
            AW3WorldLoadCoordinator.TryBeginGenerationLoad(root);
        AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);

        AW3WorldLoadCoordinator.ObserveGeneratedWorldQueued();
        MapBox.RaiseWorldLoaded();

        Equal(0, AW3RuntimeRestorePipeline.RestoreCalls.Count,
            "generated fallback does not use the failed generation archive");
        Equal(1, AW3RuntimeRestorePipeline.GeneratedCalls,
            "generated fallback initializes fresh AW3 state");
        Equal(1, AWAsyncWorldLifecycle.StartCalls,
            "generated fallback starts async services");
        Equal(AW3MultiplayerWorldLoadError.WorldLoadFallback,
            AW3WorldLoadCoordinator.GetStatus(begin.OperationId).Error,
            "strict generation remains failed after generated fallback");
    }

    private static void AssertRepairsCompleted()
    {
        Equal(1, CityReservePoolService.EndCalls, "world-load cleanup runs once");
        Equal(1, DeJureRegionStore.RepairCalls, "de jure repair runs once");
        Equal(1, CountyAdministrationStore.RepairCalls, "county repair runs once");
        Equal(1, SocialIdentityMigrationService.RepairCalls,
            "social identity migration runs once");
    }

    private static void DuplicateCallbackDoesNotBypassFailedRestore(string root, bool strict)
    {
        Reset();
        AW3RuntimeRestorePipeline.Result = AW3RestoreResult.Failed(
            "lineage_archive", "archive could not be restored");
        if (strict) AW3WorldLoadCoordinator.TryBeginGenerationLoad(root);
        else AW3WorldLoadCoordinator.ObserveLoadWorldStarted(root);
        AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);
        MapBox.RaiseWorldLoaded();

        MapBox.RaiseWorldLoaded();

        Equal(0, AWAsyncWorldLifecycle.StartCalls,
            "duplicate notification cannot bypass failed archive restoration");
        AssertRepairsCompleted();

        AW3RuntimeRestorePipeline.Result = null;
        AW3MultiplayerWorldLoadStartResult next =
            AW3WorldLoadCoordinator.TryBeginGenerationLoad(root);
        True(next.Accepted, "failed world restoration does not leave the coordinator busy");
        AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);
        MapBox.RaiseWorldLoaded();
        Equal(AW3MultiplayerWorldLoadState.Completed,
            AW3WorldLoadCoordinator.GetStatus(next.OperationId).State,
            "an explicit new load resets callback deduplication");
    }

    private static void PendingOrdinaryLoadDoesNotStartBeforeWorldData(string root)
    {
        Reset();
        AW3WorldLoadCoordinator.ObserveLoadWorldStarted(root);

        MapBox.RaiseWorldLoaded();

        Equal(0, AWAsyncWorldLifecycle.StartCalls,
            "stale callback before loadData cannot start the old world's services");
        Equal(0, CountyAdministrationStore.RepairCalls,
            "stale callback before loadData cannot repair the old world");
        AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);
        MapBox.RaiseWorldLoaded();
        Equal(1, AW3RuntimeRestorePipeline.RestoreCalls.Count,
            "the matching ordinary world data still restores normally");
        AssertRepairsCompleted();
    }

    private static void InvocationExceptionKeepsAlreadyQueuedWorld(string root)
    {
        Reset();
        World.world.save_manager.LoadAction = directory =>
        {
            AW3WorldLoadCoordinator.ObserveWorldDataQueued(directory);
            throw new InvalidOperationException("injected failure after loadData");
        };
        AW3MultiplayerWorldLoadStartResult begin =
            AW3WorldLoadCoordinator.TryBeginGenerationLoad(root);

        MapBox.RaiseWorldLoaded();

        Equal(1, AW3RuntimeRestorePipeline.RestoreCalls.Count,
            "load invocation failure cannot lose already queued archive restoration");
        Equal((root, false), AW3RuntimeRestorePipeline.RestoreCalls[0],
            "queued world completes through ordinary restoration after invocation failure");
        Equal(AW3MultiplayerWorldLoadError.WorldLoadFallback,
            AW3WorldLoadCoordinator.GetStatus(begin.OperationId).Error,
            "invocation failure remains visible on the strict operation");
    }

    private static void DuplicateUntrackedCallbackDoesNotRepeatRepairs()
    {
        Reset();
        MapBox.RaiseWorldLoaded();

        MapBox.RaiseWorldLoaded();

        Equal(1, AWAsyncWorldLifecycle.StartCalls,
            "an untracked initial world starts its services once");
        AssertRepairsCompleted();
    }

    private static void TimedOutLoadStillRestoresArrivingWorld(string root, bool queued)
    {
        Reset();
        AW3MultiplayerWorldLoadStartResult begin =
            AW3WorldLoadCoordinator.TryBeginGenerationLoad(root);
        if (queued) AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);

        AW3WorldLoadCoordinator.Tick(DateTime.UtcNow.AddMinutes(3));
        if (!queued)
        {
            MapBox.RaiseWorldLoaded();
            Equal(0, AWAsyncWorldLifecycle.StartCalls,
                "timeout does not treat stale callbacks as a restored world");
            AW3WorldLoadCoordinator.ObserveWorldDataQueued(root);
        }
        MapBox.RaiseWorldLoaded();

        Equal(1, AW3RuntimeRestorePipeline.RestoreCalls.Count,
            "timed-out WorldBox load still restores the archive when its world arrives");
        Equal((root, false), AW3RuntimeRestorePipeline.RestoreCalls[0],
            "late world after timeout uses ordinary restoration");
        Equal(AW3MultiplayerWorldLoadError.WorldLoadTimeout,
            AW3WorldLoadCoordinator.GetStatus(begin.OperationId).Error,
            "late restoration cannot overwrite the strict timeout diagnosis");
        AssertRepairsCompleted();
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message +
                $" (expected {expected}, actual {actual})");
    }

    private static void Contains(string value, string expected, string message)
    {
        if (value == null ||
            value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidOperationException(message + $" (actual {value})");
    }
}
