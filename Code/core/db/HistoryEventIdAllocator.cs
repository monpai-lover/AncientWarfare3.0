using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.db
{
    internal sealed class HistoryEventIdAllocator
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, long> _lastByTable;

        public HistoryEventIdAllocator(IEnumerable<string> pTables)
        {
            _lastByTable = new Dictionary<string, long>(
                StringComparer.Ordinal);
            if (pTables == null) return;
            foreach (string table in pTables)
            {
                if (!string.IsNullOrWhiteSpace(table) &&
                    !_lastByTable.ContainsKey(table))
                    _lastByTable.Add(table, 0L);
            }
        }

        public void Seed(string pTable, long pMaximum)
        {
            lock (_gate)
            {
                EnsureKnown(pTable);
                if (pMaximum > _lastByTable[pTable])
                    _lastByTable[pTable] = pMaximum;
            }
        }

        public long Next(string pTable)
        {
            lock (_gate)
            {
                EnsureKnown(pTable);
                long next = checked(_lastByTable[pTable] + 1L);
                _lastByTable[pTable] = next;
                return next;
            }
        }

        private void EnsureKnown(string pTable)
        {
            if (string.IsNullOrEmpty(pTable) ||
                !_lastByTable.ContainsKey(pTable))
                throw new ArgumentException(
                    "Unknown history event table.", nameof(pTable));
        }
    }
}
