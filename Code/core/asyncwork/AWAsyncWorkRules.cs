using System;

namespace AncientWarfare3.core.asyncwork
{
    internal enum AWAsyncLifecycleState
    {
        Stopped,
        Starting,
        Running,
        Draining,
        Faulted
    }

    internal enum AWAsyncLane
    {
        None,
        Database,
        Traversal,
        Ui,
        Ai
    }

    internal enum AWAsyncCommitMode
    {
        MainThread,
        Background
    }

    internal readonly struct AWAsyncStamp
    {
        public AWAsyncStamp(long pWorldGeneration, long pCaptureTick,
            long pSourceRevision)
        {
            WorldGeneration = pWorldGeneration;
            CaptureTick = pCaptureTick;
            SourceRevision = pSourceRevision;
        }

        public long WorldGeneration { get; }
        public long CaptureTick { get; }
        public long SourceRevision { get; }
    }

    internal static class AWAsyncCapacity
    {
        public const int DatabaseAppend = 8192;
        public const int DatabaseState = 4096;
        public const int Ai = 256;
        public const int Ui = 32;
        public const int CompletionBatches = 512;
        public const int FaultRecords = 32;

        public static int HighWatermark(int pCapacity)
        {
            return pCapacity * 3 / 4;
        }

        public static int LowWatermark(int pCapacity)
        {
            return pCapacity / 2;
        }
    }

    internal static class AWAsyncWorkerRules
    {
        internal const int MaximumWorkers = 4;

        internal static int ResolveWorkerCount(int pProcessorCount)
        {
            int parallelBudget = Math.Max(1, pProcessorCount - 2);
            return Math.Min(MaximumWorkers,
                Math.Max(1, (parallelBudget + 2) / 3));
        }

        internal static int NormalizeWorkerCount(int pRequested,
            int pProcessorCount)
        {
            return pRequested <= 0
                ? ResolveWorkerCount(pProcessorCount)
                : Math.Min(MaximumWorkers, Math.Max(1, pRequested));
        }

        internal static int ResolveWorkerSignalReleaseCount(
            int pWorkerCount, int pActiveWorkerCount, int pPendingSignalCount)
        {
            int workers = Math.Max(0, pWorkerCount);
            int active = Math.Max(0, pActiveWorkerCount);
            int pending = Math.Max(0, pPendingSignalCount);
            return Math.Max(0, workers - active - pending);
        }

        internal static int ResolveWorkSignalReleaseCount(
            int pQueuedWorkCount, int pWorkerCount,
            int pActiveWorkerCount, int pPendingSignalCount)
        {
            int queued = Math.Max(0, pQueuedWorkCount);
            int workers = Math.Max(0, pWorkerCount);
            int active = Math.Min(workers, Math.Max(0, pActiveWorkerCount));
            int pending = Math.Max(0, pPendingSignalCount);
            int idle = Math.Max(0, workers - active);
            return Math.Max(0, Math.Min(idle, queued) - pending);
        }
    }

    internal static class AWAsyncVersionRules
    {
        public static bool Accept(long resultWorldGeneration,
            long currentWorldGeneration, long resultRevision,
            long currentRevision)
        {
            return resultWorldGeneration == currentWorldGeneration &&
                   resultRevision == currentRevision;
        }
    }

    internal static class AWAsyncLifecycleRules
    {
        public static bool CanTransition(AWAsyncLifecycleState pCurrent,
            AWAsyncLifecycleState pNext)
        {
            switch (pCurrent)
            {
                case AWAsyncLifecycleState.Stopped:
                    return pNext == AWAsyncLifecycleState.Starting;
                case AWAsyncLifecycleState.Starting:
                    return pNext == AWAsyncLifecycleState.Running ||
                           pNext == AWAsyncLifecycleState.Faulted;
                case AWAsyncLifecycleState.Running:
                    return pNext == AWAsyncLifecycleState.Draining ||
                           pNext == AWAsyncLifecycleState.Faulted;
                case AWAsyncLifecycleState.Draining:
                    return pNext == AWAsyncLifecycleState.Stopped ||
                           pNext == AWAsyncLifecycleState.Faulted;
                default:
                    return false;
            }
        }
    }

    internal static class AWAsyncFeatureRules
    {
        public static bool ShouldEnable(string pValue)
        {
            return string.Equals(pValue, "1", System.StringComparison.Ordinal) ||
                   string.Equals(pValue, "true",
                       System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldStartCompute(bool database, bool ai,
            bool traversal, bool ui, bool shadow)
        {
            return ai || traversal || ui || shadow;
        }
    }

    internal static class AWAsyncPriorityRules
    {
        private const int MaxHighPriorityStreak = 3;

        public static AWAsyncLane SelectComputeLane(bool hasTraversal,
            bool hasVisibleUi, bool hasAi, int highPriorityStreak)
        {
            return SelectComputeLane(hasTraversal, hasVisibleUi, hasAi,
                highPriorityStreak, AWAsyncLane.None);
        }

        public static AWAsyncLane SelectComputeLane(bool hasTraversal,
            bool hasVisibleUi, bool hasAi, int highPriorityStreak,
            AWAsyncLane lastHighPriorityLane)
        {
            if (highPriorityStreak >= MaxHighPriorityStreak)
            {
                if (hasAi) return AWAsyncLane.Ai;
            }
            if (hasTraversal && hasVisibleUi)
                return lastHighPriorityLane == AWAsyncLane.Traversal
                    ? AWAsyncLane.Ui
                    : AWAsyncLane.Traversal;
            if (hasTraversal) return AWAsyncLane.Traversal;
            if (hasVisibleUi) return AWAsyncLane.Ui;
            if (hasAi) return AWAsyncLane.Ai;
            return AWAsyncLane.None;
        }
    }
}
