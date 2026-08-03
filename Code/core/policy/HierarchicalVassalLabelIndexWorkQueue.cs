using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    internal sealed class HierarchicalVassalLabelIndexWorkQueue
    {
        private readonly Queue<IndexOperation> _operations =
            new Queue<IndexOperation>();
        private readonly Dictionary<string, int> _pendingOperationsByKey =
            new Dictionary<string, int>();
        private IndexOperation _current;

        internal bool HasPendingWork => _current != null ||
            _operations.Count > 0;

        internal void EnqueueReplace(string pKey, HashSet<int> pPrevious,
            HashSet<int> pCurrent)
        {
            Enqueue(pKey, pPrevious, false);
            Enqueue(pKey, pCurrent, true);
        }

        internal void EnqueueRemove(string pKey, HashSet<int> pPrevious)
        {
            Enqueue(pKey, pPrevious, false);
        }

        internal bool IsKeyPending(string pKey)
        {
            return !string.IsNullOrEmpty(pKey) &&
                _pendingOperationsByKey.ContainsKey(pKey);
        }

        internal int Advance(IDictionary<int, HashSet<string>> pIndex,
            int pBudget)
        {
            if (pIndex == null || pBudget <= 0) return 0;
            int consumed = 0;
            while (consumed < pBudget && TryGetCurrent())
            {
                int zoneId = _current.Current;
                if (_current.Add)
                    AddIndexedKey(pIndex, zoneId, _current.Key);
                else
                    RemoveIndexedKey(pIndex, zoneId, _current.Key);
                consumed++;
                if (!_current.MoveNext()) CompleteCurrent();
            }
            return consumed;
        }

        internal void Clear()
        {
            _current?.Dispose();
            _current = null;
            while (_operations.Count > 0)
                _operations.Dequeue().Dispose();
            _pendingOperationsByKey.Clear();
        }

        private void Enqueue(string pKey, HashSet<int> pZoneIds, bool pAdd)
        {
            if (string.IsNullOrEmpty(pKey) || pZoneIds == null ||
                pZoneIds.Count == 0) return;
            var operation = new IndexOperation(pKey, pZoneIds, pAdd);
            if (!operation.HasCurrent)
            {
                operation.Dispose();
                return;
            }
            _operations.Enqueue(operation);
            _pendingOperationsByKey.TryGetValue(pKey, out int count);
            _pendingOperationsByKey[pKey] = count + 1;
        }

        private bool TryGetCurrent()
        {
            if (_current != null) return true;
            if (_operations.Count == 0) return false;
            _current = _operations.Dequeue();
            return true;
        }

        private void CompleteCurrent()
        {
            string key = _current.Key;
            _current.Dispose();
            _current = null;
            if (!_pendingOperationsByKey.TryGetValue(key, out int count))
                return;
            if (count <= 1) _pendingOperationsByKey.Remove(key);
            else _pendingOperationsByKey[key] = count - 1;
        }

        private static void AddIndexedKey(
            IDictionary<int, HashSet<string>> pIndex, int pZoneId,
            string pKey)
        {
            if (!pIndex.TryGetValue(pZoneId, out HashSet<string> keys))
            {
                keys = new HashSet<string>();
                pIndex[pZoneId] = keys;
            }
            keys.Add(pKey);
        }

        private static void RemoveIndexedKey(
            IDictionary<int, HashSet<string>> pIndex, int pZoneId,
            string pKey)
        {
            if (!pIndex.TryGetValue(pZoneId, out HashSet<string> keys))
                return;
            keys.Remove(pKey);
            if (keys.Count == 0) pIndex.Remove(pZoneId);
        }

        private sealed class IndexOperation : IDisposable
        {
            private HashSet<int>.Enumerator _enumerator;

            internal IndexOperation(string pKey, HashSet<int> pZoneIds,
                bool pAdd)
            {
                Key = pKey;
                Add = pAdd;
                _enumerator = pZoneIds.GetEnumerator();
                HasCurrent = _enumerator.MoveNext();
            }

            internal string Key { get; }

            internal bool Add { get; }

            internal bool HasCurrent { get; private set; }

            internal int Current => _enumerator.Current;

            internal bool MoveNext()
            {
                HasCurrent = _enumerator.MoveNext();
                return HasCurrent;
            }

            public void Dispose()
            {
                _enumerator.Dispose();
                HasCurrent = false;
            }
        }
    }
}
