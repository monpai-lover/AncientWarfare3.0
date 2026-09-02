using System;
using System.Collections.Generic;
#if !AW3_RULES_TESTS
using AncientWarfare3.core.db;
using AncientWarfare3.core.historyapi;
#endif

namespace AncientWarfare3.api.history
{
    public static class AW3HistoryApi
    {
        public const string ApiVersion = "1.0";

#if AW3_RULES_TESTS
        public static bool IsAvailable => false;
        public static long RuntimeDatabaseEpoch => 0L;
#else
        public static bool IsAvailable
        {
            get
            {
                try { return LineageArchiveManager.Instance.IsOperational; }
                catch { return false; }
            }
        }
        public static long RuntimeDatabaseEpoch =>
            LineageArchiveManager.RuntimeDatabaseEpoch;
#endif

        public static AW3HistoryPage<AW3HistoryEvent> ReadEvents(
            AW3HistoryQuery query)
        {
#if AW3_RULES_TESTS
            return AW3HistoryPage<AW3HistoryEvent>.Create(
                new List<AW3HistoryEvent>());
#else
            return AW3HistoryReadService.ReadEvents(query);
#endif
        }

        public static IDisposable Subscribe(AW3HistorySubscription filter,
            Action<AW3HistoryEvent> handler)
        {
#if AW3_RULES_TESTS
            return NoopSubscription.Instance;
#else
            return AW3HistoryEventPublisher.Subscribe(filter, handler);
#endif
        }

        private sealed class NoopSubscription : IDisposable
        {
            internal static readonly NoopSubscription Instance =
                new NoopSubscription();

            public void Dispose() { }
        }
    }
}
