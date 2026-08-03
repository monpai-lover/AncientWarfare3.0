using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.db
{
    internal sealed class HistoricalWriteCallbackRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<long, Action<long, object>>
            _commitCallbacks =
                new Dictionary<long, Action<long, object>>();
        private readonly Dictionary<long, Action<long, string>>
            _failureCallbacks =
                new Dictionary<long, Action<long, string>>();

        public bool TryEnqueue(HistoricalWriteWorker pWorker,
            HistoricalWriteEnvelope pEnvelope, Action<long> pOnAccepted,
            Action<long, object> pOnCommitted,
            Action<long, string> pOnFailed,
            out HistoricalWriteEnvelope pReplaced)
        {
            Action<long, long> accepted = pOnAccepted == null
                ? null
                : (sequence, replacedSequence) => pOnAccepted(sequence);
            return TryEnqueue(pWorker, pEnvelope, accepted, pOnCommitted,
                pOnFailed, out pReplaced);
        }

        public bool TryEnqueue(HistoricalWriteWorker pWorker,
            HistoricalWriteEnvelope pEnvelope,
            Action<long, long> pOnAccepted,
            Action<long, object> pOnCommitted,
            Action<long, string> pOnFailed,
            out HistoricalWriteEnvelope pReplaced)
        {
            lock (_gate)
            {
                pReplaced = null;
                if (pWorker == null ||
                    !pWorker.TryEnqueue(pEnvelope, out pReplaced))
                    return false;

                if (pReplaced != null)
                    RemoveLocked(pReplaced.Sequence);
                if (pOnCommitted != null)
                    _commitCallbacks[pEnvelope.Sequence] = pOnCommitted;
                if (pOnFailed != null)
                    _failureCallbacks[pEnvelope.Sequence] = pOnFailed;
                pOnAccepted?.Invoke(pEnvelope.Sequence,
                    pReplaced?.Sequence ?? 0L);
                return true;
            }
        }

        public void Take(long pSequence,
            out Action<long, object> pOnCommitted,
            out Action<long, string> pOnFailed)
        {
            lock (_gate)
            {
                _commitCallbacks.TryGetValue(pSequence, out pOnCommitted);
                _failureCallbacks.TryGetValue(pSequence, out pOnFailed);
                RemoveLocked(pSequence);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _commitCallbacks.Clear();
                _failureCallbacks.Clear();
            }
        }

        private void RemoveLocked(long pSequence)
        {
            _commitCallbacks.Remove(pSequence);
            _failureCallbacks.Remove(pSequence);
        }
    }
}
