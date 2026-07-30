using System;

namespace AncientWarfare3.core.uiquery
{
    internal readonly struct AWUiQueryKey
    {
        public AWUiQueryKey(string windowId, long contextId, string filter,
            long revision, long queryGeneration)
        {
            WindowId = windowId ?? string.Empty;
            ContextId = contextId;
            Filter = filter ?? string.Empty;
            Revision = revision;
            QueryGeneration = queryGeneration;
        }

        public string WindowId { get; }
        public long ContextId { get; }
        public string Filter { get; }
        public long Revision { get; }
        public long QueryGeneration { get; }
    }

    internal sealed class AWUiPage<T>
    {
        public AWUiPage(AWUiQueryKey key, int totalCount, int pageIndex,
            T[] items)
        {
            Key = key;
            TotalCount = Math.Max(0, totalCount);
            PageIndex = Math.Max(0, pageIndex);
            Items = items == null ? Array.Empty<T>() : (T[])items.Clone();
        }

        public AWUiQueryKey Key { get; }
        public int TotalCount { get; }
        public int PageIndex { get; }
        public T[] Items { get; }
    }

    internal readonly struct AWUiCandidateRow
    {
        public AWUiCandidateRow(long actorId, double primaryScore,
            double secondaryScore, string label)
        {
            ActorId = actorId;
            PrimaryScore = Finite(primaryScore);
            SecondaryScore = Finite(secondaryScore);
            Label = label ?? string.Empty;
        }

        public long ActorId { get; }
        public double PrimaryScore { get; }
        public double SecondaryScore { get; }
        public string Label { get; }

        private static double Finite(double pValue)
        {
            return double.IsNaN(pValue) || double.IsInfinity(pValue)
                ? 0d
                : pValue;
        }
    }

    internal readonly struct AWUiLayoutPoint
    {
        public AWUiLayoutPoint(float pX, float pY)
        {
            X = pX;
            Y = pY;
        }

        public float X { get; }
        public float Y { get; }
    }

    internal sealed class AWUiQueryState
    {
        private readonly string _windowId;
        private long _queryGeneration;
        private AWUiQueryKey _current;
        private bool _open;

        public AWUiQueryState(string pWindowId)
        {
            _windowId = pWindowId ?? string.Empty;
        }

        public AWUiQueryKey Begin(long contextId, string filter,
            long revision)
        {
            _queryGeneration = _queryGeneration == long.MaxValue
                ? 1L
                : _queryGeneration + 1L;
            _current = new AWUiQueryKey(_windowId, contextId, filter,
                revision, _queryGeneration);
            _open = true;
            return _current;
        }

        public bool Accept(AWUiQueryKey pKey)
        {
            return _open &&
                   string.Equals(_current.WindowId, pKey.WindowId,
                       StringComparison.Ordinal) &&
                   _current.ContextId == pKey.ContextId &&
                   string.Equals(_current.Filter, pKey.Filter,
                       StringComparison.Ordinal) &&
                   _current.Revision == pKey.Revision &&
                   _current.QueryGeneration == pKey.QueryGeneration;
        }

        public bool Accept(AWUiQueryKey pKey, long currentRevision)
        {
            return pKey.Revision == currentRevision && Accept(pKey);
        }

        public void Close()
        {
            _open = false;
            if (_queryGeneration < long.MaxValue) _queryGeneration++;
        }
    }
}
