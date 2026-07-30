using System;
using System.Data.SQLite;
using System.Threading;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.uiquery
{
    internal static class AWHistoricalMainThreadReadService
    {
        public static object Read(LineageTreeReadExecution pExecution,
            CancellationToken pToken)
        {
            if (!ThreadHelper.isMainThread())
                throw new InvalidOperationException(
                    "Historical fallback reads require the main thread.");
            SQLiteConnection connection =
                LineageArchiveManager.Instance.OperatingDB;
            var adapter = new LineageMainThreadReadAdapter(connection);
            return adapter.Execute(pExecution, pToken);
        }
    }
}
