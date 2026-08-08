using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using AncientWarfare3.api.multiplayer;
using UnityEngine;

namespace AncientWarfare3.core.performance
{
    /// <summary>
    /// Schedules status actions and expiry checks by due simulation tick.
    /// Status presentation is already snapshot driven; this prevents the
    /// large scheduler from calling every status on every fixed tick.
    /// </summary>
    internal static class AWStatusSimulationScheduler
    {
        private const long Never = long.MaxValue;
        private const int TimingWheelSize = 4096;
        private const int TimingWheelMask = TimingWheelSize - 1;

        private enum MutationKind : byte
        {
            Added,
            DurationChanged,
            Finished,
            Removed
        }

        private sealed class Entry
        {
            internal Status Status;
            internal long Order;
            internal long Version;
            internal long NextActionTick;
            internal long NextExpiryTick;
            internal long LastProcessedTick;
        }

        private readonly struct Mutation
        {
            internal Mutation(Status pStatus, MutationKind pKind)
            {
                Status = pStatus;
                Kind = pKind;
            }

            internal Status Status { get; }
            internal MutationKind Kind { get; }
        }

        private readonly struct Node
        {
            internal Node(Entry pEntry, long pDueTick)
            {
                Entry = pEntry;
                DueTick = pDueTick;
                Version = pEntry.Version;
                Order = pEntry.Order;
            }

            internal Entry Entry { get; }
            internal long DueTick { get; }
            internal long Version { get; }
            internal long Order { get; }
        }

        private static readonly ConcurrentQueue<Mutation> Mutations =
            new ConcurrentQueue<Mutation>();
        private static readonly Dictionary<Status, Entry> Entries =
            new Dictionary<Status, Entry>();
        private static readonly List<Node>[] TimingWheel =
            new List<Node>[TimingWheelSize];
        private static readonly Stack<List<Node>> TimingWheelListPool =
            new Stack<List<Node>>();
        private static readonly List<Node> Heap = new List<Node>();
        private static readonly Comparison<Entry> ReverseOrderComparison =
            CompareEntryOrderDescending;
        private static readonly List<Entry> Removals =
            new List<Entry>();
        private static readonly object StateLock = new object();
        private static StatusManager _manager;
        private static int _worldGeneration = -1;
        private static int _lastListSyncFrame = -1;
        private static long _tick;
        private static long _nextOrder;
        private static long _processingTick = Never;
        private static long _updates;
        private static long _dueChecks;
        private static long _actionCalls;
        private static long _expiryCalls;
        private static long _removedStatuses;
        private static long _staleNodes;
        private static long _rebuilds;
        private static long _listSyncs;
        private static long _listSyncSkips;
        private static long _timingWheelNodes;
        private static long _timingWheelVisits;
        private static long _timingWheelDeferred;

        internal static bool Enabled =>
            AWPerformanceSettings.EnableStatusSimulationScheduler &&
            AWPerformanceSettings.EnableFramePriorityScheduler &&
            Config.game_loaded &&
            !SmoothLoader.isLoading() &&
            World.world != null &&
            !AW3MultiplayerReplicaScope.IsReplicaSession;

        internal static bool TryUpdate(StatusManager pManager,
            float pElapsed)
        {
            if (!Enabled || pManager == null || World.world.isPaused() ||
                Math.Abs(pElapsed - AWFrameSchedulerRules
                    .FixedSimulationStepSeconds) > 0.000001f)
            {
                Disable();
                return false;
            }

            lock (StateLock)
            {
                EnsureWorld(pManager);
                long currentTick = ++_tick;
                float worldTime = (float)World.world.getCurWorldTime();
                _processingTick = currentTick;
                try
                {
                    PrepareDueNodes(currentTick);
                    long currentOrder = long.MinValue;
                    DrainMutations(currentTick, currentOrder, worldTime);
                    while (Heap.Count > 0 &&
                           Heap[0].DueTick <= currentTick)
                    {
                        Node node = Pop();
                        Entry entry = node.Entry;
                        if (entry == null || node.Version != entry.Version ||
                            !Entries.TryGetValue(entry.Status,
                                out Entry current) ||
                            !ReferenceEquals(entry, current))
                        {
                            _staleNodes++;
                            continue;
                        }

                        Status status = entry.Status;
                        currentOrder = Math.Max(currentOrder, entry.Order);
                        entry.LastProcessedTick = currentTick;
                        _dueChecks++;
                        if (status == null || status.is_finished)
                        {
                            QueueRemoval(entry);
                            DrainMutations(currentTick, currentOrder,
                                worldTime);
                            continue;
                        }

                        bool actionDue = entry.NextActionTick <= currentTick;
                        bool expiryDue = entry.NextExpiryTick <= currentTick;
                        if (actionDue) status._action_timer = 0f;
                        if (actionDue || expiryDue)
                        {
                            bool wasFinished = status.is_finished;
                            status.update(0f, worldTime);
                            if (actionDue) _actionCalls++;
                            if (!wasFinished && status.is_finished)
                                _expiryCalls++;
                        }

                        if (status.is_finished)
                        {
                            QueueRemoval(entry);
                        }
                        else
                        {
                            if (actionDue)
                                entry.NextActionTick =
                                    ComputeNextActionTickAfterCall(currentTick,
                                        status);
                            if (expiryDue)
                                entry.NextExpiryTick =
                                    ComputeExpiryTick(currentTick,
                                        worldTime, status);
                            Schedule(entry);
                        }
                        DrainMutations(currentTick, currentOrder, worldTime);
                    }

                    DrainMutations(currentTick, long.MaxValue, worldTime);
                    RemoveFinished(pManager);
                    _updates++;
                    return true;
                }
                finally
                {
                    _processingTick = Never;
                }
            }
        }

        internal static void NotifyAdded(Status pStatus)
        {
            Enqueue(pStatus, MutationKind.Added);
        }

        internal static void NotifyDurationChanged(Status pStatus)
        {
            Enqueue(pStatus, MutationKind.DurationChanged);
        }

        internal static void NotifyFinished(Status pStatus)
        {
            Enqueue(pStatus, MutationKind.Finished);
        }

        internal static void NotifyRemoved(Status pStatus)
        {
            Enqueue(pStatus, MutationKind.Removed);
        }

        internal static bool ShouldRunListSync()
        {
            if (!Enabled || !AWSimulationStepContext.IsActive)
            {
                _listSyncs++;
                return true;
            }

            if (_lastListSyncFrame != Time.frameCount)
            {
                _lastListSyncFrame = Time.frameCount;
                _listSyncs++;
                return true;
            }

            _listSyncSkips++;
            return false;
        }

        internal static void EnsureListCurrent(StatusManager pManager)
        {
            if (!Enabled || pManager == null) return;
            pManager.checkLists();
            _listSyncs++;
        }

        internal static string GetDiagnostics()
        {
            lock (StateLock)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "active={0} statuses={1} scheduled={2} mutations={3} " +
                    "updates={4} due={5} actions={6} expiry={7} removed={8} " +
                    "stale={9} rebuilds={10} list_sync={11}/{12}(run/skip) " +
                    "wheel={13}/{14}(visit/defer)",
                    _manager != null, Entries.Count,
                    _timingWheelNodes + Heap.Count,
                    Mutations.Count, _updates, _dueChecks, _actionCalls,
                    _expiryCalls, _removedStatuses, _staleNodes, _rebuilds,
                    _listSyncs, _listSyncSkips, _timingWheelVisits,
                    _timingWheelDeferred);
            }
        }

        internal static void ClearRuntime()
        {
            lock (StateLock)
            {
                ClearState(restoreTimers: false);
            }
        }

        private static void Enqueue(Status pStatus, MutationKind pKind)
        {
            if (pStatus == null || !Enabled) return;
            Mutations.Enqueue(new Mutation(pStatus, pKind));
        }

        private static void EnsureWorld(StatusManager pManager)
        {
            int generation = AWSimulationTime.Generation;
            if (ReferenceEquals(_manager, pManager) &&
                _worldGeneration == generation) return;

            ClearState(restoreTimers: false);
            _manager = pManager;
            _worldGeneration = generation;
            _tick = 0L;
            _nextOrder = 0L;
            _lastListSyncFrame = -1;
            for (int i = 0; i < pManager.list.Count; i++)
                Register(pManager.list[i], 1L,
                    (float)World.world.getCurWorldTime());
            _rebuilds++;
        }

        private static void Disable()
        {
            lock (StateLock) ClearState(restoreTimers: true);
        }

        private static void ClearState(bool restoreTimers)
        {
            if (restoreTimers)
            {
                foreach (Entry entry in Entries.Values)
                    RestoreActionTimer(entry);
            }

            Entries.Clear();
            Heap.Clear();
            Removals.Clear();
            while (Mutations.TryDequeue(out _)) { }
            _manager = null;
            _worldGeneration = -1;
            _lastListSyncFrame = -1;
            _tick = 0L;
            _nextOrder = 0L;
            _processingTick = Never;
            _timingWheelNodes = 0L;
            for (int i = 0; i < TimingWheel.Length; i++)
            {
                List<Node> bucket = TimingWheel[i];
                if (bucket == null) continue;
                bucket.Clear();
                TimingWheel[i] = null;
            }
            TimingWheelListPool.Clear();
        }

        private static void Register(Status pStatus, long pFirstTick,
            float pWorldTime)
        {
            if (pStatus == null) return;
            if (Entries.TryGetValue(pStatus, out Entry existing))
            {
                existing.Version++;
                existing.NextExpiryTick = ComputeExpiryTick(pFirstTick,
                    pWorldTime, pStatus);
                Schedule(existing);
                return;
            }

            var entry = new Entry
            {
                Status = pStatus,
                Order = _nextOrder++,
                Version = 1L,
                NextActionTick = ComputeFirstActionTick(pFirstTick,
                    pStatus),
                NextExpiryTick = pStatus.is_finished
                    ? pFirstTick
                    : ComputeExpiryTick(pFirstTick, pWorldTime, pStatus)
            };
            Entries.Add(pStatus, entry);
            Schedule(entry);
        }

        private static void DrainMutations(long pCurrentTick,
            long pCurrentOrder, float pWorldTime)
        {
            while (Mutations.TryDequeue(out Mutation mutation))
            {
                Status status = mutation.Status;
                switch (mutation.Kind)
                {
                    case MutationKind.Added:
                        Register(status,
                            pCurrentOrder == long.MinValue
                                ? pCurrentTick
                                : SafeAdd(pCurrentTick, 1L),
                            pWorldTime);
                        break;
                    case MutationKind.DurationChanged:
                        if (Entries.TryGetValue(status,
                                out Entry durationEntry))
                        {
                            long earliestEligibleTick =
                                GetEarliestEligibleTick(durationEntry,
                                    pCurrentTick, pCurrentOrder);
                            durationEntry.Version++;
                            durationEntry.NextExpiryTick =
                                Math.Max(earliestEligibleTick,
                                    ComputeExpiryTick(pCurrentTick,
                                        pWorldTime, status));
                            Schedule(durationEntry);
                        }
                        break;
                    case MutationKind.Finished:
                        if (Entries.TryGetValue(status,
                                out Entry finishedEntry))
                        {
                            finishedEntry.Version++;
                            finishedEntry.NextExpiryTick =
                                GetEarliestEligibleTick(finishedEntry,
                                    pCurrentTick, pCurrentOrder);
                            Schedule(finishedEntry);
                        }
                        break;
                    case MutationKind.Removed:
                        if (Entries.TryGetValue(status,
                                out Entry removedEntry))
                        {
                            removedEntry.Version++;
                            Entries.Remove(status);
                        }
                        break;
                }
            }
        }

        private static long GetEarliestEligibleTick(Entry pEntry,
            long pCurrentTick, long pCurrentOrder)
        {
            return pEntry.LastProcessedTick == pCurrentTick ||
                   pEntry.Order <= pCurrentOrder
                ? SafeAdd(pCurrentTick, 1L)
                : pCurrentTick;
        }

        private static long ComputeFirstActionTick(long pFirstTick,
            Status pStatus)
        {
            if (pStatus.asset?.action == null) return Never;
            return SafeAdd(pFirstTick,
                CountTimerTicks(pStatus._action_timer));
        }

        private static long ComputeNextActionTickAfterCall(long pCurrentTick,
            Status pStatus)
        {
            if (pStatus.asset?.action == null) return Never;
            return SafeAdd(SafeAdd(pCurrentTick, 1L),
                CountTimerTicks(pStatus._action_timer));
        }

        private static long CountTimerTicks(float pTimer)
        {
            if (float.IsNaN(pTimer) || pTimer <= 0f) return 0L;
            if (float.IsPositiveInfinity(pTimer)) return Never;
            return Math.Max(0L, (long)Math.Ceiling(pTimer /
                AWFrameSchedulerRules.FixedSimulationStepSeconds));
        }

        private static void RestoreActionTimer(Entry pEntry)
        {
            Status status = pEntry?.Status;
            if (status == null || status.asset?.action == null ||
                pEntry.NextActionTick == Never)
                return;

            long ticksUntilAction = pEntry.NextActionTick - _tick;
            if (ticksUntilAction <= 0L)
            {
                status._action_timer = 0f;
                return;
            }

            long decrementTicks = Math.Max(0L, ticksUntilAction - 1L);
            status._action_timer = decrementTicks *
                AWFrameSchedulerRules.FixedSimulationStepSeconds;
        }

        private static long ComputeExpiryTick(long pEarliestTick,
            float pWorldTime, Status pStatus)
        {
            if (pStatus == null || pStatus.is_finished)
                return pEarliestTick;
            double endTime = pStatus._end_time;
            if (double.IsNaN(endTime) || double.IsPositiveInfinity(endTime))
                return Never;
            double remaining = endTime - pWorldTime;
            if (remaining <= 0d) return pEarliestTick;
            long delay = (long)Math.Floor(remaining /
                AWFrameSchedulerRules.FixedSimulationStepSeconds);
            return SafeAdd(pEarliestTick, Math.Max(1L, delay));
        }

        private static void Schedule(Entry pEntry)
        {
            pEntry.Version++;
            long dueTick = Math.Min(pEntry.NextActionTick,
                pEntry.NextExpiryTick);
            if (dueTick == Never) return;
            Node node = new Node(pEntry, dueTick);
            if (_processingTick != Never && dueTick <= _processingTick)
            {
                Push(node);
                return;
            }

            AddToTimingWheel(node);
        }

        private static void PrepareDueNodes(long pCurrentTick)
        {
            int bucketIndex = (int)(pCurrentTick & TimingWheelMask);
            List<Node> bucket = TimingWheel[bucketIndex];
            if (bucket == null) return;

            TimingWheel[bucketIndex] = null;
            _timingWheelNodes -= bucket.Count;
            for (int i = 0; i < bucket.Count; i++)
            {
                Node node = bucket[i];
                _timingWheelVisits++;
                if (node.DueTick <= pCurrentTick)
                    Push(node);
                else
                {
                    AddToTimingWheel(node);
                    _timingWheelDeferred++;
                }
            }

            bucket.Clear();
            TimingWheelListPool.Push(bucket);
        }

        private static void AddToTimingWheel(Node pNode)
        {
            int bucketIndex = (int)(pNode.DueTick & TimingWheelMask);
            List<Node> bucket = TimingWheel[bucketIndex];
            if (bucket == null)
            {
                bucket = TimingWheelListPool.Count > 0
                    ? TimingWheelListPool.Pop()
                    : new List<Node>(4);
                TimingWheel[bucketIndex] = bucket;
            }

            bucket.Add(pNode);
            _timingWheelNodes++;
        }

        private static void QueueRemoval(Entry pEntry)
        {
            if (pEntry != null && !Removals.Contains(pEntry))
                Removals.Add(pEntry);
        }

        private static void RemoveFinished(StatusManager pManager)
        {
            if (Removals.Count == 0) return;
            Removals.Sort(ReverseOrderComparison);
            for (int i = 0; i < Removals.Count; i++)
            {
                Entry entry = Removals[i];
                Status status = entry?.Status;
                if (status == null || !status.is_finished) continue;
                if (!Entries.TryGetValue(status, out Entry current) ||
                    !ReferenceEquals(entry, current)) continue;
                entry.Version++;
                Entries.Remove(status);
                pManager.removeObject(status);
                _removedStatuses++;
            }
            Removals.Clear();
        }

        private static long SafeAdd(long pLeft, long pRight)
        {
            if (pLeft == Never || pRight == Never ||
                pRight > 0L && pLeft > long.MaxValue - pRight)
                return Never;
            return pLeft + pRight;
        }

        private static int CompareEntryOrderDescending(Entry pLeft,
            Entry pRight)
        {
            return pRight.Order.CompareTo(pLeft.Order);
        }

        private static void Push(Node pNode)
        {
            int index = Heap.Count;
            Heap.Add(pNode);
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (Compare(Heap[parent], pNode) <= 0) break;
                Heap[index] = Heap[parent];
                index = parent;
            }
            Heap[index] = pNode;
        }

        private static Node Pop()
        {
            Node root = Heap[0];
            int lastIndex = Heap.Count - 1;
            Node last = Heap[lastIndex];
            Heap.RemoveAt(lastIndex);
            if (lastIndex == 0) return root;
            int index = 0;
            int half = lastIndex >> 1;
            while (index < half)
            {
                int left = (index << 1) + 1;
                int right = left + 1;
                int child = right < lastIndex &&
                    Compare(Heap[right], Heap[left]) < 0 ? right : left;
                if (Compare(last, Heap[child]) <= 0) break;
                Heap[index] = Heap[child];
                index = child;
            }
            Heap[index] = last;
            return root;
        }

        private static int Compare(Node pLeft, Node pRight)
        {
            int due = pLeft.DueTick.CompareTo(pRight.DueTick);
            return due != 0 ? due : pLeft.Order.CompareTo(pRight.Order);
        }
    }
}
