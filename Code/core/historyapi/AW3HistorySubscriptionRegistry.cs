using System;
using System.Collections.Generic;
using AncientWarfare3.api.history;

namespace AncientWarfare3.core.historyapi
{
    internal sealed class AW3HistorySubscriptionRegistry
    {
        private const int MaximumPendingEvents = 1024;
        private readonly object _gate = new object();
        private readonly List<Subscription> _subscriptions =
            new List<Subscription>();
        private readonly Queue<AW3HistoryEvent> _pending =
            new Queue<AW3HistoryEvent>();
        private int _overflowCount;

        public IDisposable Subscribe(AW3HistorySubscription filter,
            Action<AW3HistoryEvent> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var subscription = new Subscription(filter ?? AW3HistorySubscription.All,
                handler, this);
            lock (_gate) _subscriptions.Add(subscription);
            return subscription;
        }

        public void PublishCommitted(AW3HistoryEvent item)
        {
            if (item == null) return;
            lock (_gate)
            {
                if (_pending.Count >= MaximumPendingEvents)
                {
                    _pending.Dequeue();
                    _overflowCount++;
                }
                _pending.Enqueue(item);
            }
        }

        public void PublishFailed(AW3HistoryEvent item)
        {
            // Failed writes are intentionally invisible to subscribers.
        }

        public int Drain(int maximumEvents = 64)
        {
            if (maximumEvents <= 0) return 0;
            int delivered = 0;
            while (delivered < maximumEvents)
            {
                AW3HistoryEvent item;
                Subscription[] subscriptions;
                lock (_gate)
                {
                    if (_pending.Count == 0) break;
                    item = _pending.Dequeue();
                    subscriptions = _subscriptions.ToArray();
                }

                for (int index = 0; index < subscriptions.Length; index++)
                {
                    Subscription subscription = subscriptions[index];
                    if (!subscription.IsActive || !subscription.Filter.Matches(item))
                        continue;
                    try { subscription.Handler(item); }
                    catch { }
                }
                delivered++;
            }
            return delivered;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _pending.Clear();
                _subscriptions.Clear();
                _overflowCount = 0;
            }
        }

        internal int PendingCount
        {
            get { lock (_gate) return _pending.Count; }
        }

        internal int OverflowCount
        {
            get { lock (_gate) return _overflowCount; }
        }

        private void Remove(Subscription subscription)
        {
            lock (_gate) _subscriptions.Remove(subscription);
        }

        private sealed class Subscription : IDisposable
        {
            private readonly AW3HistorySubscriptionRegistry _owner;
            private bool _disposed;

            public Subscription(AW3HistorySubscription filter,
                Action<AW3HistoryEvent> handler,
                AW3HistorySubscriptionRegistry owner)
            {
                Filter = filter;
                Handler = handler;
                _owner = owner;
            }

            public AW3HistorySubscription Filter { get; }
            public Action<AW3HistoryEvent> Handler { get; }
            public bool IsActive => !_disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.Remove(this);
            }
        }
    }
}
