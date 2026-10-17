$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = Get-Content -Raw -LiteralPath (Join-Path $repo 'Code/core/db/HistoricalWriteService.cs')
$registry = Get-Content -Raw -LiteralPath (Join-Path $repo 'Code/core/db/HistoricalWriteCallbackRegistry.cs')
$start = $source.IndexOf('public static void DrainCompletions(int pMaxBatches)', [StringComparison]::Ordinal)
$end = $source.IndexOf('public static bool StopWorld(TimeSpan pTimeout, out string pError)', $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'Missing production completion drain methods.' }
$methods = $source.Substring($start, $end - $start)
# Execute the production drain and callback registry. The worker queue below
# supplies completed batches without starting a database thread; switching its
# identity and clearing the registry models StartWorld's sequence reset.
$harness = @'
namespace AncientWarfare3 {
    public static class ModClass { public static void LogWarning(string message) { } }
}
namespace AncientWarfare3.core.db {
    public sealed class HistoricalWriteEnvelope {
        public long Sequence;
    }
    public sealed class HistoricalWriteCompletion {
        public List<long> Sequences = new List<long>();
        public List<object> Outcomes = new List<object>();
        public bool IsCommitted;
        public string Error = "old-world failure";
    }
    public sealed class HistoricalWriteWorker {
        public readonly Queue<HistoricalWriteCompletion> Completions = new Queue<HistoricalWriteCompletion>();
        public bool TryEnqueue(HistoricalWriteEnvelope envelope, out HistoricalWriteEnvelope replaced) {
            replaced = null; return true;
        }
        public bool TryDequeueCompletion(out HistoricalWriteCompletion completion) {
            return Completions.TryDequeue(out completion);
        }
    }
    public static class HistoricalWriteService {
        private static readonly object Gate = new object();
        private static readonly HistoricalWriteCallbackRegistry Callbacks = new HistoricalWriteCallbackRegistry();
        private static HistoricalWriteWorker _worker;
        public static void Run() {
            ReentrantWorldChange(false, true);
            ReentrantWorldChange(true, true);
            ReentrantWorldChange(false, false);
            ThrowingOwnerDoesNotPreventLaterCompletion();
        }
        private static void Register(long sequence, Action<long, object> commit, Action<long, string> fault = null) {
            if (!Callbacks.TryEnqueue(_worker, new HistoricalWriteEnvelope { Sequence = sequence },
                (Action<long>)null, commit, fault, out _)) throw new Exception("registration rejected");
        }
        private static HistoricalWriteCompletion Batch(bool committed, params long[] sequences) {
            var batch = new HistoricalWriteCompletion { IsCommitted = committed };
            foreach (long sequence in sequences) {
                batch.Sequences.Add(sequence);
                batch.Outcomes.Add("result-" + sequence);
            }
            return batch;
        }
        private static void ReentrantWorldChange(bool failOldBatch, bool startReplacement) {
            Callbacks.Clear();
            _worker = new HistoricalWriteWorker();
            int staleCallbacks = 0, newCommits = 0, newFaults = 0;
            void SwitchWorld() {
                Callbacks.Clear();
                _worker = startReplacement ? new HistoricalWriteWorker() : null;
                if (!startReplacement) return;
                // New worlds restart sequence allocation; these numbers overlap
                // the still-local remainder of the old completion batch.
                Register(2, (_, _) => newCommits++, (_, _) => newFaults++);
                Register(3, (_, _) => newCommits++, (_, _) => newFaults++);
            }
            Register(1, (_, _) => SwitchWorld(), (_, _) => SwitchWorld());
            Register(2, (_, _) => staleCallbacks++, (_, _) => staleCallbacks++);
            Register(3, (_, _) => staleCallbacks++, (_, _) => staleCallbacks++);
            _worker.Completions.Enqueue(Batch(!failOldBatch, 1, 2));
            _worker.Completions.Enqueue(Batch(!failOldBatch, 3));
            DrainCompletions(10);
            Require(staleCallbacks == 0 && newCommits == 0 && newFaults == 0,
                "old-world batches must not consume new-world callbacks with reused sequence IDs");
            if (!startReplacement) return;
            _worker.Completions.Enqueue(Batch(true, 2, 3));
            DrainCompletions(10);
            Require(newCommits == 2 && newFaults == 0,
                "replacement-world callbacks remain registered for their own completions");
        }
        private static void ThrowingOwnerDoesNotPreventLaterCompletion() {
            Callbacks.Clear();
            _worker = new HistoricalWriteWorker();
            int completed = 0;
            Register(1, (_, _) => throw new InvalidOperationException("owner failed"));
            Register(2, (_, _) => completed++);
            _worker.Completions.Enqueue(Batch(true, 1, 2));
            DrainCompletions(10);
            Require(completed == 1, "same-world remaining completions survive an owner exception");
        }
        private static void Require(bool value, string message) {
            if (!value) throw new InvalidOperationException(message);
        }
// PRODUCTION_DRAIN
    }
}
'@
$harness = $harness.Replace('// PRODUCTION_DRAIN', $methods)
$code = $registry.Replace('using System;', "using System;`nusing System.Diagnostics;") + "`n" + $harness
Add-Type -TypeDefinition $code -Language CSharp
[AncientWarfare3.core.db.HistoricalWriteService]::Run()
Write-Output 'Historical write completion lifecycle: 4 cases passed.'
