using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Reflection;
using AncientWarfare3.core.db;

internal static class Program
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(3);

    private static int Main()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("shutdown keeps timed-out checkpoint and connection for retry", ShutdownTimesOutWithoutClosing),
            ("shutdown bounds gate wait after completed task", CompletedTaskDoesNotPermitUnboundedLock),
            ("save fails within gate deadline and can retry", SaveTimesOutAndRecovers),
            ("faulted completed checkpoint can still close safely", FaultedTaskCanClose),
            ("generated world rejects failed archive before projections", GeneratedArchiveStageTests.Run),
            ("runtime reset finishes caches when checkpoint times out", AuthorityCheckpointResetTests.Run),
            ("failed archive is private until successful initialization", ArchiveAvailabilityTests.QuarantineAndRetry),
            ("disabled archive can load and install a valid replacement", ArchiveAvailabilityTests.LoadAndInstallAfterDisabled)
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine($"Checkpoint boundary: {cases.Length - failed}/{cases.Length} passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void ShutdownTimesOutWithoutClosing()
    {
        using var db = OpenDatabase();
        using var holder = new HeldCheckpoint();
        Set("_connection", db);
        Set("_connectionPath", "test-owned-checkpoint");
        Set("_running", holder.Task);
        Task<Exception> stopping = Task.Run(() => Capture(LineageArchiveCheckpointService.Shutdown));
        try
        {
            bool returned = stopping.Wait(TestDeadline);
            Require(returned, "Shutdown ignored its 2-second task timeout and blocked again on Gate");
            Require(stopping.Result is TimeoutException, "unfinished checkpoint must make shutdown fail explicitly");
            Require(ReferenceEquals(Get("_running"), holder.Task), "timeout must retain unfinished task identity");
            Require(ReferenceEquals(Get("_connection"), db) && db.State == ConnectionState.Open,
                "timeout must not close a connection still owned by the background checkpoint");
        }
        finally
        {
            holder.Release();
            Require(stopping.Wait(TestDeadline), "shutdown must finish after the held checkpoint is released");
            LineageArchiveCheckpointService.Shutdown();
        }
        Require(Get("_running") == null && Get("_connection") == null,
            "retry after completion closes and clears the old checkpoint");
    }

    private static void CompletedTaskDoesNotPermitUnboundedLock()
    {
        using var holder = new HeldCheckpoint();
        Set("_running", Task.CompletedTask);
        Task<Exception> stopping = Task.Run(() => Capture(LineageArchiveCheckpointService.Shutdown));
        try
        {
            Require(stopping.Wait(TestDeadline), "completed task must not bypass the gate wait deadline");
            Require(stopping.Result is TimeoutException, "gate contention must be reported as shutdown timeout");
        }
        finally
        {
            holder.Release();
            Require(stopping.Wait(TestDeadline), "shutdown task cleanup");
            LineageArchiveCheckpointService.Shutdown();
        }
    }

    private static void SaveTimesOutAndRecovers()
    {
        using var db = OpenDatabase();
        using var holder = new HeldCheckpoint();
        Task<bool> saving = Task.Run(() => LineageArchivePragmaService.CheckpointForSave(db));
        try
        {
            Require(saving.Wait(TestDeadline), "CheckpointForSave waits forever behind a stalled background checkpoint");
            Require(!saving.Result, "a save that did not acquire the checkpoint boundary must fail");
            Require(db.State == ConnectionState.Open, "save timeout must leave the live archive available");
        }
        finally
        {
            holder.Release();
            Require(saving.Wait(TestDeadline), "save task cleanup");
        }
        Require(LineageArchivePragmaService.CheckpointForSave(db), "save retries after background checkpoint completes");
    }

    private static void FaultedTaskCanClose()
    {
        using var db = OpenDatabase();
        Set("_connection", db);
        Set("_running", Task.FromException(new IOException("checkpoint failed")));
        LineageArchiveCheckpointService.Shutdown();
        Require(Get("_connection") == null && Get("_running") == null,
            "completed faulted task has no active owner and can be cleaned up");
        try { Require(db.State == ConnectionState.Closed, "completed fault cleanup closes connection"); }
        catch (ObjectDisposedException) { } // Disposed is also a completed close.
    }

    private static SQLiteConnection OpenDatabase()
    {
        var db = new SQLiteConnection("Data Source=:memory:;Pooling=False");
        db.Open();
        return db;
    }

    private static Exception Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }

    private static object Get(string field) => typeof(LineageArchiveCheckpointService)
        .GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static void Set(string field, object value) => typeof(LineageArchiveCheckpointService)
        .GetField(field, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // Model a native checkpoint that has already taken the production Gate and
    // whose I/O has stalled. No production timeout or lifecycle method is stubbed.
    internal sealed class HeldCheckpoint : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        internal Task Task { get; }
        internal HeldCheckpoint()
        {
            Task = System.Threading.Tasks.Task.Run(() =>
            {
                lock (LineageArchiveCheckpointService.Gate)
                {
                    _entered.Set();
                    if (!_release.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("test checkpoint was not released");
                }
            });
            Require(_entered.Wait(TestDeadline), "held checkpoint started");
        }
        internal void Release()
        {
            _release.Set();
            Require(Task.Wait(TestDeadline), "held checkpoint completed");
        }
        public void Dispose()
        {
            Release();
            _entered.Dispose();
            _release.Dispose();
        }
    }
}

namespace AncientWarfare3
{
    internal sealed class ModClass
    {
        internal static ModClass Instance { get; } = new();
        internal static string FolderPath;
        internal Declaration GetDeclaration() => new Declaration { FolderPath = FolderPath };
        internal sealed class Declaration { public string FolderPath; }
        internal static void LogWarning(string message) { }
    }
}
namespace AncientWarfare3.core.db
{
    // Game table/catalog helpers are outside this lifecycle test; schema creation,
    // archive file operations, checkpoint shutdown and public access are production.
    internal sealed class LocalizedNameIdentityTableItem { }
    internal static class ActorArchivePresenceIndex
    {
        internal static void ClearUnknown() { }
        internal static void ResetEmpty() { }
        internal static bool Rebuild(SQLiteConnection db) => true;
    }
    internal static class LineageArchiveIndexManager { internal static void EnsureIndexes(SQLiteConnection db) { } }
    internal static class LineageFamilyArchiveMigration { internal static bool IsVersionOwnedColumn(string table, string column) => false; }
}
namespace AncientWarfare3.core.lineage
{
    internal sealed class ReadScope : IDisposable { public void Dispose() { } }
    internal static class HistoryQuery { internal static IDisposable EnterBackgroundRead(SQLiteConnection db) => new ReadScope(); }
    internal static class LineageQuery { internal static IDisposable EnterBackgroundRead(SQLiteConnection db) => new ReadScope(); }
}
