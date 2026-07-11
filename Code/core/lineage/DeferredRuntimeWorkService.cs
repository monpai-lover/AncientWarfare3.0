using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AncientWarfare3.core.lineage
{
    public enum DeferredWorkClass
    {
        Persistent,
        Runtime
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

        private static readonly LinkedList<WorkItem> Queue = new LinkedList<WorkItem>();
        private static readonly Dictionary<string, LinkedListNode<WorkItem>> Coalesced =
            new Dictionary<string, LinkedListNode<WorkItem>>(StringComparer.Ordinal);

        public static int PendingCount => Queue.Count;

        public static void EnqueueCoalesced(string pKey, DeferredWorkClass pClass, Action pAction)
        {
            if (string.IsNullOrEmpty(pKey) || pAction == null) return;
            if (Coalesced.TryGetValue(pKey, out LinkedListNode<WorkItem> existing))
            {
                existing.Value.workClass = pClass;
                existing.Value.action = pAction;
                existing.Value.attempts = 0;
                return;
            }

            var item = new WorkItem { key = pKey, workClass = pClass, action = pAction };
            LinkedListNode<WorkItem> node = Queue.AddLast(item);
            Coalesced[pKey] = node;
        }

        public static void EnqueueOrdered(DeferredWorkClass pClass, Action pAction)
        {
            if (pAction == null) return;
            Queue.AddLast(new WorkItem { workClass = pClass, action = pAction });
        }

        public static void DrainFrame(double pMilliseconds = 1.5, int pMaxItems = 1)
        {
            if (Queue.Count == 0) return;
            long start = Stopwatch.GetTimestamp();
            long budget = MillisecondsToTicks(pMilliseconds);
            int processed = 0;
            while (Queue.Count > 0 &&
                   !DeferredRuntimeWorkRules.ShouldStopDrain(
                       processed, pMaxItems, Stopwatch.GetTimestamp() - start, budget))
            {
                LinkedListNode<WorkItem> node = Queue.First;
                Remove(node);
                Execute(node.Value);
                processed++;
            }
        }

        public static void FlushPersistent()
        {
            LinkedListNode<WorkItem> node = Queue.First;
            while (node != null)
            {
                LinkedListNode<WorkItem> next = node.Next;
                if (node.Value.workClass == DeferredWorkClass.Persistent)
                {
                    Remove(node);
                    Execute(node.Value);
                }
                node = next ?? Queue.First;
                if (node != null && node == Queue.First &&
                    !ContainsPersistent()) break;
            }
        }

        public static void ClearRuntimeState()
        {
            Queue.Clear();
            Coalesced.Clear();
        }

        private static void Execute(WorkItem pItem)
        {
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
                ModClass.LogWarning("Deferred work failed: " + e.Message);
            }
        }

        private static void Requeue(WorkItem pItem)
        {
            LinkedListNode<WorkItem> node = Queue.AddLast(pItem);
            if (!string.IsNullOrEmpty(pItem.key)) Coalesced[pItem.key] = node;
        }

        private static void Remove(LinkedListNode<WorkItem> pNode)
        {
            if (pNode == null) return;
            string key = pNode.Value.key;
            Queue.Remove(pNode);
            if (!string.IsNullOrEmpty(key) && Coalesced.TryGetValue(key, out LinkedListNode<WorkItem> indexed) &&
                indexed == pNode)
                Coalesced.Remove(key);
        }

        private static bool ContainsPersistent()
        {
            for (LinkedListNode<WorkItem> node = Queue.First; node != null; node = node.Next)
                if (node.Value.workClass == DeferredWorkClass.Persistent)
                    return true;
            return false;
        }

        private static long MillisecondsToTicks(double pMilliseconds)
        {
            return Math.Max(1L, (long)(Stopwatch.Frequency * Math.Max(0.01, pMilliseconds) / 1000.0));
        }
    }
}
