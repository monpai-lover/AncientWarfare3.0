using System.Threading;

namespace AncientWarfare3.core.performance
{
    internal readonly struct AWIdleBehaviourThrottleDiagnosticSnapshot
    {
        public AWIdleBehaviourThrottleDiagnosticSnapshot(
            long pSocializeAllowed, long pSocializeDeferred,
            long pEmoteAllowed, long pEmoteDeferred,
            long pSleepAllowed, long pSleepDeferred)
        {
            SocializeAllowed = pSocializeAllowed;
            SocializeDeferred = pSocializeDeferred;
            EmoteAllowed = pEmoteAllowed;
            EmoteDeferred = pEmoteDeferred;
            SleepAllowed = pSleepAllowed;
            SleepDeferred = pSleepDeferred;
        }

        public long SocializeAllowed { get; }
        public long SocializeDeferred { get; }
        public long EmoteAllowed { get; }
        public long EmoteDeferred { get; }
        public long SleepAllowed { get; }
        public long SleepDeferred { get; }
    }

    internal static class AWIdleBehaviourThrottleDiagnostics
    {
        private static long _socializeAllowed;
        private static long _socializeDeferred;
        private static long _emoteAllowed;
        private static long _emoteDeferred;
        private static long _sleepAllowed;
        private static long _sleepDeferred;

        public static void Record(AWIdleBehaviourKind pKind, bool allowed)
        {
            switch (pKind)
            {
                case AWIdleBehaviourKind.Socialize:
                    Interlocked.Increment(ref allowed
                        ? ref _socializeAllowed
                        : ref _socializeDeferred);
                    break;
                case AWIdleBehaviourKind.EmoteSearch:
                    Interlocked.Increment(ref allowed
                        ? ref _emoteAllowed
                        : ref _emoteDeferred);
                    break;
                case AWIdleBehaviourKind.Sleep:
                    Interlocked.Increment(ref allowed
                        ? ref _sleepAllowed
                        : ref _sleepDeferred);
                    break;
            }
        }

        public static AWIdleBehaviourThrottleDiagnosticSnapshot Snapshot()
        {
            return new AWIdleBehaviourThrottleDiagnosticSnapshot(
                Volatile.Read(ref _socializeAllowed),
                Volatile.Read(ref _socializeDeferred),
                Volatile.Read(ref _emoteAllowed),
                Volatile.Read(ref _emoteDeferred),
                Volatile.Read(ref _sleepAllowed),
                Volatile.Read(ref _sleepDeferred));
        }

        public static void Reset()
        {
            Interlocked.Exchange(ref _socializeAllowed, 0L);
            Interlocked.Exchange(ref _socializeDeferred, 0L);
            Interlocked.Exchange(ref _emoteAllowed, 0L);
            Interlocked.Exchange(ref _emoteDeferred, 0L);
            Interlocked.Exchange(ref _sleepAllowed, 0L);
            Interlocked.Exchange(ref _sleepDeferred, 0L);
        }
    }
}
