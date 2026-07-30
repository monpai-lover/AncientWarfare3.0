using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.asyncwork
{
    internal sealed class AWBoundedLatestQueue<T>
    {
        private readonly int _capacity;
        private readonly Func<T, string> _keySelector;
        private readonly LinkedList<T> _queue = new LinkedList<T>();
        private readonly Dictionary<string, LinkedListNode<T>> _byKey =
            new Dictionary<string, LinkedListNode<T>>(StringComparer.Ordinal);

        public AWBoundedLatestQueue(int pCapacity, Func<T, string> pKeySelector)
        {
            _capacity = pCapacity;
            _keySelector = pKeySelector;
        }

        public int Count => _queue.Count;
        public int Capacity => _capacity;

        public bool ContainsKey(string pKey)
        {
            return _byKey.ContainsKey(pKey);
        }

        public bool CanEnqueue(T pItem)
        {
            string key = _keySelector(pItem);
            return _byKey.ContainsKey(key) || _queue.Count < _capacity;
        }

        public bool TryEnqueue(T pItem)
        {
            return TryEnqueue(pItem, out _);
        }

        public bool TryEnqueue(T pItem, out T pReplaced)
        {
            string key = _keySelector(pItem);
            if (_byKey.TryGetValue(key, out LinkedListNode<T> existing))
            {
                pReplaced = existing.Value;
                existing.Value = pItem;
                return true;
            }
            pReplaced = default;
            if (_queue.Count >= _capacity) return false;
            LinkedListNode<T> node = _queue.AddLast(pItem);
            _byKey.Add(key, node);
            return true;
        }

        public bool TryDequeue(out T pItem)
        {
            LinkedListNode<T> first = _queue.First;
            if (first == null)
            {
                pItem = default;
                return false;
            }
            _queue.RemoveFirst();
            _byKey.Remove(_keySelector(first.Value));
            pItem = first.Value;
            return true;
        }

        public void Clear()
        {
            _queue.Clear();
            _byKey.Clear();
        }
    }

    internal sealed class AWBoundedOrderedQueue<T>
    {
        private readonly int _capacity;
        private readonly Queue<T> _queue = new Queue<T>();

        public AWBoundedOrderedQueue(int pCapacity)
        {
            _capacity = pCapacity;
        }

        public int Count => _queue.Count;

        public bool TryEnqueue(T pItem)
        {
            if (_queue.Count >= _capacity) return false;
            _queue.Enqueue(pItem);
            return true;
        }

        public bool TryDequeue(out T pItem)
        {
            if (_queue.Count == 0)
            {
                pItem = default;
                return false;
            }
            pItem = _queue.Dequeue();
            return true;
        }
    }
}
