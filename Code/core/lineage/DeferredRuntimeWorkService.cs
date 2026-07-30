using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    public enum DeferredWorkClass
    {
        Persistent,
        Runtime,
        CriticalRuntime
    }

    public static class DeferredRuntimeWorkService
    {
        private const int MaxAttempts = 2;

        private sealed class WorkItem
        {
            public string key;
            public DeferredWorkClass workClass;
            public Action action;
            public int attempts;
        }

        private static readonly LinkedList<WorkItem> PersistentQueue =
            new LinkedList<WorkItem>();
        private static readonly LinkedList<WorkItem> RuntimeQueue =
            new LinkedList<WorkItem>();
        private static readonly LinkedList<WorkItem> CriticalRuntimeQueue =
            new LinkedList<WorkItem>();
        private static readonly Dictionary<string, LinkedListNode<WorkItem>> Coalesced =
            new Dictionary<string, LinkedListNode<WorkItem>>(StringComparer.Ordinal);
        private static int _consecutiveCriticalRuntimeWork;
        private static int _consecutiveRuntimeWork;

        public static int PendingCount => PersistentQueue.Count +
                                          RuntimeQueue.Count +
                                          CriticalRuntimeQueue.Count;

        public static void EnqueueCoalesced(string pKey, DeferredWorkClass pClass, Action pAction)
        {
            if (string.IsNullOrEmpty(pKey) || pAction == null) return;
            if (Coalesced.TryGetValue(pKey, out LinkedListNode<WorkItem> existing))
            {
                if (existing.Value.workClass != pClass)
                {
                    WorkItem movedItem = existing.Value;
                    existing.List?.Remove(existing);
                    movedItem.workClass = pClass;
                    movedItem.action = pAction;
                    movedItem.attempts = 0;
                    Coalesced[pKey] = QueueFor(pClass).AddLast(movedItem);
                    return;
                }
                existing.Value.workClass = pClass;
                existing.Value.action = pAction;
                existing.Value.attempts = 0;
                return;
            }

            var item = new WorkItem { key = pKey, workClass = pClass, action = pAction };
            LinkedListNode<WorkItem> node = QueueFor(pClass).AddLast(item);
            Coalesced[pKey] = node;
        }

        public static void EnqueueOrdered(DeferredWorkClass pClass, Action pAction)
        {
            if (pAction == null) return;
            QueueFor(pClass).AddLast(new WorkItem
            {
                workClass = pClass,
                action = pAction
            });
        }

        public static void DrainFrame(double pMilliseconds = 1.5, int pMaxItems = 1)
        {
            if (PendingCount == 0) return;
            long start = Stopwatch.GetTimestamp();
            long budget = MillisecondsToTicks(pMilliseconds);
            int processed = 0;
            while (PendingCount > 0 &&
                   !DeferredRuntimeWorkRules.ShouldStopDrain(
                       processed, pMaxItems, Stopwatch.GetTimestamp() - start, budget))
            {
                LinkedListNode<WorkItem> node = TakeNext();
                if (node == null) return;
                Remove(node);
                Execute(node.Value);
                processed++;
            }
        }

        public static void FlushPersistent()
        {
            while (PersistentQueue.Count > 0)
            {
                LinkedListNode<WorkItem> node = PersistentQueue.First;
                Remove(node);
                Execute(node.Value);
            }
        }

        public static void ClearRuntimeState()
        {
            PersistentQueue.Clear();
            RuntimeQueue.Clear();
            CriticalRuntimeQueue.Clear();
            Coalesced.Clear();
            _consecutiveCriticalRuntimeWork = 0;
            _consecutiveRuntimeWork = 0;
        }

        private static void Execute(WorkItem pItem)
        {
            long diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try
            {
                pItem.action();
            }
            catch (Exception e)
            {
                pItem.attempts++;
                if (DeferredRuntimeWorkRules.ShouldRetry(pItem.attempts, MaxAttempts))
                {
                    Requeue(pItem);
                    return;
                }
                ModClass.LogWarning(DeferredRuntimeWorkRules.FormatFailure(
                    pItem.key, e));
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDeferredItem(pItem.key,
                    diagnostic);
            }
        }

        private static void Requeue(WorkItem pItem)
        {
            LinkedListNode<WorkItem> node = QueueFor(pItem.workClass)
                .AddLast(pItem);
            if (!string.IsNullOrEmpty(pItem.key)) Coalesced[pItem.key] = node;
        }

        private static void Remove(LinkedListNode<WorkItem> pNode)
        {
            if (pNode == null) return;
            string key = pNode.Value.key;
            pNode.List?.Remove(pNode);
            if (!string.IsNullOrEmpty(key) && Coalesced.TryGetValue(key, out LinkedListNode<WorkItem> indexed) &&
                indexed == pNode)
                Coalesced.Remove(key);
        }

        private static LinkedListNode<WorkItem> TakeNext()
        {
            bool criticalPending = CriticalRuntimeQueue.Count > 0;
            bool runtimePending = RuntimeQueue.Count > 0;
            bool persistentPending = PersistentQueue.Count > 0;
            if (DeferredRuntimeWorkRules.ShouldPrioritizeCriticalRuntimeWork(
                    criticalPending, runtimePending, persistentPending,
                    _consecutiveCriticalRuntimeWork))
            {
                _consecutiveCriticalRuntimeWork++;
                return CriticalRuntimeQueue.First;
            }
            if (DeferredRuntimeWorkRules.ShouldPrioritizeRuntimeWork(
                    runtimePending, persistentPending,
                    _consecutiveRuntimeWork))
            {
                _consecutiveRuntimeWork++;
                _consecutiveCriticalRuntimeWork = 0;
                return RuntimeQueue.First;
            }
            if (persistentPending)
            {
                _consecutiveCriticalRuntimeWork = 0;
                _consecutiveRuntimeWork = 0;
                return PersistentQueue.First;
            }
            if (criticalPending)
            {
                _consecutiveCriticalRuntimeWork++;
                _consecutiveRuntimeWork = 0;
                return CriticalRuntimeQueue.First;
            }
            _consecutiveCriticalRuntimeWork = 0;
            return RuntimeQueue.First;
        }

        private static LinkedList<WorkItem> QueueFor(DeferredWorkClass pClass)
        {
            switch (pClass)
            {
                case DeferredWorkClass.CriticalRuntime:
                    return CriticalRuntimeQueue;
                case DeferredWorkClass.Runtime:
                    return RuntimeQueue;
                default:
                    return PersistentQueue;
            }
        }

        private static long MillisecondsToTicks(double pMilliseconds)
        {
            return Math.Max(1L, (long)(Stopwatch.Frequency * Math.Max(0.01, pMilliseconds) / 1000.0));
        }
    }
}
