using System;
using System.Diagnostics;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    internal enum AWPathfindingBenchmarkMetric
    {
        Reuse,
        ReuseMiss,
        Create,
        TaskCreate,
        Cancel,
        CancelEmpty,
        Enqueue,
        QueueWait,
        BackgroundPath,
        Count
    }

    // Cultiway's opt-in path lifecycle profiler. It is deliberately disabled
    // by default and uses lock-free counters so diagnostics cannot become a
    // source of path contention.
    internal static class AWPathfindingProfiler
    {
        private static AWPathfindingProfilerSession _active;

        internal static void SetEnabled(bool pEnabled)
        {
            AWPathfindingProfilerSession current =
                Volatile.Read(ref _active);
            if (pEnabled)
            {
                if (current == null)
                    Interlocked.CompareExchange(ref _active,
                        new AWPathfindingProfilerSession(), null);
            }
            else if (current != null)
            {
                Interlocked.Exchange(ref _active, null);
            }
        }

        internal static AWPathfindingProfilerMeasurement Start()
        {
            return Start(Volatile.Read(ref _active));
        }

        internal static AWPathfindingProfilerMeasurement Start(
            AWPathfindingProfilerSession pSession)
        {
            return IsCurrent(pSession)
                ? new AWPathfindingProfilerMeasurement(pSession,
                    Stopwatch.GetTimestamp())
                : default;
        }

        internal static long MarkEnqueued(
            AWPathfindingProfilerSession pSession)
        {
            return IsCurrent(pSession) ? Stopwatch.GetTimestamp() : 0L;
        }

        internal static void RecordQueueWait(
            AWPathfindingProfilerSession pSession, long pEnqueuedAt)
        {
            if (pEnqueuedAt == 0L || !IsCurrent(pSession)) return;
            pSession.Record(AWPathfindingBenchmarkMetric.QueueWait,
                Math.Max(0L, Stopwatch.GetTimestamp() - pEnqueuedAt), false);
        }

        internal static void RecordInstant(
            AWPathfindingProfilerSession pSession,
            AWPathfindingBenchmarkMetric pMetric)
        {
            if (!IsCurrent(pSession)) return;
            pSession.Record(pMetric, 0L, false);
        }

        internal static AWPathfindingProfilerSnapshot CaptureSnapshot()
        {
            AWPathfindingProfilerSession session = Volatile.Read(ref _active);
            return session?.CaptureSnapshot() ?? default;
        }

        private static bool IsCurrent(AWPathfindingProfilerSession pSession)
        {
            return pSession != null &&
                   ReferenceEquals(Volatile.Read(ref _active), pSession);
        }

        internal sealed class AWPathfindingProfilerSession
        {
            private readonly long[] _elapsed = new long[
                (int)AWPathfindingBenchmarkMetric.Count];
            private readonly long[] _counters = new long[
                (int)AWPathfindingBenchmarkMetric.Count];
            private readonly long _timestampOverhead =
                MeasureTimestampOverhead();

            internal void Record(AWPathfindingBenchmarkMetric pMetric,
                long pTicks, bool pSubtractOverhead)
            {
                if (!IsCurrent(this)) return;
                if (pSubtractOverhead)
                    pTicks = Math.Max(0L, pTicks - _timestampOverhead);
                int index = (int)pMetric;
                Interlocked.Add(ref _elapsed[index], pTicks);
                Interlocked.Increment(ref _counters[index]);
            }

            internal AWPathfindingProfilerSnapshot CaptureSnapshot()
            {
                return new AWPathfindingProfilerSnapshot(this,
                    Capture(AWPathfindingBenchmarkMetric.Reuse),
                    Capture(AWPathfindingBenchmarkMetric.ReuseMiss),
                    Capture(AWPathfindingBenchmarkMetric.Create),
                    Capture(AWPathfindingBenchmarkMetric.TaskCreate),
                    Capture(AWPathfindingBenchmarkMetric.Cancel),
                    Capture(AWPathfindingBenchmarkMetric.CancelEmpty),
                    Capture(AWPathfindingBenchmarkMetric.Enqueue),
                    Capture(AWPathfindingBenchmarkMetric.QueueWait),
                    Capture(AWPathfindingBenchmarkMetric.BackgroundPath));
            }

            private AWPathfindingMetricSnapshot Capture(
                AWPathfindingBenchmarkMetric pMetric)
            {
                int index = (int)pMetric;
                return new AWPathfindingMetricSnapshot(
                    Interlocked.Read(ref _elapsed[index]),
                    Interlocked.Read(ref _counters[index]));
            }

            private static long MeasureTimestampOverhead()
            {
                long minimum = long.MaxValue;
                for (int i = 0; i < 16; i++)
                {
                    long started = Stopwatch.GetTimestamp();
                    minimum = Math.Min(minimum,
                        Stopwatch.GetTimestamp() - started);
                }
                return minimum == long.MaxValue ? 0L : minimum;
            }
        }

        internal readonly struct AWPathfindingProfilerMeasurement
        {
            private readonly long _startedAt;

            internal AWPathfindingProfilerMeasurement(
                AWPathfindingProfilerSession pSession, long pStartedAt)
            {
                Session = pSession;
                _startedAt = pStartedAt;
            }

            internal AWPathfindingProfilerSession Session { get; }

            internal void Complete(AWPathfindingBenchmarkMetric pMetric)
            {
                if (Session == null) return;
                Session.Record(pMetric,
                    Math.Max(0L, Stopwatch.GetTimestamp() - _startedAt), true);
            }
        }

        internal readonly struct AWPathfindingMetricSnapshot
        {
            internal AWPathfindingMetricSnapshot(long pElapsedTicks,
                long pCounter)
            {
                ElapsedTicks = pElapsedTicks;
                Counter = pCounter;
            }

            internal long ElapsedTicks { get; }
            internal long Counter { get; }
            internal double Seconds => ElapsedTicks /
                (double)Stopwatch.Frequency;

            internal AWPathfindingMetricSnapshot DeltaFrom(
                AWPathfindingMetricSnapshot pEarlier)
            {
                return new AWPathfindingMetricSnapshot(
                    Math.Max(0L, ElapsedTicks - pEarlier.ElapsedTicks),
                    Math.Max(0L, Counter - pEarlier.Counter));
            }
        }

        internal readonly struct AWPathfindingProfilerSnapshot
        {
            internal AWPathfindingProfilerSnapshot(
                AWPathfindingProfilerSession pSession,
                AWPathfindingMetricSnapshot pReuse,
                AWPathfindingMetricSnapshot pReuseMiss,
                AWPathfindingMetricSnapshot pCreate,
                AWPathfindingMetricSnapshot pTaskCreate,
                AWPathfindingMetricSnapshot pCancel,
                AWPathfindingMetricSnapshot pCancelEmpty,
                AWPathfindingMetricSnapshot pEnqueue,
                AWPathfindingMetricSnapshot pQueueWait,
                AWPathfindingMetricSnapshot pBackgroundPath)
            {
                Session = pSession;
                Reuse = pReuse;
                ReuseMiss = pReuseMiss;
                Create = pCreate;
                TaskCreate = pTaskCreate;
                Cancel = pCancel;
                CancelEmpty = pCancelEmpty;
                Enqueue = pEnqueue;
                QueueWait = pQueueWait;
                BackgroundPath = pBackgroundPath;
            }

            private AWPathfindingProfilerSession Session { get; }
            internal AWPathfindingMetricSnapshot Reuse { get; }
            internal AWPathfindingMetricSnapshot ReuseMiss { get; }
            internal AWPathfindingMetricSnapshot Create { get; }
            internal AWPathfindingMetricSnapshot TaskCreate { get; }
            internal AWPathfindingMetricSnapshot Cancel { get; }
            internal AWPathfindingMetricSnapshot CancelEmpty { get; }
            internal AWPathfindingMetricSnapshot Enqueue { get; }
            internal AWPathfindingMetricSnapshot QueueWait { get; }
            internal AWPathfindingMetricSnapshot BackgroundPath { get; }

            internal AWPathfindingProfilerSnapshot DeltaFrom(
                AWPathfindingProfilerSnapshot pEarlier)
            {
                if (Session == null || !ReferenceEquals(Session,
                        pEarlier.Session)) return default;
                return new AWPathfindingProfilerSnapshot(Session,
                    Reuse.DeltaFrom(pEarlier.Reuse),
                    ReuseMiss.DeltaFrom(pEarlier.ReuseMiss),
                    Create.DeltaFrom(pEarlier.Create),
                    TaskCreate.DeltaFrom(pEarlier.TaskCreate),
                    Cancel.DeltaFrom(pEarlier.Cancel),
                    CancelEmpty.DeltaFrom(pEarlier.CancelEmpty),
                    Enqueue.DeltaFrom(pEarlier.Enqueue),
                    QueueWait.DeltaFrom(pEarlier.QueueWait),
                    BackgroundPath.DeltaFrom(pEarlier.BackgroundPath));
            }
        }
    }
}
