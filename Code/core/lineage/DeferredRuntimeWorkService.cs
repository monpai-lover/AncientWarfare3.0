using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.core.policy;
using UnityEngine;

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
        // 按 key 前缀累计每类 work item 的执行耗时,跨帧累加、按采样区间取走。
        private static readonly Dictionary<string, long[]> PrefixCost =
            new Dictionary<string, long[]>(StringComparer.Ordinal);
        private static readonly Dictionary<string, LinkedListNode<WorkItem>> Coalesced =
            new Dictionary<string, LinkedListNode<WorkItem>>(StringComparer.Ordinal);
        private static int _consecutiveCriticalRuntimeWork;
        private static int _consecutiveRuntimeWork;
        private static int _lastDrainFrame = -1;
        private static long _orderedEnqueued;
        private static long _coalescedEnqueued;
        private static long _drained;
        private static int _lastDrainProcessed;

        public static int PendingCount => PersistentQueue.Count +
                                          RuntimeQueue.Count +
                                          CriticalRuntimeQueue.Count;

        public static string GetDiagnostics()
        {
            return "persistent=" + PersistentQueue.Count +
                   " runtime=" + RuntimeQueue.Count +
                   " critical=" + CriticalRuntimeQueue.Count +
                   " coalesced_index=" + Coalesced.Count +
                   " ordered_enqueued=" + _orderedEnqueued +
                   " coalesced_enqueued=" + _coalescedEnqueued +
                   " drained=" + _drained +
                   " last_drain=" + _lastDrainProcessed +
                   " pending_prefixes=" + PendingPrefixDiagnostics();
        }

        public static void EnqueueCoalesced(string pKey, DeferredWorkClass pClass, Action pAction)
        {
            if (string.IsNullOrEmpty(pKey) || pAction == null) return;
            _coalescedEnqueued++;
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
            _orderedEnqueued++;
            QueueFor(pClass).AddLast(new WorkItem
            {
                workClass = pClass,
                action = pAction
            });
        }

        public static void DrainFrame(double pMilliseconds = 1.5, int pMaxItems = 1)
        {
            if (PendingCount == 0) return;
            int frame = Time.frameCount;
            if (!DeferredRuntimeWorkRules.ShouldStartFrameDrain(
                    _lastDrainFrame, frame)) return;
            _lastDrainFrame = frame;
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
            _lastDrainProcessed = processed;
            _drained += processed;
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
            _lastDrainFrame = -1;
            _orderedEnqueued = 0L;
            _coalescedEnqueued = 0L;
            _drained = 0L;
            _lastDrainProcessed = 0;
        }

        private static void Execute(WorkItem pItem)
        {
            // A deferred item may execute outside a diagnostic sampling frame;
            // measure it directly at the active frame boundary.
            long diagnostic = RuntimePerformanceDiagnostic.BeginDeferredItemScope();
            // deferred_key / deferred_item_ms 是按帧字段,13 个采样里 11 个报
            // none/0 —— 采样帧上通常没有 item 在跑。这里按前缀跨帧累加,才能看出
            // 那 2.555ms/项 到底花在哪类活上。这一份不受采样门控,每次都收。
            long costStarted = Stopwatch.GetTimestamp();
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
                AccountPrefixCost(pItem.key, costStarted);
                RuntimePerformanceDiagnostic.EndDeferredItem(pItem.key,
                    diagnostic);
            }
        }

        private static void AccountPrefixCost(string pKey, long pStarted)
        {
            string prefix = DeferredRuntimeWorkRules.DiagnosticPrefix(pKey);
            if (string.IsNullOrEmpty(prefix)) return;
            if (!PrefixCost.TryGetValue(prefix, out long[] entry))
            {
                entry = new long[2];
                PrefixCost[prefix] = entry;
            }

            entry[0] += Stopwatch.GetTimestamp() - pStarted;
            entry[1]++;
        }

        public static string TakePrefixCostDiagnostics()
        {
            if (PrefixCost.Count == 0) return "none";
            var ranked = new List<KeyValuePair<string, long[]>>(PrefixCost);
            PrefixCost.Clear();
            ranked.Sort((left, right) =>
            {
                int byTicks = right.Value[0].CompareTo(left.Value[0]);
                return byTicks != 0 ? byTicks :
                    string.CompareOrdinal(left.Key, right.Key);
            });
            int limit = Math.Min(12, ranked.Count);
            var parts = new string[limit];
            for (int i = 0; i < limit; i++)
            {
                parts[i] = ranked[i].Key + ":" +
                    (ranked[i].Value[0] * 1000.0 / Stopwatch.Frequency)
                        .ToString("0.###",
                            System.Globalization.CultureInfo.InvariantCulture) +
                    "/" + ranked[i].Value[1];
            }

            return string.Join(",", parts);
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

        private static string PendingPrefixDiagnostics()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            CountPrefixes(PersistentQueue, counts);
            CountPrefixes(RuntimeQueue, counts);
            CountPrefixes(CriticalRuntimeQueue, counts);
            if (counts.Count == 0) return "none";

            var ranked = new List<KeyValuePair<string, int>>(counts);
            ranked.Sort((left, right) =>
            {
                int byCount = right.Value.CompareTo(left.Value);
                return byCount != 0 ? byCount :
                    string.CompareOrdinal(left.Key, right.Key);
            });
            int limit = Math.Min(12, ranked.Count);
            var parts = new string[limit];
            for (int i = 0; i < limit; i++)
                parts[i] = ranked[i].Key + "=" + ranked[i].Value;
            return string.Join(",", parts);
        }

        private static void CountPrefixes(LinkedList<WorkItem> pQueue,
            Dictionary<string, int> pCounts)
        {
            for (LinkedListNode<WorkItem> node = pQueue.First;
                 node != null; node = node.Next)
            {
                string prefix = DeferredRuntimeWorkRules.DiagnosticPrefix(
                    node.Value.key);
                pCounts.TryGetValue(prefix, out int count);
                pCounts[prefix] = count + 1;
            }
        }
    }
}
