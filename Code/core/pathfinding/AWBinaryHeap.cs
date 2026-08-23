// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.pathfinding
{
    internal sealed class AWBinaryHeap<T>
    {
        private readonly IComparer<T> _comparer;
        private T[] _items;

        public AWBinaryHeap(int pCapacity, IComparer<T> pComparer)
        {
            _items = new T[Math.Max(4, pCapacity)];
            _comparer = pComparer ?? Comparer<T>.Default;
        }

        public int Count { get; private set; }

        internal void EnsureCapacity(int pCapacity)
        {
            if (pCapacity <= _items.Length) return;
            int capacity = _items.Length;
            while (capacity < pCapacity) capacity *= 2;
            Array.Resize(ref _items, capacity);
        }

        public void Enqueue(T pItem)
        {
            EnsureCapacity(Count + 1);
            int index = Count++;
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (_comparer.Compare(pItem, _items[parent]) >= 0) break;
                _items[index] = _items[parent];
                index = parent;
            }
            _items[index] = pItem;
        }

        public T Dequeue()
        {
            if (Count <= 0) throw new InvalidOperationException("Heap is empty");
            T result = _items[0];
            T tail = _items[--Count];
            _items[Count] = default;
            if (Count == 0) return result;

            int index = 0;
            int half = Count >> 1;
            while (index < half)
            {
                int child = index * 2 + 1;
                int right = child + 1;
                if (right < Count && _comparer.Compare(_items[right], _items[child]) < 0)
                    child = right;
                if (_comparer.Compare(tail, _items[child]) <= 0) break;
                _items[index] = _items[child];
                index = child;
            }
            _items[index] = tail;
            return result;
        }

        public void Clear()
        {
            if (Count <= 0) return;
            Array.Clear(_items, 0, Count);
            Count = 0;
        }
    }
}
