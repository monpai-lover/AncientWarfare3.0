using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class ActorDeathArchiveService
    {
        private sealed class PendingLineageDeath
        {
            public long WorldGeneration;
            public ActorArchiveTableItem Snapshot;
            public FamilyTreeProjectionChange ProjectionChange;
            public bool FinalizeProjection;
            public int Attempts;
            public long ReadyFrame;
            public long InFlightSequence;
            public string LastError = string.Empty;
        }

        private const int Capacity = 8192;
        private static readonly Queue<long> Order = new Queue<long>();
        private static readonly Dictionary<long, PendingLineageDeath> Pending =
            new Dictionary<long, PendingLineageDeath>();
        private static long _frame;
        private static int _inFlightCount;

        internal static int PendingCount => Pending.Count;

        internal static bool EnqueueLineage(ActorArchiveTableItem pSnapshot,
            FamilyTreeProjectionChange pProjectionChange,
            bool pFinalizeProjection)
        {
            if (pSnapshot == null || pSnapshot.id < 0L) return false;
            long actorId = pSnapshot.id;
            var item = new PendingLineageDeath
            {
                WorldGeneration = AWAsyncRuntime.WorldGeneration,
                Snapshot = pSnapshot,
                ProjectionChange = pProjectionChange,
                FinalizeProjection = pFinalizeProjection,
                ReadyFrame = _frame
            };
            if (Pending.TryGetValue(actorId,
                    out PendingLineageDeath existing))
            {
                if (existing.InFlightSequence <= 0L)
                {
                    item.Attempts = existing.Attempts;
                    Pending[actorId] = item;
                }
                FamilyTreeProjectionRevision.Advance(pProjectionChange);
                return true;
            }
            if (Pending.Count >= Capacity) return false;
            Pending[actorId] = item;
            Order.Enqueue(actorId);
            FamilyTreeProjectionRevision.Advance(pProjectionChange);
            return true;
        }

        internal static void ProcessAuthorityCycle()
        {
            Process(
                pMilliseconds: ActorDeathArchiveRules.
                    ResolveAuthorityMilliseconds(Pending.Count),
                pMaxItems: ActorDeathArchiveRules.
                    ResolveAuthorityItemLimit(Pending.Count),
                pIgnoreBackoff: false);
        }

        internal static bool FlushForSave(TimeSpan pTimeout,
            out string pError)
        {
            string persistenceError = string.Empty;
            if (HistoricalWriteModeRules.ShouldRecoverRequiredWorker(
                    AWAsyncRuntime.DatabaseEnabled, Pending.Count,
                    HistoricalWriteService.Ready) &&
                !HistoricalWriteService.EnsureRequiredWorker(
                    out persistenceError))
            {
                // The synchronous fallback can still preserve the archive.
                // Keep the recovery error for the final diagnostic if it fails.
            }

            long deadline = Stopwatch.GetTimestamp() +
                Math.Max(1L, (long)(Stopwatch.Frequency *
                    Math.Max(0.01, pTimeout.TotalSeconds)));
            while (Pending.Count > 0 && Stopwatch.GetTimestamp() < deadline)
            {
                bool progressed = Process(pMilliseconds: 4.0,
                    pMaxItems: 256,
                    pIgnoreBackoff: true);
                HistoricalWriteService.DrainCompletions(256);
                if (Pending.Count == 0) break;
                if (progressed) continue;

                TimeSpan remaining = Remaining(deadline);
                if (remaining <= TimeSpan.Zero) break;
                if (_inFlightCount > 0)
                {
                    int pendingBeforeFlush = Pending.Count;
                    int inFlightBeforeFlush = _inFlightCount;
                    if (!HistoricalWriteService.FlushForSave(remaining,
                            out persistenceError)) break;
                    HistoricalWriteService.DrainCompletions(int.MaxValue);
                    if (Pending.Count >= pendingBeforeFlush &&
                        _inFlightCount >= inFlightBeforeFlush)
                    {
                        persistenceError =
                            "completion_no_progress after historical flush";
                        break;
                    }
                    continue;
                }

                if (!TryWriteOneSynchronously(remaining,
                        out persistenceError)) break;
            }
            bool ready = ActorDeathArchiveRules.ReadyForSave(Pending.Count,
                running: _inFlightCount, retries: 0, completions: 0);
            pError = ready
                ? string.Empty
                : DescribePendingForSave(persistenceError);
            return ready;
        }

        internal static void OnWriteAccepted(long pActorId, long pSequence,
            long pReplacedSequence)
        {
            if (pSequence <= 0L ||
                !Pending.TryGetValue(pActorId,
                    out PendingLineageDeath item)) return;
            if (item.WorldGeneration != AWAsyncRuntime.WorldGeneration)
                return;
            if (item.InFlightSequence <= 0L) _inFlightCount++;
            item.InFlightSequence = pSequence;
            item.LastError = string.Empty;
        }

        internal static void OnWriteCommitted(long pActorId, long pSequence)
        {
            if (!Pending.TryGetValue(pActorId,
                    out PendingLineageDeath item) ||
                item.WorldGeneration != AWAsyncRuntime.WorldGeneration ||
                item.InFlightSequence != pSequence) return;
            Pending.Remove(pActorId);
            if (_inFlightCount > 0) _inFlightCount--;
        }

        internal static void OnWriteFailed(long pActorId, long pSequence,
            string pError)
        {
            if (!Pending.TryGetValue(pActorId,
                    out PendingLineageDeath item) ||
                item.WorldGeneration != AWAsyncRuntime.WorldGeneration ||
                item.InFlightSequence != pSequence) return;
            item.InFlightSequence = 0L;
            if (_inFlightCount > 0) _inFlightCount--;
            item.Attempts++;
            item.ReadyFrame = _frame +
                ActorDeathArchiveRules.RetryDelayFrames(item.Attempts);
            item.LastError = pError ?? string.Empty;
            Order.Enqueue(pActorId);
        }

        internal static void Reset()
        {
            Pending.Clear();
            Order.Clear();
            _frame = 0L;
            _inFlightCount = 0;
        }

        private static bool Process(double pMilliseconds, int pMaxItems,
            bool pIgnoreBackoff)
        {
            if (_frame < long.MaxValue) _frame++;
            if (pMaxItems <= 0 || Pending.Count == 0) return false;
            bool progressed = false;
            long deadline = Stopwatch.GetTimestamp() +
                Math.Max(1L, (long)(Stopwatch.Frequency *
                    Math.Max(0.01, pMilliseconds) / 1000.0));
            int scan = Math.Min(Order.Count, pMaxItems);
            while (scan-- > 0 && Stopwatch.GetTimestamp() < deadline &&
                   Order.Count > 0)
            {
                long actorId = Order.Dequeue();
                if (!Pending.TryGetValue(actorId, out PendingLineageDeath item))
                    continue;
                if (item.WorldGeneration != AWAsyncRuntime.WorldGeneration)
                {
                    Pending.Remove(actorId);
                    progressed = true;
                    continue;
                }
                if (item.InFlightSequence > 0L) continue;
                if (!pIgnoreBackoff && item.ReadyFrame > _frame)
                {
                    Order.Enqueue(actorId);
                    continue;
                }
                bool queueAccepted =
                    LineageArchiveWriter.TryQueueCapturedDeath(item.Snapshot,
                        item.ProjectionChange, item.FinalizeProjection);
                if (queueAccepted)
                {
                    progressed = true;
                    continue;
                }
                string writeError = string.Empty;
                if (ActorDeathArchiveRules.ShouldAttemptSynchronousWrite(
                        HistoricalWriteService.Ready, queueAccepted) &&
                    LineageArchiveWriter.WriteCapturedDeathSynchronously(
                        item.Snapshot, item.ProjectionChange,
                        item.FinalizeProjection,
                        TimeSpan.FromMilliseconds(25), out writeError))
                {
                    Pending.Remove(actorId);
                    progressed = true;
                    continue;
                }
                item.Attempts++;
                item.ReadyFrame = _frame +
                    ActorDeathArchiveRules.RetryDelayFrames(item.Attempts);
                item.LastError = writeError ?? string.Empty;
                Order.Enqueue(actorId);
            }
            return progressed;
        }

        private static bool TryWriteOneSynchronously(TimeSpan pTimeout,
            out string pError)
        {
            pError = string.Empty;
            int scan = Order.Count;
            while (scan-- > 0 && Order.Count > 0)
            {
                long actorId = Order.Dequeue();
                if (!Pending.TryGetValue(actorId, out PendingLineageDeath item))
                    continue;
                if (item.WorldGeneration != AWAsyncRuntime.WorldGeneration)
                {
                    Pending.Remove(actorId);
                    continue;
                }
                if (item.InFlightSequence > 0L) continue;
                if (!LineageArchiveWriter.WriteCapturedDeathSynchronously(
                        item.Snapshot, item.ProjectionChange,
                        item.FinalizeProjection, pTimeout, out pError))
                {
                    item.Attempts++;
                    item.LastError = pError ?? string.Empty;
                    Order.Enqueue(actorId);
                    return false;
                }
                Pending.Remove(actorId);
                return true;
            }
            return Pending.Count == 0;
        }

        private static TimeSpan Remaining(long pDeadline)
        {
            long ticks = pDeadline - Stopwatch.GetTimestamp();
            return ticks <= 0L
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds((double)ticks /
                    Stopwatch.Frequency);
        }

        private static string DescribePendingForSave(string pWriterError)
        {
            long firstActorId = -1L;
            int firstAttempts = 0;
            foreach (long actorId in Order)
            {
                if (!Pending.TryGetValue(actorId,
                        out PendingLineageDeath item)) continue;
                firstActorId = actorId;
                firstAttempts = item.Attempts;
                if (string.IsNullOrWhiteSpace(pWriterError))
                    pWriterError = item.LastError;
                break;
            }
            if (firstActorId < 0L)
                foreach (KeyValuePair<long, PendingLineageDeath> pair in
                         Pending)
                {
                    firstActorId = pair.Key;
                    firstAttempts = pair.Value.Attempts;
                    if (string.IsNullOrWhiteSpace(pWriterError))
                        pWriterError = pair.Value.LastError;
                    break;
                }
            string detail = ActorDeathArchiveRules.DescribePendingForSave(
                Pending.Count, firstActorId, firstAttempts) +
                " in_flight=" + Math.Max(0, _inFlightCount);
            return string.IsNullOrWhiteSpace(pWriterError)
                ? detail
                : detail + " writer_error=" + pWriterError;
        }
    }
}
