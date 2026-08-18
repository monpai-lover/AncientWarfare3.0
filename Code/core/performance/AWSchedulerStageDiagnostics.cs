using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

namespace AncientWarfare3.core.performance
{
    internal enum AWSchedulerStageBucket
    {
        Maintenance,
        World,
        Map,
        Cities,
        Actors,
        Buildings,
        Armies,
        Kingdoms,
        Statuses,
        OtherVanilla,
        Aw3Authority,
        Count
    }

    internal static class AWSchedulerStageDiagnostics
    {
        private static readonly long[] Ticks =
            new long[(int)AWSchedulerStageBucket.Count];
        private static readonly int[] Calls =
            new int[(int)AWSchedulerStageBucket.Count];
        private static int _sampling;
        private static long _schedulerTicks;

        internal static void BeginFrame(bool pSampling)
        {
            Volatile.Write(ref _sampling, pSampling ? 1 : 0);
            Interlocked.Exchange(ref _schedulerTicks, 0L);
            for (int i = 0; i < Ticks.Length; i++)
            {
                Interlocked.Exchange(ref Ticks[i], 0L);
                Interlocked.Exchange(ref Calls[i], 0);
            }
        }

        internal static long BeginSchedulerFrame()
        {
            return Volatile.Read(ref _sampling) != 0
                ? Stopwatch.GetTimestamp()
                : 0L;
        }

        internal static void EndSchedulerFrame(long pStarted)
        {
            if (pStarted <= 0L) return;
            Interlocked.Add(ref _schedulerTicks,
                Math.Max(0L, Stopwatch.GetTimestamp() - pStarted));
        }

        internal static long Begin(AWSchedulerStageBucket pBucket)
        {
            _ = pBucket;
            return Volatile.Read(ref _sampling) != 0
                ? Stopwatch.GetTimestamp()
                : 0L;
        }

        internal static void End(AWSchedulerStageBucket pBucket,
            long pStarted)
        {
            if (pStarted <= 0L) return;
            int index = (int)pBucket;
            Interlocked.Add(ref Ticks[index],
                Math.Max(0L, Stopwatch.GetTimestamp() - pStarted));
            Interlocked.Increment(ref Calls[index]);
        }

        internal static AWSchedulerStageDiagnosticSnapshot TakeSnapshot()
        {
            long[] ticks = new long[Ticks.Length];
            int[] calls = new int[Calls.Length];
            long total = 0L;
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] = Interlocked.Exchange(ref Ticks[i], 0L);
                calls[i] = Interlocked.Exchange(ref Calls[i], 0);
                total += ticks[i];
            }

            return new AWSchedulerStageDiagnosticSnapshot(
                ticks, calls, total,
                Interlocked.Exchange(ref _schedulerTicks, 0L), 0L, 0L);
        }
    }

    internal readonly struct AWSchedulerStageDiagnosticSnapshot
    {
        internal AWSchedulerStageDiagnosticSnapshot(long[] pTicks,
            int[] pCalls, long pTotalTicks, long pSchedulerTicks,
            long pUnaccountedTicks, long pHostUnaccountedTicks)
        {
            Ticks = pTicks ?? Array.Empty<long>();
            Calls = pCalls ?? Array.Empty<int>();
            TotalTicks = Math.Max(0L, pTotalTicks);
            SchedulerTicks = Math.Max(0L, pSchedulerTicks);
            UnaccountedTicks = Math.Max(0L, pUnaccountedTicks);
            HostUnaccountedTicks = Math.Max(0L, pHostUnaccountedTicks);
        }

        internal long[] Ticks { get; }
        internal int[] Calls { get; }
        internal long TotalTicks { get; }
        internal long SchedulerTicks { get; }
        internal long UnaccountedTicks { get; }
        internal long HostUnaccountedTicks { get; }

        internal AWSchedulerStageDiagnosticSnapshot WithFrameTicks(
            long pFrameTicks)
        {
            return new AWSchedulerStageDiagnosticSnapshot(Ticks, Calls,
                TotalTicks, SchedulerTicks,
                Math.Max(0L, SchedulerTicks - TotalTicks),
                Math.Max(0L, pFrameTicks - SchedulerTicks));
        }

        internal string FormatMilliseconds()
        {
            var text = new StringBuilder();
            for (int i = 0; i < Ticks.Length; i++)
            {
                if (Ticks[i] <= 0L && Calls[i] <= 0) continue;
                if (text.Length > 0) text.Append(',');
                text.Append(Id((AWSchedulerStageBucket)i));
                text.Append(':');
                text.Append((Ticks[i] * 1000d / Stopwatch.Frequency)
                    .ToString("0.###", CultureInfo.InvariantCulture));
                text.Append('/');
                text.Append(Calls[i]);
            }
            return text.Length == 0 ? "none" : text.ToString();
        }

        private static string Id(AWSchedulerStageBucket pBucket)
        {
            return pBucket switch
            {
                AWSchedulerStageBucket.Maintenance => "maintenance",
                AWSchedulerStageBucket.World => "world",
                AWSchedulerStageBucket.Map => "map",
                AWSchedulerStageBucket.Cities => "cities",
                AWSchedulerStageBucket.Actors => "actors",
                AWSchedulerStageBucket.Buildings => "buildings",
                AWSchedulerStageBucket.Armies => "armies",
                AWSchedulerStageBucket.Kingdoms => "kingdoms",
                AWSchedulerStageBucket.Statuses => "statuses",
                AWSchedulerStageBucket.OtherVanilla => "other_vanilla",
                AWSchedulerStageBucket.Aw3Authority => "aw3_authority",
                _ => "unknown"
            };
        }
    }
}
