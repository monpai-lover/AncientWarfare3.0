using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AncientWarfare3.api.history
{
    public sealed class AW3HistoryPage<T>
    {
        private AW3HistoryPage(IReadOnlyList<T> items, bool hasMore,
            string nextCursor)
        {
            var copy = new List<T>(items ?? new List<T>());
            Items = new ReadOnlyCollection<T>(copy);
            HasMore = hasMore;
            NextCursor = nextCursor ?? "";
        }

        public IReadOnlyList<T> Items { get; }
        public bool HasMore { get; }
        public string NextCursor { get; }

        public static AW3HistoryPage<T> Create(IEnumerable<T> items,
            bool hasMore = false, string nextCursor = "")
        {
            return new AW3HistoryPage<T>(
                new List<T>(items ?? new List<T>()), hasMore, nextCursor);
        }
    }
}
