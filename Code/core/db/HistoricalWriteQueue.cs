using System;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.db
{
    internal sealed class HistoricalWriteQueue
    {
        private readonly int _appendCapacity;
        private readonly int _stateCapacity;
        private readonly LinkedList<HistoricalWriteEnvelope> _queue =
            new LinkedList<HistoricalWriteEnvelope>();
        private readonly Dictionary<string,
            LinkedListNode<HistoricalWriteEnvelope>> _stateByKey =
            new Dictionary<string, LinkedListNode<HistoricalWriteEnvelope>>(
                StringComparer.Ordinal);
        private int _appendCount;

        public HistoricalWriteQueue(int appendCapacity, int stateCapacity)
        {
            _appendCapacity = Math.Max(1, appendCapacity);
            _stateCapacity = Math.Max(1, stateCapacity);
        }

        public int Count => _queue.Count;
        public int AppendCount => _appendCount;
        public int StateCount => _stateByKey.Count;

        public bool TryEnqueue(HistoricalWriteEnvelope pEnvelope,
            out HistoricalWriteEnvelope pReplaced)
        {
            pReplaced = null;
            if (pEnvelope == null ||
                string.IsNullOrEmpty(pEnvelope.OperationKey)) return false;
            if (pEnvelope.Kind == HistoricalWriteKind.Append)
            {
                if (_appendCount >= _appendCapacity) return false;
                _queue.AddLast(pEnvelope);
                _appendCount++;
                return true;
            }

            if (_stateByKey.TryGetValue(pEnvelope.OperationKey,
                    out LinkedListNode<HistoricalWriteEnvelope> existing))
            {
                pReplaced = existing.Value;
                _queue.Remove(existing);
                LinkedListNode<HistoricalWriteEnvelope> replacement =
                    _queue.AddLast(pEnvelope);
                _stateByKey[pEnvelope.OperationKey] = replacement;
                return true;
            }
            if (_stateByKey.Count >= _stateCapacity) return false;
            LinkedListNode<HistoricalWriteEnvelope> added =
                _queue.AddLast(pEnvelope);
            _stateByKey.Add(pEnvelope.OperationKey, added);
            return true;
        }

        public bool TryDequeue(out HistoricalWriteEnvelope pEnvelope)
        {
            LinkedListNode<HistoricalWriteEnvelope> first = _queue.First;
            if (first == null)
            {
                pEnvelope = null;
                return false;
            }
            _queue.RemoveFirst();
            pEnvelope = first.Value;
            if (pEnvelope.Kind == HistoricalWriteKind.Append)
            {
                _appendCount--;
            }
            else if (_stateByKey.TryGetValue(pEnvelope.OperationKey,
                         out LinkedListNode<HistoricalWriteEnvelope> indexed) &&
                     ReferenceEquals(first, indexed))
            {
                _stateByKey.Remove(pEnvelope.OperationKey);
            }
            return true;
        }
    }

    internal sealed class HistoricalWriteProgress
    {
        private long _lastCommittedSequence;

        public long LastCommittedSequence =>
            Interlocked.Read(ref _lastCommittedSequence);

        public void MarkCommitted(long pSequence)
        {
            long current;
            do
            {
                current = Interlocked.Read(ref _lastCommittedSequence);
                if (pSequence <= current) return;
            }
            while (Interlocked.CompareExchange(ref _lastCommittedSequence,
                       pSequence, current) != current);
        }

        public bool IsBarrierComplete(long pSequence)
        {
            return LastCommittedSequence >= pSequence;
        }
    }
}
