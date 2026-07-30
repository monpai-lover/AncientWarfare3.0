using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal sealed class NobleDeathSuccessionRetryQueue<T> where T : class
    {
        private readonly Dictionary<long, T> _pending =
            new Dictionary<long, T>();

        public int Count => _pending.Count;

        public bool TryUpsert(long pActorId, T pValue)
        {
            if (pActorId < 0L || pValue == null) return false;
            _pending[pActorId] = pValue;
            return true;
        }

        public bool TryProcess(long pActorId, Func<T, bool> pProcess)
        {
            if (!_pending.TryGetValue(pActorId, out T value)) return true;
            if (!TryInvoke(pProcess, value)) return false;
            _pending.Remove(pActorId);
            return true;
        }

        public bool TryProcessOne(Func<T, bool> pProcess)
        {
            long actorId = -1L;
            foreach (KeyValuePair<long, T> pair in _pending)
            {
                actorId = pair.Key;
                break;
            }
            return actorId < 0L || TryProcess(actorId, pProcess);
        }

        public bool TryFlushAll(Func<T, bool> pProcess)
        {
            var actorIds = new List<long>(_pending.Keys);
            for (int i = 0; i < actorIds.Count; i++)
                TryProcess(actorIds[i], pProcess);
            return _pending.Count == 0;
        }

        public void Clear()
        {
            _pending.Clear();
        }

        private static bool TryInvoke(Func<T, bool> pProcess, T pValue)
        {
            if (pProcess == null) return false;
            try { return pProcess(pValue); }
            catch { return false; }
        }
    }
}
