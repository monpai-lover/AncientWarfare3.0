using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.uiquery
{
    internal readonly struct AWUiRetryTicket
    {
        public AWUiRetryTicket(long generation, long worldGeneration,
            long contentRevision, string specKey)
        {
            Generation = generation;
            WorldGeneration = worldGeneration;
            ContentRevision = contentRevision;
            SpecKey = specKey ?? string.Empty;
        }

        public long Generation { get; }
        public long WorldGeneration { get; }
        public long ContentRevision { get; }
        public string SpecKey { get; }
    }

    internal sealed class AWUiBoundedRetryState
    {
        private readonly int _maxAttempts;
        private readonly int _initialBackoffFrames;
        private readonly int _maximumBackoffFrames;
        private long _generation;
        private AWUiRetryTicket _current;
        private bool _active;
        private bool _inFlight;
        private int _attempts;
        private long _nextAttemptFrame;

        public AWUiBoundedRetryState(int maxAttempts,
            int initialBackoffFrames, int maximumBackoffFrames)
        {
            _maxAttempts = Math.Max(1, maxAttempts);
            _initialBackoffFrames = Math.Max(1, initialBackoffFrames);
            _maximumBackoffFrames = Math.Max(_initialBackoffFrames,
                maximumBackoffFrames);
        }

        public bool Exhausted => _active && !_inFlight &&
                                   _attempts >= _maxAttempts;
        public bool InFlight => _inFlight;
        public int Attempts => _attempts;

        public AWUiRetryTicket Begin(long worldGeneration,
            long contentRevision, string specKey, long currentFrame)
        {
            string normalizedKey = specKey ?? string.Empty;
            if (_active && _current.WorldGeneration == worldGeneration &&
                _current.ContentRevision == contentRevision &&
                string.Equals(_current.SpecKey, normalizedKey,
                    StringComparison.Ordinal))
                return _current;

            AdvanceGeneration();
            _current = new AWUiRetryTicket(_generation, worldGeneration,
                contentRevision, normalizedKey);
            _active = true;
            _inFlight = false;
            _attempts = 0;
            _nextAttemptFrame = currentFrame;
            return _current;
        }

        public bool Accept(AWUiRetryTicket ticket,
            long currentWorldGeneration, long currentContentRevision,
            string currentSpecKey)
        {
            return _active && ticket.Generation == _current.Generation &&
                   ticket.WorldGeneration == currentWorldGeneration &&
                   ticket.ContentRevision == currentContentRevision &&
                   string.Equals(ticket.SpecKey, currentSpecKey ?? string.Empty,
                       StringComparison.Ordinal);
        }

        public bool TryStart(AWUiRetryTicket ticket, long currentFrame)
        {
            if (!_active || _inFlight || Exhausted ||
                ticket.Generation != _current.Generation ||
                currentFrame < _nextAttemptFrame) return false;
            _attempts++;
            _inFlight = true;
            return true;
        }

        public void RecordFault(AWUiRetryTicket ticket, long currentFrame)
        {
            if (!_active || ticket.Generation != _current.Generation) return;
            _inFlight = false;
            if (_attempts >= _maxAttempts) return;
            int shift = Math.Min(30, Math.Max(0, _attempts - 1));
            long delay = Math.Min(_maximumBackoffFrames,
                (long)_initialBackoffFrames << shift);
            _nextAttemptFrame = currentFrame + delay;
        }

        public void RecordSuccess(AWUiRetryTicket ticket)
        {
            if (!_active || ticket.Generation != _current.Generation) return;
            _inFlight = false;
            _active = false;
        }

        public void Cancel()
        {
            _active = false;
            _inFlight = false;
            AdvanceGeneration();
        }

        private void AdvanceGeneration()
        {
            _generation = _generation == long.MaxValue
                ? 1L
                : _generation + 1L;
        }
    }

    internal readonly struct AWUiMaterializationIntentLease
    {
        internal AWUiMaterializationIntentLease(long leaseGeneration,
            bool preservePan, float panX, float panY, bool expandLive)
        {
            LeaseGeneration = leaseGeneration;
            PreservePan = preservePan;
            PanX = panX;
            PanY = panY;
            ExpandLive = expandLive;
        }

        internal long LeaseGeneration { get; }
        public bool PreservePan { get; }
        public float PanX { get; }
        public float PanY { get; }
        public bool ExpandLive { get; }
    }

    internal sealed class AWUiMaterializationIntentState
    {
        private long _leaseGeneration;
        private bool _preservePan;
        private float _panX;
        private float _panY;
        private bool _expandLive;

        public void RequestPan(float panX, float panY)
        {
            _preservePan = true;
            _panX = panX;
            _panY = panY;
            InvalidateLeases();
        }

        public void RequestExpandLive()
        {
            _expandLive = true;
            InvalidateLeases();
        }

        public AWUiMaterializationIntentLease Capture()
        {
            InvalidateLeases();
            return new AWUiMaterializationIntentLease(_leaseGeneration,
                _preservePan, _panX, _panY, _expandLive);
        }

        public bool Commit(AWUiMaterializationIntentLease lease)
        {
            if (lease.LeaseGeneration != _leaseGeneration) return false;
            if (lease.PreservePan) _preservePan = false;
            if (lease.ExpandLive) _expandLive = false;
            InvalidateLeases();
            return true;
        }

        public void CancelForManualFold()
        {
            _expandLive = false;
            InvalidateLeases();
        }

        public void CancelAll()
        {
            _preservePan = false;
            _expandLive = false;
            InvalidateLeases();
        }

        private void InvalidateLeases()
        {
            _leaseGeneration = _leaseGeneration == long.MaxValue
                ? 1L
                : _leaseGeneration + 1L;
        }
    }

    internal sealed class AWUiIncrementalIdComparison
    {
        private readonly IReadOnlyList<long> _synchronous;
        private readonly IReadOnlyList<long> _asynchronous;
        private int _index;

        public AWUiIncrementalIdComparison(
            IReadOnlyList<long> synchronous,
            IReadOnlyList<long> asynchronous)
        {
            _synchronous = synchronous ?? Array.Empty<long>();
            _asynchronous = asynchronous ?? Array.Empty<long>();
            IsMatch = _synchronous.Count == _asynchronous.Count;
            MismatchIndex = IsMatch ? -1 : Math.Min(_synchronous.Count,
                _asynchronous.Count);
        }

        public int ComparedCount { get; private set; }
        public bool Completed { get; private set; }
        public bool IsMatch { get; private set; }
        public int MismatchIndex { get; private set; }

        public bool MoveNext()
        {
            if (Completed) return false;
            int comparable = Math.Min(_synchronous.Count,
                _asynchronous.Count);
            if (_index >= comparable)
            {
                Completed = true;
                return false;
            }

            if (_synchronous[_index] != _asynchronous[_index])
            {
                IsMatch = false;
                if (MismatchIndex < 0) MismatchIndex = _index;
            }
            _index++;
            ComparedCount++;
            if (_index >= comparable) Completed = true;
            return true;
        }
    }

    internal sealed class AWUiActorVisitBudget
    {
        private readonly int _maximumActors;
        private readonly HashSet<long> _visited = new HashSet<long>();

        public AWUiActorVisitBudget(int maximumActors)
        {
            _maximumActors = Math.Max(1, maximumActors);
        }

        public int Count => _visited.Count;
        public bool Exhausted { get; private set; }

        public bool TryVisit(long actorId)
        {
            if (actorId < 0L || _visited.Contains(actorId)) return false;
            if (_visited.Count >= _maximumActors)
            {
                Exhausted = true;
                return false;
            }
            _visited.Add(actorId);
            return true;
        }
    }

    internal sealed class AWUiBoundedCleanupQueue<T>
    {
        private sealed class OwnedBatch
        {
            public List<T> Items;
            public long WorldGeneration;
            public bool Invalidated;
        }

        private readonly Queue<OwnedBatch> _batches =
            new Queue<OwnedBatch>();

        public int PendingCount { get; private set; }

        public void EnqueueOwned(List<T> items, long worldGeneration)
        {
            if (items == null || items.Count == 0) return;
            _batches.Enqueue(new OwnedBatch
            {
                Items = items,
                WorldGeneration = worldGeneration
            });
            PendingCount += items.Count;
        }

        public int Drain(int maximumSteps, Action<T> cleanup)
        {
            int budget = Math.Max(0, maximumSteps);
            int drained = 0;
            while (drained < budget && _batches.Count > 0)
            {
                OwnedBatch batch = _batches.Peek();
                int index = batch.Items.Count - 1;
                T item = batch.Items[index];
                batch.Items.RemoveAt(index);
                PendingCount--;
                drained++;
                cleanup?.Invoke(item);
                if (batch.Items.Count == 0) _batches.Dequeue();
            }
            return drained;
        }

        public void InvalidateWorld(long currentWorldGeneration)
        {
            foreach (OwnedBatch batch in _batches)
                if (batch.WorldGeneration != currentWorldGeneration)
                    batch.Invalidated = true;
        }

        public bool HoldsWorld(long worldGeneration)
        {
            foreach (OwnedBatch batch in _batches)
                if (!batch.Invalidated &&
                    batch.WorldGeneration == worldGeneration) return true;
            return false;
        }
    }
}
