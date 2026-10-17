using System.Data.SQLite;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.uiquery;

internal static class Program
{
    private static string _path;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static void Main()
    {
        _path = Path.Combine(Path.GetTempPath(), "aw3-read-worker-" + Guid.NewGuid() + ".sqlite");
        using (var db = new SQLiteConnection("Data Source=" + _path)) db.Open();
        try
        {
            SaveCancellationReleasesOwnerAfterBarrier();
            TimedOutSaveStillReleasesCancelledOwner();
            WorldChangeDropsDeferredSaveCallbacks();
            ExplicitCancellationDropsDeferredSaveCallback();
            ThrowingCancellationCallbackDoesNotStrandNextOwner();
            ReplacementSurvivesOldSaveCancellation();
            Console.WriteLine("Historical read lifecycle: 6 cases passed.");
        }
        finally { File.Delete(_path); }
    }

    private static AWHistoricalReadWorker Start()
    {
        var worker = new AWHistoricalReadWorker();
        worker.StartWorld(_path, 7, 1);
        return worker;
    }

    private static AWHistoricalReadRequest Request(string key, Action<object> commit,
        Action<Exception> fault, Func<SQLiteConnection, CancellationToken, object> execute = null)
    {
        return new AWHistoricalReadRequest(key, new AWAsyncStamp(1, 0, 7),
            execute ?? ((_, _) => "result"), commit, fault, 7);
    }

    private static void Wait(Func<bool> condition, string message)
    {
        Require(SpinWait.SpinUntil(condition, Timeout), message);
    }

    private static void SaveCancellationReleasesOwnerAfterBarrier()
    {
        using var worker = Start();
        bool busy = true;
        int faults = 0, commits = 0;
        Require(worker.TrySchedule(Request("export", _ => { busy = false; commits++; }, error =>
        {
            Require(error is OperationCanceledException, "save reports cancellation");
            busy = false;
            faults++;
        })), "export admitted");
        Wait(() => worker.PendingCompletionCount > 0, "export completed on worker");
        Require(worker.TryEnterSaveBarrier(Timeout, out var error), error);
        worker.DrainMainThread(10);
        Require(busy && faults == 0, "UI callback must wait until save barrier exits");
        worker.ExitSaveBarrier();
        worker.DrainMainThread(10);
        Require(!busy && faults == 1 && commits == 0,
            "save cancellation must release the owner's pending state exactly once");
        worker.DrainMainThread(10);
        Require(faults == 1, "cancellation is delivered once");
    }

    private static void TimedOutSaveStillReleasesCancelledOwner()
    {
        using var worker = Start();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int faults = 0;
        try
        {
            Require(worker.TrySchedule(Request("slow", _ => throw new Exception("cancelled result committed"),
                _ => faults++, (_, _) => { started.Set(); release.Wait(Timeout); return 1; })), "slow read admitted");
            Require(started.Wait(Timeout), "slow read started");
            Require(!worker.TryEnterSaveBarrier(TimeSpan.Zero, out _), "active read times out save");
            worker.ExitSaveBarrier();
            worker.DrainMainThread(10);
            Require(faults == 1, "failed save must still release cancelled UI work");
            release.Set();
            Wait(() => worker.PendingCount == 0, "cancelled execution exits");
            worker.DrainMainThread(10);
            Require(faults == 1, "late execution cannot repeat cancellation");
        }
        finally { release.Set(); }
    }

    private static void WorldChangeDropsDeferredSaveCallbacks()
    {
        using var worker = Start();
        int callbacks = 0;
        worker.TrySchedule(Request("old-world", _ => callbacks++, _ => callbacks++));
        Wait(() => worker.PendingCompletionCount > 0, "old read completed");
        Require(worker.TryEnterSaveBarrier(Timeout, out var error), error);
        worker.ExitSaveBarrier();
        Require(worker.ClearWorld(Timeout, out error), error);
        worker.StartWorld(_path, 8, 2);
        worker.DrainMainThread(10);
        Require(callbacks == 0, "old-world save cancellation cannot reach new world");
    }

    private static void ExplicitCancellationDropsDeferredSaveCallback()
    {
        using var worker = Start();
        int callbacks = 0;
        worker.TrySchedule(Request("closed-window", _ => callbacks++, _ => callbacks++), out long id);
        Wait(() => worker.PendingCompletionCount > 0, "closed-window read completed");
        Require(worker.TryEnterSaveBarrier(Timeout, out var error), error);
        worker.Cancel(id, "closed-window");
        worker.ExitSaveBarrier();
        worker.DrainMainThread(10);
        Require(callbacks == 0, "closed window must not receive deferred cancellation");
    }

    private static void ThrowingCancellationCallbackDoesNotStrandNextOwner()
    {
        using var worker = Start();
        int completed = 0;
        worker.TrySchedule(Request("throws", _ => { }, _ => throw new InvalidOperationException("owner failure")));
        worker.TrySchedule(Request("next", _ => { }, _ => completed++));
        Wait(() => worker.PendingCompletionCount == 2, "both reads completed");
        Require(worker.TryEnterSaveBarrier(Timeout, out var error), error);
        worker.ExitSaveBarrier();
        worker.DrainMainThread(10);
        Require(completed == 1, "one throwing owner cannot prevent the next cancellation");
    }

    private static void ReplacementSurvivesOldSaveCancellation()
    {
        using var worker = Start();
        int oldCallbacks = 0, newCommits = 0;
        worker.TrySchedule(Request("same-key", _ => oldCallbacks++, _ => oldCallbacks++));
        Wait(() => worker.PendingCompletionCount > 0, "old same-key read completed");
        Require(worker.TryEnterSaveBarrier(Timeout, out var error), error);
        worker.ExitSaveBarrier();
        Require(worker.TrySchedule(Request("same-key", _ => newCommits++, _ => { })), "replacement admitted");
        Wait(() => worker.PendingCount == 0, "replacement completed");
        worker.DrainMainThread(10);
        Require(oldCallbacks == 0 && newCommits == 1,
            "old cancellation cannot consume or update the replacement request");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
