using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using AncientWarfare3.core.pathfinding;
using ai;
using life.taxi;

namespace AncientWarfare3.core.performance
{
    /// <summary>
    /// Captures validated Actor.goTo calls during one post cycle. The actual
    /// WorldBox mutation and AWPathFinder submission happen on the main thread
    /// at the next frame boundary, in the original request order.
    /// </summary>
    internal static class AWDeferredPathRequestBatch
    {
        private readonly struct RequestWorkItem
        {
            internal RequestWorkItem(Actor pActor, WorldTile pTarget,
                bool pPathOnWater, bool pWalkOnBlocks, bool pWalkOnLava,
                int pRegionLimit)
            {
                Actor = pActor;
                Target = pTarget;
                PathOnWater = pPathOnWater;
                WalkOnBlocks = pWalkOnBlocks;
                WalkOnLava = pWalkOnLava;
                RegionLimit = pRegionLimit;
            }

            internal Actor Actor { get; }
            internal WorldTile Target { get; }
            internal bool PathOnWater { get; }
            internal bool WalkOnBlocks { get; }
            internal bool WalkOnLava { get; }
            internal int RegionLimit { get; }
            internal long ActorId => Actor?.data?.id ?? -1L;
        }

        private static readonly List<RequestWorkItem> Requests =
            new List<RequestWorkItem>(64);
        private static readonly List<RequestWorkItem> Pending =
            new List<RequestWorkItem>(64);
        private static readonly Dictionary<long, int> RequestSlots =
            new Dictionary<long, int>();
        private static readonly Dictionary<long, int> PendingSlots =
            new Dictionary<long, int>();
        private static bool cycleActive;
        private static bool accepting;
        private static long capturedRequests;
        private static long replacedRequests;
        private static long rejectedAtCapacity;
        private static long submittedRequests;
        private static long failedRequests;

        internal static bool HasPendingRequests => Pending.Count != 0;
        internal static bool IsCapturing => accepting;
        internal static int PendingCount => Pending.Count;

        internal static void StartCycle()
        {
            if (cycleActive || accepting)
                throw new InvalidOperationException(
                    "AW deferred path request cycle is already active.");
            cycleActive = true;
            RequestSlots.Clear();
            Requests.Clear();
        }

        internal static void BeginCapture()
        {
            if (!cycleActive || accepting)
                throw new InvalidOperationException(
                    "AW deferred path request capture has invalid state.");
            accepting = true;
        }

        internal static bool TryCapture(Actor pActor, WorldTile pTarget,
            bool pPathOnWater, bool pWalkOnBlocks, bool pWalkOnLava,
            int pRegionLimit)
        {
            if (!accepting || pActor?.data == null ||
                pTarget?.data == null || pActor.current_tile?.data == null)
                return false;

            long actorId = pActor.data.id;
            int slot = AWDeferredPathRequestBatchRules.ReplaceSlotForActor(
                RequestSlots, actorId);
            RequestWorkItem item = new RequestWorkItem(pActor, pTarget,
                pPathOnWater, pWalkOnBlocks, pWalkOnLava, pRegionLimit);
            if (slot >= 0)
            {
                Requests[slot] = item;
                Interlocked.Increment(ref replacedRequests);
            }
            else
            {
                if (!AWDeferredPathRequestBatchRules.CanCapture(
                        Requests.Count, AWDeferredPathRequestBatchRules.DefaultCapacity,
                        accepting))
                {
                    Interlocked.Increment(ref rejectedAtCapacity);
                    return false;
                }
                RequestSlots.Add(actorId, Requests.Count);
                Requests.Add(item);
            }

            Interlocked.Increment(ref capturedRequests);
            return true;
        }

        internal static void EndCapture()
        {
            if (!cycleActive || !accepting)
                throw new InvalidOperationException(
                    "AW deferred path request capture has not started.");
            accepting = false;
        }

        internal static void CompleteCycle()
        {
            if (!cycleActive || accepting)
                throw new InvalidOperationException(
                    "AW deferred path request cycle has invalid state.");

            for (int i = 0; i < Requests.Count; i++)
            {
                RequestWorkItem item = Requests[i];
                long actorId = item.ActorId;
                if (actorId < 0) continue;
                if (PendingSlots.TryGetValue(actorId, out int slot))
                {
                    Pending[slot] = item;
                }
                else if (Pending.Count < AWDeferredPathRequestBatchRules.DefaultCapacity)
                {
                    PendingSlots.Add(actorId, Pending.Count);
                    Pending.Add(item);
                }
                else
                {
                    Interlocked.Increment(ref rejectedAtCapacity);
                }
            }

            Requests.Clear();
            RequestSlots.Clear();
            cycleActive = false;
        }

        internal static int FlushAtFrameStart()
        {
            int submitted = 0;
            for (int i = 0; i < Pending.Count; i++)
            {
                RequestWorkItem item = Pending[i];
                if (item.Actor?.data == null || item.Target?.data == null ||
                    item.Actor.current_tile?.data == null)
                {
                    Interlocked.Increment(ref failedRequests);
                    continue;
                }

                ExecuteEvent result = AWPathMovementBridge.Submit(item.Actor,
                    item.Target, item.PathOnWater, item.WalkOnBlocks,
                    item.WalkOnLava, item.RegionLimit);
                if (result == ExecuteEvent.True)
                {
                    submitted++;
                    Interlocked.Increment(ref submittedRequests);
                }
                else
                {
                    Interlocked.Increment(ref failedRequests);
                }
            }

            Pending.Clear();
            PendingSlots.Clear();
            return submitted;
        }

        internal static void AbortCycle()
        {
            accepting = false;
            cycleActive = false;
            Requests.Clear();
            RequestSlots.Clear();
        }

        internal static void Clear()
        {
            AbortCycle();
            Pending.Clear();
            PendingSlots.Clear();
        }

        internal static string GetDiagnostics()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "captured={0} replaced={1} pending={2} submitted={3} " +
                "failed={4} capacity_rejects={5}",
                Interlocked.Read(ref capturedRequests),
                Interlocked.Read(ref replacedRequests), Pending.Count,
                Interlocked.Read(ref submittedRequests),
                Interlocked.Read(ref failedRequests),
                Interlocked.Read(ref rejectedAtCapacity));
        }
    }
}
