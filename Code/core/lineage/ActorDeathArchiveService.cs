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
        }

        private const int Capacity = 8192;
        private static readonly Queue<long> Order = new Queue<long>();
        private static readonly Dictionary<long, PendingLineageDeath> Pending =
            new Dictionary<long, PendingLineageDeath>();
        private static long _frame;

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
            if (Pending.ContainsKey(actorId))
            {
                Pending[actorId] = item;
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
            long deadline = Stopwatch.GetTimestamp() +
                Math.Max(1L, (long)(Stopwatch.Frequency *
                    Math.Max(0.01, pTimeout.TotalSeconds)));
            while (Pending.Count > 0 && Stopwatch.GetTimestamp() < deadline)
            {
                int before = Pending.Count;
                Process(pMilliseconds: 4.0, pMaxItems: 256,
                    pIgnoreBackoff: true);
                if (Pending.Count < before) continue;
                if (!TryWriteOneSynchronously()) break;
            }
            bool ready = ActorDeathArchiveRules.ReadyForSave(Pending.Count,
                running: 0, retries: 0, completions: 0);
            pError = ready ? string.Empty : DescribePendingForSave();
            return ready;
        }

        internal static void Reset()
        {
            Pending.Clear();
            Order.Clear();
            _frame = 0L;
        }

        private static void Process(double pMilliseconds, int pMaxItems,
            bool pIgnoreBackoff)
        {
            if (_frame < long.MaxValue) _frame++;
            if (pMaxItems <= 0 || Pending.Count == 0) return;
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
                    continue;
                }
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
                    Pending.Remove(actorId);
                    continue;
                }
                if (ActorDeathArchiveRules.ShouldAttemptSynchronousWrite(
                        queueAccepted) &&
                    LineageArchiveWriter.WriteCapturedDeathSynchronously(
                        item.Snapshot, item.ProjectionChange,
                        item.FinalizeProjection,
                        TimeSpan.FromMilliseconds(25)))
                {
                    Pending.Remove(actorId);
                    continue;
                }
                item.Attempts++;
                item.ReadyFrame = _frame +
                    ActorDeathArchiveRules.RetryDelayFrames(item.Attempts);
                Order.Enqueue(actorId);
            }
        }

        private static bool TryWriteOneSynchronously()
        {
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
                if (!LineageArchiveWriter.WriteCapturedDeathSynchronously(
                        item.Snapshot, item.ProjectionChange,
                        item.FinalizeProjection))
                {
                    Order.Enqueue(actorId);
                    return false;
                }
                Pending.Remove(actorId);
                return true;
            }
            return Pending.Count == 0;
        }

        private static string DescribePendingForSave()
        {
            long firstActorId = -1L;
            int firstAttempts = 0;
            foreach (long actorId in Order)
            {
                if (!Pending.TryGetValue(actorId,
                        out PendingLineageDeath item)) continue;
                firstActorId = actorId;
                firstAttempts = item.Attempts;
                break;
            }
            return ActorDeathArchiveRules.DescribePendingForSave(
                Pending.Count, firstActorId, firstAttempts);
        }
    }
}
