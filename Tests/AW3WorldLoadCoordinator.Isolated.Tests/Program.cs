using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.multiplayer;

public static class MapBox
{
    public static event Action on_world_loaded;
    public static void RaiseWorldLoaded() => on_world_loaded?.Invoke();
}

public static class World
{
    public static readonly TestWorld world = new TestWorld();
}

public sealed class TestWorld
{
    public TestSaveManager save_manager { get; } = new TestSaveManager();
}

public sealed class TestSaveManager
{
    public void loadWorld(string directory, bool allowFallback) { }
}

public static class ModClass
{
    public static readonly List<string> Warnings = new List<string>();
    public static void LogWarning(string message) => Warnings.Add(message);
}

namespace AncientWarfare3.core.asyncwork
{
    internal static class AWAsyncWorldLifecycle
    {
        public static int StartCalls;
        public static Exception StartError;

        public static long StartWorld()
        {
            StartCalls++;
            if (StartError != null) throw StartError;
            return StartCalls;
        }
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
    }

    internal static class AW3RuntimeRestorePipeline
    {
        public static ManualResetEventSlim RestoreEntered;
        public static ManualResetEventSlim RestoreRelease;
        public static Exception RestoreError;

        internal static AW3RestoreResult TryRestoreFromDirectory(
            string directory, bool strict)
        {
            RestoreEntered?.Set();
            RestoreRelease?.Wait(5000);
            if (RestoreError != null) throw RestoreError;
            return AW3RestoreResult.Succeeded();
        }

        internal static AW3RestoreResult TryInitializeGeneratedWorld(bool strict)
        {
            return AW3RestoreResult.Succeeded();
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
            Console.WriteLine("AW3 world-load coordinator isolated tests passed.");
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
    }

    private static void Reset()
    {
        AncientWarfare3.core.asyncwork.AWAsyncWorldLifecycle.StartCalls = 0;
        AncientWarfare3.core.asyncwork.AWAsyncWorldLifecycle.StartError = null;
        AW3RuntimeRestorePipeline.RestoreEntered = null;
        AW3RuntimeRestorePipeline.RestoreRelease = null;
        AW3RuntimeRestorePipeline.RestoreError = null;
        ModClass.Warnings.Clear();
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
        if (value?.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidOperationException(message + $" (actual {value})");
    }
}
