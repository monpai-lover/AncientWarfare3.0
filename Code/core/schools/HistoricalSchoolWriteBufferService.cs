using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolWriteBufferService
    {
        private static readonly HistoricalSchoolWriteBuffer Buffer =
            new HistoricalSchoolWriteBuffer();
        private static long _frame;

        public static int Count => Buffer.Count;

        public static bool TryEnqueue(IHistoricalSchoolWriteOperation pOperation,
            bool pDurableReady = true)
        {
            return Buffer.TryEnqueue(pOperation, pDurableReady);
        }

        public static bool ProcessFrame()
        {
            if (Buffer.Count == 0) return false;
            if (_frame < long.MaxValue) _frame++;
            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            return Buffer.ProcessFrame(_frame,
                new HistoricalSchoolSqlWriteBatchExecutor(db));
        }

        public static bool FlushForSave()
        {
            if (Buffer.Count == 0) return true;
            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            return Buffer.FlushForSave(new HistoricalSchoolSqlWriteBatchExecutor(db));
        }

        public static void Clear()
        {
            Buffer.Clear();
            _frame = 0L;
        }
    }
}
