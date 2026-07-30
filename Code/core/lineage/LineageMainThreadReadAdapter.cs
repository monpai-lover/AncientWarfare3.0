using System;
using System.Data.SQLite;
using System.Threading;

namespace AncientWarfare3.core.lineage
{
    internal sealed class LineageMainThreadReadAdapter
    {
        private readonly SQLiteConnection _connection;
        private readonly int _ownerThreadId;

        public LineageMainThreadReadAdapter(SQLiteConnection pConnection)
        {
            _connection = pConnection;
            _ownerThreadId = Environment.CurrentManagedThreadId;
        }

        public object Execute(LineageTreeReadExecution pExecution,
            CancellationToken pToken)
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
                throw new InvalidOperationException(
                    "Lineage main-thread read adapter used from another thread.");
            if (_connection == null)
                throw new InvalidOperationException(
                    "Lineage archive database is unavailable.");
            if (pExecution == null)
                throw new ArgumentNullException(nameof(pExecution));
            return pExecution.Execute(_connection, pToken);
        }
    }
}
